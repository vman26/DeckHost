using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Plugins.Isolation;

/// <summary>
/// The plugin process's <see cref="IRenderContext"/>: it records what the action draws instead of
/// drawing it. DeckHost replays the commands on the real surface (<see cref="DisplayList.Replay"/>).
/// </summary>
public sealed class RecordingRenderContext(WireContext context, IVariableStore variables) : IRenderContext
{
    private readonly List<DrawCommand> _commands = [];

    public IReadOnlyList<DrawCommand> Commands => _commands;

    public string DeviceId => context.DeviceId;
    public string ControlId => context.ControlId;
    public int Width => context.Width;
    public int Height => context.Height;
    public IVariableStore Variables => variables;
    public ActionSettings Settings => context.Settings;
    public string ProfileId => context.ProfileId;
    public bool IsCompact => context.IsCompact;
    public TextStyle? TextStyle => context.TextStyle;

    public bool DrawEncodedImage(byte[] encodedImage) => DrawEncodedImage(encodedImage, clearEdgeBlack: false);

    public bool DrawEncodedImage(byte[] encodedImage, bool clearEdgeBlack)
    {
        _commands.Add(new DrawCommand("encoded") { Data = encodedImage, Flag = clearEdgeBlack });
        return encodedImage.Length > 0;
    }

    public void DrawLabel(string text, TextVerticalAlignment defaultVertical = TextVerticalAlignment.Middle, float? defaultSize = null, uint defaultColor = 0xFFFFFFFF) =>
        _commands.Add(new DrawCommand("label") { Text = text, Vertical = defaultVertical, DefaultSize = defaultSize, Color = defaultColor });

    public void Clear(uint argbColor) => _commands.Add(new DrawCommand("clear") { Color = argbColor });

    public void DrawText(string text, double x, double y, float size, uint argbColor = 0xFFFFFFFF) =>
        _commands.Add(new DrawCommand("text") { Text = text, X = x, Y = y, Size = size, Color = argbColor });

    public void DrawCenteredText(string text, double centerX, double centerY, float size, uint argbColor = 0xFFFFFFFF) =>
        _commands.Add(new DrawCommand("centered") { Text = text, X = centerX, Y = centerY, Size = size, Color = argbColor });

    public void FillRoundedRect(double x, double y, double width, double height, double radius, uint argbColor) =>
        _commands.Add(new DrawCommand("rect") { X = x, Y = y, W = width, H = height, R = radius, Color = argbColor });

    public void DrawImage(byte[] rgbaPixels, int width, int height) =>
        _commands.Add(new DrawCommand("rgba") { Data = rgbaPixels, PixelWidth = width, PixelHeight = height });

    public bool DrawImageFile(string path)
    {
        _commands.Add(new DrawCommand("file") { Text = path });
        return File.Exists(path);
    }

    public void DrawIcon(string svgPathData, double centerX, double centerY, double size, uint argbColor = 0xFFFFFFFF) =>
        _commands.Add(new DrawCommand("icon") { Text = svgPathData, X = centerX, Y = centerY, W = size, Color = argbColor });

    public void FillGradientRect(double x, double y, double width, double height, double radius, uint fromArgb, uint toArgb) =>
        _commands.Add(new DrawCommand("gradient") { X = x, Y = y, W = width, H = height, R = radius, Color = fromArgb, Color2 = toArgb });

    public void DrawRightAlignedText(string text, double right, double baselineY, float size, uint argbColor = 0xFFFFFFFF) =>
        _commands.Add(new DrawCommand("right") { Text = text, X = right, Y = baselineY, Size = size, Color = argbColor });
}

public static class DisplayList
{
    /// <summary>Draws recorded commands on a real surface.</summary>
    public static void Replay(IRenderContext target, IReadOnlyList<DrawCommand> commands)
    {
        foreach (var command in commands)
        {
            switch (command.Op)
            {
                case "encoded" when command.Data is { } data:
                    target.DrawEncodedImage(data, command.Flag);
                    break;
                case "label":
                    target.DrawLabel(command.Text ?? string.Empty, command.Vertical, command.DefaultSize, command.Color);
                    break;
                case "clear":
                    target.Clear(command.Color);
                    break;
                case "text":
                    target.DrawText(command.Text ?? string.Empty, command.X, command.Y, command.Size, command.Color);
                    break;
                case "centered":
                    target.DrawCenteredText(command.Text ?? string.Empty, command.X, command.Y, command.Size, command.Color);
                    break;
                case "rect":
                    target.FillRoundedRect(command.X, command.Y, command.W, command.H, command.R, command.Color);
                    break;
                case "rgba" when command.Data is { } pixels && pixels.Length >= command.PixelWidth * command.PixelHeight * 4:
                    target.DrawImage(pixels, command.PixelWidth, command.PixelHeight);
                    break;
                case "file" when command.Text is { } path:
                    target.DrawImageFile(path);
                    break;
                case "icon" when command.Text is { } icon:
                    target.DrawIcon(icon, command.X, command.Y, command.W, command.Color);
                    break;
                case "gradient":
                    target.FillGradientRect(command.X, command.Y, command.W, command.H, command.R, command.Color, command.Color2);
                    break;
                case "right":
                    target.DrawRightAlignedText(command.Text ?? string.Empty, command.X, command.Y, command.Size, command.Color);
                    break;
            }
        }
    }
}
