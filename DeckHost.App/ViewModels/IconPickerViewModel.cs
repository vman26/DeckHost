using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Core.Icons;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DeckHost.App.ViewModels;

/// <summary>What the user picked: an icon file, to show as the tile's icon or as the whole picture.</summary>
public sealed record IconChoice(string Path, bool AsPicture);

/// <summary>One result in the icon picker: an installed icon, or one found on Iconify.</summary>
public sealed class IconResultViewModel(string name, string source, string? license, bool isSingleColor, Bitmap? preview, IconEntry? installed, IconifyIcon? online)
{
    public string Name { get; } = name;

    /// <summary>The pack or icon set it comes from.</summary>
    public string Source { get; } = source;

    public string? License { get; } = license;

    /// <summary>A single-color icon (tinted on the tile); others are usually whole key pictures.</summary>
    public bool IsSingleColor { get; } = isSingleColor;

    public Bitmap? Preview { get; } = preview;

    public IconEntry? Installed { get; } = installed;

    public IconifyIcon? Online { get; } = online;

    public string ToolTip => License is null ? $"{Name} ({Source})" : $"{Name} ({Source}, {License})";
}

/// <summary>An installed pack in the picker's pack list.</summary>
public sealed class IconPackItemViewModel(IconPack pack)
{
    public IconPack Pack { get; } = pack;

    public string Title => Pack.Name;

    public string Detail =>
        $"{Pack.Icons.Count} icon{(Pack.Icons.Count == 1 ? string.Empty : "s")}" +
        (Pack.Author is null ? string.Empty : $" · {Pack.Author}") +
        (Pack.IsDownloads ? string.Empty : $" · v{Pack.Version}");

    public string License => string.IsNullOrWhiteSpace(Pack.LicenseText) ? "No license file in the pack." : Pack.LicenseText.Trim();
}

/// <summary>
/// The icon picker: search the installed icon packs, or Iconify's open-source icon sets online,
/// and pick an icon for a control. Installs and removes packs too. An online icon is saved into
/// the library (with its set's license) when it's picked, so profiles work offline.
/// </summary>
public sealed partial class IconPickerViewModel : ViewModelBase
{
    private const int PreviewSize = 56;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(350);

    private readonly IconLibrary _library;
    private readonly IconifyClient? _iconify;
    private readonly Dictionary<string, string> _onlineSvgs = new(StringComparer.Ordinal);
    private CancellationTokenSource? _search;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectionText), nameof(SuggestPicture))]
    [NotifyCanExecuteChangedFor(nameof(UseAsIconCommand), nameof(UseAsPictureCommand))]
    private IconResultViewModel? _selected;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    public IconPickerViewModel(IconLibrary library, IconifyClient? iconify)
    {
        _library = library;
        _iconify = iconify;
        RefreshPacks();
        _ = SearchAsync();
    }

    /// <summary>Raised when the user picked an icon; the window closes with it.</summary>
    public event EventHandler<IconChoice>? Picked;

    public ObservableCollection<IconResultViewModel> Results { get; } = [];

    public ObservableCollection<IconPackItemViewModel> Packs { get; } = [];

    public bool CanSearchOnline => _iconify is not null;

    public bool HasSelection => Selected is not null;

    public string SelectionText => Selected is null
        ? "Pick an icon."
        : Selected.License is null ? $"{Selected.Name} · {Selected.Source}" : $"{Selected.Name} · {Selected.Source} · {Selected.License}";

    /// <summary>Colored icons from packs are usually made as the whole key picture.</summary>
    public bool SuggestPicture => Selected is { IsSingleColor: false };

    partial void OnSearchTextChanged(string value) => _ = SearchAsync(SearchDelay);

    partial void OnIsOnlineChanged(bool value) => _ = SearchAsync();

    /// <summary>Installs a pack (.streamDeckIconPack, .zip or a folder) and shows its icons.</summary>
    public async Task InstallAsync(string path)
    {
        IsBusy = true;
        try
        {
            var pack = await Task.Run(() => _library.Install(path));
            RefreshPacks();
            Status = $"Installed {pack.Name} ({pack.Icons.Count} icons).";
            IsOnline = false;
            SearchText = string.Empty;
            await SearchAsync();
        }
        catch (Exception ex) when (ex is IconPackException or IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(IconPackItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await Task.Run(() => _library.Uninstall(item.Pack.Id));
        RefreshPacks();
        Status = $"Removed {item.Pack.Name}. Controls that used its icons show their action's own icon again.";
        await SearchAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task UseAsIconAsync() => PickAsync(asPicture: false);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task UseAsPictureAsync() => PickAsync(asPicture: true);

    private async Task PickAsync(bool asPicture)
    {
        if (Selected is not { } selected)
        {
            return;
        }

        try
        {
            var path = selected.Installed?.FilePath;
            if (path is null && selected.Online is { } online && _onlineSvgs.TryGetValue(online.Id, out var svg))
            {
                path = await Task.Run(() => _library.SaveDownloaded(online, svg));
            }

            if (path is not null)
            {
                Picked?.Invoke(this, new IconChoice(path, asPicture));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't keep the icon: {ex.Message}";
        }
    }

    private void RefreshPacks()
    {
        Packs.Clear();
        foreach (var pack in _library.Packs)
        {
            Packs.Add(new IconPackItemViewModel(pack));
        }
    }

    private async Task SearchAsync(TimeSpan delay = default)
    {
        _search?.Cancel();
        var cancellation = _search = new CancellationTokenSource();
        var token = cancellation.Token;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            var query = SearchText;
            var online = IsOnline;
            IsBusy = true;
            var results = online ? await SearchOnlineAsync(query, token) : await Task.Run(() => SearchInstalled(query), token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            Results.Clear();
            foreach (var result in results)
            {
                Results.Add(result);
            }

            Selected = null;
            Status = results.Count > 0 ? null
                : online ? string.IsNullOrWhiteSpace(query) ? "Type what you're looking for (play, microphone, spotify, ...)." : "Nothing found on Iconify."
                : _library.Packs.Count == 0 ? "No icon packs installed yet. Install one, or search online."
                : "No installed icon matches.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (IconPackException ex)
        {
            Results.Clear();
            Status = ex.Message;
        }
        finally
        {
            if (_search == cancellation)
            {
                IsBusy = false;
            }
        }
    }

    private List<IconResultViewModel> SearchInstalled(string query) =>
        _library.Search(query, limit: 240)
            .Select(icon =>
            {
                var single = icon.FilePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && IsSingleColorFile(icon.FilePath);
                return new IconResultViewModel(icon.Name, icon.PackName, null, single, ToBitmap(IconPreview.FromFile(icon.FilePath, PreviewSize)), icon, null);
            })
            .ToList();

    private async Task<List<IconResultViewModel>> SearchOnlineAsync(string query, CancellationToken token)
    {
        if (_iconify is null || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var icons = await _iconify.SearchAsync(query, 96, token);
        var svgs = await _iconify.GetSvgsAsync(icons, token);
        foreach (var (id, svg) in svgs)
        {
            _onlineSvgs[id] = svg;
        }

        return await Task.Run(() => icons
            .Where(icon => svgs.ContainsKey(icon.Id))
            .Select(icon => new IconResultViewModel(icon.Name, icon.SetName, icon.License, icon.IsMonochrome,
                ToBitmap(IconPreview.FromSvg(svgs[icon.Id], PreviewSize, icon.IsMonochrome)), null, icon))
            .ToList(), token);
    }

    private static bool IsSingleColorFile(string path)
    {
        try
        {
            return File.ReadAllText(path).Contains("currentColor", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static Bitmap? ToBitmap(byte[]? png)
    {
        if (png is null)
        {
            return null;
        }

        using var stream = new MemoryStream(png);
        return new Bitmap(stream);
    }
}
