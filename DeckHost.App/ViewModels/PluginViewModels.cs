using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DeckHost.App.ViewModels;

public class PluginActionGroupViewModel : ViewModelBase
{
    private bool _isExpanded = true;

    public PluginActionGroupViewModel(string pluginId, string pluginName, IEnumerable<ActionItemViewModel> actions)
    {
        PluginId = pluginId;
        PluginName = pluginName;
        foreach (var action in actions)
        {
            Actions.Add(action);
        }
    }

    public string PluginId { get; }

    public string PluginName { get; }

    public ObservableCollection<ActionItemViewModel> Actions { get; } = new();

    public string ActionCountLabel => $"{Actions.Count} action{(Actions.Count == 1 ? string.Empty : "s")}";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

/// <summary>Plugin Manager: lists plugins and devices, installs, uninstalls, enables and disables.</summary>
public partial class PluginManagerViewModel : ViewModelBase
{
    private readonly PluginCatalog _catalog;
    private readonly IDeviceHostService _host;
    private string _statusMessage = string.Empty;
    private bool _statusIsError;

    public PluginManagerViewModel(PluginCatalog catalog, IDeviceHostService host)
    {
        _catalog = catalog;
        _host = host;
        _catalog.Changed += OnCatalogChanged;
        _host.DevicesChanged += OnDevicesChanged;
        Refresh();
    }

    public ObservableCollection<PluginEntryViewModel> SystemPlugins { get; } = [];

    public ObservableCollection<PluginEntryViewModel> InstalledPlugins { get; } = [];

    public ObservableCollection<DeviceInfoViewModel> Devices { get; } = [];

    public bool HasInstalledPlugins => InstalledPlugins.Count > 0;

    public bool HasNoInstalledPlugins => InstalledPlugins.Count == 0;

    public string InstallDirectory => _catalog.InstallDirectory;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public void Install(string packagePath)
    {
        try
        {
            var entry = _catalog.Install(packagePath);
            ShowStatus($"Installed {entry.Name} {entry.Version}. Its actions are in the action list now.", isError: false);
        }
        catch (PluginException ex)
        {
            ShowStatus($"Could not install the plugin: {ex.Message}", isError: true);
        }
    }

    public void Detach()
    {
        _catalog.Changed -= OnCatalogChanged;
        _host.DevicesChanged -= OnDevicesChanged;
    }

    internal void SetEnabled(PluginEntryViewModel plugin, bool enabled)
    {
        try
        {
            _catalog.SetEnabled(plugin.Id, enabled);
            ShowStatus(enabled ? $"Enabled {plugin.Name}." : $"Disabled {plugin.Name}. Controls using it keep their mapping but do nothing until you enable it again.", isError: false);
        }
        catch (PluginException ex)
        {
            ShowStatus(ex.Message, isError: true);
            Refresh();
        }
    }

    internal void Uninstall(PluginEntryViewModel plugin)
    {
        try
        {
            _catalog.Uninstall(plugin.Id);
            ShowStatus($"Uninstalled {plugin.Name}. Controls that used it keep their mapping as unavailable.", isError: false);
        }
        catch (PluginException ex)
        {
            ShowStatus(ex.Message, isError: true);
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }

    private void OnCatalogChanged(object? sender, EventArgs e) => Refresh();

    private void OnDevicesChanged() => Dispatcher.UIThread.Post(RefreshDevices);

    private void Refresh()
    {
        SystemPlugins.Clear();
        InstalledPlugins.Clear();
        foreach (var entry in _catalog.Entries)
        {
            (entry.IsSystem ? SystemPlugins : InstalledPlugins).Add(new PluginEntryViewModel(this, entry));
        }

        OnPropertyChanged(nameof(HasInstalledPlugins));
        OnPropertyChanged(nameof(HasNoInstalledPlugins));
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        Devices.Clear();
        foreach (var device in _host.Devices)
        {
            Devices.Add(new DeviceInfoViewModel(device, _host.ConnectionStatuses.FirstOrDefault(status => status.DeviceId == device.Id)?.Message));
        }
    }
}

public partial class PluginEntryViewModel : ViewModelBase
{
    private readonly PluginManagerViewModel _owner;
    private bool _isEnabled;

    public PluginEntryViewModel(PluginManagerViewModel owner, PluginEntry entry)
    {
        _owner = owner;
        Entry = entry;
        _isEnabled = entry.IsEnabled;
    }

    public PluginEntry Entry { get; }

    public string Id => Entry.Id;

    public string Name => Entry.Name;

    public string Version => $"v{Entry.Version}";

    public string Details => string.Join(" · ", new[]
    {
        Entry.Kind == PluginKind.Device ? "Device plugin" : "Action plugin",
        Entry.Author is null ? null : $"by {Entry.Author}",
        Entry.ActionCount is { } count ? $"{count} action{(count == 1 ? string.Empty : "s")}" : null,
    }.Where(static part => part is not null));

    public string Description => Entry.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Entry.Description);

    public string? Error => Entry.Error;

    public bool HasError => Entry.Error is not null;

    public bool IsSystem => Entry.IsSystem;

    public bool CanToggle => Entry.CanDisable;

    public bool IsLocked => !Entry.CanDisable;

    public string LockedReason => Entry.Kind == PluginKind.Device ? "Always on" : "Required";

    public bool CanUninstall => Entry.CanUninstall;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _owner.SetEnabled(this, value);
            }
        }
    }

    [RelayCommand]
    private void Uninstall() => _owner.Uninstall(this);
}

public sealed class DeviceInfoViewModel(DeviceDefinition device, string? status)
{
    public string Name => device.Name;

    public string State => device.IsConnected ? "Connected" : "Not connected";

    public bool IsConnected => device.IsConnected;

    public string Detail => device.Detail ?? status ?? "Plug in the device to read its serial number and firmware version.";
}
