using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace DeckHost.Core.Support;

/// <summary>
/// Crash reports and support bundles. Nothing leaves the computer by itself: a crash is written to
/// <c>crashes/crash-&lt;time&gt;.txt</c>; on the next start DeckHost offers to report it, which opens a
/// pre-filled GitHub issue the user reads and sends (or not), or saves a support bundle to attach.
/// </summary>
public static class CrashReports
{
    public const string FilePrefix = "crash-";

    /// <summary>Most characters of a report that go into an issue link (browsers cut long URLs).</summary>
    public const int MaxIssueBodyLength = 5500;

    private const string TimeFormat = "yyyyMMdd-HHmmss";

    /// <summary>
    /// Writes a report of <paramref name="exception"/> into <paramref name="directory"/> and returns its
    /// path, or null when it couldn't be written (a crash handler must not throw).
    /// </summary>
    public static string? Write(string directory, Exception exception, string version, string source, DateTimeOffset? now = null)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var time = now ?? DateTimeOffset.Now;
            var path = Path.Combine(directory, $"{FilePrefix}{time.ToString(TimeFormat, CultureInfo.InvariantCulture)}.txt");
            File.WriteAllText(path, Format(exception, version, source, time));
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The report's text: what crashed where, the version and the system, then the exception.</summary>
    public static string Format(Exception exception, string version, string source, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = new StringBuilder();
        var headline = source.StartsWith("recovered", StringComparison.Ordinal)
            ? $"DeckHost {version} hit an unexpected error and carried on ({source})."
            : $"DeckHost {version} closed unexpectedly ({source}).";
        text.AppendLine(headline);
        text.AppendLine(CultureInfo.InvariantCulture, $"Time: {time.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"System: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
        text.AppendLine();
        text.AppendLine(Scrub(exception.ToString()));
        return text.ToString();
    }

    /// <summary>Reports newer than <paramref name="seenUntil"/>, newest first.</summary>
    public static IReadOnlyList<CrashReport> Find(string directory, DateTimeOffset? seenUntil = null)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var reports = new List<CrashReport>();
        foreach (var path in Directory.EnumerateFiles(directory, FilePrefix + "*.txt"))
        {
            var stamp = Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..];
            if (DateTime.TryParseExact(stamp, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
            {
                var time = new DateTimeOffset(local);
                if (seenUntil is null || time > seenUntil)
                {
                    reports.Add(new CrashReport(path, time));
                }
            }
        }

        return [.. reports.OrderByDescending(static report => report.Time)];
    }

    /// <summary>Keeps the newest <paramref name="keep"/> reports and deletes the rest.</summary>
    public static void Prune(string directory, int keep = 20)
    {
        foreach (var old in Find(directory).Skip(keep))
        {
            try
            {
                File.Delete(old.Path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// A link that opens a new issue in <paramref name="repository"/> ("owner/name") with the title and
    /// the report filled in (cut to <see cref="MaxIssueBodyLength"/>; the bundle has everything).
    /// </summary>
    public static Uri IssueUri(string repository, string title, string body)
    {
        if (body.Length > MaxIssueBodyLength)
        {
            body = body[..MaxIssueBodyLength] + "\n…\n(cut short: please attach the support bundle from DeckHost settings → Help)";
        }

        return new Uri($"https://github.com/{repository}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}");
    }

    /// <summary>The issue text for a problem report (with a crash report when there is one).</summary>
    public static string IssueBody(string version, string? crash, IEnumerable<string> devices)
    {
        var text = new StringBuilder();
        text.AppendLine("**What happened, and what did you expect?**");
        text.AppendLine();
        text.AppendLine("**Steps to reproduce**");
        text.AppendLine("1. ");
        text.AppendLine();
        text.AppendLine("---");
        text.AppendLine(CultureInfo.InvariantCulture, $"DeckHost {version} · {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        var list = devices.ToList();
        if (list.Count > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Devices: {string.Join(", ", list)}");
        }

        if (crash is not null)
        {
            text.AppendLine();
            text.AppendLine("<details><summary>Crash report</summary>");
            text.AppendLine();
            text.AppendLine("```");
            text.AppendLine(crash.Trim());
            text.AppendLine("```");
            text.AppendLine("</details>");
        }

        return text.ToString();
    }

    /// <summary>
    /// Replaces the user's home folder and name in a text with placeholders, so a report or log
    /// doesn't carry them.
    /// </summary>
    public static string Scrub(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 2)
        {
            text = text.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        }

        var user = Environment.UserName;
        if (user.Length > 2)
        {
            text = text.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }
}

/// <summary>A crash report file.</summary>
public sealed record CrashReport(string Path, DateTimeOffset Time)
{
    public string Read()
    {
        try
        {
            return File.ReadAllText(Path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// A zip of what helps to find a problem: the recent logs (also plugins' logs), crash reports, the
/// app and device settings, the plugin list and a summary. Profiles are left out unless asked for
/// (they can hold passwords that actions keep). Home folder and user name are replaced in the text files.
/// </summary>
public static class SupportBundle
{
    /// <param name="dataDirectory">DeckHost's data folder (<c>%AppData%/DeckHost</c>).</param>
    /// <param name="summary">A summary written as <c>summary.txt</c> (version, system, devices, plugins).</param>
    public static void Create(string dataDirectory, string destination, string summary, int logDays = 3, bool includeProfiles = false, DateTimeOffset? now = null)
    {
        var since = (now ?? DateTimeOffset.Now).AddDays(-Math.Max(1, logDays));
        var temp = destination + ".tmp";
        using (var stream = File.Create(temp))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            AddText(zip, "summary.txt", summary);
            foreach (var file in Candidates(dataDirectory, since, includeProfiles))
            {
                var name = Path.GetRelativePath(dataDirectory, file).Replace('\\', '/');
                try
                {
                    // Logs are still open for writing: share them.
                    using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(source);
                    AddText(zip, name, CrashReports.Scrub(reader.ReadToEnd()));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        File.Move(temp, destination, overwrite: true);
    }

    /// <summary>The files that go into a bundle.</summary>
    public static IEnumerable<string> Candidates(string dataDirectory, DateTimeOffset since, bool includeProfiles)
    {
        if (!Directory.Exists(dataDirectory))
        {
            yield break;
        }

        foreach (var name in new[] { "settings.json", "store.json", Path.Combine("plugins", "state.json") }.Concat(includeProfiles ? ["profiles.json"] : []))
        {
            var path = Path.Combine(dataDirectory, name);
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        foreach (var folder in new[] { "devices", "crashes", "logs" })
        {
            var path = Path.Combine(dataDirectory, folder);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file);
                var recent = File.GetLastWriteTime(file) >= since.LocalDateTime;
                if (folder == "devices" ? extension == ".json" : extension is ".log" or ".txt" && (recent || folder == "crashes"))
                {
                    yield return file;
                }
            }
        }
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
