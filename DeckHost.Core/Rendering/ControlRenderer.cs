using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Rendering;

/// <summary>
/// Draws one control: background, the binding's appearance image, then the bound action's own
/// content. Unbound controls are left dark so the device shows nothing unexpected.
/// </summary>
internal static class ControlRenderer
{
    public const uint BackgroundColor = 0xFF0B1220;

    public static void Draw(IRenderContext context, ControlBinding? binding, IDeckAction? action)
    {
        context.Clear(BackgroundColor);

        if (binding is null)
        {
            return;
        }

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
        var text = binding.Appearance.Text ?? binding.Action.ActionName;
        context.DrawCenteredText(text, context.Width / 2d, context.Height / 2d, Math.Max(12f, context.Height * 0.16f), 0xFFFCA5A5);
    }
}
