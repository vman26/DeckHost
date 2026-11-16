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
/// every release since the running one, and downloads and installs it. The user can skip a
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
    private System.Diagnostics.ProcessStartInfo? _pendingInstall;
    private string? _pendingVersion;
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

    /// <summary>DeckHost should exit now: the installer waits for it and starts it again.</summary>
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
    /// again then). Only where the install needs no questions (<see cref="UpdateInstaller.CanInstallSilently"/>).
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
    public bool IsReadyToInstallOnExit => _pendingInstall is not null;

    public string ReadyText => _pendingVersion is { } version ? $"DeckHost {version} is downloaded and installs when you close DeckHost." : string.Empty;

    /// <summary>Called as DeckHost exits: starts the downloaded update's installer (it waits for DeckHost to end).</summary>
    public void InstallPendingOnExit()
    {
        if (_pendingInstall is { } helper)
        {
            _pendingInstall = null;
            try
            {
                UpdateInstaller.Start(helper);
            }
            catch (Exception ex) when (ex is UpdateException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }
    }

    /// <summary>The automatic install: download and prepare quietly; the installer starts on exit.</summary>
    private async Task PrepareInBackgroundAsync()
    {
        if (!InstallAutomatically || !CanInstallAutomatically || IsInstalling || _pendingInstall is not null || Latest is not { } release ||
            UpdateChecker.SelectAsset(release, UpdateChecker.CurrentRuntimeIdentifier) is not { } asset)
        {
            return;
        }

        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "DeckHost", "updates", release.Version.ToString());
            var package = await Task.Run(() => _checker.DownloadAsync(release, asset, directory, null, CancellationToken.None));
            var helper = await UpdateInstaller.PrepareAsync(package, CancellationToken.None, relaunch: false);
            if (helper is not null)
            {
                _pendingInstall = helper;
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
    public bool ShowBanner => IsUpdateAvailable && !_dismissed && !IsSkipped;

    public bool IsSkipped => Latest is { } latest && string.Equals(_skippedVersion, latest.Version.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (SetProperty(ref _isInstalling, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public bool CanCancel => IsInstalling;

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

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        if (Latest is not { } release)
        {
            return;
        }

        if (UpdateChecker.SelectAsset(release, UpdateChecker.CurrentRuntimeIdentifier) is not { } asset)
        {
            Status = $"This release has no package for {UpdateChecker.CurrentRuntimeIdentifier}; the release page opens instead.";
            OpenReleasePage();
            return;
        }

        IsInstalling = true;
        _install = new CancellationTokenSource();
        Progress = 0;
        ProgressText = $"Downloading {asset.Name}…";
        var directory = Path.Combine(Path.GetTempPath(), "DeckHost", "updates", release.Version.ToString());
        var progress = new Progress<double>(fraction =>
        {
            Progress = Math.Max(0, fraction);
            ProgressText = fraction < 0 ? $"Downloading {asset.Name}…" : $"Downloading {asset.Name}: {fraction:P0}";
        });
        try
        {
            var package = await Task.Run(() => _checker.DownloadAsync(release, asset, directory, progress, _install.Token));
            ProgressText = "Preparing the update…";
            var helper = await UpdateInstaller.PrepareAsync(package, _install.Token);
            if (helper is null)
            {
                Status = $"Downloaded {asset.Name}. Install it the same way as DeckHost itself; the folder is open.";
                UpdateInstaller.Reveal(package);
                return;
            }

            ProgressText = "Restarting DeckHost to install the update…";
            UpdateInstaller.Start(helper);
            RestartRequested?.Invoke();
        }
        catch (OperationCanceledException)
        {
            Status = "Update cancelled.";
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Status = $"The update failed: {ex.Message}";
        }
        finally
        {
            IsInstalling = false;
            ProgressText = string.Empty;
            _install.Dispose();
            _install = null;
        }
    }

    private bool CanInstall() => IsUpdateAvailable && !IsInstalling;

    [RelayCommand]
    private void Cancel() => _install?.Cancel();

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
