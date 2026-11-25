using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DeckHost.App.Platform;

/// <summary>Application settings (start at sign-in, tray behavior), stored in <c>%AppData%/DeckHost/settings.json</c>.</summary>
/// <param name="CloseToTray">
/// On by default where a tray icon is always available (Windows, macOS). Linux desktops without a
/// status-notifier tray (plain GNOME) would leave no way back to a hidden window, so it starts off there.
/// </param>
/// <param name="CheckForUpdates">Check GitHub for a new DeckHost release now and then.</param>
/// <param name="IncludePrereleases">Offer pre-releases too; by default only to someone running one.</param>
/// <param name="SkippedVersion">A release the user chose to skip: not announced again (a newer one is).</param>
/// <param name="ShowDebugPanel">Show the debug panel next to the editor (control capabilities, runtime connection and log).</param>
/// <param name="CrashesSeenUntil">Crash reports up to this time were shown to the user (newer ones are offered on start).</param>
/// <param name="OfferCrashReports">After a crash, offer to report it on the next start (nothing is ever sent without asking).</param>
/// <param name="WelcomeShown">The welcome tour was shown (it opens by itself on the first start only).</param>
/// <param name="AutoUpdatePlugins">Install plugin updates from the store catalogs by themselves.</param>
/// <param name="InstallUpdatesAutomatically">Download a new DeckHost in the background and install it when DeckHost closes.</param>
/// <param name="OnlyCompatibleActions">The action list shows only the actions that fit the selected control.</param>
/// <param name="DeviceSimulator">Developer option: let device simulator windows connect (☰ → Device simulator).</param>
/// <param name="CollapsedPluginSections">The sections of the Plugins page the user folded up (<c>PluginManagerViewModel</c> section ids).</param>
public sealed record AppSettingsData(
    bool StartMinimized = true,
    bool? CloseToTray = null,
    bool CheckForUpdates = true,
    bool? IncludePrereleases = null,
    string? SkippedVersion = null,
    DateTimeOffset? LastUpdateCheck = null,
    bool ShowDebugPanel = false,
    DateTimeOffset? CrashesSeenUntil = null,
    bool OfferCrashReports = true,
    bool WelcomeShown = false,
    string Theme = AppTheme.Dark,
    bool AutoUpdatePlugins = false,
    bool InstallUpdatesAutomatically = false,
    bool DeviceSimulator = false,
    bool OnlyCompatibleActions = false,
    string[]? CollapsedPluginSections = null)
{
    public bool KeepsRunningInTray => CloseToTray ?? !OperatingSystem.IsLinux();
}

/// <summary>DeckHost's look: dark (as designed), light, or the system's choice.</summary>
public static class AppTheme
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";

    public static Avalonia.Styling.ThemeVariant ToVariant(string? theme) => theme switch
    {
        Light => Avalonia.Styling.ThemeVariant.Light,
        System => Avalonia.Styling.ThemeVariant.Default,
        _ => Avalonia.Styling.ThemeVariant.Dark,
    };
}

public static class AppSettings
{
    /// <summary>Passed by the sign-in entry, so DeckHost knows it was started at sign-in.</summary>
    public const string StartupArgument = "--startup";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DeckHost";
    private const string MacLaunchAgentLabel = "app.deckhost.DeckHost";

    private static string LinuxAutostartPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config ? config : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart",
        "deckhost.desktop");

    private static string MacLaunchAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", MacLaunchAgentLabel + ".plist");

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "settings.json");

    public static bool CanStartAtLogin =>
        (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && Environment.ProcessPath is not null;

    public static AppSettingsData Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(SettingsPath)) ?? new AppSettingsData();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new AppSettingsData();
    }

    public static void Save(AppSettingsData settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Whether this user's sign-in starts DeckHost (Windows Run key, XDG autostart entry or macOS LaunchAgent).</summary>
    public static bool IsStartAtLoginEnabled()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string command && command.Length > 0;
        }

        if (OperatingSystem.IsLinux())
        {
            return File.Exists(LinuxAutostartPath);
        }

        return OperatingSystem.IsMacOS() && File.Exists(MacLaunchAgentPath);
    }

    /// <summary>Adds or removes DeckHost's sign-in entry for the current user (no admin rights needed).</summary>
    public static void SetStartAtLogin(bool enabled)
    {
        // From an AppImage the process lives in a temporary mount: start the file the user has. A
        // Velopack install on Windows: its launcher, whose path survives updates (current\ is replaced).
        var exe = AppImage.Path ?? AppUpdater.StableLauncher ?? Environment.ProcessPath;
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled && exe is not null)
            {
                key.SetValue(RunValueName, $"\"{exe}\" {StartupArgument}");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            WriteOrDelete(LinuxAutostartPath, enabled && exe is not null ? LinuxAutostartEntry(exe) : null);
        }
        else if (OperatingSystem.IsMacOS())
        {
            WriteOrDelete(MacLaunchAgentPath, enabled && exe is not null ? MacLaunchAgent(exe) : null);
        }
    }

    /// <summary>
    /// Points start at sign-in at this DeckHost again: turned on by <paramref name="migration"/> (the old
    /// setup's uninstaller removed it), or rewritten when it names another program (the install moved).
    /// </summary>
    internal static void RepairStartAtLogin(MigrationNote? migration)
    {
        try
        {
            if (migration?.StartAtLogin == true || (IsStartAtLoginEnabled() && !StartAtLoginPointsHere()))
            {
                SetStartAtLogin(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Left as it is; the setting can be turned on again by hand.
        }
    }

    private static bool StartAtLoginPointsHere()
    {
        var exe = AppImage.Path ?? AppUpdater.StableLauncher ?? Environment.ProcessPath;
        if (exe is null)
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string command && command.Contains(exe, StringComparison.OrdinalIgnoreCase);
        }

        var entry = OperatingSystem.IsLinux() ? LinuxAutostartPath : MacLaunchAgentPath;
        return !File.Exists(entry) || File.ReadAllText(entry).Contains(exe, StringComparison.Ordinal);
    }

    /// <summary>An XDG autostart entry (freedesktop.org Desktop Entry).</summary>
    internal static string LinuxAutostartEntry(string exe) => $"""
        [Desktop Entry]
        Type=Application
        Name=DeckHost
        Comment=Control surface host
        Exec="{exe.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}" {StartupArgument}
        Terminal=false
        X-GNOME-Autostart-enabled=true

        """;

    /// <summary>
    /// A LaunchAgent that starts DeckHost at sign-in. Inside an app bundle it opens the bundle,
    /// so macOS treats it as the app (Dock, permissions) rather than as a bare program.
    /// </summary>
    internal static string MacLaunchAgent(string exe)
    {
        var bundleEnd = exe.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
        string[] arguments = bundleEnd > 0
            ? ["/usr/bin/open", "-a", exe[..(bundleEnd + 4)], "--args", StartupArgument]
            : [exe, StartupArgument];
        var items = string.Concat(arguments.Select(argument => $"\n        <string>{System.Security.SecurityElement.Escape(argument)}</string>"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{MacLaunchAgentLabel}</string>
                <key>ProgramArguments</key>
                <array>{items}
                </array>
                <key>RunAtLoad</key>
                <true/>
            </dict>
            </plist>

            """;
    }

    private static void WriteOrDelete(string path, string? content)
    {
        if (content is null)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
