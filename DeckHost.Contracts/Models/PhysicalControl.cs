using DeckHost.Contracts.Enums;

namespace DeckHost.Contracts.Models;

/// <summary>
/// One physical control on a device.
/// </summary>
/// <param name="Id">Stable logical id, for example <c>key.0</c>, <c>encoder.2</c> or <c>touch.1</c>.</param>
/// <param name="Bounds">Position in the designer's layout coordinates (not device pixels).</param>
/// <param name="RenderWidth">Pixel width of the control's own image on the device; 0 when it has none.</param>
/// <param name="RenderHeight">Pixel height of the control's own image on the device; 0 when it has none.</param>
/// <param name="HardwareId">The id the hardware/SDK uses for this control, for diagnostics.</param>
/// <param name="LedIndex">Index of the RGB LED tied to this control, when there is one.</param>
/// <param name="DisplayControlId">
/// For a control without a display of its own: the id of the control whose display belongs to it
/// (the touch zone above a knob). The host shows this control's action there; see
/// <see cref="ControlAppearance.ShowOnLinkedDisplay"/>.
/// </param>
public sealed record PhysicalControl(
    string Id,
    string DisplayName,
    ControlType Type,
    Rect Bounds,
    InputCapabilities Inputs,
    OutputCapabilities Outputs,
    int RenderWidth = 0,
    int RenderHeight = 0,
    string? HardwareId = null,
    int? LedIndex = null,
    string? DisplayControlId = null)
{
    public bool HasImageOutput => RenderWidth > 0 && RenderHeight > 0 && Outputs != OutputCapabilities.None;

    public bool HasLight => LedIndex is not null;

    public bool HasLinkedDisplay => !string.IsNullOrEmpty(DisplayControlId);
}
