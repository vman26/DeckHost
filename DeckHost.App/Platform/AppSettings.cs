using Microsoft.Win32;
using System;
using System.IO;
using System.Text.Json;

namespace DeckHost.App.Platform;

/// <summary>Application settings (start with Windows, tray behavior), stored in <c>%AppData%/DeckHost/settings.json</c>.</summary>
public sealed record AppSettingsData(bool StartMinimized = true, bool CloseToTray = true);

public static class AppSettings
{
    /// <summary>Passed by the Windows Run entry, so DeckHost knows it was started at sign-in.</summary>
    public const string StartupArgument = "--startup";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DeckHost";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "settings.json");

    public static bool CanStartWithWindows => OperatingSystem.IsWindows() && Environment.ProcessPath is not null;

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

    /// <summary>Whether the current user's Run key starts this DeckHost at sign-in.</summary>
    public static bool IsStartWithWindowsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string command && command.Length > 0;
    }

    /// <summary>Adds or removes DeckHost's entry in the current user's Run key (no admin rights needed).</summary>
    public static void SetStartWithWindows(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled && Environment.ProcessPath is { } exe)
        {
            key.SetValue(RunValueName, $"\"{exe}\" {StartupArgument}");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }
}
