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
            ? new DeviceSettingsViewModel(plugin, device, device.Controls.Any(static control => control.HasLight) && lights is not null ? new KnobLightsViewModel(lights(), device.Id, [.. device.Controls.Where(static control => control.HasLight).Select(static control => control.DisplayName)]) : null)
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
/// The Knob lights card: the light every LED of the device shows while idle, in all profiles,
/// unless a control has a light of its own (Customize → Light). Applies right away. The editing
/// is <see cref="LightStyleEditorViewModel"/>; Off here means no light at all.
/// </summary>
public sealed partial class KnobLightsViewModel : ViewModelBase
{
    public const string Off = "default-off";

    private readonly IDeviceLightSettings _settings;
    private readonly string _deviceId;

    public KnobLightsViewModel(IDeviceLightSettings settings, string deviceId, IReadOnlyList<string>? ledNames = null)
    {
        _settings = settings;
        _deviceId = deviceId;
        var style = settings.GetDeviceLight(deviceId);
        Editor = new LightStyleEditorViewModel(style, Off);
        Editor.Changed += (_, _) => Apply();

        // Each LED its own light: one editor per LED (Off there is dark, not "the device's light").
        _eachLedOwn = style?.Leds is { Count: > 0 };
        var index = 0;
        foreach (var name in ledNames ?? [])
        {
            var led = new LedLightViewModel(name, new LightStyleEditorViewModel(style?.ForLed(index) is { } own && own != style ? own : style));
            led.Editor.Changed += (_, _) => Apply();
            Leds.Add(led);
            index++;
        }
    }

    public LightStyleEditorViewModel Editor { get; }

    /// <summary>One light editor per LED (when <see cref="EachLedOwn"/> is on).</summary>
    public System.Collections.ObjectModel.ObservableCollection<LedLightViewModel> Leds { get; } = [];

    public bool CanEachLedOwn => Leds.Count > 1;

    private bool _eachLedOwn;

    /// <summary>Each LED its own effect (a composer per LED) instead of one light for all.</summary>
    public bool EachLedOwn
    {
        get => _eachLedOwn;
        set
        {
            if (SetProperty(ref _eachLedOwn, value))
            {
                OnPropertyChanged(nameof(ShowSharedEditor));
                Apply();
            }
        }
    }

    public bool ShowSharedEditor => !_eachLedOwn;

    private void Apply()
    {
        var shared = Editor.ToStyle();
        if (!_eachLedOwn || Leds.Count == 0)
        {
            _settings.SetDeviceLight(_deviceId, shared);
            return;
        }

        _settings.SetDeviceLight(_deviceId, (shared ?? new LightStyle(Effect: LightEffects.Off)) with
        {
            Leds = [.. Leds.Select(static led => led.Editor.ToStyle() ?? new LightStyle(Effect: LightEffects.Off))],
        });
    }

    public bool ShowColor => Editor.ShowColor;

    public bool ShowSpeed => Editor.ShowSpeed;

    public IReadOnlyList<ActionSettingOption> Effects => Editor.Effects;

    public ActionSettingOption SelectedEffect
    {
        get => Editor.SelectedEffect;
        set => Editor.SelectedEffect = value;
    }

    public string Color
    {
        get => Editor.Color;
        set => Editor.Color = value;
    }

    /// <summary>Seconds per cycle of the effect.</summary>
    public double PeriodSeconds
    {
        get => Editor.PeriodSeconds;
        set => Editor.PeriodSeconds = value;
    }
}

/// <summary>One LED's light in the per-LED composer.</summary>
public sealed class LedLightViewModel(string name, LightStyleEditorViewModel editor)
{
    public string Name { get; } = name;

    public LightStyleEditorViewModel Editor { get; } = editor;
}
