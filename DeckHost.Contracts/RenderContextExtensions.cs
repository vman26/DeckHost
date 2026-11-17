using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Contracts;

/// <summary>Ready-made layouts for actions, built on <see cref="IRenderContext"/>.</summary>
public static class RenderContextExtensions
{
    private const uint LabelColor = 0xFF93C5FD;
    private const uint CaptionColor = 0xFFCBD5E1;
    private const uint BarBackgroundColor = 0xFF1E293B;
    private const uint WideBarBackgroundColor = 0x40FFFFFF;

    /// <summary>Wider than this (width / height) is a touch zone rather than a key.</summary>
    private const double WideAspect = 1.3;

    /// <summary>
    /// Draws a live reading (a volume, a CPU load, a dial's value): a small label, a large value
    /// and, with a <paramref name="fraction"/> from 0 to 1, a level bar. In a strip
    /// (<see cref="IRenderContext.IsCompact"/>) it becomes one line.
    /// </summary>
    public static void DrawReading(this IRenderContext context, string label, string value, double? fraction = null, uint barColor = LabelColor) =>
        DrawReading(context, label, value, fraction, barColor, icon: null);

    /// <summary>
    /// <see cref="DrawReading(IRenderContext, string, string, double?, uint)"/> with an icon
    /// (<see cref="Icons"/>). On a touch zone it is laid out like a Stream Deck + dial: the label
    /// small at the top left, the icon on the left, the value on the right with a thin bar under it.
    /// </summary>
    public static void DrawReading(this IRenderContext context, string label, string value, double? fraction, uint barColor, string? icon)
    {
        ArgumentNullException.ThrowIfNull(context);
        var width = context.Width;
        var height = context.Height;

        if (context.IsCompact)
        {
            DrawCompactLine(context, icon, $"{label} {value}".Trim());
            return;
        }

        if (width > height * WideAspect)
        {
            var pad = Math.Max(6d, height * 0.09d);
            context.DrawText(label, pad, pad + (height * 0.12d), (float)Math.Max(10d, height * 0.12d), CaptionColor);

            var left = pad;
            if (!string.IsNullOrEmpty(icon))
            {
                var iconSize = height * 0.36d;
                context.DrawIcon(icon, pad + (iconSize / 2d), height * 0.6d, iconSize, TextColorOr(context, 0xFFFFFFFF));
                left = pad + iconSize + pad;
            }

            context.DrawRightAlignedText(value, width - pad, height * 0.64d, (float)Math.Max(12d, height * 0.21d), TextColorOr(context, 0xFFFFFFFF));
            if (fraction is { } wideLevel)
            {
                DrawBar(context, left, height * 0.76d, width - pad - left, Math.Max(3d, height * 0.045d), wideLevel, WideBarBackgroundColor, barColor);
            }

            return;
        }

        var hasIcon = !string.IsNullOrEmpty(icon);
        if (hasIcon && IsCustomText(context, label))
        {
            // The user's own text (Customize → Text) goes under the value, in their text style, so
            // the key shows icon, value and their text.
            context.DrawIcon(icon!, width / 2d, height * 0.22d, height * 0.22d, TextColorOr(context, 0xFFFFFFFF));
            context.DrawCenteredText(value, width / 2d, height * 0.5d, Math.Max(14f, height * 0.2f), TextColorOr(context, 0xFFFFFFFF));
            if (fraction is { } customLevel)
            {
                DrawBar(context, width * 0.14d, height * 0.64d, width * 0.72d, Math.Max(3d, height * 0.05d), customLevel, BarBackgroundColor, barColor);
            }

            context.DrawLabel(label, TextVerticalAlignment.Bottom, Math.Max(10f, height * 0.12f), LabelColor);
            return;
        }

        if (hasIcon)
        {
            context.DrawIcon(icon!, width / 2d, height * 0.27d, height * 0.26d, TextColorOr(context, 0xFFFFFFFF));
        }
        else if (IsCustomText(context, label))
        {
            context.DrawLabel(label, TextVerticalAlignment.Top, Math.Max(10f, height * 0.12f), LabelColor);
        }
        else
        {
            context.DrawCenteredText(label, width / 2d, height * 0.22d, Math.Max(10f, height * 0.12f), LabelColor);
        }

        context.DrawCenteredText(value, width / 2d, height * (hasIcon ? 0.58d : 0.52d), Math.Max(14f, height * 0.22f), TextColorOr(context, 0xFFFFFFFF));
        if (fraction is { } level)
        {
            var barHeight = Math.Max(4d, height * 0.06d);
            DrawBar(context, width * 0.14d, height * 0.78d, width * 0.72d, barHeight, level, BarBackgroundColor, barColor);
        }
    }

    /// <summary>
    /// Draws an action as an icon. On a key: a rounded tile with a diagonal gradient from
    /// <paramref name="fromArgb"/> to <paramref name="toArgb"/> and the icon in white, the
    /// <paramref name="caption"/> (if any) small at the bottom in the binding's text style. With both
    /// colors 0 there is no tile (the control's own background shows). On a touch zone the strip
    /// stays one surface: no tile, the caption at the top left and the icon in the middle. In a
    /// strip (<see cref="IRenderContext.IsCompact"/>) the icon and the caption on one line.
    /// </summary>
    public static void DrawTile(this IRenderContext context, string icon, uint fromArgb, uint toArgb, string? caption = null) =>
        DrawTile(context, icon, fromArgb, toArgb, caption, iconArgb: null);

    /// <summary>
    /// <see cref="DrawTile(IRenderContext, string, uint, uint, string?)"/> with the icon's color:
    /// without a tile (both colors 0) a colored icon reads better than a white one.
    /// </summary>
    public static void DrawTile(this IRenderContext context, string icon, uint fromArgb, uint toArgb, string? caption, uint? iconArgb)
    {
        ArgumentNullException.ThrowIfNull(context);
        var width = context.Width;
        var height = context.Height;
        var hasCaption = !string.IsNullOrWhiteSpace(caption);

        if (context.IsCompact)
        {
            DrawCompactLine(context, icon, caption);
            return;
        }

        if (width > height * WideAspect)
        {
            var pad = Math.Max(6d, height * 0.09d);
            if (hasCaption)
            {
                context.DrawText(caption!, pad, pad + (height * 0.12d), (float)Math.Max(10d, height * 0.12d), CaptionColor);
            }

            context.DrawIcon(icon, width / 2d, height * (hasCaption ? 0.58d : 0.5d), height * 0.42d, iconArgb ?? (fromArgb == 0 ? 0xFFFFFFFF : fromArgb));
            return;
        }

        if (fromArgb != 0 || toArgb != 0)
        {
            var inset = Math.Max(2d, width * 0.035d);
            context.FillGradientRect(inset, inset, width - (2 * inset), height - (2 * inset), width * 0.16d, fromArgb, toArgb);
        }

        context.DrawIcon(icon, width / 2d, height * (hasCaption ? 0.42d : 0.5d), height * (hasCaption ? 0.4d : 0.46d), iconArgb ?? 0xFFFFFFFF);
        if (hasCaption)
        {
            context.DrawLabel(caption!, TextVerticalAlignment.Bottom, Math.Max(10f, height * 0.13f));
        }
    }

    /// <summary>Whether <paramref name="text"/> is the control's own text from Customize (drawn in the user's text style).</summary>
    private static bool IsCustomText(IRenderContext context, string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceText), out var own) &&
        string.Equals(own, text, StringComparison.Ordinal);

    private static void DrawCompactLine(IRenderContext context, string? icon, string? text)
    {
        var height = context.Height;
        var size = (float)Math.Max(10d, height * 0.46d);
        if (string.IsNullOrEmpty(icon))
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                context.DrawLabel(text, TextVerticalAlignment.Middle, size);
            }

            return;
        }

        var iconSize = height * 0.6d;
        var pad = Math.Max(4d, height * 0.2d);
        context.DrawIcon(icon, pad + (iconSize / 2d), height / 2d, iconSize, TextColorOr(context, 0xFFFFFFFF));
        if (!string.IsNullOrWhiteSpace(text))
        {
            context.DrawText(text, pad + iconSize + pad, (height / 2d) + (size * 0.36d), size, TextColorOr(context, 0xFFFFFFFF));
        }
    }

    /// <summary>The binding's text color (Customize → Text style) as ARGB, else <paramref name="fallback"/>.</summary>
    public static uint TextColorOr(this IRenderContext context, uint fallback)
    {
        ArgumentNullException.ThrowIfNull(context);
        var hex = context.TextStyle?.Color?.Trim().TrimStart('#');
        return hex is { Length: 6 or 8 } &&
               uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? hex.Length == 6 ? 0xFF000000 | value : value
            : fallback;
    }

    private static void DrawBar(IRenderContext context, double x, double y, double width, double height, double level, uint background, uint color)
    {
        if (width <= 0)
        {
            return;
        }

        context.FillRoundedRect(x, y, width, height, height / 2d, background);
        if (level > 0)
        {
            context.FillRoundedRect(x, y, Math.Max(height, width * Math.Min(level, 1d)), height, height / 2d, color);
        }
    }
}
