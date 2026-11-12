using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

    private readonly DeviceSettingsService? _deviceSettings;

    public PluginManagerViewModel(PluginCatalog catalog, IDeviceHostService host, DeviceSettingsService? deviceSettings = null)
    {
        _catalog = catalog;
        _host = host;
        _deviceSettings = deviceSettings;
        _catalog.Changed += OnCatalogChanged;
        _host.DevicesChanged += OnDevicesChanged;
        Store = new PluginStoreViewModel(catalog);
        Store.StatusReported += ShowStatus;
        Refresh();
    }

    public PluginStoreViewModel Store { get; }

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

    public string StreamDeckDirectory => _catalog.StreamDeckDirectory;

    public string LogDirectory => Platform.Logging.LogDirectory;

    public DeviceSettingsViewModel? CreateDeviceSettings(DeviceInfoViewModel device) => _deviceSettings?.Create(device.Device);

    public async Task InstallAsync(string packagePath)
    {
        await RunBusyAsync(
            $"Installing {Path.GetFileName(packagePath)}…",
            progress => _catalog.Install(packagePath, progress: progress, origin: "File"),
            entry => entry.Error is null
                ? $"Installed {entry.Name} {entry.Version}. Its actions are in the action list now."
                : $"Installed {entry.Name}, but it can't run: {entry.Error}");
    }

    public void Detach()
    {
        _catalog.Changed -= OnCatalogChanged;
        _host.DevicesChanged -= OnDevicesChanged;
    }

    [RelayCommand]
    private void OpenPluginFolder() => OpenFolder(Path.GetDirectoryName(_catalog.InstallDirectory)!);

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(LogDirectory);

    internal async Task SetEnabledAsync(PluginEntryViewModel plugin, bool enabled)
    {
        plugin.BusyText = enabled ? "Enabling…" : "Disabling…";
        var ok = await RunBusyAsync<object?>(
            plugin.BusyText,
            progress =>
            {
                _catalog.SetEnabled(plugin.Id, enabled, progress);
                return null;
            },
            _ => enabled ? $"Enabled {plugin.Name}." : $"Disabled {plugin.Name}. Controls using it keep their mapping but do nothing until you enable it again.",
            plugin);
        if (!ok)
        {
            Refresh();
        }
    }

    internal async Task UninstallAsync(PluginEntryViewModel plugin)
    {
        await RunBusyAsync<object?>(
            $"Uninstalling {plugin.Name}…",
            progress =>
            {
                _catalog.Uninstall(plugin.Id, progress);
                return null;
            },
            _ => $"Uninstalled {plugin.Name}. Controls that used it keep their mapping as unavailable.",
            plugin);
    }

    /// <summary>
    /// Runs slow catalog work on a background thread (so the window keeps responding) while the
    /// status line shows each step, e.g. "Stopping Discord…", "Removing files…".
    /// </summary>
    private async Task<bool> RunBusyAsync<T>(string startText, Func<IProgress<string>, T> work, Func<T, string> doneText, PluginEntryViewModel? plugin = null)
    {
        IsBusy = true;
        ShowStatus(startText, isError: false);
        if (plugin is not null)
        {
            plugin.BusyText = startText;
        }

        var progress = new Progress<string>(step =>
        {
            ShowStatus(step, isError: false);
            if (plugin is not null)
            {
                plugin.BusyText = step;
            }
        });

        try
        {
            var result = await Task.Run(() => work(progress));
            ShowStatus(doneText(result), isError: false);
            return true;
        }
        catch (PluginException ex)
        {
            ShowStatus(ex.Message, isError: true);
            return false;
        }
        finally
        {
            IsBusy = false;
            if (plugin is not null)
            {
                plugin.BusyText = string.Empty;
            }
        }
    }

    private bool _isBusy;

    /// <summary>True while an install, uninstall or enable/disable is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }

    // The catalog raises Changed on whatever thread did the work.
    private void OnCatalogChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        Refresh();
        Store.RefreshInstalledState();
    });

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
            Devices.Add(new DeviceInfoViewModel(
                device,
                _host.ConnectionStatuses.FirstOrDefault(status => status.DeviceId == device.Id)?.Message,
                _deviceSettings?.HasSettings(device.Id) == true));
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
        Entry.Kind == PluginKind.Device ? "Device plugin" : Entry.Source == PluginCatalog.StreamDeckSource ? "Stream Deck plugin" : "Action plugin",
        Entry.Author is null ? null : $"by {Entry.Author}",
        Entry.ActionCount is { } count ? $"{count} action{(count == 1 ? string.Empty : "s")}" : null,
        Entry.Kind == PluginKind.Device ? null : PluginCategories.Resolve(Entry.Category, Entry.Name, Entry.Description, Entry.Id),
    }.Where(static part => part is not null));

    /// <summary>Where it came from: a store source, "File" or "Built into DeckHost".</summary>
    public string Origin => Entry.Kind == PluginKind.Device ? "Built into DeckHost" : Entry.Origin ?? "File";

    public string OriginColor => Origin switch
    {
        Core.Plugins.PluginStore.OpenActionSource => "#14532D",
        Core.Plugins.PluginStore.ElgatoArchiveSource => "#1E3A8A",
        "Built into DeckHost" => "#334155",
        _ => "#4C1D95",
    };

    public string? InstallDirectory => Entry.InstallDirectory;

    public bool HasInstallDirectory => Entry.InstallDirectory is not null;

    private string _busyText = string.Empty;

    /// <summary>Current step while this plugin is being enabled, disabled or uninstalled.</summary>
    public string BusyText
    {
        get => _busyText;
        set
        {
            if (SetProperty(ref _busyText, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsBusy => BusyText.Length > 0;

    public bool IsIdle => !IsBusy;

    [RelayCommand]
    private void OpenFolder()
    {
        if (Entry.InstallDirectory is { } directory && System.IO.Directory.Exists(directory))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true })?.Dispose();
        }
    }

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
                _ = _owner.SetEnabledAsync(this, value);
            }
        }
    }

    [RelayCommand]
    private Task Uninstall() => _owner.UninstallAsync(this);
}

public sealed class DeviceInfoViewModel(DeviceDefinition device, string? status, bool hasSettings = false)
{
    public DeviceDefinition Device => device;

    /// <summary>The device plugin offers settings (vibration, brightness, idle screen, ...).</summary>
    public bool HasSettings => hasSettings;

    public string Id => device.Id;

    public string Name => device.Name;

    public string State => device.IsConnected ? "Connected" : "Not connected";

    public bool IsConnected => device.IsConnected;

    public string Detail => device.Detail ?? status ?? "Plug in the device to read its serial number and firmware version.";
}
