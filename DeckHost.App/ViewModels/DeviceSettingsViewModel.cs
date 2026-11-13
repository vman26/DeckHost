using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.App.ViewModels;

/// <summary>Finds the device plugin that owns a device and offers its settings (vibration, brightness, idle screen, ...).</summary>
public sealed class DeviceSettingsService(IEnumerable<IDevicePlugin> plugins, Func<IDeviceLightSettings>? lights = null)
{
    private readonly IReadOnlyList<IDevicePlugin> _plugins = plugins.ToList();

    public bool HasSettings(string deviceId) => FindPlugin(deviceId) is not null;

    public DeviceSettingsViewModel? Create(DeviceDefinition device) =>
        FindPlugin(device.Id) is { } plugin
            ? new DeviceSettingsViewModel(plugin, device, device.Controls.Any(static control => control.HasLight) && lights is not null ? new KnobLightsViewModel(lights(), device.Id) : null)
            : null;

    private IConfigurableDevicePlugin? FindPlugin(string deviceId) =>
        _plugins.FirstOrDefault(plugin => plugin is IConfigurableDevicePlugin
            && plugin.Devices.Any(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))) as IConfigurableDevicePlugin;
}

/// <summary>The Device Settings window: an editor generated from the device plugin's setting definitions.</summary>
public sealed partial class DeviceSettingsViewModel : ViewModelBase
{
    private readonly IConfigurableDevicePlugin _plugin;
    private readonly DeviceDefinition _device;
    private string _status = string.Empty;
    private bool _statusIsError;
    private bool _isSaving;

    public DeviceSettingsViewModel(IConfigurableDevicePlugin plugin, DeviceDefinition device, KnobLightsViewModel? lights = null)
    {
        _plugin = plugin;
        _device = device;
        Editor = new SettingsEditorViewModel(plugin.GetDeviceSettingDefinitions(device.Id), plugin.GetDeviceSettings(device.Id));
        Lights = lights;
    }

    /// <summary>The default light of the device's LEDs (DeckHost draws it, effects included); null without LEDs.</summary>
    public KnobLightsViewModel? Lights { get; }

    public bool HasLights => Lights is not null;

    public string DeviceName => _device.Name;

    public string ConnectionText => _device.IsConnected
        ? "Connected. Saving applies the settings right away."
        : "Not connected. The settings are saved and applied when the device is plugged in.";

    public SettingsEditorViewModel Editor { get; }

    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanSave => !IsSaving;

    /// <summary>Saves and applies; returns true on success. Device I/O runs off the UI thread.</summary>
    [RelayCommand]
    public async Task<bool> SaveAsync()
    {
        if (Editor.HasErrors)
        {
            StatusIsError = true;
            Status = "Fix the highlighted values first.";
            return false;
        }

        IsSaving = true;
        StatusIsError = false;
        Status = "Applying…";
        try
        {
            var settings = Editor.ToSettings();
            var error = await Task.Run(() => _plugin.ApplyDeviceSettings(_device.Id, settings));
            StatusIsError = error is not null;
            Status = error ?? (_device.IsConnected ? "Saved and applied." : "Saved. Applied when the device connects.");
            return error is null;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"Could not apply the settings: {ex.Message}";
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }
}


/// <summary>Where the default lights of devices are kept (the main view model: they're saved with the profiles).</summary>
public interface IDeviceLightSettings
{
    LightStyle? GetDeviceLight(string deviceId);

    void SetDeviceLight(string deviceId, LightStyle? style);
}

/// <summary>
/// The Knob lights card: the color and effect every LED of the device shows while idle, in all
/// profiles, unless a control has a light of its own (Customize → Light). Applies right away.
/// </summary>
public sealed partial class KnobLightsViewModel : ViewModelBase
{
    public const string Off = "default-off";

    private readonly IDeviceLightSettings _settings;
    private readonly string _deviceId;
    private string _effect;
    private string _color;
    private double _periodSeconds;

    public KnobLightsViewModel(IDeviceLightSettings settings, string deviceId)
    {
        _settings = settings;
        _deviceId = deviceId;
        var style = settings.GetDeviceLight(deviceId);
        _effect = style?.Effect is { } effect && effect != LightEffects.Off ? effect : Off;
        _color = "#" + (style?.Color ?? "0000FF").TrimStart('#');
        _periodSeconds = style?.PeriodSeconds ?? LightStyle.DefaultPeriodSeconds;
    }

    /// <summary>The idle effects a light can have, for the light editors.</summary>
    public static IReadOnlyList<ActionSettingOption> EffectOptions { get; } =
    [
        new(Off, "Off"),
        new(LightEffects.Solid, "Steady color"),
        new(LightEffects.Breathe, "Breathing"),
        new(LightEffects.Rainbow, "Rainbow"),
    ];

    public IReadOnlyList<ActionSettingOption> Effects => EffectOptions;

    public ActionSettingOption SelectedEffect
    {
        get => EffectOptions.First(option => option.Value == _effect);
        set
        {
            if (value is not null && value.Value != _effect)
            {
                _effect = value.Value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowColor));
                OnPropertyChanged(nameof(ShowSpeed));
                Apply();
            }
        }
    }

    public bool ShowColor => _effect is LightEffects.Solid or LightEffects.Breathe;

    public bool ShowSpeed => LightEffects.IsAnimated(_effect);

    public string Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value ?? string.Empty))
            {
                Apply();
            }
        }
    }

    /// <summary>Seconds per cycle of the effect.</summary>
    public double PeriodSeconds
    {
        get => _periodSeconds;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, LightStyle.MinPeriodSeconds, 10), 1);
            if (SetProperty(ref _periodSeconds, clamped))
            {
                OnPropertyChanged(nameof(PeriodText));
                Apply();
            }
        }
    }

    public string PeriodText => $"{_periodSeconds:0.#} s";

    private void Apply()
    {
        var hex = _color.Trim().TrimStart('#');
        if (_effect != Off && !System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$"))
        {
            return;
        }

        _settings.SetDeviceLight(_deviceId, _effect == Off ? null : new LightStyle(hex.ToUpperInvariant(), _effect, _periodSeconds));
    }
}
