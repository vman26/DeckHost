using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Profiles;
using DeckHost.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DeckHost.App.ViewModels;

/// <summary>
/// After a Stream Deck profile import: the plugins it uses that aren't installed, each with its store entry (by plugin
/// UUID, which is the Elgato archive's and OpenAction's id) and an Install button, or "Not in the store" with a search
/// link. Without the store (offline) the plugins are still listed, by the name or id the profile gives.
/// </summary>
public partial class MissingPluginsViewModel : ViewModelBase
{
    private const string MarketplaceSearchUrl = "https://marketplace.elgato.com/stream-deck/plugins?search=";
    private static readonly Lazy<HttpClient> Http = new(PluginStore.CreateHttpClient);

    private readonly PluginCatalog? _catalog;
    private readonly Func<CancellationToken, Task<(IReadOnlyList<StoreEntry> Entries, IReadOnlyList<string> Errors)>> _loadStore;
    private readonly Func<StoreEntry, IProgress<double>, Task<string>> _download;
    private string _storeStatus = string.Empty;
    private bool _isLoadingStore;
    private int _generation;

    /// <param name="catalog">Where plugins are installed; null lists the plugins without installing (tests, previews).</param>
    /// <param name="loadStore">Reads the store catalogs; DeckHost's store sources when null.</param>
    /// <param name="download">Downloads an entry's package and returns its path; the store's download when null.</param>
    public MissingPluginsViewModel(
        PluginCatalog? catalog,
        Func<CancellationToken, Task<(IReadOnlyList<StoreEntry> Entries, IReadOnlyList<string> Errors)>>? loadStore = null,
        Func<StoreEntry, IProgress<double>, Task<string>>? download = null)
    {
        _catalog = catalog;
        _loadStore = loadStore ?? (token => CreateStore().LoadAsync(token));
        _download = download ?? ((entry, progress) => CreateStore().DownloadAsync(entry, Path.Combine(Path.GetTempPath(), "DeckHost", "downloads"), progress, CancellationToken.None));
    }

    public ObservableCollection<MissingPluginItemViewModel> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public string Title => Items.Count == 1
        ? "This profile uses 1 plugin you don't have"
        : $"This profile uses {Items.Count} plugins you don't have";

    /// <summary>Loading the store, or why it couldn't be read.</summary>
    public string StoreStatus
    {
        get => _storeStatus;
        private set
        {
            if (SetProperty(ref _storeStatus, value))
            {
                OnPropertyChanged(nameof(HasStoreStatus));
            }
        }
    }

    public bool HasStoreStatus => StoreStatus.Length > 0;

    public bool IsLoadingStore
    {
        get => _isLoadingStore;
        private set
        {
            if (SetProperty(ref _isLoadingStore, value))
            {
                RetryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The ids of the installed plugins (none without a catalog).</summary>
    public IEnumerable<string> InstalledPluginIds => _catalog?.Entries.Select(static entry => entry.Id) ?? [];

    /// <summary>Lists <paramref name="plugins"/> (replacing what was shown) and looks them up in the store.</summary>
    public Task ShowAsync(IReadOnlyList<MissingPlugin> plugins)
    {
        _generation++;
        Items.Clear();
        foreach (var plugin in plugins)
        {
            Items.Add(new MissingPluginItemViewModel(this, plugin));
        }

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Title));
        StoreStatus = string.Empty;
        return Items.Count > 0 ? LookUpAsync() : Task.CompletedTask;
    }

    [RelayCommand]
    private void Dismiss()
    {
        _generation++;
        Items.Clear();
        StoreStatus = string.Empty;
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Title));
    }

    private bool CanRetry() => !IsLoadingStore;

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync() => LookUpAsync();

    private async Task LookUpAsync()
    {
        var generation = _generation;
        IsLoadingStore = true;
        StoreStatus = "Looking for them in the plugin store…";
        try
        {
            var (entries, errors) = await Task.Run(() => _loadStore(CancellationToken.None));
            if (generation != _generation)
            {
                return;
            }

            foreach (var item in Items)
            {
                item.SetStoreEntry(FindInStore(item.Plugin, entries));
            }

            StoreStatus = entries.Count == 0 && errors.Count > 0
                ? $"The plugin store couldn't be reached ({errors[0]}). Try again when you're online."
                : string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            if (generation == _generation)
            {
                StoreStatus = $"The plugin store couldn't be reached ({ex.Message}). Try again when you're online.";
            }
        }
        finally
        {
            IsLoadingStore = false;
        }
    }

    /// <summary>
    /// The store's entry for a missing plugin: the one with its id, else the one whose id is the longest prefix of one of its
    /// action UUIDs (<c>com.elgato.discord</c> for <c>com.elgato.discord.voice.mute</c>). One that can be downloaded wins a tie.
    /// </summary>
    internal static StoreEntry? FindInStore(MissingPlugin plugin, IEnumerable<StoreEntry> entries) =>
        entries
            .Where(entry => entry.Id.Length > 0 &&
                            (entry.Id.Equals(plugin.Id, StringComparison.OrdinalIgnoreCase) ||
                             plugin.ActionUuids.Any(action => action.StartsWith(entry.Id + ".", StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(entry => entry.Id.Equals(plugin.Id, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(static entry => entry.Id.Length)
            .ThenByDescending(static entry => entry.DownloadUrl is not null)
            .FirstOrDefault();

    internal async Task InstallAsync(MissingPluginItemViewModel item)
    {
        if (item.Entry is not { } entry || _catalog is null)
        {
            return;
        }

        item.IsInstalling = true;
        item.Progress = "Downloading…";
        string? package = null;
        try
        {
            var progress = new Progress<double>(fraction => item.Progress = fraction < 0 ? "Downloading…" : $"Downloading {fraction:P0}");
            package = await Task.Run(() => _download(entry, progress));
            item.Progress = "Installing…";
            var installed = await Task.Run(() => _catalog.Install(package, entry.Id, null, entry.Source));
            item.MarkInstalled(installed.Error is null
                ? "Installed. Its actions are in the action list now; set the keys up again."
                : $"Installed, but it can't run: {installed.Error}");
        }
        catch (Exception ex) when (ex is PluginException or HttpRequestException or IOException or TaskCanceledException or InvalidDataException or InvalidOperationException)
        {
            item.Progress = $"Could not install: {ex.Message}";
        }
        finally
        {
            item.IsInstalling = false;
            if (package is not null)
            {
                try
                {
                    File.Delete(package);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    internal bool CanInstallPlugins => _catalog is not null;

    internal static void OpenUrl(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https")
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }
    }

    internal static string SearchUrl(string text) => MarketplaceSearchUrl + Uri.EscapeDataString(text);

    private static PluginStore CreateStore() => new(Http.Value) { DeckHostCatalogUrls = PluginStoreViewModel.LoadCatalogUrls() };
}

/// <summary>One plugin an imported profile needs.</summary>
public partial class MissingPluginItemViewModel : ViewModelBase
{
    private readonly MissingPluginsViewModel _owner;
    private StoreEntry? _entry;
    private bool _looked;
    private bool _isInstalling;
    private bool _isInstalled;
    private string _progress = string.Empty;

    public MissingPluginItemViewModel(MissingPluginsViewModel owner, MissingPlugin plugin)
    {
        _owner = owner;
        Plugin = plugin;
    }

    public MissingPlugin Plugin { get; }

    /// <summary>The store's entry, once the store was read and lists it.</summary>
    public StoreEntry? Entry => _entry;

    /// <summary>The store's name, else the profile's, else the plugin id.</summary>
    public string Name => _entry?.Name ?? Plugin.Name ?? Plugin.Id;

    public string Id => Plugin.Id;

    public string Details => (Plugin.ActionUuids.Count == 1 ? "1 action" : $"{Plugin.ActionUuids.Count} actions")
        + (_entry is { } entry ? $" · {entry.Source}" + (entry.Author is { Length: > 0 } author ? $" · by {author}" : string.Empty) : string.Empty);

    public bool IsInStore => _entry is not null;

    /// <summary>The store was read and doesn't list it.</summary>
    public bool IsNotInStore => _looked && _entry is null;

    public string SearchHint => $"Not in the store. Search the Elgato Marketplace for “{Plugin.Name ?? Plugin.Id}”, then install the file with Plugins → Install plugin….";

    /// <summary>A paid plugin: Buy opens its page.</summary>
    public bool IsPaid => _entry is { PurchaseUrl: not null, DownloadUrl: null };

    /// <summary>Made for other systems only (when the store knows).</summary>
    public bool RunsElsewhere => PluginPlatforms.RunsOn(_entry?.Platforms, PluginPlatforms.Current) == false;

    public string ElsewhereText => _entry?.Platforms is { } platforms ? $"Made for {PluginPlatforms.List(platforms)} only" : string.Empty;

    public bool ShowInstall => IsInStore && !IsPaid && !IsInstalled && !RunsElsewhere && _owner.CanInstallPlugins;

    public bool ShowBuy => IsPaid && !IsInstalled;

    public bool IsInstalling
    {
        get => _isInstalling;
        internal set
        {
            if (SetProperty(ref _isInstalling, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        private set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                OnPropertyChanged(nameof(ShowInstall));
                OnPropertyChanged(nameof(ShowBuy));
            }
        }
    }

    public string Progress
    {
        get => _progress;
        internal set
        {
            if (SetProperty(ref _progress, value))
            {
                OnPropertyChanged(nameof(HasProgress));
            }
        }
    }

    public bool HasProgress => Progress.Length > 0;

    internal void SetStoreEntry(StoreEntry? entry)
    {
        _entry = entry;
        _looked = true;
        OnPropertyChanged(string.Empty);
        InstallCommand.NotifyCanExecuteChanged();
    }

    internal void MarkInstalled(string message)
    {
        IsInstalled = true;
        Progress = message;
    }

    private bool CanInstall() => ShowInstall && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync() => _owner.InstallAsync(this);

    [RelayCommand]
    private void Buy() => MissingPluginsViewModel.OpenUrl(_entry?.PurchaseUrl ?? _entry?.StorePageUrl);

    [RelayCommand]
    private void Search() => MissingPluginsViewModel.OpenUrl(MissingPluginsViewModel.SearchUrl(Plugin.Name ?? Plugin.Id));
}
