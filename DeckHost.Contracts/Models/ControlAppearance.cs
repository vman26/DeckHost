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

    /// <summary>
    /// Draw the action's colored tile behind its icon. Off by default: controls are transparent, so
    /// the profile background shows, unless the user picks the tile or a <see cref="BackgroundColor"/>.
    /// </summary>
    public bool ShowTile { get; init; }

    /// <summary>How the tile looks when <see cref="ShowTile"/> is on; null for the action's own colors and corners.</summary>
    public TileStyle? Tile { get; init; }
}

/// <summary>
/// A background for a whole profile. The image spans the device layout, so every control shows
/// its own slice of it (one picture across all keys). GIFs animate.
/// </summary>
/// <summary>
/// The look of an action's tile: its colors (a gradient from <paramref name="From"/> to
/// <paramref name="To"/>, top left to bottom right, or one color without <paramref name="To"/>) and
/// how round its corners are (<paramref name="CornerPercent"/> of the key's width: 0 is square, 50 a circle).
/// Each part left null keeps the action's own.
/// </summary>
public sealed record TileStyle(string? From = null, string? To = null, double? CornerPercent = null)
{
    /// <summary>The corner rounding of the action's own tile.</summary>
    public const double DefaultCornerPercent = 16;
}

public sealed record ProfileBackground(string? Color = null, string? ImagePath = null)
{
    /// <summary>
    /// More layers, drawn over <see cref="Color"/> and <see cref="ImagePath"/> (which are the bottom ones), in this
    /// order. An init property, so the record's constructor stays binary-compatible.
    /// </summary>
    public IReadOnlyList<BackgroundLayer>? Layers { get; init; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Color) && string.IsNullOrWhiteSpace(ImagePath) && Layers is not { Count: > 0 };
}

/// <summary>The kinds of <see cref="BackgroundLayer"/> DeckHost draws; an unknown kind is kept in the file and not drawn.</summary>
public static class BackgroundLayerKinds
{
    /// <summary>One color over the whole layout.</summary>
    public const string Solid = "solid";

    /// <summary>A gradient across the whole layout, from <see cref="BackgroundLayer.Color"/> to <see cref="BackgroundLayer.ToColor"/> at <see cref="BackgroundLayer.Angle"/> degrees.</summary>
    public const string Gradient = "gradient";

    /// <summary>
    /// A radial gradient across the whole layout: from the center (<see cref="BackgroundLayer.CenterX"/>, <see cref="BackgroundLayer.CenterY"/>)
    /// out to <see cref="BackgroundLayer.Radius"/>, through the same colors as a <see cref="Gradient"/>.
    /// </summary>
    public const string Radial = "radial";

    /// <summary>A picture (or animated GIF) spread over the whole layout, every control showing its slice.</summary>
    public const string Image = "image";

    public static IReadOnlyList<string> Known { get; } = [Solid, Gradient, Radial, Image];
}

/// <summary>What a bound property of a layer follows.</summary>
public static class BackgroundBindingTargets
{
    /// <summary>The layer's color (a solid layer's color, a gradient's end color) goes from <see cref="BackgroundBinding.FromColor"/> to <see cref="BackgroundBinding.ToColor"/>.</summary>
    public const string Color = "color";

    /// <summary>The layer's opacity goes from 0 to the layer's own <see cref="BackgroundLayer.Opacity"/>.</summary>
    public const string Opacity = "opacity";

    /// <summary>The layer shows while the variable is at or above <see cref="BackgroundBinding.Min"/> (true counts as 1).</summary>
    public const string Visible = "visible";

    public static IReadOnlyList<string> All { get; } = [Color, Opacity, Visible];
}

/// <summary>
/// One layer of a profile background, drawn over the layers before it. <paramref name="Kind"/> is text so a layer of a
/// kind this version doesn't know survives a save.
/// </summary>
/// <param name="Visible">Off keeps the layer but doesn't draw it.</param>
/// <param name="Opacity">0 to 1.</param>
public sealed record BackgroundLayer(string Kind, bool Visible = true, double Opacity = 1)
{
    /// <summary>#RRGGBB or #AARRGGBB: a solid layer's color, a gradient's start.</summary>
    public string? Color { get; init; }

    /// <summary>A gradient's end color.</summary>
    public string? ToColor { get; init; }

    /// <summary>A gradient's direction in degrees: 0 left to right, 90 top to bottom.</summary>
    public double? Angle { get; init; }

    /// <summary>
    /// A gradient's colors with their places along it. With two or more, they replace <see cref="Color"/> and <see cref="ToColor"/>
    /// (which still hold the first and last color, so older versions show a two-color gradient).
    /// </summary>
    public IReadOnlyList<GradientStop>? Stops { get; init; }

    /// <summary>A radial gradient's center across the layout, 0 (left) to 1 (right); 0.5 when not set.</summary>
    public double? CenterX { get; init; }

    /// <summary>A radial gradient's center down the layout, 0 (top) to 1 (bottom); 0.5 when not set.</summary>
    public double? CenterY { get; init; }

    /// <summary>A radial gradient's radius as a share of the layout's half diagonal (1 reaches the corners); 1 when not set.</summary>
    public double? Radius { get; init; }

    public string? ImagePath { get; init; }

    /// <summary>Makes a property of the layer follow a variable (the CPU load, a Set variable node's value, …).</summary>
    public BackgroundBinding? Binding { get; init; }
}

/// <summary>One color of a gradient and where it starts: <paramref name="Position"/> 0 (the start) to 1 (the end).</summary>
public sealed record GradientStop(string Color, double Position);

/// <summary>
/// A layer property that follows a variable. The value is read as a number between <paramref name="Min"/> and
/// <paramref name="Max"/> (<c>true</c> counts as 1, anything unreadable as <see cref="Fallback"/>, else 0).
/// </summary>
public sealed record BackgroundBinding(string Variable, string Target = BackgroundBindingTargets.Color, double Min = 0, double Max = 100)
{
    /// <summary>The color at <see cref="Min"/> (target color).</summary>
    public string? FromColor { get; init; }

    /// <summary>The color at <see cref="Max"/> (target color).</summary>
    public string? ToColor { get; init; }

    /// <summary>The value to use while the variable has none (or isn't a number).</summary>
    public double? Fallback { get; init; }
}
