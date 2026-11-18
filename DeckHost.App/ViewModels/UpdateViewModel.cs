using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Platform;
using DeckHost.Core.Updates;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DeckHost.App.ViewModels;

/// <summary>
/// DeckHost's updates: checks GitHub for a newer release (automatically, a little after start and
/// then every few hours, or on request), announces it in the main window with the changelog of
/// every release since the running one, and installs it through Velopack (<see cref="IAppUpdater"/>):
/// the download with its percentage here, then DeckHost exits normally and Velopack swaps in the new
/// version and starts it. A DeckHost from the older Windows setup moves to the Velopack install once;
/// anything else (the Linux tar.gz) gets the package to install by hand. The user can skip a
/// release; a newer one is announced again. One instance (a service) serves the main window's
/// banner, Settings and the update window.
/// </summary>
public sealed partial class UpdateViewModel : ViewModelBase, IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);

    private readonly UpdateChecker _checker;
    private readonly IAppUpdater _updater;
    private readonly Func<AppSettingsData> _loadSettings;
    private readonly Action<AppSettingsData> _saveSettings;
    private readonly SemanticVersion? _current;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _install;
    private UpdateCheckResult? _result;
    private bool _checkAutomatically;
    private bool _installAutomatically;
    private string? _pendingVersion;
    private bool _canCancel;
    private bool _isProgressIndeterminate;
    private bool _includePrereleases;
    private bool _isChecking;
    private bool _isInstalling;
    private bool _dismissed;
    private string? _skippedVersion;
    private string _status = string.Empty;
    private double _progress;
    private string _progressText = string.Empty;

    public UpdateViewModel(UpdateChecker checker, string currentVersion, Func<AppSettingsData> loadSettings, Action<AppSettingsData> saveSettings, IAppUpdater? updater = null)
    {
        _checker = checker;
        _updater = updater ?? ManualUpdater.Instance;
        _loadSettings = loadSettings;
        _saveSettings = saveSettings;
        CurrentVersion = currentVersion;
        SemanticVersion.TryParse(currentVersion, out var current);
        _current = current;

        var settings = loadSettings();
        _checkAutomatically = settings.CheckForUpdates;
        _installAutomatically = settings.InstallUpdatesAutomatically;
        _includePrereleases = settings.IncludePrereleases ?? current?.IsPrerelease ?? false;
        _skippedVersion = settings.SkippedVersion;
        _status = settings.LastUpdateCheck is { } last ? $"Last checked {last.ToLocalTime():g}." : "Not checked yet.";
    }

    /// <summary>DeckHost should exit now: Velopack (or the move to it) waits for that, installs and starts the new version.</summary>
    public event Action? RestartRequested;

    /// <summary>The update window should open (the banner's "What's new").</summary>
    public event Action? DetailsRequested;

    public string CurrentVersion { get; }

    public string CurrentVersionText => $"You have DeckHost {CurrentVersion}.";

    public bool CheckAutomatically
    {
        get => _checkAutomatically;
        set
        {
            if (SetProperty(ref _checkAutomatically, value))
            {
                Save(settings => settings with { CheckForUpdates = value });
                if (value && _timer is not null)
                {
                    _ = CheckAsync(manual: false);
                }
            }
        }
    }

    /// <summary>
    /// Download a new release in the background and install it when DeckHost closes (not started
    /// again then). Only for a Velopack install.
    /// </summary>
    public bool InstallAutomatically
    {
        get => _installAutomatically;
        set
        {
            if (SetProperty(ref _installAutomatically, value))
            {
                Save(settings => settings with { InstallUpdatesAutomatically = value });
                if (value && IsUpdateAvailable && !IsSkipped)
                {
                    _ = PrepareInBackgroundAsync();
                }
            }
        }
    }

    public bool CanInstallAutomatically => _updater.Mode == UpdateMode.Velopack;

    /// <summary>A downloaded update waits for DeckHost to close.</summary>
    public bool IsReadyToInstallOnExit => _pendingVersion is not null && _updater.HasDownloaded;

    public string ReadyText => _pendingVersion is { } version ? $"DeckHost {version} is downloaded and installs when you close DeckHost." : string.Empty;

    /// <summary>Called as DeckHost exits: Velopack applies the downloaded update once DeckHost has ended (it isn't started again).</summary>
    public void InstallPendingOnExit()
    {
        if (IsReadyToInstallOnExit)
        {
            _pendingVersion = null;
            _updater.ApplyAfterExit(restart: false);
        }
    }

    /// <summary>The automatic install: download quietly; Velopack applies it when DeckHost closes (or at once by Install).</summary>
    private async Task PrepareInBackgroundAsync()
    {
        if (!InstallAutomatically || !CanInstallAutomatically || IsInstalling || IsReadyToInstallOnExit || !IsUpdateAvailable)
        {
            return;
        }

        try
        {
            var version = await _updater.DownloadAsync(IncludePrereleases, new Progress<int>(), CancellationToken.None);
            if (version is not null)
            {
                _pendingVersion = version;
                Status = ReadyText;
                OnPropertyChanged(nameof(IsReadyToInstallOnExit));
                OnPropertyChanged(nameof(ReadyText));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"The update couldn't be downloaded: {ex.Message}";
        }
    }

    public bool IncludePrereleases
    {
        get => _includePrereleases;
        set
        {
            if (SetProperty(ref _includePrereleases, value))
            {
                Save(settings => settings with { IncludePrereleases = value });
                if (_result is not null)
                {
                    _ = CheckAsync(manual: true);
                }
            }
        }
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                CheckNowCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsUpdateAvailable => _result?.IsUpdateAvailable == true;

    /// <summary>The newest release offered.</summary>
    public ReleaseInfo? Latest => IsUpdateAvailable ? _result!.Newer[0] : null;

    public string AvailableTitle => Latest is { } latest ? $"DeckHost {latest.Version} is available" : string.Empty;

    public string AvailableSummary => Latest is { } latest
        ? $"You have {CurrentVersion}." + (_result!.Newer.Count > 1 ? $" {_result.Newer.Count} releases since then." : string.Empty)
        : string.Empty;

    /// <summary>The release notes of every release since the running one (Markdown).</summary>
    public string Changelog => _result?.Changelog ?? string.Empty;

    /// <summary>The main window's notice: an update the user hasn't skipped or put off this session.</summary>
    /// <summary>Also while an update downloads, so its progress is in view.</summary>
    public bool ShowBanner => (IsUpdateAvailable && !_dismissed && !IsSkipped) || IsInstalling;

    public bool IsSkipped => Latest is { } latest && string.Equals(_skippedVersion, latest.Version.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (SetProperty(ref _isInstalling, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ShowBanner));
            }
        }
    }

    /// <summary>The download can be cancelled; the install that follows can't.</summary>
    public bool CanCancel
    {
        get => _canCancel;
        private set => SetProperty(ref _canCancel, value);
    }

    /// <summary>The amount isn't known (the installer of the move runs).</summary>
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    /// <summary>The download, 0 to 1.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string InstallLabel => _updater.Mode switch
    {
        UpdateMode.Velopack => "Install and restart",
        UpdateMode.MigrateWindows => "Switch to the new installer",
        _ => "Download",
    };

    /// <summary>Shown under the button: what it does here.</summary>
    public string InstallHint => _updater.Mode switch
    {
        UpdateMode.MigrateWindows => "DeckHost now updates itself with a new installer. This moves DeckHost to it once (your profiles and settings stay); after that, updates download and install in DeckHost.",
        UpdateMode.ManualDownload => "This copy of DeckHost can't update itself: the new version is downloaded for you to install.",
        _ => string.Empty,
    };

    /// <summary>Checks once a little after start, then every <see cref="CheckInterval"/> (when switched on).</summary>
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
            if (CheckAutomatically)
            {
                await CheckAsync(manual: false);
                if (IsUpdateAvailable && !IsSkipped)
                {
                    await PrepareInBackgroundAsync();
                }
            }
        };
        _timer.Start();
    }

    /// <summary>Checks GitHub. A manual check reports every outcome; an automatic one stays quiet on errors.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (IsChecking || IsInstalling)
        {
            return;
        }

        if (_current is null)
        {
            Status = $"This build's version ({CurrentVersion}) can't be compared with releases.";
            return;
        }

        IsChecking = true;
        if (manual)
        {
            Status = "Checking for updates…";
        }

        try
        {
            var result = await _checker.CheckAsync(CurrentVersion, IncludePrereleases);
            _result = result;
            if (manual && IsSkipped)
            {
                // Checking by hand shows a skipped release again.
                _skippedVersion = null;
                Save(settings => settings with { SkippedVersion = null });
            }

            var now = DateTimeOffset.Now;
            Save(settings => settings with { LastUpdateCheck = now });
            Status = result.IsUpdateAvailable
                ? $"DeckHost {result.Newer[0].Version} is available."
                : result.Latest is null
                    ? "No releases are published yet."
                    : $"DeckHost is up to date ({CurrentVersion}). Checked {now:t}.";
            RaiseResultChanged();
        }
        catch (UpdateException ex)
        {
            if (manual)
            {
                Status = ex.Message;
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _install?.Cancel();
        _install?.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckNow() => CheckAsync(manual: true);

    private bool CanCheck() => !IsChecking;

    /// <summary>How long "Restarting…" stays in view before DeckHost exits.</summary>
    internal static TimeSpan RestartNoticeDelay { get; set; } = TimeSpan.FromSeconds(1);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        if (Latest is not { } release)
        {
            return;
        }

        IsInstalling = true;
        CanCancel = true;
        _install = new CancellationTokenSource();
        try
        {
            switch (_updater.Mode)
            {
                case UpdateMode.Velopack:
                    await InstallWithVelopackAsync(release, _install.Token);
                    break;
                case UpdateMode.MigrateWindows:
                    await MigrateAsync(release, _install.Token);
                    break;
                default:
                    await DownloadForHandInstallAsync(release, _install.Token);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Status = CanCancel ? "Update cancelled." : "The update wasn't installed: it was cancelled or administrator rights were declined.";
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or System.ComponentModel.Win32Exception or System.Net.Http.HttpRequestException)
        {
            Status = $"The update failed: {ex.Message}";
        }
        catch (Exception ex) when (_updater.Mode == UpdateMode.Velopack)
        {
            // Velopack's own errors (the feed, a package that doesn't verify).
            Status = $"The update failed: {ex.Message}";
        }
        finally
        {
            IsInstalling = false;
            CanCancel = false;
            IsProgressIndeterminate = false;
            ProgressText = string.Empty;
            _install.Dispose();
            _install = null;
        }
    }

    /// <summary>Download (0–100 %), then exit: Velopack swaps in the new version and starts it.</summary>
    private async Task InstallWithVelopackAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        var version = IsReadyToInstallOnExit ? _pendingVersion : null;
        if (version is null)
        {
            SetProgress(0, $"Downloading DeckHost {release.Version}: 0 %");
            version = await _updater.DownloadAsync(IncludePrereleases, new Progress<int>(percent =>
                SetProgress(percent / 100.0, $"Downloading DeckHost {release.Version}: {percent} %")), cancellationToken);
            if (version is null)
            {
                Status = $"DeckHost {release.Version} isn't ready to install yet (its update packages aren't published). Try again in a few minutes.";
                return;
            }
        }

        CanCancel = false;
        _pendingVersion = null;
        OnPropertyChanged(nameof(IsReadyToInstallOnExit));
        SetProgress(1, $"Downloaded DeckHost {version}. Restarting to finish the update…");
        await Task.Delay(RestartNoticeDelay, CancellationToken.None);
        _updater.ApplyAfterExit(restart: true);
        RestartRequested?.Invoke();
    }

    /// <summary>The once-only move from the older Windows setup: the new installer, then the old one is removed after DeckHost exits.</summary>
    private async Task MigrateAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        var forAllUsers = UpdateInstaller.IsInstalledForAllUsers;
        if (UpdateInstaller.SelectMigrationAsset(release, UpdateChecker.CurrentRuntimeIdentifier, forAllUsers) is not { } asset)
        {
            // A release from before the new installer: the older setup still updates this copy.
            await DownloadForHandInstallAsync(release, cancellationToken);
            return;
        }

        var package = await DownloadAsync(release, asset, cancellationToken);
        CanCancel = false;
        IsProgressIndeterminate = true;
        ProgressText = forAllUsers ? "Installing… allow the installer to make changes when Windows asks." : "Installing…";
        await UpdateInstaller.RunNewInstallerAsync(package);
        IsProgressIndeterminate = false;
        SetProgress(1, $"DeckHost {release.Version} is installed. Restarting…");
        await Task.Delay(RestartNoticeDelay, CancellationToken.None);
        UpdateInstaller.FinishMigrationAfterExit(AppSettings.IsStartAtLoginEnabled());
        RestartRequested?.Invoke();
    }

    /// <summary>A copy DeckHost can't update (the Linux tar.gz, a development build): download the package and show it.</summary>
    private async Task DownloadForHandInstallAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        if (UpdateChecker.SelectAsset(release, UpdateChecker.CurrentRuntimeIdentifier, Platform.AppImage.IsActive) is not { } asset)
        {
            Status = $"This release has no package for {UpdateChecker.CurrentRuntimeIdentifier}; the release page opens instead.";
            OpenReleasePage();
            return;
        }

        var package = await DownloadAsync(release, asset, cancellationToken);
        Status = $"Downloaded {asset.Name}. Install it the same way as DeckHost itself; the folder is open.";
        UpdateInstaller.Reveal(package);
    }

    /// <summary>Downloads a release file (checked against the release's checksums) with its percentage.</summary>
    private Task<string> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, CancellationToken cancellationToken)
    {
        SetProgress(0, $"Downloading {asset.Name}: 0 %");
        var directory = Path.Combine(Path.GetTempPath(), "DeckHost", "updates", release.Version.ToString());
        var progress = new Progress<double>(fraction =>
        {
            if (fraction < 0)
            {
                IsProgressIndeterminate = true;
                ProgressText = $"Downloading {asset.Name}…";
            }
            else
            {
                SetProgress(fraction, $"Downloading {asset.Name}: {fraction * 100:0} %");
            }
        });
        return Task.Run(() => _checker.DownloadAsync(release, asset, directory, progress, cancellationToken), cancellationToken);
    }

    private void SetProgress(double fraction, string text)
    {
        IsProgressIndeterminate = false;
        Progress = Math.Clamp(fraction, 0, 1);
        ProgressText = text;
    }

    private bool CanInstall() => IsUpdateAvailable && !IsInstalling;

    [RelayCommand]
    private void Cancel()
    {
        if (CanCancel)
        {
            _install?.Cancel();
        }
    }

    /// <summary>Don't announce this release again (a newer one is announced).</summary>
    [RelayCommand]
    private void Skip()
    {
        if (Latest is { } latest)
        {
            _skippedVersion = latest.Version.ToString();
            Save(settings => settings with { SkippedVersion = _skippedVersion });
            Status = $"Skipped DeckHost {latest.Version}. Check for updates to see it again.";
            RaiseResultChanged();
        }
    }

    /// <summary>Hide the notice until DeckHost starts again.</summary>
    [RelayCommand]
    private void Later()
    {
        _dismissed = true;
        OnPropertyChanged(nameof(ShowBanner));
    }

    [RelayCommand]
    private void ShowDetails() => DetailsRequested?.Invoke();

    [RelayCommand]
    private void OpenReleasePage()
    {
        var url = Latest?.PageUrl is { Length: > 0 } page ? page : _checker.ReleasesPageUrl;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https")
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

    private void RaiseResultChanged()
    {
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(Latest));
        OnPropertyChanged(nameof(AvailableTitle));
        OnPropertyChanged(nameof(AvailableSummary));
        OnPropertyChanged(nameof(Changelog));
        OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(ShowBanner));
        InstallCommand.NotifyCanExecuteChanged();
    }

    private void Save(Func<AppSettingsData, AppSettingsData> change)
    {
        try
        {
            _saveSettings(change(_loadSettings()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not save the update settings: {ex.Message}";
        }
    }
}
