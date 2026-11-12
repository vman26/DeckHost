using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Rendering;

/// <summary>
/// Draws one control in layers: dark base, the profile background slice, the control's own
/// background color, its appearance image (GIFs animate), then the bound action's content.
/// </summary>
internal static class ControlRenderer
{
    public const uint BackgroundColor = 0xFF0B1220;

    public static void Draw(
        SkiaRenderContext context,
        ControlBinding? binding,
        IDeckAction? action,
        ProfileBackground? profileBackground,
        Rect layoutBounds,
        Rect controlBounds)
    {
        context.Clear(BackgroundColor);

        if (profileBackground is not null)
        {
            context.FillColor(profileBackground.Color);
            if (!string.IsNullOrWhiteSpace(profileBackground.ImagePath))
            {
                context.DrawLayoutSlice(profileBackground.ImagePath, layoutBounds, controlBounds);
            }
        }

        if (binding is null)
        {
            return;
        }

        context.TextStyle = binding.Appearance.TextStyle;
        context.FillColor(binding.Appearance.BackgroundColor);

        if (!string.IsNullOrWhiteSpace(binding.Appearance.ImagePath))
        {
            context.DrawImageFile(binding.Appearance.ImagePath);
        }

        if (action is not null)
        {
            action.Render(context);
            return;
        }

        // The binding points at a plugin/action that isn't loaded: show its name so the user can tell.
        context.DrawLabel(binding.Appearance.Text ?? binding.Action.ActionName, defaultColor: 0xFFFCA5A5);
    }

    /// <summary>Union of all controls' bounds: the area a profile background image covers.</summary>
    public static Rect LayoutBounds(DeviceDefinition device)
    {
        if (device.Controls.Count == 0)
        {
            return new Rect(0, 0, 1, 1);
        }

        var minX = device.Controls.Min(static control => control.Bounds.X);
        var minY = device.Controls.Min(static control => control.Bounds.Y);
        var maxX = device.Controls.Max(static control => control.Bounds.X + control.Bounds.Width);
        var maxY = device.Controls.Max(static control => control.Bounds.Y + control.Bounds.Height);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
