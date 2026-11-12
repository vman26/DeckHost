using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.App.ViewModels;

/// <summary>Finds the device plugin that owns a device and offers its settings (vibration, brightness, idle screen, ...).</summary>
public sealed class DeviceSettingsService(IEnumerable<IDevicePlugin> plugins)
{
    private readonly IReadOnlyList<IDevicePlugin> _plugins = plugins.ToList();

    public bool HasSettings(string deviceId) => FindPlugin(deviceId) is not null;

    public DeviceSettingsViewModel? Create(DeviceDefinition device) =>
        FindPlugin(device.Id) is { } plugin ? new DeviceSettingsViewModel(plugin, device) : null;

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

    public DeviceSettingsViewModel(IConfigurableDevicePlugin plugin, DeviceDefinition device)
    {
        _plugin = plugin;
        _device = device;
        Editor = new SettingsEditorViewModel(plugin.GetDeviceSettingDefinitions(device.Id), plugin.GetDeviceSettings(device.Id));
    }

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
