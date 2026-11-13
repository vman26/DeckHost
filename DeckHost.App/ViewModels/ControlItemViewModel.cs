using Avalonia.Media;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using System;

namespace DeckHost.App.ViewModels;

public class ControlItemViewModel : ViewModelBase
{
    private const double CompactHeight = 60;

    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#141A26"));

    private ControlBindingViewModel? _assignedBinding;
    private bool _isSelected;

    public ControlItemViewModel(PhysicalControl control)
    {
        Control = control;
        Id = control.Id;
        DisplayName = control.DisplayName;
        Type = control.Type;
        X = control.Bounds.X;
        Y = control.Bounds.Y;
        Width = control.Bounds.Width;
        Height = control.Bounds.Height;
        InputsLabel = CapabilityFormatter.Format(control.Inputs, "None");
        OutputsLabel = CapabilityFormatter.Format(control.Outputs, "None");
        SupportsAppearance = control.Outputs != OutputCapabilities.None || control.HasLinkedDisplay;
    }

    public PhysicalControl Control { get; }

    public string Id { get; }

    public string DisplayName { get; }

    public ControlType Type { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>Whether text and images can be set: the control has a display, or borrows one (a knob and its touch zone).</summary>
    public bool SupportsAppearance { get; }

    public bool SupportsLight => Control.HasLight;

    /// <summary>Every control can be customized: appearance, action settings, Multi Action steps or its LED.</summary>
    public bool SupportsCustomization => true;

    /// <summary>Thin controls (the swipe bar) only show their name.</summary>
    public bool IsCompact => Height < CompactHeight;

    public bool ShowDetails => !IsCompact && !IsKnob;

    /// <summary>A knob shows one line (its action, or its name); keys and zones their name on top.</summary>
    public bool ShowName => !IsKnob;

    public string InputsLabel { get; }

    public string OutputsLabel { get; }

    public string TypeLabel => Type switch
    {
        ControlType.Key => "Key",
        ControlType.Encoder => "Knob",
        ControlType.TouchSurface => "Touch",
        ControlType.Display => "Display",
        ControlType.Slider => "Slider",
        _ => Type.ToString(),
    };

    public ControlBindingViewModel? AssignedBinding
    {
        get => _assignedBinding;
        set
        {
            if (ReferenceEquals(_assignedBinding, value))
            {
                return;
            }

            _assignedBinding = value;
            OnPropertyChanged(nameof(AssignedBinding));
            RefreshComputedState();
        }
    }

    private Avalonia.Media.Imaging.WriteableBitmap? _previewBitmap;
    private bool _isPressed;
    private string _inputIndicator = string.Empty;
    private Avalonia.Threading.DispatcherTimer? _flashTimer;

    /// <summary>The exact image the device shows for this control (null for controls without a display).</summary>
    public Avalonia.Media.Imaging.WriteableBitmap? PreviewBitmap
    {
        get => _previewBitmap;
        private set
        {
            if (SetProperty(ref _previewBitmap, value))
            {
                OnPropertyChanged(nameof(HasPreview));
                OnPropertyChanged(nameof(ShowTextPreview));
            }
        }
    }

    public bool HasPreview => PreviewBitmap is not null;

    /// <summary>Text card for controls without a display (knobs, swipe bar) or before the first frame.</summary>
    public bool ShowTextPreview => !HasPreview && (ShowDetails || IsKnob);

    /// <summary>True while the control is held down on the device (or in test mode).</summary>
    public bool IsPressed
    {
        get => _isPressed;
        private set => SetProperty(ref _isPressed, value);
    }

    /// <summary>Short-lived marker for rotations and swipes, e.g. "◀" or "▶".</summary>
    public string InputIndicator
    {
        get => _inputIndicator;
        private set
        {
            if (SetProperty(ref _inputIndicator, value))
            {
                OnPropertyChanged(nameof(HasInputIndicator));
            }
        }
    }

    public bool HasInputIndicator => InputIndicator.Length > 0;

    public void SetPreview(int width, int height, byte[] rgba)
    {
        var bitmap = PreviewBitmap;
        if (bitmap is null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
        {
            bitmap = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(width, height),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Premul);
        }

        using (var buffer = bitmap.Lock())
        {
            for (var row = 0; row < height; row++)
            {
                System.Runtime.InteropServices.Marshal.Copy(rgba, row * width * 4, buffer.Address + (row * buffer.RowBytes), width * 4);
            }
        }

        // A new instance makes the Image control redraw.
        PreviewBitmap = null;
        PreviewBitmap = bitmap;
    }

    /// <summary>Shows an input on the canvas: held state for press/release, a short flash for the rest.</summary>
    public void ShowInput(Contracts.Enums.InputCapabilities inputEvent, int value)
    {
        switch (inputEvent)
        {
            case Contracts.Enums.InputCapabilities.Press when Type != ControlType.TouchSurface:
                IsPressed = true;
                InputIndicator = string.Empty;
                return;
            case Contracts.Enums.InputCapabilities.Release:
                IsPressed = false;
                return;
            case Contracts.Enums.InputCapabilities.Rotate:
            case Contracts.Enums.InputCapabilities.Swipe:
                Flash(value < 0 ? "◀" : "▶");
                return;
            case Contracts.Enums.InputCapabilities.LongPress:
                Flash("⏱");
                return;
            default:
                // Touch zones only report a tap: flash it.
                Flash(string.Empty);
                return;
        }
    }

    private void Flash(string indicator)
    {
        IsPressed = true;
        InputIndicator = indicator;
        _flashTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _flashTimer.Stop();
        _flashTimer.Tick -= OnFlashEnded;
        _flashTimer.Tick += OnFlashEnded;
        _flashTimer.Start();
    }

    private void OnFlashEnded(object? sender, EventArgs e)
    {
        _flashTimer?.Stop();
        IsPressed = false;
        InputIndicator = string.Empty;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(SurfaceBrush));
                OnPropertyChanged(nameof(OutlineBrush));
                OnPropertyChanged(nameof(OutlineThickness));
            }
        }
    }

    public string AssignmentLabel => AssignedBinding?.DisplayName ?? "Drop action here";

    public bool HasAssignment => AssignedBinding is not null;

    public bool ShowRemoveHint => HasAssignment && ShowDetails;

    public string CapabilitySummary => $"{InputsLabel} / {OutputsLabel}";

    public string PreviewText => !string.IsNullOrWhiteSpace(AssignedBinding?.AppearanceText)
        ? AssignedBinding.AppearanceText
        : AssignedBinding is null ? DisplayName
        : AssignedBinding.IsDisplayContentOnly ? string.Empty
        : AssignedBinding.DisplayName;

    public string PreviewImageLabel => string.IsNullOrWhiteSpace(AssignedBinding?.AppearanceImagePath)
        ? string.Empty
        : System.IO.Path.GetFileName(AssignedBinding.AppearanceImagePath);

    public bool HasPreviewImage => !string.IsNullOrWhiteSpace(AssignedBinding?.AppearanceImagePath);

    public bool ShowPreviewLabel => SupportsAppearance;

    /// <summary>Every control is the same dark surface (keys, the touch strip and knobs alike); the image shows its content.</summary>
    public IBrush SurfaceBrush => DefaultBrush;

    private static readonly IBrush OutlineDefaultBrush = new SolidColorBrush(Color.Parse("#2A3446"));

    /// <summary>A thin rim; the selected control a blue one.</summary>
    public IBrush OutlineBrush => IsSelected ? SelectedBrush : OutlineDefaultBrush;

    public Avalonia.Thickness OutlineThickness => new(IsSelected ? 2.5 : 1.5);

    public string ToolTipText => AssignedBinding is null
        ? $"{DisplayName}: drop an action here. Double-click to customize."
        : $"{DisplayName}: {AssignedBinding.DisplayName}. Double-click to customize, drag to move, Ctrl+drag to copy.";

    /// <summary>Knobs are round, keys and zones rounded rectangles (a CornerRadius, so the binding takes it).</summary>
    public Avalonia.CornerRadius CornerRadius => new(IsKnob ? Width / 2d : Math.Min(14d, Height / 3d));

    public bool IsKnob => Type == ControlType.Encoder;

    private static readonly IBrush LightOffBrush = new SolidColorBrush(Color.Parse("#2A3446"));
    private IBrush _lightBrush = LightOffBrush;
    private bool _isLightOn;

    /// <summary>The LED's current color (the host reports it, effects included), for the ring around a knob.</summary>
    public IBrush LightBrush
    {
        get => _lightBrush;
        private set => SetProperty(ref _lightBrush, value);
    }

    public bool IsLightOn
    {
        get => _isLightOn;
        private set => SetProperty(ref _isLightOn, value);
    }

    public void SetLight(byte red, byte green, byte blue)
    {
        IsLightOn = red > 0 || green > 0 || blue > 0;
        LightBrush = IsLightOn ? new SolidColorBrush(Color.FromRgb(red, green, blue)) : LightOffBrush;
    }


    private void RefreshComputedState()
    {
        OnPropertyChanged(nameof(HasAssignment));
        OnPropertyChanged(nameof(ShowRemoveHint));
        OnPropertyChanged(nameof(AssignmentLabel));
        OnPropertyChanged(nameof(SurfaceBrush));
        OnPropertyChanged(nameof(OutlineBrush));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(PreviewImageLabel));
        OnPropertyChanged(nameof(HasPreviewImage));
        OnPropertyChanged(nameof(ToolTipText));
    }
}
