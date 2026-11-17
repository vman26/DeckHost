namespace DeckHost.Contracts.Models;

/// <summary>
/// A device a plugin can drive: its descriptor (master plan 7.1). Plugins also expose definitions
/// for devices that are not plugged in, so profiles can be edited offline; <see cref="IsConnected"/>
/// tells them apart. The control topology is <see cref="Controls"/>; identity, displays,
/// capabilities and limits are optional properties, so plugins built before them keep working.
/// When the topology changes, the plugin raises <c>DevicesChanged</c> with the new definition;
/// bindings are kept by control id, so a control that disappears doesn't move others.
/// </summary>
public sealed record DeviceDefinition(
    string Id,
    string Name,
    IReadOnlyList<PhysicalControl> Controls,
    bool IsConnected = false,
    string? Detail = null)
{
    /// <summary>The descriptor format version this definition follows.</summary>
    public const int CurrentDescriptorVersion = 1;

    /// <summary>The descriptor format version; plugins built for an older one say so.</summary>
    public int DescriptorVersion { get; init; } = CurrentDescriptorVersion;

    /// <summary>Manufacturer, model, serial, revision, firmware and connection; null when unknown.</summary>
    public DeviceIdentity? Identity { get; init; }

    /// <summary>The displays or display regions; empty when the device has none or doesn't say.</summary>
    public IReadOnlyList<DeviceDisplay> Displays { get; init; } = [];

    /// <summary>What the device can do.</summary>
    public DeviceCapabilities Capabilities { get; init; }

    /// <summary>What it can't do, or not fully; null when nothing is known.</summary>
    public DeviceLimits? Limits { get; init; }

    /// <summary>The device's outer size in millimetres; null when unknown.</summary>
    public PhysicalSize? PhysicalSize { get; init; }

    /// <summary>Whether the device declares <paramref name="capability"/> (all of the given flags).</summary>
    public bool Has(DeviceCapabilities capability) => (Capabilities & capability) == capability;

    public DeviceDisplay? FindDisplay(string? displayId) =>
        displayId is null ? null : Displays.FirstOrDefault(display => string.Equals(display.Id, displayId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What's inconsistent in this descriptor, in words (empty when it's fine): duplicate control
    /// ids, controls with images but no display (when the device lists displays), displays that list
    /// unknown controls or none, links to unknown controls. Device plugin authors can test with it.
    /// </summary>
    public IReadOnlyList<string> FindDescriptorProblems()
    {
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in Controls)
        {
            if (!ids.Add(control.Id))
            {
                problems.Add($"Control id {control.Id} is used twice.");
            }
        }

        foreach (var control in Controls)
        {
            if (control.DisplayControlId is { } linked && FindControl(linked) is null)
            {
                problems.Add($"{control.Id} links to {linked}, which isn't a control of this device.");
            }

            if (control.DisplayId is { } displayId && FindDisplay(displayId) is null)
            {
                problems.Add($"{control.Id} is on display {displayId}, which isn't listed.");
            }

            if (Displays.Count > 0 && control.HasImageOutput && control.DisplayId is null)
            {
                problems.Add($"{control.Id} shows images but names no display.");
            }
        }

        foreach (var display in Displays)
        {
            if (display.Width <= 0 || display.Height <= 0)
            {
                problems.Add($"Display {display.Id} has no size.");
            }

            foreach (var controlId in display.ControlIds)
            {
                if (FindControl(controlId) is not { } control)
                {
                    problems.Add($"Display {display.Id} lists {controlId}, which isn't a control of this device.");
                }
                else if (!string.Equals(control.DisplayId, display.Id, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"Display {display.Id} lists {controlId}, but that control says it's on {control.DisplayId ?? "no display"}.");
                }
            }
        }

        return problems;
    }

    public PhysicalControl? FindControl(string controlId) =>
        Controls.FirstOrDefault(control => string.Equals(control.Id, controlId, StringComparison.OrdinalIgnoreCase));
}
