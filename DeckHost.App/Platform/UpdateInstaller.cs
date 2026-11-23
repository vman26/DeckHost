using DeckHost.Core.Updates;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace DeckHost.App.Platform;

/// <summary>
/// What DeckHost does with a release outside Velopack (<see cref="AppUpdater"/>):
/// <list type="bullet">
/// <item>Shows a downloaded package for installing by hand (the Linux tar.gz, a development build).</item>
/// <item>Moves a DeckHost installed by the older Windows setup to the Velopack install, once: runs the new
/// installer, then (after DeckHost exits) the old uninstaller, and starts the new DeckHost. Profiles and
/// settings live in DeckHost's data folder and stay; start at sign-in is carried over (<see cref="MigrationNote"/>).</item>
/// </list>
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>Win32 ERROR_CANCELLED: the user said no to the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    /// <summary>Where DeckHost runs from.</summary>
    public static string InstallLocation => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>The older setup installed DeckHost for all users (Program Files): the MSI moves it, asking for administrator rights.</summary>
    public static bool IsInstalledForAllUsers
    {
        get
        {
            var location = InstallLocation;
            return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .Select(Environment.GetFolderPath)
                .Any(folder => folder.Length > 0 && location.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>The release file the move needs: the per-user installer, or the MSI for an install in Program Files.</summary>
    internal static ReleaseAsset? SelectMigrationAsset(ReleaseInfo release, string runtimeIdentifier, bool forAllUsers)
    {
        ArgumentNullException.ThrowIfNull(release);
        var suffix = forAllUsers ? $"-{runtimeIdentifier}.msi" : $"-{runtimeIdentifier}-install.exe";
        return release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Runs the new installer and waits for it: the per-user setup silently, the MSI with its own
    /// window (where to install, the UAC prompt).
    /// </summary>
    /// <exception cref="OperationCanceledException">The administrator prompt was declined.</exception>
    /// <exception cref="UpdateException">The installer failed.</exception>
    public static async Task RunNewInstallerAsync(string installer)
    {
        var msi = installer.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
        var start = msi
            ? new ProcessStartInfo("msiexec.exe") { UseShellExecute = true, ArgumentList = { "/i", installer } }
            : new ProcessStartInfo(installer) { UseShellExecute = false, ArgumentList = { "--silent" } };
        int exitCode;
        try
        {
            using var process = Process.Start(start) ?? throw new UpdateException("The installer didn't start.");
            await process.WaitForExitAsync().ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new OperationCanceledException("The administrator prompt was declined.", ex);
        }

        // msiexec: 1602 = cancelled by the user.
        if (msi && exitCode == 1602)
        {
            throw new OperationCanceledException("The installation was cancelled.");
        }

        if (exitCode != 0)
        {
            throw new UpdateException($"The installer stopped with code {exitCode}. DeckHost is unchanged.");
        }
    }

    /// <summary>The older setup's uninstaller, in its install folder.</summary>
    internal const string OldUninstallerName = "unins000.exe";

    /// <summary>How long the new DeckHost waits for the old uninstaller before it starts anyway.</summary>
    private static readonly TimeSpan OldUninstallTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// After the new DeckHost is installed: notes what to carry over and which uninstaller removes the old
    /// install, and starts the new DeckHost with <c>--after-exit</c>. That one waits for this DeckHost to
    /// exit, runs the uninstaller (<see cref="RemoveOldInstall"/>) and starts. DeckHost must exit right after.
    /// (No script helper: an unsigned program starting a hidden PowerShell is what virus scanners look for.)
    /// </summary>
    /// <exception cref="UpdateException">The new DeckHost wasn't found or didn't start.</exception>
    public static void FinishMigrationAfterExit(bool startAtLogin)
    {
        var launcher = NewLauncherCandidates().FirstOrDefault(File.Exists)
            ?? throw new UpdateException("The new DeckHost is installed, but it wasn't found. Start it from the Start menu.");
        MigrationNote.Write(new MigrationNote(startAtLogin) { OldUninstaller = Path.Combine(InstallLocation, OldUninstallerName) });
        var start = new ProcessStartInfo(launcher)
        {
            UseShellExecute = false,
            // Not the old install folder: a process working in it would keep the uninstaller from removing it.
            WorkingDirectory = Path.GetDirectoryName(launcher)!,
            ArgumentList = { "--after-exit", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        (Process.Start(start) ?? throw new UpdateException("The new DeckHost didn't start. Start it from the Start menu.")).Dispose();
    }

    /// <summary>
    /// In the new DeckHost, before it takes the single-instance mutex (the old uninstaller refuses while
    /// that is held): runs the uninstaller the move noted, silently, and waits until it has removed itself.
    /// Does nothing without a note, outside Windows or for a path that isn't an old DeckHost's uninstaller.
    /// </summary>
    public static void RemoveOldInstall(MigrationNote? note)
    {
        if (!OperatingSystem.IsWindows() || note?.OldUninstaller is not { } uninstaller
            || !IsOldUninstaller(uninstaller, AppContext.BaseDirectory, File.Exists))
        {
            return;
        }

        try
        {
            // Shell execute: an install in Program Files asks for administrator rights.
            var start = new ProcessStartInfo(uninstaller) { UseShellExecute = true, ArgumentList = { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" } };
            using (var process = Process.Start(start))
            {
                process?.WaitForExit(OldUninstallTimeout);
            }

            // The uninstaller hands over to a copy of itself in the temp folder and exits; that copy
            // deletes unins000.exe last.
            var deadline = DateTime.UtcNow + OldUninstallTimeout;
            while (File.Exists(uninstaller) && DateTime.UtcNow < deadline)
            {
                System.Threading.Thread.Sleep(250);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Declined or failed: the old DeckHost stays installed and can be removed in Windows' settings.
        }
    }

    /// <summary>
    /// True for an older setup's uninstaller: named <see cref="OldUninstallerName"/>, next to a DeckHost
    /// program, and not in (or above) the folder this DeckHost runs from.
    /// </summary>
    internal static bool IsOldUninstaller(string path, string runningFrom, Func<string, bool> fileExists)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFileName(path), OldUninstallerName, StringComparison.OrdinalIgnoreCase)
            || Path.GetDirectoryName(path) is not { Length: > 0 } folder)
        {
            return false;
        }

        var ownFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(runningFrom));
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var inside = ownFolder.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || ownFolder.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return !inside && fileExists(path) && fileExists(Path.Combine(folder, "DeckHost.App.exe"));
    }

    /// <summary>Where the Velopack install puts its launcher: per user, or in Program Files from the MSI.</summary>
    internal static string[] NewLauncherCandidates()
    {
        var exe = Path.GetFileName(Environment.ProcessPath) ?? "DeckHost.App.exe";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return
        [
            Path.Combine(local, "DeckHost", exe),
            Path.Combine(programs, "Marco Veltman", "DeckHost", exe),
            Path.Combine(programs, "DeckHost", exe),
        ];
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
}

/// <summary>
/// What the move from the older Windows setup carries over to the new install (its uninstaller removes
/// the start-at-sign-in entry): written before the old DeckHost exits, read and deleted by the new one.
/// </summary>
internal sealed record MigrationNote(bool StartAtLogin)
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "migration.json");

    public static void Write(MigrationNote note, string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(note));
    }

    /// <summary>The older setup's uninstaller, which the new DeckHost runs (<see cref="UpdateInstaller.RemoveOldInstall"/>). Null in notes from before it was recorded.</summary>
    public string? OldUninstaller { get; init; }

    /// <summary>The note, left in place. Null when there is none (or it can't be read).</summary>
    public static MigrationNote? Peek(string? path = null) => Read(path ?? FilePath, delete: false);

    /// <summary>The note, once: it is deleted. Null when there is none (or it can't be read).</summary>
    public static MigrationNote? Take(string? path = null) => Read(path ?? FilePath, delete: true);

    private static MigrationNote? Read(string path, bool delete)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var note = JsonSerializer.Deserialize<MigrationNote>(File.ReadAllText(path));
            if (delete)
            {
                File.Delete(path);
            }

            return note;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
