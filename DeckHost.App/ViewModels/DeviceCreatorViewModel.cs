using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Device.Hid;

namespace DeckHost.App.ViewModels;

/// <summary>
/// The device creator: makes a USB device DeckHost doesn't know into a deck. Pick the device, then
/// add its keys and knobs by pressing and turning them (<see cref="HidLearner"/> works out where they
/// show up in the device's reports), place them as they sit on the device, and save: the device
/// joins the device picker with its own profiles (<see cref="HidDevicePlugin"/>).
/// </summary>
public sealed partial class DeviceCreatorViewModel : ViewModelBase, IDisposable
{
    public const double KeySize = 72;
    public const double KnobSize = 56;
    public const double Gap = 14;

    /// <summary>How long before "Add" counts as the device being left alone.</summary>
    private static readonly TimeSpan IdleWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>Knob learning ends after this many changed reports (or Done).</summary>
    private const int KnobReports = 8;

    private readonly HidDevicePlugin? _plugin;
    private readonly IHidDeviceSource _source;
    private readonly Action<Action> _post;
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<(DateTimeOffset Time, byte[] Report)> _recent = [];
    private readonly List<byte[]> _learnIdle = [];
    private readonly List<byte[]> _learnActive = [];
    private IDisposable? _listener;
    private HidInputParser? _liveParser;
    private HidDeviceItem? _selectedHardware;
    private CreatorControlViewModel? _selectedControl;
    private LearnMode _learning;
    private string _name = "My USB device";
    private string _lastReport = string.Empty;
    private string _status = "Pick the device in the list. Plug it in first; it shows up after Refresh.";
    private string? _editingId;
    private int _reportCount;
    private int _columns = 4;

    public DeviceCreatorViewModel(HidDevicePlugin? plugin, IHidDeviceSource source, Action<Action>? post = null, Func<DateTimeOffset>? clock = null)
    {
        _plugin = plugin;
        _source = source;
        _post = post ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        RefreshHardware();
    }

    private enum LearnMode
    {
        None,
        Key,
        Knob,
        KnobPress,
    }

    // ---------------------------------------------------------------- the device

    public ObservableCollection<HidDeviceItem> Hardware { get; } = [];

    public IReadOnlyList<HidDeviceDescription> SavedDevices => _plugin?.Descriptions ?? [];

    public bool HasSavedDevices => SavedDevices.Count > 0;

    public HidDeviceItem? SelectedHardware
    {
        get => _selectedHardware;
        set
        {
            if (!SetProperty(ref _selectedHardware, value))
            {
                return;
            }

            Listen();
            OnPropertyChanged(nameof(HasHardware));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(UdevRule));
            OnPropertyChanged(nameof(ShowUdevRule));
        }
    }

    public bool HasHardware => _selectedHardware is not null;

    [RelayCommand]
    public void RefreshHardware()
    {
        var path = _selectedHardware?.Info.Path;
        Hardware.Clear();
        foreach (var info in _source.List())
        {
            Hardware.Add(new HidDeviceItem(info));
        }

        if (path is not null && Hardware.FirstOrDefault(item => item.Info.Path == path) is { } same)
        {
            _selectedHardware = same;
            OnPropertyChanged(nameof(SelectedHardware));
        }

        if (Hardware.Count == 0)
        {
            Status = "No USB HID devices found. Plug the device in and click Refresh.";
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(UdevInstructions));
            }
        }
    }

    public string LastReport
    {
        get => _lastReport;
        private set => SetProperty(ref _lastReport, value);
    }

    public string ReportInfo => _reportCount == 0
        ? "Nothing received yet: press a key on the device."
        : string.Create(CultureInfo.InvariantCulture, $"{_reportCount} report(s) received. The last one:");

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private void Listen()
    {
        _listener?.Dispose();
        _listener = null;
        _recent.Clear();
        _reportCount = 0;
        LastReport = string.Empty;
        OnPropertyChanged(nameof(ReportInfo));
        StopLearning();
        if (_selectedHardware is not { } item)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_editingId) && (_name == "My USB device" || string.IsNullOrWhiteSpace(_name)))
        {
            Name = item.Info.Name;
        }

        Status = item.Warning ?? "Listening. Press a key on the device to see its reports, then add the keys and knobs one by one.";
        _listener = _source.Listen(item.Info.Path, report => _post(() => OnReport(report)), message => _post(() => Status = message + (OperatingSystem.IsLinux() ? " The rule below gives DeckHost access." : string.Empty)));
    }

    /// <summary>A report from the device (on the UI thread).</summary>
    internal void OnReport(byte[] report)
    {
        var now = _clock();
        _recent.Add((now, report));
        _recent.RemoveAll(entry => now - entry.Time > IdleWindow);
        _reportCount++;
        LastReport = string.Join(' ', report.Select(static value => value.ToString("X2", CultureInfo.InvariantCulture)));
        OnPropertyChanged(nameof(ReportInfo));

        if (_learning != LearnMode.None)
        {
            Learn(report);
            return;
        }

        ShowLive(report);
    }

    // ---------------------------------------------------------------- learning

    public bool IsLearning => _learning != LearnMode.None;

    public bool IsLearningKnob => _learning == LearnMode.Knob;

    public bool CanAdd => HasHardware && !IsLearning;

    [RelayCommand]
    private void AddKey() => StartLearning(LearnMode.Key, "Press the key now (and let go).");

    [RelayCommand]
    private void AddKnob() => StartLearning(LearnMode.Knob, "Turn the knob to the right (clockwise) a few clicks, then click Done.");

    [RelayCommand]
    private void LearnPress()
    {
        if (SelectedControl is { IsKnob: true })
        {
            StartLearning(LearnMode.KnobPress, $"Push {SelectedControl.Name} now.");
        }
    }

    [RelayCommand]
    private void DoneTurning()
    {
        if (_learning == LearnMode.Knob)
        {
            FinishKnob();
        }
    }

    [RelayCommand]
    private void CancelLearning()
    {
        StopLearning();
        Status = "Cancelled.";
    }

    private void StartLearning(LearnMode mode, string prompt)
    {
        if (!HasHardware)
        {
            return;
        }

        // What came in just before is the device at rest.
        _learnIdle.Clear();
        _learnIdle.AddRange(_recent.Select(static entry => entry.Report));
        _learnActive.Clear();
        _learning = mode;
        Status = prompt;
        RaiseLearning();
    }

    private void StopLearning()
    {
        if (_learning != LearnMode.None)
        {
            // What the user did for this control isn't "the device at rest" for the next one.
            _recent.Clear();
        }

        _learning = LearnMode.None;
        _learnActive.Clear();
        RaiseLearning();
    }

    private void RaiseLearning()
    {
        OnPropertyChanged(nameof(IsLearning));
        OnPropertyChanged(nameof(IsLearningKnob));
        OnPropertyChanged(nameof(CanAdd));
    }

    private void Learn(byte[] report)
    {
        _learnActive.Add(report);
        switch (_learning)
        {
            case LearnMode.Key or LearnMode.KnobPress when HidLearner.LearnKey(_learnIdle, _learnActive) is { } input:
                if (FindByInput(input) is { } existing)
                {
                    StopLearning();
                    Status = $"That's {existing.Name}, which is already there. Press Add again for another one.";
                    return;
                }

                if (_learning == LearnMode.KnobPress && SelectedControl is { } knob)
                {
                    knob.Press = input;
                    Status = $"{knob.Name} can be pushed now ({input.Describe()}).";
                }
                else
                {
                    var key = AddControl(HidControlKind.Key, input);
                    Status = $"Added {key.Name} ({input.Describe()}). Press Add key for the next one.";
                }

                StopLearning();
                Rebuild();
                break;
            case LearnMode.Knob when _learnActive.Count >= KnobReports:
                FinishKnob();
                break;
        }
    }

    private void FinishKnob()
    {
        var input = HidLearner.LearnKnob(_learnIdle, _learnActive);
        StopLearning();
        if (input is null)
        {
            Status = "Nothing changed while turning. Try again, or the knob may send key presses (add it as two keys).";
            return;
        }

        var knob = AddControl(HidControlKind.Knob, input);
        Status = $"Added {knob.Name} ({input.Describe()}). If it can be pushed, select it and click Learn push.";
        Rebuild();
    }

    private CreatorControlViewModel? FindByInput(HidInput input) =>
        Controls.FirstOrDefault(control => control.Input == input || control.Press == input);

    // ---------------------------------------------------------------- controls and layout

    public ObservableCollection<CreatorControlViewModel> Controls { get; } = [];

    public bool HasControls => Controls.Count > 0;

    public CreatorControlViewModel? SelectedControl
    {
        get => _selectedControl;
        set
        {
            var old = _selectedControl;
            if (SetProperty(ref _selectedControl, value))
            {
                if (old is not null)
                {
                    old.IsSelected = false;
                }

                if (value is not null)
                {
                    value.IsSelected = true;
                }

                OnPropertyChanged(nameof(HasSelectedControl));
                OnPropertyChanged(nameof(SelectedIsKnob));
            }
        }
    }

    public bool HasSelectedControl => _selectedControl is not null;

    public bool SelectedIsKnob => _selectedControl?.IsKnob == true;

    /// <summary>Keys per row for Arrange.</summary>
    public int Columns
    {
        get => _columns;
        set
        {
            if (SetProperty(ref _columns, Math.Clamp(value, 1, 16)))
            {
                OnPropertyChanged(nameof(ColumnsValue));
            }
        }
    }

    /// <summary><see cref="Columns"/> for the number box (which works in decimals).</summary>
    public decimal? ColumnsValue
    {
        get => _columns;
        set => Columns = (int)(value ?? 4);
    }

    private CreatorControlViewModel AddControl(HidControlKind kind, HidInput input)
    {
        var keys = Controls.Count(static control => !control.IsKnob);
        var knobs = Controls.Count(static control => control.IsKnob);
        var (x, y) = kind == HidControlKind.Key
            ? ((keys % Columns) * (KeySize + Gap), (keys / Columns) * (KeySize + Gap))
            : (knobs * (KeySize + Gap) + ((KeySize - KnobSize) / 2), (Math.Max(1, (keys + Columns - 1) / Columns) * (KeySize + Gap)) + 8);
        var control = new CreatorControlViewModel(this, kind, kind == HidControlKind.Key ? $"Key {keys + 1}" : $"Knob {knobs + 1}", input, x, y);
        Controls.Add(control);
        SelectedControl = control;
        OnPropertyChanged(nameof(HasControls));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        return control;
    }

    [RelayCommand]
    private void RemoveControl()
    {
        if (SelectedControl is { } control)
        {
            Controls.Remove(control);
            SelectedControl = Controls.LastOrDefault();
            OnPropertyChanged(nameof(HasControls));
            OnPropertyChanged(nameof(CanSave));
            Rebuild();
        }
    }

    /// <summary>Keys in a grid of <see cref="Columns"/>, knobs in a row below.</summary>
    [RelayCommand]
    public void Arrange()
    {
        var keys = Controls.Where(static control => !control.IsKnob).ToList();
        for (var index = 0; index < keys.Count; index++)
        {
            keys[index].MoveTo((index % Columns) * (KeySize + Gap), (index / Columns) * (KeySize + Gap));
        }

        var top = Math.Max(1, (keys.Count + Columns - 1) / Columns) * (KeySize + Gap) + 8;
        var knobs = Controls.Where(static control => control.IsKnob).ToList();
        for (var index = 0; index < knobs.Count; index++)
        {
            knobs[index].MoveTo(index * (KeySize + Gap) + ((KeySize - KnobSize) / 2), keys.Count == 0 ? 0 : top);
        }

        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    public double CanvasWidth => Math.Max(480, Controls.Select(static control => control.X + control.Width).DefaultIfEmpty(0).Max() + 40);

    public double CanvasHeight => Math.Max(260, Controls.Select(static control => control.Y + control.Height).DefaultIfEmpty(0).Max() + 40);

    internal void OnControlMoved()
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    /// <summary>Lights up the keys that are down and shows knob turns, as the reports come in.</summary>
    private void ShowLive(byte[] report)
    {
        if (_liveParser is null)
        {
            return;
        }

        foreach (var input in _liveParser.Parse(report))
        {
            if (Controls.FirstOrDefault(control => control.Id == input.ControlId) is { } control)
            {
                control.ShowInput(input);
            }
        }
    }

    /// <summary>Ids by layout (keys in reading order, knobs left to right) and a fresh live parser.</summary>
    internal void Rebuild()
    {
        var keys = Controls.Where(static control => !control.IsKnob)
            .OrderBy(static control => Math.Round(control.Y / (KeySize / 2)))
            .ThenBy(static control => control.X)
            .ToList();
        for (var index = 0; index < keys.Count; index++)
        {
            keys[index].Id = string.Create(CultureInfo.InvariantCulture, $"key.{index}");
        }

        var knobs = Controls.Where(static control => control.IsKnob).OrderBy(static control => control.X).ThenBy(static control => control.Y).ToList();
        for (var index = 0; index < knobs.Count; index++)
        {
            knobs[index].Id = string.Create(CultureInfo.InvariantCulture, $"encoder.{index}");
        }

        _liveParser = new HidInputParser(BuildDescription());
    }

    // ---------------------------------------------------------------- saving

    public bool CanSave => HasHardware && HasControls && !string.IsNullOrWhiteSpace(Name);

    public HidDeviceDescription BuildDescription()
    {
        var info = _selectedHardware?.Info;
        return new HidDeviceDescription(
            _editingId ?? HidDeviceDescription.MakeId(Name),
            Name.Trim(),
            info?.VendorId ?? 0,
            info?.ProductId ?? 0,
            [.. Controls.OrderBy(static control => control.IsKnob).ThenBy(static control => NumberOf(control.Id)).Select(static control => control.ToModel())])
        {
            UsagePage = info?.UsagePage,
            Usage = info?.Usage,
        };
    }

    private static int NumberOf(string id) =>
        int.TryParse(id.AsSpan(id.IndexOf('.', StringComparison.Ordinal) + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;

    [RelayCommand]
    private void Save()
    {
        if (!CanSave || _plugin is null)
        {
            return;
        }

        Rebuild();
        var description = BuildDescription();
        try
        {
            _plugin.Save(description);
            _editingId = description.Id;
            OnPropertyChanged(nameof(SavedDevices));
            OnPropertyChanged(nameof(HasSavedDevices));
            Status = $"Saved {description.Name}. It's in the device picker in the header, with its own profiles."
                + (OperatingSystem.IsLinux() ? " On Linux, install the device rule below once." : string.Empty);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't save: {ex.Message}";
        }
    }

    /// <summary>Opens a saved device for changes (its hardware is picked when it's plugged in).</summary>
    [RelayCommand]
    private void Edit(HidDeviceDescription description)
    {
        _editingId = description.Id;
        Name = description.Name;
        Controls.Clear();
        foreach (var control in description.Controls)
        {
            Controls.Add(CreatorControlViewModel.From(this, control));
        }

        SelectedHardware = Hardware.FirstOrDefault(item => item.Info.VendorId == description.VendorId && item.Info.ProductId == description.ProductId &&
            (description.UsagePage is null || item.Info.UsagePage == description.UsagePage));
        Status = SelectedHardware is null ? $"Editing {description.Name}. Plug it in and click Refresh to test or add controls." : $"Editing {description.Name}.";
        OnPropertyChanged(nameof(HasControls));
        OnPropertyChanged(nameof(CanSave));
        OnControlMoved();
        Rebuild();
    }

    [RelayCommand]
    private void Delete(HidDeviceDescription description)
    {
        _plugin?.Delete(description.Id);
        if (_editingId == description.Id)
        {
            _editingId = null;
        }

        OnPropertyChanged(nameof(SavedDevices));
        OnPropertyChanged(nameof(HasSavedDevices));
        Status = $"Removed {description.Name}. Its profiles stay saved in case you add it again.";
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null;
        Controls.Clear();
        SelectedControl = null;
        Name = _selectedHardware?.Info.Name ?? "My USB device";
        OnPropertyChanged(nameof(HasControls));
        OnPropertyChanged(nameof(CanSave));
        Rebuild();
        Status = "A new device: add its keys and knobs.";
    }

    /// <summary>On Linux: the udev rule that lets DeckHost open the device without root.</summary>
    public string UdevRule => _selectedHardware is { } item
        ? string.Create(CultureInfo.InvariantCulture, $"SUBSYSTEM==\"hidraw\", ATTRS{{idVendor}}==\"{item.Info.VendorId:x4}\", ATTRS{{idProduct}}==\"{item.Info.ProductId:x4}\", MODE=\"0660\", TAG+=\"uaccess\"")
        : string.Empty;

    public string UdevInstructions => $"Save it as /etc/udev/rules.d/70-deckhost-{HidDeviceDescription.MakeId(Name)}.rules, run: sudo udevadm control --reload && sudo udevadm trigger, then plug the device in again.";

    public bool ShowUdevRule => OperatingSystem.IsLinux() && HasHardware;

    /// <summary>Listens again after <see cref="Dispose"/> (the page came back).</summary>
    public void Resume()
    {
        if (_listener is null && _selectedHardware is not null)
        {
            Listen();
        }
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
    }
}

/// <summary>A HID interface in the creator's list.</summary>
public sealed class HidDeviceItem(HidDeviceInfo info)
{
    public HidDeviceInfo Info { get; } = info;

    public string Label => $"{Info.Name}{(Info.Manufacturer is { } maker && !Info.Name.Contains(maker, StringComparison.OrdinalIgnoreCase) ? $" ({maker})" : string.Empty)}";

    public string Details => $"{Info.Ids} · {Info.Kind}";

    /// <summary>Why this one may not work, or null.</summary>
    public string? Warning => Info.IsKnownDeck
        ? "DeckHost already drives Elgato Stream Decks: pick another device."
        : Info.IsReservedOnWindows && OperatingSystem.IsWindows()
            ? "Windows keeps keyboards and mice to itself, so DeckHost can't read this interface. Many macro pads have a second, vendor-defined interface in the list: try that one."
            : null;
}

/// <summary>A control in the creator: what it is, where it sits and where its input shows up.</summary>
public sealed partial class CreatorControlViewModel : ViewModelBase
{
    private readonly DeviceCreatorViewModel _owner;
    private string _name;
    private string _id = string.Empty;
    private double _x;
    private double _y;
    private bool _isSelected;
    private bool _isDown;
    private string _activity = string.Empty;
    private HidInput? _press;

    public CreatorControlViewModel(DeviceCreatorViewModel owner, HidControlKind kind, string name, HidInput input, double x, double y)
    {
        _owner = owner;
        Kind = kind;
        _name = name;
        Input = input;
        _x = x;
        _y = y;
    }

    internal static CreatorControlViewModel From(DeviceCreatorViewModel owner, HidControl control) =>
        new(owner, control.Kind, control.Name, control.Input, control.X, control.Y) { Id = control.Id, Press = control.Press };

    public HidControlKind Kind { get; }

    public HidInput Input { get; }

    public HidInput? Press
    {
        get => _press;
        set
        {
            if (SetProperty(ref _press, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public bool IsKnob => Kind == HidControlKind.Knob;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double Width => IsKnob ? DeviceCreatorViewModel.KnobSize : DeviceCreatorViewModel.KeySize;

    public double Height => Width;

    public Avalonia.CornerRadius CornerRadius => new(IsKnob ? Width / 2 : 10);

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>The key is down right now (live from the device).</summary>
    public bool IsDown
    {
        get => _isDown;
        private set => SetProperty(ref _isDown, value);
    }

    /// <summary>The last turn, "↻ +2", shown on knobs.</summary>
    public string Activity
    {
        get => _activity;
        private set => SetProperty(ref _activity, value);
    }

    public string Summary => Input.Describe() + (Press is { } press ? $"; push: {press.Describe()}" : string.Empty);

    /// <summary>Moves it (snapped to a 4-unit grid, never off the canvas's top-left).</summary>
    public void MoveTo(double x, double y)
    {
        X = Math.Max(0, Math.Round(x / 4) * 4);
        Y = Math.Max(0, Math.Round(y / 4) * 4);
        _owner.OnControlMoved();
    }

    /// <summary>A drag ended: ids follow the new reading order.</summary>
    public void Dropped() => _owner.Rebuild();

    internal void ShowInput(HidInputEvent input)
    {
        switch (input.Event)
        {
            case Contracts.Enums.InputCapabilities.Press:
                IsDown = true;
                break;
            case Contracts.Enums.InputCapabilities.Release:
                IsDown = false;
                break;
            case Contracts.Enums.InputCapabilities.Rotate:
                Activity = input.Value > 0 ? $"↻ +{input.Value}" : $"↺ {input.Value}";
                break;
        }
    }

    public HidControl ToModel() => new(Id, Name.Trim().Length == 0 ? Id : Name.Trim(), Kind, X, Y, Width, Height, Input) { Press = Press };
}
