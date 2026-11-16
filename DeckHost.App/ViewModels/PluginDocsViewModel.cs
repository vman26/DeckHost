using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DeckHost.App.ViewModels;

/// <summary>Builds the documentation page of a plugin (Plugins page, right-click in the editor).</summary>
public sealed class PluginDocsService(PluginCatalog catalog)
{
    /// <summary>The page for a plugin, or null when no plugin with that id is known.</summary>
    public PluginDocsViewModel? Create(string pluginId)
    {
        var entry = catalog.Entries.FirstOrDefault(candidate => string.Equals(candidate.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var plugin = catalog.EnabledApplicationPlugins.FirstOrDefault(candidate => string.Equals(candidate.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        var docs = new PluginDocsViewModel(entry, plugin, PluginStoreViewModel.SharedMedia);
        _ = docs.LoadReadmeAsync();
        return docs;
    }
}

/// <summary>A link on the documentation page.</summary>
public sealed record PluginDocsLink(string Label, string Url);

/// <summary>One of the plugin's actions on its documentation page.</summary>
public sealed record PluginDocsAction(string Name, string Description, string Works, ListIcon Icon);

/// <summary>
/// What there is to know about a plugin: who made it, its links, its actions (from the loaded
/// plugin) and its README (from its folder, else from its GitHub repository).
/// </summary>
public sealed partial class PluginDocsViewModel : ViewModelBase
{
    private readonly PluginEntry _entry;
    private readonly StoreMedia _media;
    private string _readmeStatus = string.Empty;
    private string? _readme;
    private Uri? _imageBase;
    private Uri? _linkBase;

    public PluginDocsViewModel(PluginEntry entry, IApplicationPlugin? plugin, StoreMedia media)
    {
        _entry = entry;
        _media = media;
        Links = BuildLinks(entry);
        Actions = plugin is null
            ? []
            : [.. plugin.Actions
                .Where(static action => action.ShowInPalette)
                .OrderBy(static action => action.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(action =>
                {
                    var item = ActionItemViewModel.Create(plugin.Id, plugin.Name, action);
                    return new PluginDocsAction(action.Name, action.Description ?? string.Empty, $"Uses: {item.InputsLabel}. Shows: {item.OutputsLabel}.", item.Icon);
                })];
    }

    public string Name => _entry.Name;

    public string Subtitle => string.Join(" · ", new[]
    {
        $"v{_entry.Version}",
        _entry.Author is { Length: > 0 } author ? $"by {author}" : null,
        _entry.Kind == PluginKind.Device ? "Device plugin" : _entry.Source == PluginCatalog.StreamDeckSource ? "Stream Deck plugin" : "Action plugin",
        _entry.IsSystem ? "built into DeckHost" : null,
    }.Where(static part => part is not null));

    public string Description => _entry.Description ?? string.Empty;

    public bool HasDescription => Description.Length > 0;

    public ListIcon Icon => ListIcon.From(_entry.IconPath ?? SystemIcon(_entry), 0xFFE2E8F0, Contracts.Icons.Apps);

    public IReadOnlyList<PluginDocsLink> Links { get; }

    public bool HasLinks => Links.Count > 0;

    public IReadOnlyList<PluginDocsAction> Actions { get; }

    public bool HasActions => Actions.Count > 0;

    public string ActionsTitle => Actions.Count == 1 ? "1 action" : $"{Actions.Count} actions";

    public string? ReadmeMarkdown
    {
        get => _readme;
        private set
        {
            if (SetProperty(ref _readme, value))
            {
                OnPropertyChanged(nameof(HasReadme));
            }
        }
    }

    public bool HasReadme => !string.IsNullOrWhiteSpace(_readme);

    public Uri? ReadmeImageBase
    {
        get => _imageBase;
        private set => SetProperty(ref _imageBase, value);
    }

    public Uri? ReadmeLinkBase
    {
        get => _linkBase;
        private set => SetProperty(ref _linkBase, value);
    }

    public string ReadmeStatus
    {
        get => _readmeStatus;
        private set
        {
            if (SetProperty(ref _readmeStatus, value))
            {
                OnPropertyChanged(nameof(HasReadmeStatus));
            }
        }
    }

    public bool HasReadmeStatus => ReadmeStatus.Length > 0;

    public Func<string, Task<byte[]?>> ImageLoader => url => _media.GetImageAsync(url);

    /// <summary>Reads the README: the plugin's own file, else its GitHub repository's.</summary>
    public async Task LoadReadmeAsync()
    {
        if (_entry.ReadmePath is { } path)
        {
            try
            {
                var directory = Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar;
                ReadmeImageBase = new Uri(directory);
                ReadmeLinkBase = new Uri(directory);
                ReadmeMarkdown = await File.ReadAllTextAsync(path).ConfigureAwait(true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ReadmeStatus = $"Could not read the README: {ex.Message}";
            }
        }

        var repository = RepositoryOf(_entry);
        if (repository is null)
        {
            ReadmeStatus = _entry.IsSystem || HasActions ? string.Empty : "This plugin has no README.";
            return;
        }

        ReadmeStatus = "Loading the README…";
        try
        {
            var readme = await Task.Run(() => _media.GetReadmeAsync(new StoreEntry(_entry.Id, _entry.Name, _entry.Author, _entry.Description, string.Empty, _entry.Version, null, repository, null))).ConfigureAwait(true);
            if (readme is null)
            {
                ReadmeStatus = "The plugin's repository has no README.";
                return;
            }

            ReadmeImageBase = readme.ImageBase;
            ReadmeLinkBase = readme.LinkBase;
            ReadmeMarkdown = readme.Markdown;
            ReadmeStatus = string.Empty;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException)
        {
            ReadmeStatus = $"Could not load the README: {ex.Message}";
        }
    }

    [RelayCommand]
    private static void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file")
        {
            Process.Start(new ProcessStartInfo(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>Where the plugin's documentation is: its website, its source, DeckHost's docs for its own plugins.</summary>
    internal static IReadOnlyList<PluginDocsLink> BuildLinks(PluginEntry entry)
    {
        var links = new List<PluginDocsLink>();
        if (entry.IsSystem)
        {
            links.Add(new PluginDocsLink("DeckHost documentation", SupportViewModel.DocumentationUrl + (entry.Kind == PluginKind.Device ? "devices/" : "reference/actions/")));
        }

        if (IsWebLink(entry.Url))
        {
            links.Add(new PluginDocsLink("Website", entry.Url!));
        }

        if (RepositoryOf(entry) is { } repository && !string.Equals(repository, entry.Url, StringComparison.OrdinalIgnoreCase))
        {
            links.Add(new PluginDocsLink("Source code", repository));
        }

        if (entry.InstallDirectory is { } folder && Directory.Exists(folder))
        {
            links.Add(new PluginDocsLink("Plugin folder", new Uri(folder + Path.DirectorySeparatorChar).AbsoluteUri));
        }

        return links;
    }

    /// <summary>The plugin's GitHub repository: plugin.json's repository, else a GitHub website.</summary>
    internal static string? RepositoryOf(PluginEntry entry) =>
        IsWebLink(entry.Repository) ? entry.Repository
        : IsWebLink(entry.Url) && GitHubLocation.Parse(entry.Url) is not null ? entry.Url
        : null;

    private static bool IsWebLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string? SystemIcon(PluginEntry entry) => entry.Kind == PluginKind.Device
        ? Contracts.Icons.Chip
        : entry.Id switch
        {
            SystemPlugins.SystemPluginSet.BuiltInPluginId => Contracts.Icons.Apps,
            SystemPlugins.SystemPluginSet.MediaPluginId => Contracts.Icons.PlayPause,
            SystemPlugins.SystemPluginSet.SystemControlsPluginId => Contracts.Icons.Keyboard,
            _ => null,
        };
}
