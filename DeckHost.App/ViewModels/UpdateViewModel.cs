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
/// every release since the running one, and downloads it, installs it while DeckHost runs (asking
/// for administrator rights when DeckHost's folder needs them) and restarts DeckHost. The user can skip a
/// release; a newer one is announced again. One instance (a service) serves the main window's
/// banner, Settings and the update window.
/// </summary>
public sealed partial class UpdateViewModel : ViewModelBase, IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);

    private readonly UpdateChecker _checker;
    private readonly Func<AppSettingsData> _loadSettings;
    private readonly Action<AppSettingsData> _saveSettings;
    private readonly SemanticVersion? _current;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _install;
    private UpdateCheckResult? _result;
    private bool _checkAutomatically;
    private bool _installAutomatically;
    /// <summary>A downloaded and unpacked update (Install updates automatically): installed when DeckHost closes, or at once by Install.</summary>
    private string? _pendingStaged;
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

    public UpdateViewModel(UpdateChecker checker, string currentVersion, Func<AppSettingsData> loadSettings, Action<AppSettingsData> saveSettings)
    {
        _checker = checker;
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

    /// <summary>The update is installed and the new version started (it waits for this one): DeckHost should exit now.</summary>
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
    /// again then). Only where the install needs no administrator rights (<see cref="UpdateInstaller.CanInstallSilently"/>).
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

    public bool CanInstallAutomatically => UpdateInstaller.CanInstallSilently;

    /// <summary>A downloaded update waits for DeckHost to close.</summary>
    public bool IsReadyToInstallOnExit => _pendingStaged is not null;

    public string ReadyText => _pendingVersion is { } version ? $"DeckHost {version} is downloaded and installs when you close DeckHost." : string.Empty;

    /// <summary>Called as DeckHost exits: installs the downloaded update (DeckHost isn't started again).</summary>
    public void InstallPendingOnExit()
    {
        if (_pendingStaged is { } staged && _pendingVersion is { } version)
        {
            _pendingStaged = null;
            try
            {
                UpdateInstaller.InstallOnExit(staged, version);
            }
            catch (UpdateException)
            {
                // Unchanged; the next check offers it again.
            }
        }
    }

    /// <summary>The automatic install: download and unpack quietly; it's installed on exit (or by Install).</summary>
    private async Task PrepareInBackgroundAsync()
    {
        if (!InstallAutomatically || !CanInstallAutomatically || IsInstalling || _pendingStaged is not null || Latest is not { } release ||
            SelectPackage(release) is not { } asset)
        {
            return;
        }

        try
        {
            var directory = DownloadFolder(release);
            var package = await Task.Run(() => _checker.DownloadAsync(release, asset, directory, null, CancellationToken.None));
            var staged = await UpdateInstaller.StageAsync(package, null, CancellationToken.None);
            if (staged is not null)
            {
                _pendingStaged = staged;
                _pendingVersion = release.Version.ToString();
                Status = ReadyText;
                OnPropertyChanged(nameof(IsReadyToInstallOnExit));
                OnPropertyChanged(nameof(ReadyText));
            }
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or System.Net.Http.HttpRequestException)
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
    /// <summary>Also while an update installs, so its progress is in view.</summary>
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

    /// <summary>The download and unpacking can be cancelled; the install itself can't (it finishes or puts everything back).</summary>
    public bool CanCancel
    {
        get => _canCancel;
        private set => SetProperty(ref _canCancel, value);
    }

    /// <summary>The amount isn't known (waiting for the administrator prompt, unpacking a tar.gz).</summary>
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    /// <summary>The current step, 0 to 1.</summary>
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

    public string InstallLabel => UpdateInstaller.InstallLabel;

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

    /// <summary>How long "Installed, restarting" stays in view before DeckHost restarts.</summary>
    internal static readonly TimeSpan RestartNoticeDelay = TimeSpan.FromSeconds(1);

    /// <summary>Download (or reuse the one downloaded in the background), install while DeckHost runs, restart.</summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        if (Latest is not { } release)
        {
            return;
        }

        var version = release.Version.ToString();
        if (SelectPackage(release) is not { } asset)
        {
            Status = $"This release has no package for {UpdateChecker.CurrentRuntimeIdentifier}; the release page opens instead.";
            OpenReleasePage();
            return;
        }

        IsInstalling = true;
        CanCancel = true;
        _install = new CancellationTokenSource();
        SetProgress(0, $"Downloading {asset.Name}…");
        var downloadProgress = new Progress<double>(fraction =>
            SetProgress(fraction, fraction < 0 ? $"Downloading {asset.Name}…" : $"Downloading {asset.Name}: {fraction:P0}"));
        var installProgress = new Progress<UpdateStep>(ShowStep);
        try
        {
            var staged = _pendingVersion == version ? _pendingStaged : null;
            if (staged is null)
            {
                var package = await Task.Run(() => _checker.DownloadAsync(release, asset, DownloadFolder(release), downloadProgress, _install.Token));
                staged = await UpdateInstaller.StageAsync(package, installProgress, _install.Token);
                if (staged is null)
                {
                    Status = $"Downloaded {asset.Name}. Install it the same way as DeckHost itself; the folder is open.";
                    UpdateInstaller.Reveal(package);
                    return;
                }
            }

            // From here on it either finishes or puts everything back.
            CanCancel = false;
            _pendingStaged = null;
            OnPropertyChanged(nameof(IsReadyToInstallOnExit));
            SetProgress(0, "Installing…");
            await UpdateInstaller.InstallAsync(staged, version, installProgress, elevating: () => Dispatcher.UIThread.Post(() =>
            {
                IsProgressIndeterminate = true;
                ProgressText = "Installing… allow DeckHost to make changes when your system asks.";
            }));

            IsProgressIndeterminate = false;
            SetProgress(1, $"Installed DeckHost {version}. Restarting…");
            Status = $"DeckHost {version} is installed.";
            await Task.Delay(RestartNoticeDelay);
            try
            {
                UpdateInstaller.Restart();
            }
            catch (Exception ex) when (ex is UpdateException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Status = $"DeckHost {version} is installed, but it couldn't restart ({ex.Message}). Close DeckHost and start it again.";
                return;
            }

            RestartRequested?.Invoke();
        }
        catch (OperationCanceledException)
        {
            Status = CanCancel ? "Update cancelled." : "The update wasn't installed: administrator rights were declined.";
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
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

    /// <summary>The package to download: the archive DeckHost installs itself, where it can; else the one to install by hand.</summary>
    private static ReleaseAsset? SelectPackage(ReleaseInfo release) =>
        UpdateChecker.SelectAsset(release, UpdateChecker.CurrentRuntimeIdentifier, Platform.AppImage.IsActive, forInstall: UpdateInstaller.CanInstallInPlace);

    private static string DownloadFolder(ReleaseInfo release) => Path.Combine(Path.GetTempPath(), "DeckHost", "updates", release.Version.ToString());

    private void SetProgress(double fraction, string text)
    {
        IsProgressIndeterminate = false;
        Progress = Math.Max(0, fraction);
        ProgressText = text;
    }

    private void ShowStep(UpdateStep step)
    {
        if (step.Phase == UpdatePhase.Done)
        {
            return;
        }

        var verb = step.Phase == UpdatePhase.Unpacking ? "Unpacking" : "Installing";
        if (step.Total == 0)
        {
            IsProgressIndeterminate = true;
            if (!ProgressText.StartsWith(verb, StringComparison.Ordinal))
            {
                ProgressText = $"{verb}…";
            }

            return;
        }

        SetProgress(step.Fraction, $"{verb}… {step.Done} of {step.Total} files");
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
