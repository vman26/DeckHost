using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Platform;
using DeckHost.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace DeckHost.App.ViewModels;

/// <summary>
/// Updates for installed plugins (DeckHost, Stream Deck and OpenAction plugins from the store
/// catalogs): checked a minute after start and every few hours, installed one by one or all at
/// once, or by themselves when <see cref="InstallAutomatically"/> is on. One for the app.
/// </summary>
public sealed partial class PluginUpdatesViewModel : ViewModelBase
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly Func<Task<IReadOnlyList<PluginUpdate>>> _check;
    private readonly Func<PluginUpdate, IProgress<string>, Task<PluginEntry>> _install;
    private readonly Func<AppSettingsData> _loadSettings;
    private readonly Action<AppSettingsData> _saveSettings;
    private DispatcherTimer? _timer;
    private bool _isBusy;
    private string _status = string.Empty;
    private bool _installAutomatically;

    public PluginUpdatesViewModel(PluginUpdater updater, Func<AppSettingsData> loadSettings, Action<AppSettingsData> saveSettings)
        : this(() => updater.CheckAsync(), (update, progress) => updater.InstallAsync(update, progress), loadSettings, saveSettings)
    {
    }

    public PluginUpdatesViewModel(
        Func<Task<IReadOnlyList<PluginUpdate>>> check,
        Func<PluginUpdate, IProgress<string>, Task<PluginEntry>> install,
        Func<AppSettingsData> loadSettings,
        Action<AppSettingsData> saveSettings)
    {
        _check = check;
        _install = install;
        _loadSettings = loadSettings;
        _saveSettings = saveSettings;
        _installAutomatically = loadSettings().AutoUpdatePlugins;
    }

    /// <summary>The updates found by the last check (minus the ones installed since).</summary>
    public ObservableCollection<PluginUpdate> Available { get; } = [];

    public bool HasUpdates => Available.Count > 0;

    public string UpdateAllLabel => Available.Count == 1 ? "Update 1 plugin" : $"Update {Available.Count} plugins";

    /// <summary>Raised on the UI thread when <see cref="Available"/> changed.</summary>
    public event EventHandler? Changed;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !_isBusy;

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

    /// <summary>Install plugin updates by themselves (each one can still be rolled back on the Plugins page).</summary>
    public bool InstallAutomatically
    {
        get => _installAutomatically;
        set
        {
            if (SetProperty(ref _installAutomatically, value))
            {
                try
                {
                    _saveSettings(_loadSettings() with { AutoUpdatePlugins = value });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Status = $"Could not save the setting: {ex.Message}";
                }
            }
        }
    }

    /// <summary>The update for a plugin, if the last check found one.</summary>
    public PluginUpdate? For(string pluginId) =>
        Available.FirstOrDefault(update => string.Equals(update.Id, pluginId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Checks a minute after start, then every <see cref="CheckInterval"/>; installs when switched on.</summary>
    public void StartAutomaticChecks()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await CheckAsync(manual: false);
            if (InstallAutomatically && HasUpdates)
            {
                await UpdateAllAsync();
            }
        };
        _timer.Start();
    }

    [RelayCommand]
    private Task Check() => CheckAsync(manual: true);

    /// <summary>Asks the catalogs. A manual check reports every outcome; an automatic one stays quiet on errors.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        if (manual)
        {
            Status = "Checking the store for plugin updates…";
        }

        try
        {
            var updates = await Task.Run(_check);
            Replace(updates);
            if (manual || updates.Count > 0)
            {
                Status = updates.Count switch
                {
                    0 => "Your plugins are up to date.",
                    1 => $"An update for {updates[0].Name} is available.",
                    _ => $"Updates for {updates.Count} plugins are available.",
                };
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            if (manual)
            {
                Status = $"Could not check for plugin updates: {ex.Message}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task Update(PluginUpdate update) => InstallAsync([update]);

    [RelayCommand]
    private Task UpdateAll() => UpdateAllAsync();

    public Task UpdateAllAsync() => InstallAsync([.. Available]);

    private async Task InstallAsync(IReadOnlyList<PluginUpdate> updates)
    {
        if (IsBusy || updates.Count == 0)
        {
            return;
        }

        IsBusy = true;
        var failed = new List<string>();
        var installed = 0;
        var progress = new Progress<string>(step => Status = step);
        foreach (var update in updates)
        {
            try
            {
                Status = $"Updating {update.Name} to {update.ToVersion}…";
                var entry = await Task.Run(() => _install(update, progress));
                Available.Remove(update);
                installed++;
                if (entry.Error is not null)
                {
                    failed.Add($"{update.Name}: {entry.Error}");
                }
            }
            catch (Exception ex) when (ex is PluginException or HttpRequestException or IOException or TaskCanceledException or InvalidDataException or InvalidOperationException)
            {
                failed.Add($"{update.Name}: {ex.Message}");
            }
        }

        IsBusy = false;
        Notify();
        Status = failed.Count == 0
            ? installed == 1 ? $"Updated {updates[0].Name} to {updates[0].ToVersion}." : $"Updated {installed} plugins."
            : $"Not everything was updated. {string.Join(" ", failed)}";
    }

    private void Replace(IReadOnlyList<PluginUpdate> updates)
    {
        Available.Clear();
        foreach (var update in updates)
        {
            Available.Add(update);
        }

        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(UpdateAllLabel));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
