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
public sealed record AppSettingsData(
    bool StartMinimized = true,
    bool? CloseToTray = null,
    bool CheckForUpdates = true,
    bool? IncludePrereleases = null,
    string? SkippedVersion = null,
    DateTimeOffset? LastUpdateCheck = null)
{
    public bool KeepsRunningInTray => CloseToTray ?? !OperatingSystem.IsLinux();
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
        var exe = Environment.ProcessPath;
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
