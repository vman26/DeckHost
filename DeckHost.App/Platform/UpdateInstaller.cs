using DeckHost.Core.Updates;
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DeckHost.App.Platform;

/// <summary>
/// Installs a downloaded update. DeckHost can't replace itself while it runs, so a small helper
/// waits for it to exit, installs, and starts DeckHost again:
/// <list type="bullet">
/// <item>Windows: the setup program. Installed by setup (unins000.exe next to DeckHost): silently,
/// into the same folder; otherwise the setup wizard opens.</item>
/// <item>Linux: the tar.gz is unpacked first; the helper swaps DeckHost's folder for the new one
/// (keeping the old one until the copy succeeded).</item>
/// <item>macOS: the zip is unpacked with ditto; the helper swaps the DeckHost.app bundle.</item>
/// </list>
/// Where that isn't possible (a folder DeckHost can't write), the download is shown instead.
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>Passed to the setup program so it starts DeckHost again after a silent update (see DeckHost.iss).</summary>
    public const string RelaunchParameter = "/relaunch=1";

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

    /// <summary>Whether this installation can be updated in place (else the download is shown).</summary>
    /// <remarks>
    /// Only a released (self-contained) DeckHost: a development build (bin/Debug) is never swapped.
    /// </remarks>
    public static bool CanInstallInPlace => OperatingSystem.IsWindows() ||
        (Environment.ProcessPath is not null && IsSelfContained && CanReplace(InstallLocation));

    private static bool IsSelfContained =>
        new[] { "libcoreclr.so", "libcoreclr.dylib", "coreclr.dll" }.Any(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name)));

    /// <summary>What the install button does here.</summary>
    public static string InstallLabel => CanInstallInPlace ? "Install and restart" : "Download";

    /// <summary>
    /// Gets a downloaded package ready (unpacks it) and returns the helper to start, or null when
    /// the update has to be installed by hand (the package is then shown in the file manager).
    /// </summary>
    public static async Task<ProcessStartInfo?> PrepareAsync(string package, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return package.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? WindowsSetup(package) : null;
        }

        if (!CanInstallInPlace)
        {
            return null;
        }

        var unpacked = Path.Combine(Path.GetDirectoryName(package)!, "unpacked");
        if (Directory.Exists(unpacked))
        {
            Directory.Delete(unpacked, recursive: true);
        }

        Directory.CreateDirectory(unpacked);
        string source;
        if (OperatingSystem.IsMacOS())
        {
            await Task.Run(() => ExternalTool.Run("/usr/bin/ditto", ["-x", "-k", package, unpacked], TimeSpan.FromMinutes(2)), cancellationToken).ConfigureAwait(false);
            source = Directory.EnumerateDirectories(unpacked, "*.app").FirstOrDefault()
                     ?? throw new UpdateException("The update package has no DeckHost.app.");
        }
        else
        {
            await using (var file = File.OpenRead(package))
            await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                await TarFile.ExtractToDirectoryAsync(gzip, unpacked, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
            }

            source = Directory.EnumerateDirectories(unpacked, "DeckHost", SearchOption.AllDirectories)
                         .FirstOrDefault(directory => File.Exists(Path.Combine(directory, Path.GetFileName(Environment.ProcessPath!))))
                     ?? throw new UpdateException("The update package has no DeckHost folder.");
        }

        return SwapScript(source, InstallLocation);
    }

    /// <summary>Starts the helper; DeckHost must exit right after, which the helper waits for.</summary>
    public static void Start(ProcessStartInfo helper) =>
        (Process.Start(helper) ?? throw new UpdateException("The update helper didn't start.")).Dispose();

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
    /// The script for Linux and macOS: wait for DeckHost (by process id), move the old install aside,
    /// copy the new one in, put the old one back if that fails, start DeckHost. Paths are arguments,
    /// never part of the script text.
    /// </summary>
    internal const string SwapScriptText = """
        #!/bin/sh
        # DeckHost update helper: $1 = DeckHost's process id, $2 = the new version, $3 = the install, $4 = how to start it
        pid="$1"; source="$2"; target="$3"; launch="$4"
        while kill -0 "$pid" 2>/dev/null; do sleep 0.3; done
        backup="$target.update-backup"
        rm -rf "$backup"
        if mv "$target" "$backup"; then
            if cp -a "$source" "$target"; then
                rm -rf "$backup"
            else
                rm -rf "$target"
                mv "$backup" "$target"
            fi
        fi
        if [ "$launch" = "open" ]; then
            open "$target"
        else
            nohup "$target/$launch" >/dev/null 2>&1 &
        fi
        """;

    private static ProcessStartInfo SwapScript(string source, string target)
    {
        var script = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(source)!)!, "update.sh");
        File.WriteAllText(script, SwapScriptText.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        var startInfo = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(source);
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add(OperatingSystem.IsMacOS() ? "open" : Path.GetFileName(Environment.ProcessPath!));
        return startInfo;
    }

    /// <summary>
    /// Windows: PowerShell waits for DeckHost to exit, then starts setup (the setup's own check for
    /// a running DeckHost would otherwise ask the user to close it).
    /// </summary>
    private static ProcessStartInfo WindowsSetup(string setup)
    {
        var installedBySetup = File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));
        var arguments = installedBySetup ? new[] { "/SILENT", "/SP-", "/NOCANCEL", RelaunchParameter } : ["/SP-"];
        var command =
            $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
            $"Start-Process -FilePath {Quote(setup)} -ArgumentList {string.Join(',', arguments.Select(Quote))}";
        var startInfo = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", command })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>A PowerShell single-quoted string (a quote inside is doubled).</summary>
    internal static string Quote(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string? FindBundle(string directory)
    {
        // DeckHost.app/Contents/MacOS
        var contents = Path.GetDirectoryName(directory);
        var bundle = contents is null ? null : Path.GetDirectoryName(contents);
        return bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? bundle : null;
    }

    /// <summary>The install is swapped by renaming it, so its parent folder must be writable.</summary>
    private static bool CanReplace(string location)
    {
        var parent = Path.GetDirectoryName(location);
        if (parent is null)
        {
            return false;
        }

        try
        {
            var probe = Path.Combine(parent, $".deckhost-update-{Guid.NewGuid():N}");
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
