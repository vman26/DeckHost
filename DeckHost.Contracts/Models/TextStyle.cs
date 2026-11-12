namespace DeckHost.Contracts.Models;

public enum TextHorizontalAlignment
{
    Left,
    Center,
    Right,
}

public enum TextVerticalAlignment
{
    Top,
    Middle,
    Bottom,
}

/// <summary>
/// How a control's label is drawn. Every field is optional: null means "use what the action
/// would normally do". Applied by <see cref="Interfaces.IRenderContext.DrawLabel"/>.
/// </summary>
/// <param name="Color">#RRGGBB or #AARRGGBB.</param>
/// <param name="FontFamily">Installed font family name, e.g. "Segoe UI".</param>
/// <param name="Size">Font size in device pixels.</param>
/// <param name="Wrap">Wrap long lines at word boundaries (line breaks in the text always work).</param>
public sealed record TextStyle(
    string? Color = null,
    string? FontFamily = null,
    float? Size = null,
    bool Bold = false,
    bool Italic = false,
    TextHorizontalAlignment? Horizontal = null,
    TextVerticalAlignment? Vertical = null,
    bool Wrap = true);
