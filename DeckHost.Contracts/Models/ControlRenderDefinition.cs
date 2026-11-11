namespace DeckHost.Contracts.Models;

/// <summary>
/// Defines how and where a control should be rendered on the device.
/// </summary>
public sealed record ControlRenderDefinition(
    /// <summary>
    /// Logical control ID matching PhysicalControl.Id
    /// </summary>
    string ControlId,

    /// <summary>
    /// Target render width in pixels
    /// </summary>
    int Width,

    /// <summary>
    /// Target render height in pixels
    /// </summary>
    int Height,

    /// <summary>
    /// Physical control type (for rendering hints)
    /// </summary>
    ControlType ControlType);
