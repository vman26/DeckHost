using DeckHost.App.Platform;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace DeckHost.App.SystemPlugins;

internal sealed class LaunchAppAction() : DeckActionBase(
    "system.launch",
    "Launch App",
    InputCapabilities.Press,
    OutputCapabilities.None,
    "Starts a program, script or shortcut.")
{
    private const string PathKey = "path";
    private const string ArgumentsKey = "arguments";
    private const string WorkingDirectoryKey = "workingDirectory";

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(PathKey, "Program", ActionSettingKind.FilePath, Placeholder: @"C:\Program Files\App\app.exe", Description: "An .exe, a shortcut (.lnk) or any file Windows can open."),
        new(ArgumentsKey, "Arguments", Placeholder: "Optional"),
        new(WorkingDirectoryKey, "Start in", Placeholder: "Optional folder"),
    ];

    protected override void Run(ActionContext context)
    {
        var path = context.Settings.Get(PathKey);
        var arguments = context.Settings.Get(ArgumentsKey);
        var workingDirectory = context.Settings.Get(WorkingDirectoryKey);

        RunInBackground(context, () =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Choose a program in Customize.");
            }

            var startInfo = new ProcessStartInfo(path, arguments) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }
            else if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                startInfo.WorkingDirectory = directory;
            }

            using var _ = Process.Start(startInfo);
        });
    }

    protected override string Title(IRenderContext context)
    {
        var path = context.Settings.Get(PathKey);
        return string.IsNullOrWhiteSpace(path) ? Name : Path.GetFileNameWithoutExtension(path);
    }

    protected override string? Subtitle(IRenderContext context) =>
        string.IsNullOrWhiteSpace(context.Settings.Get(PathKey)) ? "Not set" : null;
}

internal sealed class OpenUrlAction() : DeckActionBase(
    "system.open",
    "Open Website or File",
    InputCapabilities.Press,
    OutputCapabilities.None,
    "Opens a web page, file or folder with its default app.")
{
    private const string TargetKey = "target";

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(TargetKey, "Address or path", Placeholder: "https://example.com or C:\\Folder\\file.pdf"),
    ];

    protected override void Run(ActionContext context)
    {
        var target = context.Settings.Get(TargetKey).Trim();
        RunInBackground(context, () =>
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                throw new InvalidOperationException("Enter an address in Customize.");
            }

            // Only web/mail links and existing files or folders: no arbitrary command lines.
            var isLink = Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";
            if (!isLink && !File.Exists(target) && !Directory.Exists(target))
            {
                throw new InvalidOperationException("Not a web address or an existing file.");
            }

            using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        });
    }

    protected override string Title(IRenderContext context)
    {
        var target = context.Settings.Get(TargetKey);
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        }

        return string.IsNullOrWhiteSpace(target) ? "Open" : Path.GetFileName(target.TrimEnd('\\', '/'));
    }

    protected override string? Subtitle(IRenderContext context) => null;
}

internal sealed class HotkeyAction() : DeckActionBase(
    "system.hotkey",
    "Hotkey",
    InputCapabilities.Press,
    OutputCapabilities.None,
    "Presses a key combination in the active app.")
{
    private const string KeysKey = "keys";

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(KeysKey, "Keys", ActionSettingKind.Hotkey, Placeholder: "Ctrl+Shift+M", Description: "Click Record and press the combination, or type it: Ctrl, Alt, Shift, Win plus one key (A-Z, 0-9, F1-F24, Enter, Tab, Esc, Space, arrows, Home, End, PageUp, PageDown, Delete, PrintScreen, media keys)."),
    ];

    protected override void Run(ActionContext context)
    {
        var keys = context.Settings.Get(KeysKey);
        RunQueued(context, () =>
        {
            if (!Hotkey.TryParse(keys, out var hotkey, out var error))
            {
                throw new InvalidOperationException(error);
            }

            SystemControl.SendHotkey(hotkey);
        });
    }

    protected override string Title(IRenderContext context) => context.Settings.Get(KeysKey, Name);

    protected override string? Subtitle(IRenderContext context) => null;
}

internal sealed class TypeTextAction() : DeckActionBase(
    "system.type-text",
    "Type Text",
    InputCapabilities.Press,
    OutputCapabilities.None,
    "Types a piece of text into the active app.")
{
    private const string TextKey = "text";

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(TextKey, "Text", Placeholder: "Text to type"),
    ];

    protected override void Run(ActionContext context)
    {
        var text = context.Settings.Get(TextKey);
        RunQueued(context, () => SystemControl.TypeText(text));
    }

    protected override string? Subtitle(IRenderContext context) => null;
}

internal sealed class LockComputerAction() : DeckActionBase(
    "system.lock",
    "Lock Computer",
    InputCapabilities.Press,
    OutputCapabilities.None,
    "Locks the computer (Win+L on Windows).")
{
    public override bool CanBeMultiActionStep => true;

    protected override void Run(ActionContext context) => RunQueued(context, SystemControl.LockScreen);

    protected override string? Subtitle(IRenderContext context) => null;
}

/// <summary>Live CPU, memory, disk or uptime reading with a usage bar.</summary>
internal sealed class SystemInfoAction() : DeckActionBase(
    "system.info",
    "System Info",
    InputCapabilities.None,
    OutputCapabilities.Text,
    "Shows CPU, memory, disk or uptime, updated every second.")
{
    private const string MetricKey = "metric";
    private const string DriveKey = "drive";
    private const uint BarNormal = 0xFF22C55E;
    private const uint BarWarning = 0xFFF59E0B;
    private const uint BarCritical = 0xFFEF4444;

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(MetricKey, "Show", ActionSettingKind.Choice, "cpu", Options:
        [
            new("cpu", "CPU usage"),
            new("memory", "Memory usage"),
            new("disk", "Free disk space"),
            new("uptime", "Uptime"),
        ]),
        new(DriveKey, "Drive", Placeholder: @"C:\", Description: "Used with 'Free disk space'."),
    ];

    public override bool CanBeMultiActionStep => false;

    public override TimeSpan? RefreshInterval => TimeSpan.FromSeconds(1);

    public override void Render(IRenderContext context)
    {
        var (label, value, fraction) = Read(context.Settings);
        context.DrawReading(ResolveDisplayText(context, label), value, fraction,
            fraction >= 0.9 ? BarCritical : fraction >= 0.75 ? BarWarning : BarNormal);
    }

    private static (string Label, string Value, double? Fraction) Read(ActionSettings settings)
    {
        switch (settings.Get(MetricKey, "cpu"))
        {
            case "memory":
                if (SystemMetrics.Memory() is not { } memory)
                {
                    return ("Memory", "n/a", null);
                }

                var used = (double)memory.Used / memory.Total;
                return ("Memory", used.ToString("P0", CultureInfo.CurrentCulture), used);

            case "disk":
                var drive = settings.Get(DriveKey, Path.GetPathRoot(Environment.SystemDirectory) ?? "/");
                if (SystemMetrics.Disk(drive) is not { } disk)
                {
                    return ($"Disk {drive}", "n/a", null);
                }

                return ($"Free {drive.TrimEnd('\\', '/')}", SystemMetrics.FormatBytes(disk.Free), 1d - ((double)disk.Free / disk.Total));

            case "uptime":
                var uptime = SystemMetrics.Uptime();
                return ("Uptime", uptime.TotalDays >= 1 ? $"{(int)uptime.TotalDays}d {uptime.Hours}h" : $"{uptime.Hours}h {uptime.Minutes:00}m", null);

            default:
                return SystemMetrics.CpuUsage() is { } cpu
                    ? ("CPU", cpu.ToString("P0", CultureInfo.CurrentCulture), cpu)
                    : ("CPU", "n/a", null);
        }
    }
}
