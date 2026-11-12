namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Drawing surface for one control. Coordinates are device pixels of that control.
/// </summary>
public interface IRenderContext
{
    string DeviceId { get; }
    string ControlId { get; }
    int Width { get; }
    int Height { get; }
    IVariableStore Variables { get; }

    /// <summary>The settings of the action being drawn.</summary>
    Models.ActionSettings Settings { get; }

    /// <summary>The profile being drawn.</summary>
    string ProfileId { get; }

    /// <summary>
    /// True when the action only has a low, wide strip to draw in (a knob's status line under the
    /// action of the touch zone it shares): draw one short line instead of the full layout.
    /// </summary>
    bool IsCompact => false;

    /// <summary>Draws an encoded image (PNG, JPG, GIF, WebP bytes) scaled to cover the control.</summary>
    bool DrawEncodedImage(byte[] encodedImage);

    /// <summary>
    /// Draws the control's label (multi-line, wrapped to the control's width) using the user's text
    /// style from Customize: color, font, size, bold/italic and alignment. Values the user didn't
    /// set fall back to the arguments, which describe how the action would draw it by default.
    /// </summary>
    void DrawLabel(
        string text,
        Models.TextVerticalAlignment defaultVertical = Models.TextVerticalAlignment.Middle,
        float? defaultSize = null,
        uint defaultColor = 0xFFFFFFFF);

    void Clear(uint argbColor);

    /// <summary>
    /// Draws text at a position the action chooses, in the user's font, bold and italic (the size,
    /// color and place are the action's; use <see cref="DrawLabel"/> for the control's label).
    /// </summary>
    void DrawText(string text, double x, double y, float size, uint argbColor = 0xFFFFFFFF);

    /// <summary>
    /// Draws text centered on a point, like <see cref="DrawText"/>. The size shrinks when needed
    /// so the text fits the control's width.
    /// </summary>
    void DrawCenteredText(string text, double centerX, double centerY, float size, uint argbColor = 0xFFFFFFFF);

    void FillRoundedRect(double x, double y, double width, double height, double radius, uint argbColor);

    /// <summary>Draws a raw RGBA8888 buffer, scaled to fill the control.</summary>
    void DrawImage(byte[] rgbaPixels, int width, int height);

    /// <summary>Draws an image file scaled to cover the control. Returns false when the file can't be decoded.</summary>
    bool DrawImageFile(string path);
}
