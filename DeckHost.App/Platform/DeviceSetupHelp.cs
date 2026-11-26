using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DeckHost.App.Platform;

/// <summary>The system the device help is for.</summary>
internal enum HelpSystem
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>What DeckHost could find out about why no device shows up.</summary>
/// <param name="AppImagePath">The AppImage DeckHost runs from (Linux), else null.</param>
/// <param name="RuleInstalled">Whether DeckHost's udev rule is installed (Linux); null elsewhere.</param>
/// <param name="RuleBesideProgram">The rule file next to the program (the Linux tar.gz), else null.</param>
/// <param name="SupportedDevicePlugged">Whether a USB device with a MiraBox or Elgato vendor id is plugged in (Linux).</param>
/// <param name="HoldingApps">The vendor apps that are running and hold the device (Windows, macOS).</param>
internal sealed record DeviceSetupFacts(
    HelpSystem System,
    string? AppImagePath = null,
    bool? RuleInstalled = null,
    string? RuleBesideProgram = null,
    bool SupportedDevicePlugged = false,
    IReadOnlyList<string>? HoldingApps = null);

/// <summary>The help the welcome tour shows when no device is connected.</summary>
/// <param name="Text">What to do, in a few sentences.</param>
/// <param name="Commands">Terminal commands to copy (Linux), else null.</param>
/// <param name="DocsPath">A page of the documentation site with the details (relative to its root), else null.</param>
/// <param name="IsProblem">Something was found that keeps the device from working (a missing rule, an app that holds it).</param>
internal sealed record DeviceSetupAdvice(string Text, string? Commands = null, string? DocsPath = null, bool IsProblem = false);

/// <summary>
/// Why a control surface may not show up, per system: on Linux the udev rule (and, for an AppImage,
/// how to take it out of the image), on Windows and macOS a vendor app that holds the device. The
/// decisions are pure (<see cref="Advise"/>); <see cref="Gather"/> looks at the system.
/// </summary>
internal static class DeviceSetupHelp
{
    /// <summary>DeckHost's udev rule (<c>packaging/linux</c>).</summary>
    public const string RuleFileName = "70-deckhost-mirabox.rules";

    /// <summary>The documentation page about the AppImage's device rule.</summary>
    public const string AppImageDocsPath = "devices/#linux-appimage";

    private const string ReloadRules = "sudo udevadm control --reload-rules && sudo udevadm trigger";

    /// <summary>Where udev reads rules from.</summary>
    internal static readonly string[] RuleDirectories = ["/etc/udev/rules.d", "/usr/lib/udev/rules.d", "/lib/udev/rules.d"];

    /// <summary>The USB vendor ids of the devices the rule covers: MiraBox / StreamDock (every model in the SDK) and Elgato.</summary>
    internal static readonly string[] SupportedVendorIds = ["5548", "5500", "6602", "6603", "0fd9"];

    /// <summary>Whether a USB vendor id (as <c>/sys/bus/usb/devices/*/idVendor</c> has it) is one DeckHost drives (pure).</summary>
    public static bool IsSupportedVendor(string? vendorId) =>
        vendorId is not null && SupportedVendorIds.Contains(vendorId.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    /// <summary>The vendor apps among running processes that would hold a device (pure): the Elgato Stream Deck app and the MiraBox StreamDock app.</summary>
    public static IReadOnlyList<string> HoldingApps(IEnumerable<string> processNames)
    {
        var apps = new List<string>();
        foreach (var name in processNames)
        {
            var plain = name.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            var app = plain switch
            {
                "streamdeck" => "the Elgato Stream Deck app",
                "streamdock" or "mirabox" or "miraboxstreamdock" or "streamdockai" => "the MiraBox StreamDock app",
                _ => null,
            };
            if (app is not null && !apps.Contains(app))
            {
                apps.Add(app);
            }
        }

        return apps;
    }

    /// <summary>A path for a POSIX shell, in single quotes (pure).</summary>
    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>The commands that take the rule out of an AppImage and install it (pure; docs/devices.md#linux-appimage).</summary>
    public static string AppImageCommands(string appImagePath) =>
        "cd \"$(mktemp -d)\"\n"
        + $"{ShellQuote(appImagePath)} --appimage-extract usr/bin/{RuleFileName}\n"
        + $"sudo install -m 0644 squashfs-root/usr/bin/{RuleFileName} /etc/udev/rules.d/\n"
        + ReloadRules;

    /// <summary>The commands that install the rule file shipped next to the program (pure).</summary>
    public static string RuleFileCommands(string ruleFile) =>
        $"sudo install -m 0644 {ShellQuote(ruleFile)} /etc/udev/rules.d/\n" + ReloadRules;

    /// <summary>What to tell someone whose device doesn't show up (pure).</summary>
    public static DeviceSetupAdvice Advise(DeviceSetupFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.System == HelpSystem.Linux ? AdviseLinux(facts) : AdviseDesktop(facts);
    }

    private static DeviceSetupAdvice AdviseLinux(DeviceSetupFacts facts)
    {
        const string Replug = " Then unplug the device and plug it in again.";
        if (facts.RuleInstalled == true)
        {
            return new DeviceSetupAdvice(facts.SupportedDevicePlugged
                ? "The device rule is installed and a device is plugged in. If it doesn't show up, unplug it and plug it in again, directly rather than through a hub, and make sure no other program (such as a vendor app) uses it."
                : "The device rule is installed. Plug the device in directly (not through a monitor's hub, if it doesn't show up).");
        }

        var start = facts.SupportedDevicePlugged
            ? "A device is plugged in, but the device rule is missing: without it DeckHost can't open the device unless it runs as root."
            : "On Linux DeckHost needs its device rule to open the device without root.";
        if (facts.AppImagePath is { } appImage)
        {
            return new DeviceSetupAdvice(
                start + " The AppImage can't install it itself: run these commands once in a terminal (they take the rule out of the AppImage and install it)." + Replug,
                AppImageCommands(appImage),
                AppImageDocsPath,
                facts.SupportedDevicePlugged);
        }

        return facts.RuleBesideProgram is { } rule
            ? new DeviceSetupAdvice(start + " Run ./install.sh from the download once, or install the rule with these commands." + Replug, RuleFileCommands(rule), "devices/", facts.SupportedDevicePlugged)
            : new DeviceSetupAdvice(start + " Run ./install.sh from the download once: it adds the rule." + Replug, null, "devices/", facts.SupportedDevicePlugged);
    }

    private static DeviceSetupAdvice AdviseDesktop(DeviceSetupFacts facts)
    {
        if (facts.HoldingApps is { Count: > 0 } apps)
        {
            var names = string.Join(" and ", apps);
            return new DeviceSetupAdvice(
                $"{char.ToUpperInvariant(names[0])}{names[1..]} {(apps.Count == 1 ? "is" : "are")} running and {(apps.Count == 1 ? "holds" : "hold")} the device: only one program can drive it at a time. Quit {(apps.Count == 1 ? "it" : "them")} (also from the {(facts.System == HelpSystem.MacOS ? "menu bar" : "tray")}), then choose Look again.",
                IsProblem: true);
        }

        return new DeviceSetupAdvice(facts.System == HelpSystem.MacOS
            ? "Plug the device in directly (not through a monitor's hub, if it doesn't show up). Close the Elgato Stream Deck app first: only one program can drive a Stream Deck."
            : "Plug the device in directly. Close the Elgato Stream Deck app or the MiraBox app first: only one program can drive the device at a time.");
    }

    /// <summary>Looks at this system: the rule files, the plugged-in USB devices and the running processes. Takes a moment; call it off the UI thread.</summary>
    public static DeviceSetupFacts Gather()
    {
        if (OperatingSystem.IsLinux())
        {
            var besideProgram = Path.Combine(AppContext.BaseDirectory, RuleFileName);
            return new DeviceSetupFacts(
                HelpSystem.Linux,
                AppImage.Path,
                RuleDirectories.Any(static directory => File.Exists(Path.Combine(directory, RuleFileName))),
                AppImage.IsActive || !File.Exists(besideProgram) ? null : besideProgram,
                PluggedVendorIds().Any(IsSupportedVendor));
        }

        return new DeviceSetupFacts(OperatingSystem.IsMacOS() ? HelpSystem.MacOS : HelpSystem.Windows, HoldingApps: HoldingApps(ProcessNames()));
    }

    private static IEnumerable<string> PluggedVendorIds()
    {
        string[] files;
        try
        {
            files = Directory.GetDirectories("/sys/bus/usb/devices").Select(static directory => Path.Combine(directory, "idVendor")).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            string? vendor = null;
            try
            {
                vendor = File.Exists(file) ? File.ReadAllText(file) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            if (vendor is not null)
            {
                yield return vendor;
            }
        }
    }

    private static List<string> ProcessNames()
    {
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                names.Add(process.ProcessName);
            }
            catch (InvalidOperationException)
            {
                // Ended while listing.
            }
            finally
            {
                process.Dispose();
            }
        }

        return names;
    }
}
