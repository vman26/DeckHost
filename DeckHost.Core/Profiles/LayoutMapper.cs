using System.Globalization;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Profiles;

/// <summary>What a mapping did: controls placed as they were, keys moved to added pages, controls left out.</summary>
public sealed record LayoutMapResult(IReadOnlyList<DeviceProfile> Layouts, int Placed, int MovedToNewPages, int Dropped, int PagesAdded)
{
    public string Summary =>
        $"{Placed} control(s) placed" +
        (MovedToNewPages > 0 ? $", {MovedToNewPages} key(s) on {PagesAdded} added page(s)" : string.Empty) +
        (Dropped > 0 ? $", {Dropped} left out (no such dial or touch zone on this device)" : string.Empty);
}

/// <summary>
/// Moves a profile made for one device onto another (an N4 Pro profile onto a Stream Deck XL, a
/// Stream Deck profile onto a Mini). DeckHost's control ids are the same kind on every device
/// (<c>key.N</c> in reading order, <c>encoder.N</c>, <c>touch.N</c>, <c>touch.swipe</c>), so:
/// <list type="bullet">
/// <item>keys keep their row and column when the source grid is known and the target is big
/// enough, else their place in reading order;</item>
/// <item>keys that don't fit go, in order, onto pages added to the profile;</item>
/// <item>dials and touch zones keep their number; those the target lacks are left out.</item>
/// </list>
/// </summary>
public static class LayoutMapper
{
    /// <param name="layouts">The profile first, then its pages and folders (ids are kept; renumber afterwards).</param>
    /// <param name="target">The controls of the device the profile goes to.</param>
    /// <param name="sourceColumns">Keys per row on the device the profile was made for, when known.</param>
    /// <param name="newPageId">Makes the id of an added page.</param>
    public static LayoutMapResult Map(IReadOnlyList<DeviceProfile> layouts, IReadOnlyList<PhysicalControl> target, int? sourceColumns, Func<int, string> newPageId)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        var ids = target.Select(static control => control.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keyCount = target.Count(static control => Parse(control.Id) is ("key", _));
        var columns = ColumnsOf(target);
        var root = layouts.FirstOrDefault(static layout => layout.IsRoot);
        var result = new List<DeviceProfile>();
        var overflow = new List<ControlBinding>();
        int placed = 0, dropped = 0;

        foreach (var layout in layouts)
        {
            var bindings = new List<ControlBinding>();
            foreach (var binding in layout.Bindings)
            {
                var targetId = TargetOf(binding.ControlId, ids, keyCount, columns, sourceColumns);
                if (targetId is not null && !bindings.Any(existing => string.Equals(existing.ControlId, targetId, StringComparison.OrdinalIgnoreCase)))
                {
                    bindings.Add(binding with { ControlId = targetId });
                    placed++;
                }
                else if (Parse(binding.ControlId) is ("key", _) && keyCount > 0 && layout.Kind != ProfileKinds.Folder)
                {
                    overflow.Add(binding);
                }
                else
                {
                    dropped++;
                }
            }

            result.Add(layout with { Bindings = bindings });
        }

        // Keys that didn't fit: onto new pages of the profile, in the order they had.
        var pages = 0;
        if (root is not null)
        {
            foreach (var chunk in overflow.Chunk(keyCount))
            {
                pages++;
                result.Add(new DeviceProfile(newPageId(pages), $"{root.Name} (more {pages})", root.DeviceId,
                    [.. chunk.Select((binding, index) => binding with { ControlId = $"key.{index.ToString(CultureInfo.InvariantCulture)}" })],
                    Background: root.Background)
                {
                    ParentId = root.Id,
                    Kind = ProfileKinds.Page,
                });
            }
        }
        else
        {
            dropped += overflow.Count;
        }

        return new LayoutMapResult(result, placed, root is null ? 0 : overflow.Count, dropped, pages);
    }

    /// <summary>Keys per row: the keys on the same row as <c>key.0</c>.</summary>
    public static int ColumnsOf(IReadOnlyList<PhysicalControl> controls)
    {
        var first = controls.FirstOrDefault(static control => string.Equals(control.Id, "key.0", StringComparison.OrdinalIgnoreCase));
        return first is null ? 0 : controls.Count(control => Parse(control.Id) is ("key", _) && Math.Abs(control.Bounds.Y - first.Bounds.Y) < first.Bounds.Height / 2);
    }

    private static string? TargetOf(string controlId, HashSet<string> ids, int keyCount, int columns, int? sourceColumns)
    {
        switch (Parse(controlId))
        {
            case ("key", var index):
                if (sourceColumns is > 0 && columns > 0 && sourceColumns != columns)
                {
                    var (row, column) = (index / sourceColumns.Value, index % sourceColumns.Value);
                    var moved = (row * columns) + column;
                    return column < columns && moved < keyCount ? $"key.{moved.ToString(CultureInfo.InvariantCulture)}" : null;
                }

                return index < keyCount ? controlId : null;
            default:
                return ids.Contains(controlId) ? controlId : null;
        }
    }

    private static (string Kind, int Index) Parse(string controlId)
    {
        var dot = controlId.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && int.TryParse(controlId.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? (controlId[..dot], index)
            : (controlId, -1);
    }
}
