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

    /// <summary>
    /// After the new DeckHost is installed: notes what to carry over, and starts a hidden helper that
    /// waits for this DeckHost to exit, removes the old install with its uninstaller and starts the new
    /// one. DeckHost must exit right after.
    /// </summary>
    public static void FinishMigrationAfterExit(bool startAtLogin)
    {
        MigrationNote.Write(new MigrationNote(startAtLogin));
        var uninstaller = Path.Combine(InstallLocation, "unins000.exe");
        var launchers = string.Join(',', NewLauncherCandidates().Select(Quote));
        var command =
            $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
            $"if (Test-Path -LiteralPath {Quote(uninstaller)}) {{ Start-Process -Wait -FilePath {Quote(uninstaller)} -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' }}; " +
            $"foreach ($launcher in @({launchers})) {{ if (Test-Path -LiteralPath $launcher) {{ Start-Process -FilePath $launcher; break }} }}";
        var helper = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", command })
        {
            helper.ArgumentList.Add(argument);
        }

        (Process.Start(helper) ?? throw new UpdateException("The update helper didn't start.")).Dispose();
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

    /// <summary>A PowerShell single-quoted string (a quote inside is doubled).</summary>
    internal static string Quote(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
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

    /// <summary>The note, once: it is deleted. Null when there is none (or it can't be read).</summary>
    public static MigrationNote? Take(string? path = null)
    {
        path ??= FilePath;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var note = JsonSerializer.Deserialize<MigrationNote>(File.ReadAllText(path));
            File.Delete(path);
            return note;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
