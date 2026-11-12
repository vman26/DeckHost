using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DeckHost.App.ViewModels;

/// <summary>
/// One overview of DeckHost plugins (catalogs, installed packages, system plugins), the OpenAction
/// catalog and the community archive of Elgato plugins, with filters. Downloads and installs run
/// in the background and report progress per plugin.
/// </summary>
public partial class PluginStoreViewModel : ViewModelBase
{
    public const string AllSources = "All sources";
    public const string AllCategories = "All categories";
    public const string AllStatuses = "All";
    public const string InstalledStatus = "Installed";
    public const string NotInstalledStatus = "Not installed";
    public const string Uncategorized = "Uncategorized";

    private static readonly HttpClient Http = PluginStore.CreateHttpClient();
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "store.json");

    private readonly PluginCatalog _catalog;
    private readonly PluginStore _store = new(Http);
    private readonly List<StoreItemViewModel> _all = [];
    private string _searchText = string.Empty;
    private string _selectedSource = AllSources;
    private string _selectedCategory = AllCategories;
    private string _selectedStatus = AllStatuses;
    private bool _isLoading;
    private bool _loaded;
    private string _summary = "Open this tab to load the catalogs.";
    private string _catalogUrlsText;

    public PluginStoreViewModel(PluginCatalog catalog)
    {
        _catalog = catalog;
        _store.DeckHostCatalogUrls = LoadCatalogUrls();
        _catalogUrlsText = string.Join(Environment.NewLine, _store.DeckHostCatalogUrls);
    }

    /// <summary>Reports install results to the Plugin Manager's status line.</summary>
    public event Action<string, bool>? StatusReported;

    public ObservableCollection<StoreItemViewModel> Items { get; } = [];

    public IReadOnlyList<string> Sources { get; } =
        [AllSources, PluginStore.DeckHostSource, PluginStore.OpenActionSource, PluginStore.ElgatoArchiveSource];

    public ObservableCollection<string> Categories { get; } = [AllCategories];

    public IReadOnlyList<string> Statuses { get; } = [AllStatuses, InstalledStatus, NotInstalledStatus];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetProperty(ref _selectedSource, value ?? AllSources))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value ?? AllCategories))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetProperty(ref _selectedStatus, value ?? AllStatuses))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>DeckHost catalog URLs, one per line.</summary>
    public string CatalogUrlsText
    {
        get => _catalogUrlsText;
        set => SetProperty(ref _catalogUrlsText, value);
    }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded)
        {
            await RefreshAsync();
        }
    }

    /// <summary>Updates the Installed badges after something was installed or removed elsewhere.</summary>
    public void RefreshInstalledState()
    {
        if (!_loaded)
        {
            return;
        }

        foreach (var item in _all)
        {
            item.IsInstalled = _catalog.IsInstalled(item.Entry.Id);
        }

        MergeInstalledPlugins();
        RefreshCategories();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task SaveCatalogUrlsAsync()
    {
        var urls = CatalogUrlsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static url => url.Trim('"'))
            .Where(static url => (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file") || Path.IsPathFullyQualified(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _store.DeckHostCatalogUrls = urls;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(new StoreSettings(urls), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException ex)
        {
            StatusReported?.Invoke($"Could not save the catalog list: {ex.Message}", true);
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        Summary = "Loading the plugin catalogs…";
        try
        {
            var (entries, errors) = await Task.Run(() => _store.LoadAsync(CancellationToken.None));
            _all.Clear();
            foreach (var entry in entries.OrderBy(static entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                _all.Add(new StoreItemViewModel(this, entry, _catalog.IsInstalled(entry.Id)));
            }

            // Plugins installed before DeckHost recorded where they came from get their store source.
            var origins = _all
                .Where(static item => item.Entry.Source is PluginStore.OpenActionSource or PluginStore.ElgatoArchiveSource)
                .Select(static item => (item.Entry.Id, item.Entry.Source))
                .ToList();
            await Task.Run(() => _catalog.RecordUnknownOrigins(origins));

            MergeInstalledPlugins();
            RefreshCategories();
            _loaded = true;
            ApplyFilter();

            var deckHostCount = _all.Count(static item => item.Entry.Source.StartsWith(PluginStore.DeckHostSource, StringComparison.Ordinal));
            Summary = $"{_all.Count} plugins ({deckHostCount} DeckHost)."
                + (_store.DeckHostCatalogUrls.Count == 0 ? " No DeckHost catalog is set up yet: add its address under Sources." : string.Empty)
                + (errors.Count == 0 ? string.Empty : $" Could not load: {string.Join("; ", errors)}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal async Task InstallAsync(StoreItemViewModel item)
    {
        item.IsInstalling = true;
        item.Progress = "Downloading…";
        var downloadDirectory = Path.Combine(Path.GetTempPath(), "DeckHost", "downloads");
        string? package = null;

        // Progress callbacks arrive on background threads; marshal them to the UI.
        var downloadProgress = new Progress<double>(fraction => item.Progress = fraction < 0 ? "Downloading…" : $"Downloading {fraction:P0}");
        var stepProgress = new Progress<string>(step => item.Progress = step);
        try
        {
            package = await Task.Run(() => _store.DownloadAsync(item.Entry, downloadDirectory, downloadProgress, CancellationToken.None));
            var installed = await Task.Run(() => _catalog.Install(package, item.Entry.Id, stepProgress, item.Entry.Source));
            item.IsInstalled = true;
            StatusReported?.Invoke(
                installed.Error is null
                    ? $"Installed {installed.Name}. Its actions are in the action list now."
                    : $"Installed {installed.Name}, but it can't run: {installed.Error}",
                installed.Error is not null);
        }
        catch (Exception ex) when (ex is PluginException or HttpRequestException or IOException or TaskCanceledException or InvalidDataException)
        {
            StatusReported?.Invoke($"Could not install {item.Entry.Name}: {ex.Message}", true);
        }
        finally
        {
            item.IsInstalling = false;
            item.Progress = string.Empty;
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

    private static List<string> LoadCatalogUrls()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<StoreSettings>(File.ReadAllText(SettingsPath))?.CatalogUrls?.ToList() ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Installed plugins that no catalog lists (system plugins, DeckHost packages, files).</summary>
    private void MergeInstalledPlugins()
    {
        foreach (var installed in _catalog.Entries.Where(static entry => entry.Kind == PluginKind.Application))
        {
            var existing = _all.FirstOrDefault(item => string.Equals(item.Entry.Id, installed.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                // The installed manifest knows the category even when the catalog doesn't.
                existing.InstalledCategory = installed.Category;
                continue;
            }

            var source = installed.Source == PluginCatalog.StreamDeckSource ? $"Stream Deck ({installed.Origin})" : PluginStore.DeckHostSource;
            _all.Add(new StoreItemViewModel(
                this,
                new StoreEntry(installed.Id, installed.Name, installed.Author, installed.Description, source, installed.Version, null, null, null,
                    PluginCategories.Resolve(installed.Category, installed.Name, installed.Description, installed.Id)),
                isInstalled: true));
        }
    }

    private void RefreshCategories()
    {
        var categories = _all.Select(static item => item.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        var selected = SelectedCategory;
        Categories.Clear();
        Categories.Add(AllCategories);
        foreach (var category in categories)
        {
            Categories.Add(category);
        }

        _selectedCategory = Categories.Contains(selected) ? selected : AllCategories;
        OnPropertyChanged(nameof(SelectedCategory));
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        Items.Clear();
        foreach (var item in _all
                     .Where(item => SelectedSource == AllSources || item.Entry.Source.StartsWith(SelectedSource, StringComparison.Ordinal))
                     .Where(item => SelectedCategory == AllCategories || string.Equals(item.Category, SelectedCategory, StringComparison.CurrentCultureIgnoreCase))
                     .Where(item => SelectedStatus == AllStatuses || (SelectedStatus == InstalledStatus) == item.IsInstalled)
                     .Where(item => search.Length == 0 ||
                                    item.Entry.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                    (item.Entry.Author?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                                    (item.Entry.Description?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                                    item.Entry.Id.Contains(search, StringComparison.OrdinalIgnoreCase))
                     .Take(500))
        {
            Items.Add(item);
        }
    }

    private sealed record StoreSettings(IReadOnlyList<string>? CatalogUrls);
}

public partial class StoreItemViewModel : ViewModelBase
{
    private readonly PluginStoreViewModel _owner;
    private bool _isInstalled;
    private bool _isInstalling;
    private string _progress = string.Empty;
    private string? _installedCategory;

    public StoreItemViewModel(PluginStoreViewModel owner, StoreEntry entry, bool isInstalled)
    {
        _owner = owner;
        Entry = entry;
        _isInstalled = isInstalled;
    }

    public StoreEntry Entry { get; }

    public string Name => Entry.Name;

    public string Initials => string.Concat(Entry.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(static word => char.ToUpperInvariant(word[0])));

    /// <summary>The installed plugin's own category (its manifest) wins over the one derived for the store.</summary>
    public string Category => _installedCategory ?? Entry.Category ?? PluginCategories.Other;

    public string? InstalledCategory
    {
        get => _installedCategory;
        set
        {
            _installedCategory = value;
            OnPropertyChanged(nameof(Category));
        }
    }

    public string Details => string.Join(" · ", new[]
    {
        Entry.Author is null ? null : $"by {Entry.Author}",
        Entry.Version is null ? null : $"v{Entry.Version}",
        Category,
        Entry.Id,
    }.Where(static part => part is not null));

    public string Description => Entry.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Entry.Description);

    public string Source => Entry.Source;

    public string SourceColor => Entry.Source switch
    {
        PluginStore.OpenActionSource => "#14532D",
        PluginStore.ElgatoArchiveSource => "#1E3A8A",
        _ when Entry.Source.StartsWith("Stream Deck", StringComparison.Ordinal) => "#7C2D12",
        _ => "#4C1D95",
    };

    public bool HasPage => Entry.StorePageUrl is not null;

    public string PageToolTip => Entry.StorePageUrl ?? string.Empty;

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        set
        {
            if (SetProperty(ref _isInstalling, value))
            {
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    /// <summary>"Downloading 42%", "Unpacking…", "Starting …" while installing.</summary>
    public string Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, value))
            {
                OnPropertyChanged(nameof(HasProgress));
            }
        }
    }

    public bool HasProgress => Progress.Length > 0;

    public bool CanInstall => !IsInstalled && !IsInstalling && (Entry.DownloadUrl is not null || Entry.RepositoryUrl is not null);

    [RelayCommand]
    private Task Install() => _owner.InstallAsync(this);

    [RelayCommand]
    private void OpenPage()
    {
        if (Uri.TryCreate(Entry.StorePageUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }
}
