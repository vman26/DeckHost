using System;
using System.IO;

namespace DeckHost.App.Platform;

/// <summary>
/// DeckHost running from an AppImage. The runtime mounts the image read-only and sets
/// <c>APPIMAGE</c> to the file the user started, so that file, not the mount, is what a sign-in
/// entry has to start, and an update means replacing that file, which DeckHost doesn't do itself.
/// </summary>
internal static class AppImage
{
    /// <summary>The AppImage file DeckHost was started from, or null when it isn't running from one.</summary>
    public static string? Path => Resolve(Environment.GetEnvironmentVariable("APPIMAGE"));

    /// <summary>Whether DeckHost is running from an AppImage.</summary>
    public static bool IsActive => Path is not null;

    /// <summary>The value of <c>APPIMAGE</c> when it names an existing file (a stale variable inherited from another process doesn't count).</summary>
    internal static string? Resolve(string? value) =>
        OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(value) && File.Exists(value) ? System.IO.Path.GetFullPath(value) : null;
}
