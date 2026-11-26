using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Platform;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;

namespace DeckHost.App.ViewModels;

/// <summary>
/// The welcome tour, shown on the first start (and from the menu): the devices DeckHost found and
/// how to connect one, a starter layout, the main preferences, and how the editor works.
/// </summary>
public sealed partial class WelcomeViewModel : ViewModelBase
{
    public const int StepCount = 4;

    /// <summary>The MiraBox plugin always lists its N4 Pro (so profiles can be made for it), even when none was ever plugged in.</summary>
    internal const string AlwaysListedDeviceId = "mirabox.n4pro";

    internal const string WebDeckId = "web.deck";
    private const string WebSharing = "sharing";
    private const string WebSharingNetwork = "network";
    private const string WebQrCode = "qr-code";

    private readonly MainViewModel? _main;
    private readonly IDeviceHostService? _host;
    private readonly UpdateViewModel? _updates;
    private readonly DeviceSettingsService? _deviceSettings;
    private readonly HashSet<string> _connectedHardware = new(StringComparer.OrdinalIgnoreCase);
    private int _step;
    private bool _starterApplied;
    private bool _watching;
    private bool _firstRefresh = true;
    private string _starterStatus = string.Empty;
    private string _foundMessage = string.Empty;
    private string _noDeviceStatus = string.Empty;
    private string _importStatus = string.Empty;
    private string _commandsStatus = string.Empty;
    private bool _startAtLogin;
    private bool _offerCrashReports;
    private bool _keepRunningInTray;
    private StarterLayout _selectedStarter = StarterLayouts.MediaStarter;
    private DeviceSetupAdvice _advice;

    public WelcomeViewModel(MainViewModel? main = null, IDeviceHostService? host = null, UpdateViewModel? updates = null, DeviceSettingsService? deviceSettings = null)
    {
        _main = main;
        _host = host;
        _updates = updates;
        _deviceSettings = deviceSettings;
        var settings = AppSettings.Load();
        _startAtLogin = AppSettings.CanStartAtLogin && AppSettings.IsStartAtLoginEnabled();
        _offerCrashReports = settings.OfferCrashReports;
        _keepRunningInTray = settings.KeepsRunningInTray;
        _advice = DeviceSetupHelp.Advise(new DeviceSetupFacts(ThisSystem));
        RefreshDevices();
    }

    /// <summary>Raised when the tour is finished or skipped (the page closes).</summary>
    public event EventHandler? Finished;

    /// <summary>Raised for Add a USB device: the main window opens the device creator.</summary>
    public event EventHandler? DeviceCreatorRequested;

    /// <summary>Raised for Open a deck on this screen: the main window shows the desktop deck.</summary>
    public event EventHandler? DesktopDeckRequested;

    /// <summary>Raised for Try the simulator (after it was allowed in the settings): the main window starts a simulator window.</summary>
    public event EventHandler? SimulatorRequested;

    [RelayCommand]
    private void AddUsbDevice() => DeviceCreatorRequested?.Invoke(this, EventArgs.Empty);

    public int Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, Math.Clamp(value, 0, StepCount - 1)))
            {
                foreach (var name in new[] { nameof(IsDevicesStep), nameof(IsStartStep), nameof(IsPreferencesStep), nameof(IsTipsStep), nameof(CanGoBack), nameof(NextLabel), nameof(StepText) })
                {
                    OnPropertyChanged(name);
                }
            }
        }
    }

    public bool IsDevicesStep => Step == 0;

    public bool IsStartStep => Step == 1;

    public bool IsPreferencesStep => Step == 2;

    public bool IsTipsStep => Step == 3;

    public bool CanGoBack => Step > 0;

    public string NextLabel => Step == StepCount - 1 ? "Start using DeckHost" : "Next";

    public string StepText => $"Step {Step + 1} of {StepCount}";

    // ---------------------------------------------------------------- devices

    public IReadOnlyList<WelcomeDevice> Devices { get; private set; } = [];

    public bool HasDevices => Devices.Count > 0;

    public bool HasConnectedDevice => Devices.Any(static device => device.IsConnected && !device.IsVirtual);

    public bool ShowNoDeviceHelp => !HasConnectedDevice;

    /// <summary>"Found your …" once a device was plugged in while the tour is open.</summary>
    public string FoundMessage
    {
        get => _foundMessage;
        private set
        {
            if (SetProperty(ref _foundMessage, value))
            {
                OnPropertyChanged(nameof(HasFoundMessage));
            }
        }
    }

    public bool HasFoundMessage => FoundMessage.Length > 0;

    /// <summary>What to do when the device isn't found, for this system (what DeckHost found out).</summary>
    public string DeviceHelp => _advice.Text;

    /// <summary>Whether the help names something that keeps the device from working (shown more prominently).</summary>
    public bool DeviceHelpIsProblem => _advice.IsProblem;

    /// <summary>Terminal commands for the help (Linux), or empty.</summary>
    public string HelpCommands => _advice.Commands ?? string.Empty;

    public bool HasHelpCommands => _advice.Commands is not null;

    public bool HasHelpDocs => _advice.DocsPath is not null;

    /// <summary>After Copy: whether the commands went to the clipboard.</summary>
    public string CommandsStatus
    {
        get => _commandsStatus;
        private set => SetProperty(ref _commandsStatus, value);
    }

    /// <summary>What happened after a no-device button (Use my phone, Try the simulator).</summary>
    public string NoDeviceStatus
    {
        get => _noDeviceStatus;
        private set
        {
            if (SetProperty(ref _noDeviceStatus, value))
            {
                OnPropertyChanged(nameof(HasNoDeviceStatus));
            }
        }
    }

    public bool HasNoDeviceStatus => NoDeviceStatus.Length > 0;

    /// <summary>The view copied the commands (or couldn't).</summary>
    public void CommandsCopied(bool copied) =>
        CommandsStatus = copied ? "Copied. Paste them into a terminal." : "Couldn't copy: select the commands and copy them by hand.";

    [RelayCommand]
    private void OpenHelpDocs() => OpenUrl(SupportViewModel.DocumentationUrl + (_advice.DocsPath ?? string.Empty));

    /// <summary>Starts following the host's device list (while the tour is shown).</summary>
    public void StartWatching()
    {
        if (_watching || _host is null)
        {
            return;
        }

        _watching = true;
        _host.DevicesChanged += OnDevicesChanged;
        RefreshDevices();
    }

    /// <summary>Stops following the device list (the tour closed or another page covers it).</summary>
    public void StopWatching()
    {
        if (!_watching || _host is null)
        {
            return;
        }

        _watching = false;
        _host.DevicesChanged -= OnDevicesChanged;
    }

    // Raised on a host thread.
    private void OnDevicesChanged() => Dispatcher.UIThread.Post(RefreshDevices);

    [RelayCommand]
    private void RefreshDevices()
    {
        var all = (_host?.Devices ?? []).ToList();
        Devices = [.. VisibleDevices(all, id => _main?.HasBindingsFor(id) == true).Select(static device => new WelcomeDevice(
            device.Id,
            device.Name,
            device.IsConnected,
            IsVirtual(device.Id)))];
        var found = NewlyConnected(_connectedHardware, all);
        _connectedHardware.Clear();
        _connectedHardware.UnionWith(all.Where(static device => device.IsConnected && !IsVirtual(device.Id)).Select(static device => device.Id));
        if (found is not null && !_firstRefresh)
        {
            FoundMessage = $"Found your {found.Name}.";
        }

        _firstRefresh = false;
        OnPropertyChanged(nameof(Devices));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasConnectedDevice));
        OnPropertyChanged(nameof(ShowNoDeviceHelp));
        RefreshStarter();
        if (ShowNoDeviceHelp)
        {
            _ = RefreshAdviceAsync();
        }
    }

    private async Task RefreshAdviceAsync()
    {
        DeviceSetupFacts facts;
        try
        {
            facts = await Task.Run(DeviceSetupHelp.Gather);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return;
        }

        _advice = DeviceSetupHelp.Advise(facts);
        foreach (var name in new[] { nameof(DeviceHelp), nameof(DeviceHelpIsProblem), nameof(HelpCommands), nameof(HasHelpCommands), nameof(HasHelpDocs) })
        {
            OnPropertyChanged(name);
        }
    }

    private static HelpSystem ThisSystem => OperatingSystem.IsLinux() ? HelpSystem.Linux : OperatingSystem.IsMacOS() ? HelpSystem.MacOS : HelpSystem.Windows;

    /// <summary>A device on screen only (the Web Deck, a simulator window), not hardware.</summary>
    internal static bool IsVirtual(string deviceId) =>
        deviceId.StartsWith("web.", StringComparison.OrdinalIgnoreCase) || deviceId.StartsWith("simulator.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The devices the tour lists (pure): all but the always-listed N4 Pro while it isn't plugged in
    /// and nothing was ever set up for it (no one has one, as far as DeckHost knows).
    /// </summary>
    internal static IReadOnlyList<DeviceDefinition> VisibleDevices(IEnumerable<DeviceDefinition> devices, Func<string, bool> hasBindings) =>
        [.. devices.Where(device => device.IsConnected
            || !string.Equals(device.Id, AlwaysListedDeviceId, StringComparison.OrdinalIgnoreCase)
            || hasBindings(device.Id))];

    /// <summary>The first hardware device that is connected now but wasn't among <paramref name="connectedBefore"/> (pure).</summary>
    internal static DeviceDefinition? NewlyConnected(IReadOnlySet<string> connectedBefore, IEnumerable<DeviceDefinition> devices) =>
        devices.FirstOrDefault(device => device.IsConnected && !IsVirtual(device.Id) && !connectedBefore.Contains(device.Id));

    /// <summary>
    /// The device the starter layout fills (pure): the edited one when it is plugged-in hardware, else
    /// the first plugged-in hardware device; null when there is none (the edited device stays).
    /// </summary>
    internal static DeviceDefinition? PickStarterTarget(IEnumerable<DeviceDefinition> devices, string? editedDeviceId)
    {
        var hardware = devices.Where(static device => device.IsConnected && !IsVirtual(device.Id)).ToList();
        return hardware.FirstOrDefault(device => string.Equals(device.Id, editedDeviceId, StringComparison.OrdinalIgnoreCase)) ?? hardware.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenDesktopDeck() => DesktopDeckRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Use my phone: turns on the Web Deck's sharing with phones and opens its QR code.</summary>
    [RelayCommand]
    private async Task UsePhoneAsync()
    {
        if (_deviceSettings?.SettingsPlugin(WebDeckId) is not { } plugin)
        {
            NoDeviceStatus = "Choose the Web Deck in the device picker at the top, open Device settings and turn on sharing with phones. Its QR code opens the deck on your phone.";
            return;
        }

        NoDeviceStatus = "Turning on the Web Deck for phones…";
        try
        {
            NoDeviceStatus = await Task.Run(() =>
            {
                var settings = plugin.GetDeviceSettings(WebDeckId);
                if (settings.Get(WebSharing) != WebSharingNetwork && plugin.ApplyDeviceSettings(WebDeckId, settings.With(WebSharing, WebSharingNetwork)) is { } error)
                {
                    return error;
                }

                var opened = plugin is IDeviceSettingActions actions ? actions.RunSettingAction(WebDeckId, WebQrCode) : "The Web Deck is shared now: its address is in its Device settings.";
                return $"The Web Deck is shared with phones on this network. {opened} Scan it with your phone's camera.";
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            NoDeviceStatus = $"Couldn't turn on the Web Deck: {ex.Message}";
        }
    }

    /// <summary>Try the simulator: allows simulator windows (DeckHost settings → Developer) and starts one.</summary>
    [RelayCommand]
    private void TrySimulator()
    {
        var settings = AppSettings.Load();
        if (!settings.DeviceSimulator)
        {
            try
            {
                AppSettingsViewModel.SaveAndAnnounce(settings with { DeviceSimulator = true });
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                NoDeviceStatus = $"Couldn't turn on the simulator: {ex.Message}";
                return;
            }
        }

        SimulatorRequested?.Invoke(this, EventArgs.Empty);
        NoDeviceStatus = "A simulator window opens: pick a model there, and it shows up here as a device. (DeckHost settings → Developer turns it off.)";
    }

    // ---------------------------------------------------------------- start

    /// <summary>The starter layouts to choose from.</summary>
    public IReadOnlyList<StarterLayout> Starters { get; } = StarterLayouts.All;

    public StarterLayout SelectedStarter
    {
        get => _selectedStarter;
        set
        {
            if (value is not null && SetProperty(ref _selectedStarter, value))
            {
                RefreshStarter();
            }
        }
    }

    public string StarterDescription => SelectedStarter.Description;

    /// <summary>What the chosen starter puts where, on the device it fills.</summary>
    public IReadOnlyList<StarterPreviewItem> StarterPreview { get; private set; } = [];

    public bool HasStarterPreview => StarterPreview.Count > 0;

    public bool StarterFillsNothing => _main is not null && StarterPreview.Count == 0;

    /// <summary>Why some of the starter's actions are missing (OBS isn't installed), or empty.</summary>
    public string StarterNote => SelectedStarter.RequiredPluginId is { } plugin && _main?.HasActionsOf(plugin) == false ? SelectedStarter.MissingNote ?? string.Empty : string.Empty;

    public bool HasStarterNote => StarterNote.Length > 0;

    /// <summary>The device the starter layout fills: plugged-in hardware first.</summary>
    public DeviceDefinition? StarterTarget => PickStarterTarget(_host?.Devices ?? [], _main?.SelectedDevice?.Id) ?? _main?.SelectedDevice;

    public string ApplyStarterLabel => StarterTarget is { } device ? $"Put these on my {device.Name}" : "Put these on my device";

    public bool CanApplyStarter => _main is not null && !_starterApplied;

    public string StarterStatus
    {
        get => _starterStatus;
        private set => SetProperty(ref _starterStatus, value);
    }

    private void RefreshStarter()
    {
        StarterPreview = _main?.PreviewStarterLayout(SelectedStarter, StarterTarget) ?? [];
        foreach (var name in new[] { nameof(StarterDescription), nameof(StarterPreview), nameof(HasStarterPreview), nameof(StarterFillsNothing), nameof(StarterNote), nameof(HasStarterNote), nameof(StarterTarget), nameof(ApplyStarterLabel) })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>Puts the chosen starter's actions on the empty controls of the device it fills.</summary>
    [RelayCommand]
    private void ApplyStarter()
    {
        if (_main is null || _starterApplied)
        {
            return;
        }

        var target = StarterTarget;
        var filled = _main.ApplyStarterLayout(SelectedStarter, target);
        _starterApplied = true;
        StarterStatus = filled == 0
            ? "Every control already has an action, so nothing changed."
            : $"Done: {filled} controls{(target is null ? string.Empty : $" of your {target.Name}")} now have an action. Change any of them later.";
        OnPropertyChanged(nameof(CanApplyStarter));
        RefreshStarter();
    }

    public bool CanImport => _main is not null;

    public string ImportStatus
    {
        get => _importStatus;
        private set => SetProperty(ref _importStatus, value);
    }

    /// <summary>Imports a Stream Deck (or DeckHost) profile the view's file picker chose, for the device the starter would fill.</summary>
    public void ImportProfile(string path)
    {
        if (_main is null)
        {
            return;
        }

        try
        {
            _main.SwitchToDeviceForStarter(StarterTarget);
            ImportStatus = _main.ImportProfile(path);
        }
        catch (Exception ex) when (ex is Core.Profiles.ProfileBundleException or System.IO.IOException or System.IO.InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            ImportStatus = $"Couldn't import it: {ex.Message}";
        }

        RefreshStarter();
    }

    // ---------------------------------------------------------------- preferences

    public bool CanStartAtLogin => AppSettings.CanStartAtLogin;

    public bool StartAtLogin
    {
        get => _startAtLogin;
        set
        {
            if (!SetProperty(ref _startAtLogin, value))
            {
                return;
            }

            try
            {
                AppSettings.SetStartAtLogin(value);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            {
                StarterStatus = $"Could not change the start-up entry: {ex.Message}";
            }
        }
    }

    /// <summary>Closing the window keeps DeckHost running in the tray (DeckHost settings has the same switch).</summary>
    public bool KeepRunningInTray
    {
        get => _keepRunningInTray;
        set
        {
            if (!SetProperty(ref _keepRunningInTray, value))
            {
                return;
            }

            try
            {
                AppSettingsViewModel.SaveAndAnnounce(AppSettings.Load() with { CloseToTray = value });
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException)
            {
                StarterStatus = $"Could not save the setting: {ex.Message}";
            }
        }
    }

    /// <summary>The hint under the tray switch (Linux desktops may have no tray).</summary>
    public string TrayHint => OperatingSystem.IsLinux()
        ? "Your device keeps working without the window; the tray icon brings it back. Some Linux desktops (plain GNOME) have no tray: leave this off there."
        : OperatingSystem.IsMacOS()
            ? "Your device keeps working without the window; the menu bar icon brings it back or quits DeckHost."
            : "Your device keeps working without the window; the tray icon brings it back or quits DeckHost.";

    public bool HasUpdates => _updates is not null;

    public bool CheckForUpdates
    {
        get => _updates?.CheckAutomatically ?? AppSettings.Load().CheckForUpdates;
        set
        {
            if (_updates is not null)
            {
                _updates.CheckAutomatically = value;
            }
            else
            {
                AppSettings.Save(AppSettings.Load() with { CheckForUpdates = value });
            }

            OnPropertyChanged();
        }
    }

    /// <summary>The look, as in DeckHost settings (applies at once).</summary>
    public IReadOnlyList<string> Themes { get; } = ["Dark", "Light", "Same as the system"];

    public string Theme
    {
        get => AppSettings.Load().Theme switch
        {
            AppTheme.Light => Themes[1],
            AppTheme.System => Themes[2],
            _ => Themes[0],
        };
        set
        {
            var theme = value == Themes[1] ? AppTheme.Light : value == Themes[2] ? AppTheme.System : AppTheme.Dark;
            AppSettings.Save(AppSettings.Load() with { Theme = theme });
            App.ApplyTheme(theme);
            OnPropertyChanged();
        }
    }

    public bool OfferCrashReports
    {
        get => _offerCrashReports;
        set
        {
            if (SetProperty(ref _offerCrashReports, value))
            {
                AppSettings.Save(AppSettings.Load() with { OfferCrashReports = value });
            }
        }
    }

    // ---------------------------------------------------------------- tips

    [RelayCommand]
    private void OpenDocumentation() => OpenUrl(SupportViewModel.DocumentationUrl);

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CommandsStatus = $"Couldn't open the browser; the page is {url}.";
        }
    }

    // ---------------------------------------------------------------- navigation

    [RelayCommand]
    private void Next()
    {
        if (Step == StepCount - 1)
        {
            Finish();
            return;
        }

        if (Step == 0)
        {
            RefreshDevices();
        }

        Step++;
    }

    [RelayCommand]
    private void Back() => Step--;

    [RelayCommand]
    private void Finish()
    {
        StopWatching();
        AppSettings.Save(AppSettings.Load() with { WelcomeShown = true });
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether the tour should open by itself (the first start).</summary>
    public static bool ShouldShow() => !AppSettings.Load().WelcomeShown;
}

/// <summary>A device on the welcome page.</summary>
public sealed record WelcomeDevice(string Id, string Name, bool IsConnected, bool IsVirtual)
{
    public string State => IsVirtual ? (IsConnected ? "on" : Id.StartsWith("web.", StringComparison.OrdinalIgnoreCase) ? "off: turn it on in Device settings" : "off") : IsConnected ? "connected" : "not connected";

    public string Dot => IsConnected ? "●" : "○";

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => $"{Name}, {State}";
}
