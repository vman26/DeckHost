namespace DeckHost.Contracts.Models;

/// <summary>
/// Visual appearance of a control, drawn under the action's own content.
/// </summary>
/// <param name="Text">Label text; replaces the action's own title.</param>
/// <param name="ImagePath">PNG, JPG, WebP or (animated) GIF, scaled to cover the control.</param>
/// <param name="RgbFeedback">LED feedback for controls with a light.</param>
/// <param name="BackgroundColor">Solid fill (#RRGGBB) drawn over the profile background.</param>
/// <param name="TextStyle">Color, font and alignment of the label (see <see cref="Interfaces.IRenderContext.DrawLabel"/>).</param>
public sealed record ControlAppearance(
    string? Text = null,
    string? ImagePath = null,
    RgbFeedback? RgbFeedback = null,
    string? BackgroundColor = null,
    TextStyle? TextStyle = null)
{
    /// <summary>
    /// For a control that borrows another control's display (<see cref="PhysicalControl.DisplayControlId"/>,
    /// a knob and the touch zone above it): whether its action is shown there. It fills the display
    /// when that control has no action of its own, and gets a strip along the edge when it has.
    /// A property rather than a constructor parameter, so plugins built against the earlier
    /// constructor keep working.
    /// </summary>
    public bool ShowOnLinkedDisplay { get; init; } = true;

    /// <summary>
    /// An icon file (SVG, PNG, ...) from the icon library that takes the place of the action's own
    /// icon on its tile. Unlike <see cref="ImagePath"/> it keeps the tile and the caption; a
    /// single-color SVG is drawn in white. An init property, so the constructor stays binary-compatible.
    /// </summary>
    public string? IconPath { get; init; }
}

/// <summary>
/// A background for a whole profile. The image spans the device layout, so every control shows
/// its own slice of it (one picture across all keys). GIFs animate.
/// </summary>
public sealed record ProfileBackground(string? Color = null, string? ImagePath = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Color) && string.IsNullOrWhiteSpace(ImagePath);
}
