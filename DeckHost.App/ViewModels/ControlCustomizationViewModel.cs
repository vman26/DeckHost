using DeckHost.Contracts.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DeckHost.App.ViewModels;

public partial class ControlCustomizationViewModel : ViewModelBase
{
    private string _displayText;
    private string _imagePath;
    private bool _lightEnabled;
    private string _activeColor;
    private string _idleColor;
    private string _activeDurationText;
    private bool _pulseOnActive;

    public ControlCustomizationViewModel(
        string controlId,
        string displayName,
        string controlType,
        bool supportsAppearance,
        bool supportsLight,
        string displayText,
        string imagePath,
        RgbFeedback? lightFeedback)
    {
        ControlId = controlId;
        DisplayName = displayName;
        ControlType = controlType;
        SupportsAppearance = supportsAppearance;
        SupportsLight = supportsLight;
        _displayText = displayText;
        _imagePath = imagePath;
        _lightEnabled = lightFeedback is not null;

        var feedback = lightFeedback ?? new RgbFeedback();
        _activeColor = "#" + feedback.ActiveColor.TrimStart('#');
        _idleColor = "#" + feedback.IdleColor.TrimStart('#');
        _activeDurationText = feedback.ActiveDuration.ToString(CultureInfo.InvariantCulture);
        _pulseOnActive = feedback.PulseOnActive;
    }

    public string ControlId { get; }

    public string DisplayName { get; }

    public string ControlType { get; }

    public bool SupportsAppearance { get; }

    public bool SupportsLight { get; }

    public bool ShowAppearanceEditor => SupportsAppearance;

    public bool ShowUnsupportedMessage => !SupportsAppearance && !SupportsLight;

    public string DisplayText
    {
        get => _displayText;
        set => SetProperty(ref _displayText, value);
    }

    public string ImagePath
    {
        get => _imagePath;
        set => SetProperty(ref _imagePath, value);
    }

    public bool LightEnabled
    {
        get => _lightEnabled;
        set => SetProperty(ref _lightEnabled, value);
    }

    public string ActiveColor
    {
        get => _activeColor;
        set
        {
            if (SetProperty(ref _activeColor, value))
            {
                OnPropertyChanged(nameof(LightError));
                OnPropertyChanged(nameof(HasLightError));
            }
        }
    }

    public string IdleColor
    {
        get => _idleColor;
        set
        {
            if (SetProperty(ref _idleColor, value))
            {
                OnPropertyChanged(nameof(LightError));
                OnPropertyChanged(nameof(HasLightError));
            }
        }
    }

    public string ActiveDurationText
    {
        get => _activeDurationText;
        set
        {
            if (SetProperty(ref _activeDurationText, value))
            {
                OnPropertyChanged(nameof(LightError));
                OnPropertyChanged(nameof(HasLightError));
            }
        }
    }

    public bool PulseOnActive
    {
        get => _pulseOnActive;
        set => SetProperty(ref _pulseOnActive, value);
    }

    public string? LightError =>
        !IsHexColor(ActiveColor) || !IsHexColor(IdleColor) ? "Colors must look like #RRGGBB, for example #00FF00."
        : !TryParseDuration(out _) ? "Duration must be a whole number of milliseconds from 0 to 10000."
        : null;

    public bool HasLightError => LightError is not null;

    /// <summary>The light settings to save, or null when the light is off or the input is invalid.</summary>
    public RgbFeedback? ToRgbFeedback()
    {
        if (!SupportsLight || !LightEnabled || HasLightError || !TryParseDuration(out var duration))
        {
            return null;
        }

        return new RgbFeedback(ActiveColor.Trim().TrimStart('#').ToUpperInvariant(), IdleColor.Trim().TrimStart('#').ToUpperInvariant(), duration, PulseOnActive);
    }

    private bool TryParseDuration(out int duration) =>
        int.TryParse(ActiveDurationText, NumberStyles.None, CultureInfo.InvariantCulture, out duration) && duration <= 10000;

    private static bool IsHexColor(string value) => HexColorRegex().IsMatch(value.Trim());

    [GeneratedRegex("^#?[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();
}
