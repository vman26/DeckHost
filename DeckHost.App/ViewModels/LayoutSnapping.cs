using System;
using System.Collections.Generic;
using System.Linq;

namespace DeckHost.App.ViewModels;

/// <summary>A box on the layout canvas.</summary>
public readonly record struct SnapRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    internal SnapRect Transposed => new(Y, X, Height, Width);
}

/// <summary>A line the canvas draws while something snaps: vertical at <c>X = Position</c>, or horizontal at <c>Y = Position</c>, from <c>From</c> to <c>To</c> along the other axis.</summary>
public readonly record struct SnapGuide(bool Vertical, double Position, double From, double To);

/// <summary>Where a moved box ends up and which guides to draw for it.</summary>
public readonly record struct SnapResult(double X, double Y, IReadOnlyList<SnapGuide> Guides);

/// <summary>How a selection lines up.</summary>
public enum LayoutAlignment
{
    Left,
    CenterHorizontally,
    Right,
    Top,
    CenterVertically,
    Bottom,
}

/// <summary>
/// The device creator's snapping and alignment: pure geometry, so it is tested without a window. A moved box snaps its
/// edges and center to those of the other boxes, and to even spacing (the middle between two neighbours, or the same gap
/// the others already have).
/// </summary>
public static class LayoutSnapping
{
    /// <summary>How close (in layout units) a box has to come before it snaps.</summary>
    public const double DefaultThreshold = 6;

    /// <summary>Snaps <paramref name="moving"/> against <paramref name="others"/>.</summary>
    public static SnapResult Snap(SnapRect moving, IReadOnlyList<SnapRect> others, double threshold = DefaultThreshold)
    {
        var (dx, vertical) = SnapAxis(moving, others, threshold);
        var (dy, horizontal) = SnapAxis(moving.Transposed, [.. others.Select(static other => other.Transposed)], threshold);
        var guides = new List<SnapGuide>(vertical);
        guides.AddRange(horizontal.Select(static guide => guide with { Vertical = false }));
        return new SnapResult(moving.X + dx, moving.Y + dy, guides);
    }

    /// <summary>The new top-left of every box after lining the group up (same order as <paramref name="boxes"/>).</summary>
    public static IReadOnlyList<(double X, double Y)> Align(IReadOnlyList<SnapRect> boxes, LayoutAlignment alignment)
    {
        if (boxes.Count == 0)
        {
            return [];
        }

        var left = boxes.Min(static box => box.X);
        var right = boxes.Max(static box => box.Right);
        var top = boxes.Min(static box => box.Y);
        var bottom = boxes.Max(static box => box.Bottom);
        return [.. boxes.Select(box => alignment switch
        {
            LayoutAlignment.Left => (left, box.Y),
            LayoutAlignment.Right => (right - box.Width, box.Y),
            LayoutAlignment.CenterHorizontally => ((left + right) / 2 - (box.Width / 2), box.Y),
            LayoutAlignment.Top => (box.X, top),
            LayoutAlignment.Bottom => (box.X, bottom - box.Height),
            _ => (box.X, (top + bottom) / 2 - (box.Height / 2)),
        })];
    }

    /// <summary>
    /// Spreads three or more boxes so the gaps between them are equal; the outermost two stay where they are. The result is in the
    /// order of <paramref name="boxes"/>.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Distribute(IReadOnlyList<SnapRect> boxes, bool horizontally)
    {
        var result = boxes.Select(static box => (box.X, box.Y)).ToArray();
        if (boxes.Count < 3)
        {
            return result;
        }

        var ordered = Enumerable.Range(0, boxes.Count)
            .OrderBy(index => horizontally ? boxes[index].X : boxes[index].Y)
            .ToArray();
        var start = horizontally ? boxes[ordered[0]].X : boxes[ordered[0]].Y;
        var last = boxes[ordered[^1]];
        var end = horizontally ? last.Right : last.Bottom;
        var sizes = ordered.Sum(index => horizontally ? boxes[index].Width : boxes[index].Height);
        var gap = (end - start - sizes) / (boxes.Count - 1);
        var position = start;
        foreach (var index in ordered)
        {
            result[index] = horizontally ? (position, boxes[index].Y) : (boxes[index].X, position);
            position += (horizontally ? boxes[index].Width : boxes[index].Height) + gap;
        }

        return result;
    }

    // The x axis; the y axis is the same with the boxes turned on their side.
    private static (double Delta, List<SnapGuide> Guides) SnapAxis(SnapRect moving, IReadOnlyList<SnapRect> others, double threshold)
    {
        var best = (Delta: 0d, Distance: double.MaxValue, Guides: new List<SnapGuide>());

        void Consider(double delta, params SnapGuide[] guides)
        {
            if (Math.Abs(delta) <= threshold && Math.Abs(delta) < best.Distance)
            {
                best = (delta, Math.Abs(delta), [.. guides]);
            }
        }

        // Edges and centers lining up.
        foreach (var other in others)
        {
            var from = Math.Min(moving.Y, other.Y);
            var to = Math.Max(moving.Bottom, other.Bottom);
            foreach (var mine in new[] { moving.X, moving.CenterX, moving.Right })
            {
                foreach (var theirs in new[] { other.X, other.CenterX, other.Right })
                {
                    Consider(theirs - mine, new SnapGuide(true, theirs, from, to));
                }
            }
        }

        // Even spacing among the boxes that share a row with the moving one.
        var row = others.Where(other => other.Y < moving.Bottom && other.Bottom > moving.Y).OrderBy(static other => other.X).ToList();
        var gaps = new List<double>();
        for (var index = 0; index + 1 < row.Count; index++)
        {
            var before = row[index];
            var after = row[index + 1];
            var gap = after.X - before.Right;
            if (gap > 0)
            {
                gaps.Add(gap);
                if (gap >= moving.Width)
                {
                    var middle = before.Right + ((gap - moving.Width) / 2);
                    Consider(middle - moving.X, new SnapGuide(true, before.Right, before.Y, before.Bottom), new SnapGuide(true, after.X, after.Y, after.Bottom));
                }
            }
        }

        foreach (var other in row)
        {
            foreach (var gap in gaps.Distinct())
            {
                Consider(other.Right + gap - moving.X, new SnapGuide(true, other.Right, other.Y, other.Bottom));
                Consider(other.X - gap - moving.Width - moving.X, new SnapGuide(true, other.X, other.Y, other.Bottom));
            }
        }

        return (best.Delta, best.Guides);
    }
}
