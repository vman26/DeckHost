using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.App.ViewModels;

/// <summary>Finds the device plugin that owns a device and offers its settings (vibration, brightness, idle screen, ...).</summary>
public sealed class DeviceSettingsService(IEnumerable<IDevicePlugin> plugins, Func<IDeviceLightSettings>? lights = null, Core.Firmware.FirmwareService? firmware = null)
{
    private readonly IReadOnlyList<IDevicePlugin> _plugins = plugins.ToList();

    /// <summary>Whether the device has a settings page: settings of its own, or firmware to show.</summary>
    public bool HasSettings(string deviceId) => FindPlugin(deviceId) is not null || firmware?.Supports(deviceId) == true;

    /// <summary>Whether the device's plugin reports firmware at all (the Web Deck doesn't).</summary>
    public bool HasFirmware(string deviceId) => firmware?.Supports(deviceId) == true;

    /// <summary>The firmware the device reports now; null when unplugged or without firmware.</summary>
    public DeviceFirmwareInfo? FirmwareInfo(string deviceId) => firmware?.GetInfo(deviceId);

    public DeviceSettingsViewModel? Create(DeviceDefinition device) =>
        HasSettings(device.Id)
            ? new DeviceSettingsViewModel(
                FindPlugin(device.Id),
                device,
                device.Controls.Any(static control => control.HasLight) && lights is not null ? new KnobLightsViewModel(lights(), device.Id, [.. device.Controls.Where(static control => control.HasLight).Select(static control => control.DisplayName)]) : null,
                firmware?.Supports(device.Id) == true ? new FirmwareViewModel(firmware, device) : null)
            : null;

    private IConfigurableDevicePlugin? FindPlugin(string deviceId) =>
        _plugins.FirstOrDefault(plugin => plugin is IConfigurableDevicePlugin
            && plugin.Devices.Any(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))) as IConfigurableDevicePlugin;
}

/// <summary>The Device Settings window: an editor generated from the device plugin's setting definitions.</summary>
public sealed partial class DeviceSettingsViewModel : ViewModelBase
{
    private readonly IConfigurableDevicePlugin? _plugin;
    private readonly DeviceDefinition _device;
    private string _status = string.Empty;
    private bool _statusIsError;
    private bool _isSaving;

    public DeviceSettingsViewModel(IConfigurableDevicePlugin? plugin, DeviceDefinition device, KnobLightsViewModel? lights = null, FirmwareViewModel? firmware = null)
    {
        _plugin = plugin;
        _device = device;
        Editor = plugin is null
            ? new SettingsEditorViewModel([], ActionSettings.Empty)
            : new SettingsEditorViewModel(plugin.GetDeviceSettingDefinitions(device.Id), plugin.GetDeviceSettings(device.Id));
        Lights = lights;
        Firmware = firmware;
        if (plugin is IDeviceSettingActions actions)
        {
            Actions = [.. actions.GetSettingActions(device.Id).Select(action => new DeviceSettingActionViewModel(this, actions, action))];
        }
    }

    /// <summary>The plugin's buttons (a new pairing code, …).</summary>
    public IReadOnlyList<DeviceSettingActionViewModel> Actions { get; } = [];

    public bool HasActions => Actions.Count > 0;

    /// <summary>After a button: the plugin may have changed values (a new code), so the fields show them again.</summary>
    internal void ReloadEditor()
    {
        if (_plugin is null)
        {
            return;
        }

        Editor = new SettingsEditorViewModel(_plugin.GetDeviceSettingDefinitions(_device.Id), _plugin.GetDeviceSettings(_device.Id));
        OnPropertyChanged(nameof(Editor));
    }

    internal void ShowStatus(string text, bool isError)
    {
        StatusIsError = isError;
        Status = text;
    }

    public string DeviceId => _device.Id;

    /// <summary>The device's firmware (version, updates); null when its plugin doesn't report it.</summary>
    public FirmwareViewModel? Firmware { get; }

    public bool HasFirmware => Firmware is not null;

    /// <summary>The plugin has settings for this device (else only lights and firmware are shown).</summary>
    public bool HasEditor => _plugin is not null && Editor.Fields.Count > 0;

    /// <summary>The default light of the device's LEDs (DeckHost draws it, effects included); null without LEDs.</summary>
    public KnobLightsViewModel? Lights { get; }

    public bool HasLights => Lights is not null;

    public string DeviceName => _device.Name;

    public string ConnectionText => !HasEditor
        ? (_device.IsConnected ? "Connected." : "Not connected.")
        : _device.IsConnected
        ? "Connected. Saving applies the settings right away."
        : "Not connected. The settings are saved and applied when the device is plugged in.";

    public SettingsEditorViewModel Editor { get; private set; }

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

    public bool CanSave => !IsSaving && _plugin is not null;

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
            var error = await Task.Run(() => _plugin?.ApplyDeviceSettings(_device.Id, settings));
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

/// <summary>One of a device plugin's buttons on its settings page, with an optional "are you sure?".</summary>
public sealed partial class DeviceSettingActionViewModel(DeviceSettingsViewModel owner, IDeviceSettingActions plugin, DeviceSettingAction action) : ViewModelBase
{
    private bool _confirming;
    private bool _isRunning;

    public string Label => action.Label;

    public string Description => action.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(action.Description);

    public string ConfirmText => action.Confirm ?? string.Empty;

    /// <summary>Waiting for the second click.</summary>
    public bool Confirming
    {
        get => _confirming;
        private set => SetProperty(ref _confirming, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !_isRunning;

    [RelayCommand]
    private async Task RunAsync()
    {
        if (action.Confirm is not null && !Confirming)
        {
            Confirming = true;
            return;
        }

        Confirming = false;
        IsRunning = true;
        try
        {
            var message = await Task.Run(() => plugin.RunSettingAction(owner.DeviceId, action.Id));
            owner.ReloadEditor();
            owner.ShowStatus(message, isError: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            owner.ShowStatus($"{action.Label} failed: {ex.Message}", isError: true);
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Confirming = false;
}
