using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace DeckHost.App.Platform;

/// <summary>How this copy of DeckHost gets a new version.</summary>
public enum UpdateMode
{
    /// <summary>Installed by Velopack (Windows setup or MSI, the macOS app, the AppImage): downloads with a percentage, swaps on restart.</summary>
    Velopack,

    /// <summary>Installed by DeckHost's older Windows setup: moves to the Velopack install once (<see cref="UpdateInstaller"/>).</summary>
    MigrateWindows,

    /// <summary>Anything else (the Linux tar.gz, a development build): the package is downloaded and shown.</summary>
    ManualDownload,
}

/// <summary>Velopack, behind an interface so <see cref="ViewModels.UpdateViewModel"/> can be tested.</summary>
public interface IAppUpdater
{
    UpdateMode Mode { get; }

    /// <summary>Downloads the newest version (a delta when it can), reporting 0–100. Returns its version, or null when the feed has nothing newer.</summary>
    Task<string?> DownloadAsync(bool includePrereleases, IProgress<int> progress, CancellationToken cancellationToken);

    /// <summary>A downloaded version waits to be applied.</summary>
    bool HasDownloaded { get; }

    /// <summary>
    /// Starts Velopack's updater, which waits for DeckHost to exit, swaps in the downloaded version
    /// and starts it (<paramref name="restart"/>) or not. DeckHost exits normally right after.
    /// </summary>
    void ApplyAfterExit(bool restart);
}

/// <summary>
/// DeckHost's updates through Velopack: the release feed is the GitHub repository the build names
/// (<see cref="AppInfo.UpdateRepository"/>), or <c>DECKHOST_UPDATE_FEED</c> (a folder or URL, for testing).
/// Velopack never touches the running install: the whole version is swapped after DeckHost exits.
/// </summary>
internal sealed class AppUpdater : IAppUpdater
{
    /// <summary>A folder or URL that replaces the GitHub releases (to test updates locally).</summary>
    public const string FeedVariable = "DECKHOST_UPDATE_FEED";

    private UpdateManager? _manager;
    private UpdateInfo? _downloaded;

    public AppUpdater()
    {
        Mode = Decide(IsVelopackInstall, OperatingSystem.IsWindows(), File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe")));
    }

    public UpdateMode Mode { get; }

    public bool HasDownloaded => _downloaded is not null;

    /// <summary>Whether Velopack installed this copy (it can then update it).</summary>
    public static bool IsVelopackInstall
    {
        get
        {
            try
            {
                return VelopackLocator.Current.CurrentlyInstalledVersion is not null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// The program to start at sign-in: on Windows Velopack's stub next to <c>current\</c>, whose path
    /// survives updates; elsewhere null (the caller uses the AppImage file or the program itself).
    /// </summary>
    public static string? StableLauncher
    {
        get
        {
            if (!OperatingSystem.IsWindows() || !IsVelopackInstall || Environment.ProcessPath is not { } process)
            {
                return null;
            }

            var stub = Path.Combine(VelopackLocator.Current.RootAppDir, Path.GetFileName(process));
            return File.Exists(stub) ? stub : null;
        }
    }

    /// <summary>The way to update, from what this install is.</summary>
    internal static UpdateMode Decide(bool velopackInstall, bool windows, bool innoUninstallerPresent) =>
        velopackInstall ? UpdateMode.Velopack
        : windows && innoUninstallerPresent ? UpdateMode.MigrateWindows
        : UpdateMode.ManualDownload;

    public async Task<string?> DownloadAsync(bool includePrereleases, IProgress<int> progress, CancellationToken cancellationToken)
    {
        if (Mode != UpdateMode.Velopack)
        {
            throw new InvalidOperationException("This DeckHost isn't installed by Velopack.");
        }

        var manager = Manager(includePrereleases);
        var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (update is null)
        {
            return null;
        }

        progress.Report(0);
        await manager.DownloadUpdatesAsync(update, percent => progress.Report(percent), cancellationToken).ConfigureAwait(false);
        _downloaded = update;
        return update.TargetFullRelease.Version.ToString();
    }

    public void ApplyAfterExit(bool restart)
    {
        if (_manager is null || _downloaded is null)
        {
            return;
        }

        // Not ApplyUpdatesAndRestart: that ends the process at once, before DeckHost saves its profiles and blanks the device.
        _manager.WaitExitThenApplyUpdates(_downloaded.TargetFullRelease, silent: !restart, restart: restart);
        _downloaded = null;
    }

    private UpdateManager Manager(bool includePrereleases)
    {
        var feed = Environment.GetEnvironmentVariable(FeedVariable);
        _manager = string.IsNullOrWhiteSpace(feed)
            ? new UpdateManager(new GithubSource($"https://github.com/{AppInfo.UpdateRepository}", accessToken: null, prerelease: includePrereleases))
            : new UpdateManager(feed);
        return _manager;
    }
}

/// <summary>A DeckHost that can't update itself (tests, development builds): only the download for installing by hand.</summary>
internal sealed class ManualUpdater : IAppUpdater
{
    public static ManualUpdater Instance { get; } = new();

    public UpdateMode Mode => UpdateMode.ManualDownload;

    public bool HasDownloaded => false;

    public Task<string?> DownloadAsync(bool includePrereleases, IProgress<int> progress, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This DeckHost can't update itself.");

    public void ApplyAfterExit(bool restart)
    {
    }
}
