using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Rendering;

/// <summary>
/// The binding of a control that has no display of its own and is shown on another control's
/// display (<see cref="PhysicalControl.DisplayControlId"/>): a knob on the touch zone above it.
/// </summary>
/// <param name="ControlId">The control the binding belongs to (the knob).</param>
/// <param name="StripAtTop">Where its strip goes when the display has its own action: the edge nearest to the control.</param>
internal sealed record LinkedBinding(string ControlId, ControlBinding Binding, IDeckAction? Action, bool StripAtTop = false);

/// <summary>
/// Draws one control in layers: dark base, the profile background slice, the control's own
/// background color, its appearance image (GIFs animate), then the bound action's content.
/// <para>
/// A display can also show a linked control's binding (a knob's, on its touch zone). With nothing
/// of its own to show, the display is the linked control's entirely. With an action of its own,
/// the two share it: the display's action keeps most of the area and the linked control gets a
/// strip along the edge nearest to it, drawn in compact form (<see cref="IRenderContext.IsCompact"/>).
/// </para>
/// </summary>
internal static class ControlRenderer
{
    public const uint BackgroundColor = 0xFF0B1220;

    /// <summary>Part of the display's height a linked control's strip takes.</summary>
    private const double LinkedStripShare = 0.32;
    private const int MinLinkedStripHeight = 18;
    private const uint LinkedStripColor = 0xC0020617;
    private const uint LinkedStripEdgeColor = 0x5094A3B8;

    public static void Draw(
        SkiaRenderContext context,
        ControlBinding? binding,
        IDeckAction? action,
        ProfileBackground? profileBackground,
        Rect layoutBounds,
        Rect controlBounds,
        LinkedBinding? linked = null) =>
        Draw(context, binding, action, profileBackground is null ? [] : BackgroundLayers.Resolve(profileBackground, context.Variables), layoutBounds, controlBounds, linked);

    /// <summary>Draws a control with a background already resolved (the host resolves it once for every control it draws).</summary>
    public static void Draw(
        SkiaRenderContext context,
        ControlBinding? binding,
        IDeckAction? action,
        IReadOnlyList<ResolvedLayer> background,
        Rect layoutBounds,
        Rect controlBounds,
        LinkedBinding? linked = null)
    {
        DrawBackground(context, background, layoutBounds, controlBounds);

        if (linked is null)
        {
            DrawBinding(context, binding, action);
            return;
        }

        if (binding is null || !HasOwnContent(binding, action))
        {
            // The display is free (at most a background color of its own): the linked control fills it.
            context.FillColor(binding?.Appearance.BackgroundColor);
            context.DrawRegion(0, 0, context.Width, context.Height, linked.ControlId, linked.Binding.Action.EffectiveSettings, compact: false,
                region => DrawBinding(region, linked.Binding, linked.Action));
            return;
        }

        var stripHeight = LinkedStripHeight(context.Height);
        var ownHeight = context.Height - stripHeight;
        var (ownTop, stripTop) = linked.StripAtTop ? (stripHeight, 0) : (0, ownHeight);

        context.DrawRegion(0, ownTop, context.Width, ownHeight, context.ControlId, binding.Action.EffectiveSettings, compact: false,
            region => DrawBinding(region, binding, action));
        context.DrawRegion(0, stripTop, context.Width, stripHeight, linked.ControlId, linked.Binding.Action.EffectiveSettings, compact: true, region =>
        {
            // Darkened, with a hairline towards the display's own action, so it reads as a separate part.
            region.FillRoundedRect(0, 0, region.Width, region.Height, 0, LinkedStripColor);
            region.FillRoundedRect(0, linked.StripAtTop ? region.Height - 1 : 0, region.Width, 1, 0, LinkedStripEdgeColor);
            DrawBinding(region, linked.Binding, linked.Action);
        });
    }

    /// <summary>The base color and the background's layers: what is under a control's content (also what the Studio preview shows).</summary>
    internal static void DrawBackground(SkiaRenderContext context, IReadOnlyList<ResolvedLayer> background, Rect layoutBounds, Rect controlBounds)
    {
        context.Clear(BackgroundColor);
        if (background.Count > 0)
        {
            context.DrawBackgroundLayers(background, layoutBounds, controlBounds);
        }
    }

    /// <summary>
    /// Whether a display shows something of its own: an action, or text or an image. A binding to
    /// an appearance-only action with nothing but a background color doesn't count (text, an image or an icon do), so a linked
    /// control can still use the display. (An action that isn't loaded counts: its name is shown.)
    /// </summary>
    public static bool HasOwnContent(ControlBinding? binding, IDeckAction? action) =>
        binding is not null &&
        !(action is { ShowsAppearanceOnly: true } &&
          string.IsNullOrWhiteSpace(binding.Appearance.Text) &&
          string.IsNullOrWhiteSpace(binding.Appearance.ImagePath) &&
          string.IsNullOrWhiteSpace(binding.Appearance.IconPath));

    /// <summary>Height of a linked control's strip on a display of the given height.</summary>
    public static int LinkedStripHeight(int displayHeight) =>
        Math.Clamp((int)Math.Round(displayHeight * LinkedStripShare), Math.Min(MinLinkedStripHeight, displayHeight / 2), displayHeight / 2);

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

    private static void DrawBinding(SkiaRenderContext context, ControlBinding? binding, IDeckAction? action)
    {
        if (binding is null)
        {
            return;
        }

        context.TextStyle = binding.Appearance.TextStyle;
        context.FillColor(binding.Appearance.BackgroundColor);

        // A strip only fits a thin slice of a picture, so it shows the text alone.
        if (!context.IsCompact && !string.IsNullOrWhiteSpace(binding.Appearance.ImagePath))
        {
            context.DrawImageFile(binding.Appearance.ImagePath);
        }

        if (action is not null)
        {
            // The user's own text shows on every action: an action that draws its own picture
            // without it (a plugin, a readout) gets it as a label at the bottom, in the text style.
            var ownText = string.IsNullOrWhiteSpace(binding.Appearance.Text) ? null : binding.Appearance.Text;
            context.ExpectedText = ownText;
            action.Render(context);
            var drawn = context.ExpectedTextDrawn;
            context.ExpectedText = null;
            if (ownText is not null && !drawn)
            {
                context.DrawLabel(ownText, TextVerticalAlignment.Bottom, Math.Max(10f, context.Height * 0.14f));
            }

            return;
        }

        // The binding points at a plugin/action that isn't loaded: show its name so the user can tell.
        context.DrawLabel(binding.Appearance.Text ?? binding.Action.ActionName, defaultColor: 0xFFFCA5A5);
    }
}
