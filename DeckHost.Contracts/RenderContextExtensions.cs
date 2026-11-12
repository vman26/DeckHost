using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Contracts;

/// <summary>Ready-made layouts for actions, built on <see cref="IRenderContext"/>.</summary>
public static class RenderContextExtensions
{
    private const uint LabelColor = 0xFF93C5FD;
    private const uint BarBackgroundColor = 0xFF1E293B;

    /// <summary>
    /// Draws a live reading (a volume, a CPU load, a dial's value): a small label, a large value
    /// and, with a <paramref name="fraction"/> from 0 to 1, a level bar. In a strip
    /// (<see cref="IRenderContext.IsCompact"/>) it becomes one line.
    /// </summary>
    public static void DrawReading(this IRenderContext context, string label, string value, double? fraction = null, uint barColor = LabelColor)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsCompact)
        {
            context.DrawLabel($"{label} {value}".Trim(), TextVerticalAlignment.Middle, Math.Max(10f, context.Height * 0.46f));
            return;
        }

        var width = context.Width;
        var height = context.Height;
        context.DrawCenteredText(label, width / 2d, height * 0.22d, Math.Max(10f, height * 0.12f), LabelColor);
        context.DrawCenteredText(value, width / 2d, height * 0.52d, Math.Max(14f, height * 0.22f));

        if (fraction is { } level)
        {
            var barX = width * 0.12d;
            var barWidth = width * 0.76d;
            var barY = height * 0.74d;
            var barHeight = Math.Max(4d, height * 0.08d);
            context.FillRoundedRect(barX, barY, barWidth, barHeight, barHeight / 2d, BarBackgroundColor);
            if (level > 0)
            {
                context.FillRoundedRect(barX, barY, Math.Max(barHeight, barWidth * Math.Min(level, 1d)), barHeight, barHeight / 2d, barColor);
            }
        }
    }
}
