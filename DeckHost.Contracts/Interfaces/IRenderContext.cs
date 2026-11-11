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

    void Clear(uint argbColor);

    void DrawText(string text, double x, double y, float size, uint argbColor = 0xFFFFFFFF);

    /// <summary>Draws text centered on a point. The size shrinks when needed so the text fits the control's width.</summary>
    void DrawCenteredText(string text, double centerX, double centerY, float size, uint argbColor = 0xFFFFFFFF);

    void FillRoundedRect(double x, double y, double width, double height, double radius, uint argbColor);

    /// <summary>Draws a raw RGBA8888 buffer, scaled to fill the control.</summary>
    void DrawImage(byte[] rgbaPixels, int width, int height);

    /// <summary>Draws an image file scaled to cover the control. Returns false when the file can't be decoded.</summary>
    bool DrawImageFile(string path);
}
