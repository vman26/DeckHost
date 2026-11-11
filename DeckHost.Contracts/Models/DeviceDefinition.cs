namespace DeckHost.Contracts.Models;

/// <summary>
/// A device a plugin can drive. Plugins also expose definitions for devices that are not
/// plugged in, so profiles can be edited offline; <see cref="IsConnected"/> tells them apart.
/// </summary>
public sealed record DeviceDefinition(
    string Id,
    string Name,
    IReadOnlyList<PhysicalControl> Controls,
    bool IsConnected = false,
    string? Detail = null)
{
    public PhysicalControl? FindControl(string controlId) =>
        Controls.FirstOrDefault(control => string.Equals(control.Id, controlId, StringComparison.OrdinalIgnoreCase));
}
