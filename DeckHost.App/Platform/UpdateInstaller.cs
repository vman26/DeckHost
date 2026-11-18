using DeckHost.Core.Updates;
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DeckHost.App.Platform;

/// <summary>
/// Installs a downloaded update while DeckHost runs, then DeckHost restarts into it:
/// <list type="number">
/// <item><see cref="StageAsync"/> unpacks the package (Windows and macOS zip, Linux tar.gz).</item>
/// <item><see cref="InstallAsync"/> puts the new files in place (<see cref="InPlaceUpdate"/>: running files
/// are renamed, not overwritten). When DeckHost's folder isn't writable (installed for all users) the
/// new version does it itself, started with administrator rights: the UAC prompt on Windows,
/// pkexec on Linux, the password dialog on macOS.</item>
/// <item><see cref="Restart"/> starts the new version, which waits for this one to end.</item>
/// </list>
/// Not for a development build (bin/Debug) or an AppImage (a read-only mount; the user replaces the
/// file): there the download is shown instead.
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>"DeckHost --apply-update &lt;new folder&gt; &lt;install&gt; &lt;log file&gt; &lt;version&gt;": the elevated install (<see cref="RunApplyUpdate"/>).</summary>
    public const string ApplyUpdateArgument = "--apply-update";

    /// <summary>Win32 ERROR_CANCELLED: the user said no to the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    /// <summary>Inno Setup's uninstall entry for DeckHost (the AppId in DeckHost.iss, plus "_is1").</summary>
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{5C8E7F0A-3B9D-4E61-9A27-6D3F1C0B8E42}_is1";

    /// <summary>Where DeckHost runs from: the folder (Windows, Linux) or the .app bundle (macOS).</summary>
    public static string InstallLocation
    {
        get
        {
            var directory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            if (OperatingSystem.IsMacOS() && FindBundle(directory) is { } bundle)
            {
                return bundle;
            }

            return directory;
        }
    }

    /// <summary>Whether DeckHost can install an update itself (else the download is shown).</summary>
    /// <remarks>Only a released (self-contained) DeckHost, where it can write its folder or ask for the rights to.</remarks>
    public static bool CanInstallInPlace =>
        !AppImage.IsActive && Environment.ProcessPath is not null && IsSelfContained && (CanReplace(InstallLocation) || CanElevate);

    /// <summary>Whether an update installs without any question (DeckHost's folder is writable): the automatic install when DeckHost closes.</summary>
    public static bool CanInstallSilently =>
        !AppImage.IsActive && Environment.ProcessPath is not null && IsSelfContained && CanReplace(InstallLocation);

    /// <summary>The install needs administrator rights (DeckHost was installed for all users).</summary>
    public static bool NeedsElevation => !CanReplace(InstallLocation);

    /// <summary>What the install button does here.</summary>
    public static string InstallLabel => CanInstallInPlace ? "Install and restart" : "Download";

    private static bool IsSelfContained =>
        new[] { "libcoreclr.so", "libcoreclr.dylib", "coreclr.dll" }.Any(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name)));

    private static bool CanElevate => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || FindPkexec() is not null;

    /// <summary>
    /// Unpacks a downloaded package next to it and returns the new version (its DeckHost folder,
    /// or DeckHost.app on macOS), or null when it isn't a package DeckHost can install itself.
    /// </summary>
    public static async Task<string?> StageAsync(string package, IProgress<UpdateStep>? progress, CancellationToken cancellationToken)
    {
        var windows = OperatingSystem.IsWindows();
        var expected = windows || OperatingSystem.IsMacOS() ? ".zip" : ".tar.gz";
        if (!CanInstallInPlace || !package.EndsWith(expected, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var unpacked = Path.Combine(Path.GetDirectoryName(package)!, "unpacked");
        if (Directory.Exists(unpacked))
        {
            Directory.Delete(unpacked, recursive: true);
        }

        Directory.CreateDirectory(unpacked);
        progress?.Report(new UpdateStep(UpdatePhase.Unpacking, 0, 0));
        if (OperatingSystem.IsMacOS())
        {
            // ditto keeps the bundle's links, permissions and signature.
            await Task.Run(() => ExternalTool.Run("/usr/bin/ditto", ["-x", "-k", package, unpacked], TimeSpan.FromMinutes(2)), cancellationToken).ConfigureAwait(false);
            return Directory.EnumerateDirectories(unpacked, "*.app").FirstOrDefault()
                   ?? throw new UpdateException("The update package has no DeckHost.app.");
        }

        if (windows)
        {
            await Task.Run(() => ExtractZip(package, unpacked, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var file = File.OpenRead(package);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzip, unpacked, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
        }

        var executable = Path.GetFileName(Environment.ProcessPath!);
        return new[] { unpacked }.Concat(Directory.EnumerateDirectories(unpacked, "*", SearchOption.AllDirectories))
                   .FirstOrDefault(directory => File.Exists(Path.Combine(directory, executable)))
               ?? throw new UpdateException("The update package has no DeckHost folder.");
    }

    /// <summary>
    /// Puts the staged version in place of the running one: here when DeckHost's folder is writable,
    /// else through the new version started with administrator rights (<paramref name="elevating"/> is
    /// called first, so the window can say it waits for the prompt).
    /// </summary>
    /// <exception cref="OperationCanceledException">The user declined the administrator prompt.</exception>
    /// <exception cref="UpdateException">It failed; DeckHost is unchanged.</exception>
    public static async Task InstallAsync(string staged, string version, IProgress<UpdateStep>? progress, Action? elevating = null)
    {
        var target = InstallLocation;
        if (CanReplace(target))
        {
            await Task.Run(() => Install(staged, target, version, progress)).ConfigureAwait(false);
            return;
        }

        elevating?.Invoke();
        progress?.Report(new UpdateStep(UpdatePhase.Installing, 0, 0));
        var log = Path.Combine(Path.GetTempPath(), "DeckHost", "updates", "apply-update.log");
        File.Delete(log);
        var helper = ElevatedHelper(staged, target, log, version);
        int exitCode;
        try
        {
            using var process = Process.Start(helper) ?? throw new UpdateException("The installer didn't start.");
            await process.WaitForExitAsync().ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new OperationCanceledException("The administrator prompt was declined.", ex);
        }

        if (exitCode != 0)
        {
            var reason = File.Exists(log) ? (await File.ReadAllTextAsync(log).ConfigureAwait(false)).Trim() : string.Empty;
            if (reason.Length == 0)
            {
                // pkexec: 126 = not authorized or the dialog was dismissed; osascript: 1 with "User canceled".
                throw exitCode == 126 || (OperatingSystem.IsMacOS() && exitCode == 1)
                    ? new OperationCanceledException("The administrator prompt was declined.")
                    : new UpdateException($"The installer stopped with code {exitCode}. DeckHost is unchanged.");
            }

            throw new UpdateException(reason);
        }

        progress?.Report(new UpdateStep(UpdatePhase.Done, 1, 1));
    }

    /// <summary>The automatic install as DeckHost closes (no prompt, no restart): only where <see cref="CanInstallSilently"/>.</summary>
    public static void InstallOnExit(string staged, string version) => Install(staged, InstallLocation, version, progress: null);

    /// <summary>Starts the newly installed DeckHost; it waits for this one to end (<c>--after-exit</c>). DeckHost must exit right after.</summary>
    public static void Restart()
    {
        var processId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        var start = OperatingSystem.IsMacOS() && FindBundle(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)) is { } bundle
            ? new ProcessStartInfo("open") { ArgumentList = { "-n", bundle, "--args", "--after-exit", processId } }
            : new ProcessStartInfo(Environment.ProcessPath ?? throw new UpdateException("DeckHost's program file isn't known.")) { ArgumentList = { "--after-exit", processId } };
        start.UseShellExecute = false;
        (Process.Start(start) ?? throw new UpdateException("The new version didn't start.")).Dispose();
    }

    /// <summary>
    /// The install with administrator rights (<see cref="ApplyUpdateArgument"/>), run by the new version
    /// from where it was unpacked: so it never runs from the folder it changes. Exit code 0 when it worked;
    /// otherwise the reason is in the log file.
    /// </summary>
    public static int RunApplyUpdate(string[] arguments)
    {
        if (arguments.Length < 4)
        {
            return 2;
        }

        var (staged, target, log, version) = (arguments[0], arguments[1], arguments[2], arguments[3]);
        try
        {
            Install(staged, target, version, progress: null);
            return 0;
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException)
        {
            try
            {
                File.WriteAllText(log, ex.Message);
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            {
                // Nowhere to report it; the exit code still says it failed.
            }

            return 1;
        }
    }

    /// <summary>On start: delete what the last update replaced, and its downloads.</summary>
    public static void CleanUpAfterUpdate()
    {
        try
        {
            InPlaceUpdate.CleanUp(InstallLocation);
            var downloads = Path.Combine(Path.GetTempPath(), "DeckHost", "updates");
            if (Directory.Exists(downloads))
            {
                Directory.Delete(downloads, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Next time.
        }
    }

    /// <summary>Shows a downloaded package in the file manager, for installing by hand.</summary>
    public static void Reveal(string package)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", package } })?.Dispose();
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", package } })?.Dispose();
            }
            else
            {
                Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { Path.GetDirectoryName(package)! } })?.Dispose();
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// The new version's program inside the staged folder (or bundle), started with administrator rights.
    /// Paths are always separate arguments, never part of a command line or script text.
    /// </summary>
    internal static ProcessStartInfo ElevatedHelper(string staged, string target, string log, string version)
    {
        var executable = OperatingSystem.IsMacOS()
            ? Path.Combine(staged, "Contents", "MacOS", Path.GetFileName(Environment.ProcessPath!))
            : Path.Combine(staged, Path.GetFileName(Environment.ProcessPath!));
        string[] arguments = [ApplyUpdateArgument, staged, target, log, version];
        if (OperatingSystem.IsWindows())
        {
            // ShellExecute with "runas" is the normal UAC prompt.
            var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            return start;
        }

        if (OperatingSystem.IsMacOS())
        {
            // The password dialog; AppleScript quotes every argument (quoted form of).
            var start = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false };
            foreach (var line in MacElevationScript)
            {
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add(line);
            }

            start.ArgumentList.Add(executable);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            return start;
        }

        var pkexec = new ProcessStartInfo(FindPkexec() ?? "pkexec") { UseShellExecute = false };
        pkexec.ArgumentList.Add(executable);
        foreach (var argument in arguments)
        {
            pkexec.ArgumentList.Add(argument);
        }

        return pkexec;
    }

    /// <summary>Runs its arguments (program first) as a shell command with administrator privileges, each one quoted.</summary>
    internal static readonly string[] MacElevationScript =
    [
        "on run argv",
        "set command to \"\"",
        "repeat with argument in argv",
        "set command to command & quoted form of (argument as text) & \" \"",
        "end repeat",
        "do shell script command with administrator privileges",
        "end run",
    ];

    private static void Install(string staged, string target, string version, IProgress<UpdateStep>? progress)
    {
        if (OperatingSystem.IsMacOS())
        {
            // A signed bundle is replaced as a whole: extra or renamed files inside it would break its seal.
            InPlaceUpdate.Swap(staged, target, progress);
        }
        else
        {
            InPlaceUpdate.Apply(staged, target, progress);
        }

        if (OperatingSystem.IsWindows())
        {
            UpdateUninstallEntry(target, version);
        }
    }

    /// <summary>
    /// Installed with setup: "Installed apps" shows the version setup wrote, so the new one is written
    /// there (per user or, with administrator rights, for all users; only the entry for this folder).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void UpdateUninstallEntry(string target, string version)
    {
        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryHive.LocalMachine })
        {
            try
            {
                using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, Microsoft.Win32.RegistryView.Registry64);
                using var key = root.OpenSubKey(UninstallKey, writable: true);
                if (key?.GetValue("InstallLocation") is string location &&
                    string.Equals(Path.TrimEndingDirectorySeparator(location), Path.TrimEndingDirectorySeparator(target), StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue("DisplayVersion", version);
                    key.SetValue("DisplayName", $"DeckHost {version}");
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // Without the rights to (the other hive): that entry isn't ours to change.
            }
        }
    }

    /// <summary>Unpacks a zip with progress; entries that would land outside the folder are refused.</summary>
    private static void ExtractZip(string package, string destination, IProgress<UpdateStep>? progress, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(package);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var entries = archive.Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            var path = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException($"The update package has a file outside its folder: {entry.FullName}");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }

            progress?.Report(new UpdateStep(UpdatePhase.Unpacking, index + 1, entries.Count));
        }
    }

    private static string? FindPkexec() =>
        new[] { "/usr/bin/pkexec", "/bin/pkexec", "/usr/local/bin/pkexec" }.FirstOrDefault(File.Exists);

    private static string? FindBundle(string directory)
    {
        // DeckHost.app/Contents/MacOS
        var contents = Path.GetDirectoryName(directory);
        var bundle = contents is null ? null : Path.GetDirectoryName(contents);
        return bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? bundle : null;
    }

    /// <summary>
    /// Whether DeckHost may write its own folder (the files in it are renamed, so the folder itself must
    /// be writable); on macOS the folder that holds DeckHost.app (the bundle is swapped as a whole).
    /// </summary>
    private static bool CanReplace(string location)
    {
        var folder = OperatingSystem.IsMacOS() ? Path.GetDirectoryName(location) : location;
        if (folder is null)
        {
            return false;
        }

        try
        {
            var probe = Path.Combine(folder, $".deckhost-update-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
