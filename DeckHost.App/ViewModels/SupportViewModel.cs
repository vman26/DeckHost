using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Platform;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Plugins;
using DeckHost.Core.Support;

namespace DeckHost.App.ViewModels;

/// <summary>
/// Help and support: the documentation, reporting a problem (a pre-filled GitHub issue the user
/// sends themselves), a support bundle to attach, the log folder, and crash reports from earlier
/// runs. Nothing is sent without the user doing it.
/// </summary>
public sealed partial class SupportViewModel : ViewModelBase
{
    private readonly IDeviceHostService? _host;
    private readonly PluginCatalog? _plugins;
    private readonly string _dataDirectory;
    private readonly string _crashDirectory;
    private bool _includeProfiles;
    private bool _offerCrashReports;
    private string _status = string.Empty;

    public SupportViewModel(IDeviceHostService? host = null, PluginCatalog? plugins = null, string? dataDirectory = null)
    {
        _host = host;
        _plugins = plugins;
        _dataDirectory = dataDirectory ?? CrashReporter.DataDirectory;
        _crashDirectory = Path.Combine(_dataDirectory, "crashes");
        var settings = AppSettings.Load();
        _offerCrashReports = settings.OfferCrashReports;
        Crashes = [.. CrashReports.Find(_crashDirectory).Take(10).Select(report => new CrashItemViewModel(report, this))];
        NewCrashes = Crashes.Where(crash => settings.CrashesSeenUntil is null || crash.Report.Time > settings.CrashesSeenUntil).ToList();
    }

    /// <summary>Whether DeckHost crashed since the user last looked (and wants to be told): open this page on start.</summary>
    public static bool HasUnseenCrashes()
    {
        var settings = AppSettings.Load();
        return settings.OfferCrashReports && CrashReports.Find(CrashReporter.CrashDirectory, settings.CrashesSeenUntil).Count > 0;
    }

    public string Version => AppInfo.Version;

    public string SystemDescription => $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    /// <summary>The documentation, published next to the releases (GitHub Pages of the releases repository).</summary>
    public static string DocumentationUrl => DocumentationUrlFor(AppInfo.UpdateRepository);

    internal static string DocumentationUrlFor(string repository)
    {
        var parts = repository.Split('/');
        return parts.Length == 2 ? $"https://{parts[0].ToLowerInvariant()}.github.io/{parts[1]}/" : "https://github.com/" + repository;
    }

    public IReadOnlyList<CrashItemViewModel> Crashes { get; }

    public bool HasCrashes => Crashes.Count > 0;

    /// <summary>Crashes since the user last looked (the notice at the top).</summary>
    public IReadOnlyList<CrashItemViewModel> NewCrashes { get; }

    public bool HasNewCrashes => NewCrashes.Count > 0 && !_newCrashesSeen;

    private bool _newCrashesSeen;

    [RelayCommand]
    private void ReportNewest() => Report(NewCrashes.FirstOrDefault());

    public string NewCrashTitle => NewCrashes.Count == 1
        ? $"DeckHost closed unexpectedly on {NewCrashes[0].When}."
        : $"DeckHost closed unexpectedly {NewCrashes.Count} times (last on {NewCrashes.FirstOrDefault()?.When}).";

    /// <summary>Also put the profiles in the bundle (passwords that actions keep are in there too).</summary>
    public bool IncludeProfiles
    {
        get => _includeProfiles;
        set => SetProperty(ref _includeProfiles, value);
    }

    public bool OfferCrashReports
    {
        get => _offerCrashReports;
        set
        {
            if (SetProperty(ref _offerCrashReports, value))
            {
                AppSettings.Save(AppSettings.Load() with { OfferCrashReports = value });
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string DefaultBundleName => $"DeckHost-support-{DateTime.Now:yyyyMMdd-HHmm}.zip";

    [RelayCommand]
    private void OpenDocumentation() => Open(DocumentationUrl);

    /// <summary>Opens a new GitHub issue with the version, system and devices filled in.</summary>
    [RelayCommand]
    private void ReportProblem() => Report(null);

    internal void Report(CrashItemViewModel? crash)
    {
        var title = crash is null ? "Problem: " : $"Crash in DeckHost {Version}: {crash.Headline}";
        var body = CrashReports.IssueBody(Version, crash?.Report.Read(), DeviceLines());
        Open(CrashReports.IssueUri(AppInfo.UpdateRepository, title, body).AbsoluteUri);
        Status = "The report opened in your browser: add what happened, attach a support bundle if you like, and submit it there.";
        MarkSeen();
    }

    /// <summary>The crashes so far were seen: no notice for them on the next start.</summary>
    [RelayCommand]
    public void MarkSeen()
    {
        var newest = Crashes.FirstOrDefault()?.Report.Time;
        if (newest is not null)
        {
            AppSettings.Save(AppSettings.Load() with { CrashesSeenUntil = newest });
        }

        _newCrashesSeen = true;
        OnPropertyChanged(nameof(HasNewCrashes));
    }

    [RelayCommand]
    private void OpenLogFolder() => Open(Logging.LogDirectory);

    /// <summary>Writes the support bundle to <paramref name="path"/>.</summary>
    public void SaveBundle(string path)
    {
        try
        {
            SupportBundle.Create(_dataDirectory, path, Summary(), includeProfiles: IncludeProfiles);
            Status = $"Saved {Path.GetFileName(path)}. Look inside before you share it; your home folder and user name were replaced.";
            MarkSeen();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't save the bundle: {ex.Message}";
        }
    }

    /// <summary>The bundle's summary: version, system, devices and plugins.</summary>
    public string Summary()
    {
        var text = new StringBuilder();
        text.AppendLine($"DeckHost {Version}");
        text.AppendLine($"System: {SystemDescription}, .NET {Environment.Version}");
        text.AppendLine($"Update repository: {AppInfo.UpdateRepository}");
        text.AppendLine();
        text.AppendLine("Devices:");
        foreach (var line in DeviceDiagnostics)
        {
            text.AppendLine("  " + line);
        }

        text.AppendLine();
        text.AppendLine("Plugins:");
        foreach (var plugin in _plugins?.Entries ?? [])
        {
            text.AppendLine($"  {plugin.Name} {plugin.Version} ({plugin.Id}, {plugin.Kind}{(plugin.IsEnabled ? string.Empty : ", disabled")}{(plugin.Error is null ? string.Empty : ", error: " + plugin.Error)})");
        }

        return CrashReports.Scrub(text.ToString());
    }

    private IEnumerable<string> DeviceLines() =>
        (_host?.Devices ?? []).Select(static device => $"{device.Name} ({(device.IsConnected ? "connected" : "not connected")})");

    /// <summary>
    /// Each device as the host sees it: connected or not, inputs received, images sent, failures and
    /// the last error since DeckHost started. "No inputs" while pressing keys points at the connection.
    /// </summary>
    public IReadOnlyList<string> DeviceDiagnostics => [.. (_host?.Devices ?? []).Select(device =>
    {
        var statistics = _host!.DeviceStatistics.FirstOrDefault(entry => string.Equals(entry.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase));
        var status = _host.ConnectionStatuses.FirstOrDefault(entry => entry.DeviceId == device.Id)?.Message;
        var parts = new List<string> { device.IsConnected ? "connected" : "not connected" };
        if (statistics is not null)
        {
            parts.Add($"{statistics.Inputs} input(s){(statistics.LastInput is { } last ? $", last {last.ToLocalTime():T}" : string.Empty)}");
            parts.Add($"{statistics.FramesSent} image(s) sent");
            if (statistics.FrameFailures + statistics.LightFailures > 0)
            {
                parts.Add($"{statistics.FrameFailures + statistics.LightFailures} failed, last: {statistics.LastError} ({statistics.LastErrorTime?.ToLocalTime():T})");
            }
        }
        else
        {
            parts.Add("nothing received or sent yet");
        }

        return $"{device.Name}: {string.Join(" · ", parts)}{(string.IsNullOrWhiteSpace(status) ? string.Empty : $" · {status}")}";
    })];

    public bool HasDeviceDiagnostics => DeviceDiagnostics.Count > 0;

    [RelayCommand]
    private void RefreshDiagnostics() => OnPropertyChanged(nameof(DeviceDiagnostics));

    internal void OpenFile(string path) => Open(path);

    private void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            Status = $"Couldn't open {target}: {ex.Message}";
        }
    }
}

/// <summary>A crash report in the list.</summary>
public sealed partial class CrashItemViewModel(CrashReport report, SupportViewModel owner)
{
    public CrashReport Report { get; } = report;

    public string When => Report.Time.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>The first line of the exception (what went wrong).</summary>
    public string Headline
    {
        get
        {
            var lines = Report.Read().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return lines.Skip(3).FirstOrDefault() is { } line ? (line.Length > 140 ? line[..139] + "…" : line) : "No details";
        }
    }

    [RelayCommand]
    private void ReportIt() => owner.Report(this);

    [RelayCommand]
    private void Open() => owner.OpenFile(Report.Path);
}
