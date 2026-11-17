using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DeckHost.App.Views;

/// <summary>
/// Where a device is drawn, in its layout units, the same in the editor and in the device
/// simulator: a body around the front controls, the screens (display regions) behind the
/// controls on them, and controls on a side of the device sticking out of that edge of the body.
/// Everything starts at (0, 0), so the device sits centered wherever it is drawn.
/// </summary>
public sealed record DeviceGeometry(double Width, double Height, Rect Body, IReadOnlyDictionary<string, Rect> Controls, IReadOnlyList<Rect> Displays)
{
    /// <summary>Room between the front controls and the edge of the body.</summary>
    public const double BodyPadding = 18;

    /// <summary>How far a side control sticks out of the body.</summary>
    public const double SideDepth = 22;

    /// <summary>How far a screen reaches beyond the controls on it.</summary>
    public const double DisplayMargin = 6;

    public static DeviceGeometry Empty { get; } = new(0, 0, default, new Dictionary<string, Rect>(), []);

    public static DeviceGeometry Of(DeviceDefinition device) => Of(device.Controls, device.Displays);

    public static DeviceGeometry Of(IReadOnlyList<PhysicalControl> controls, IReadOnlyList<DeviceDisplay> displays)
    {
        if (controls.Count == 0)
        {
            return Empty;
        }

        var front = controls.Where(static control => IsFront(control)).ToList();
        if (front.Count == 0)
        {
            front = [.. controls];
        }

        var bodyInner = Union(front.Select(static control => control.Bounds));
        var left = controls.Any(static control => control.Side == DeviceSide.Left) ? SideDepth : 0;
        var top = controls.Any(static control => control.Side == DeviceSide.Top) ? SideDepth : 0;
        var right = controls.Any(static control => control.Side == DeviceSide.Right) ? SideDepth : 0;
        var bottom = controls.Any(static control => control.Side == DeviceSide.Bottom) ? SideDepth : 0;

        // Shift so the drawing starts at (0, 0).
        var dx = left + BodyPadding - bodyInner.X;
        var dy = top + BodyPadding - bodyInner.Y;
        var body = new Rect(left, top, bodyInner.Width + (2 * BodyPadding), bodyInner.Height + (2 * BodyPadding));

        var placed = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in controls)
        {
            var rect = Shift(control.Bounds, dx, dy);
            placed[control.Id] = control.Side switch
            {
                DeviceSide.Left => new Rect(body.X - (SideDepth * 0.7), rect.Y, SideDepth, rect.Height),
                DeviceSide.Right => new Rect(body.X + body.Width - (SideDepth * 0.3), rect.Y, SideDepth, rect.Height),
                DeviceSide.Top => new Rect(rect.X, body.Y - (SideDepth * 0.7), rect.Width, SideDepth),
                DeviceSide.Bottom => new Rect(rect.X, body.Y + body.Height - (SideDepth * 0.3), rect.Width, SideDepth),
                _ => rect,
            };
        }

        var screens = displays
            .Select(display => controls.Where(control => display.ControlIds.Contains(control.Id)).Select(control => Shift(control.Bounds, dx, dy)).ToList())
            .Where(static rects => rects.Count > 0)
            .Select(static rects => Inflate(Union(rects), DisplayMargin))
            .ToList();

        return new DeviceGeometry(left + body.Width + right, top + body.Height + bottom, body, placed, screens);
    }

    /// <summary>On the front (or back) of the device, so inside its body.</summary>
    public static bool IsFront(PhysicalControl control) => control.Side is DeviceSide.Front or DeviceSide.Back;

    public Rect Place(PhysicalControl control) => Controls.TryGetValue(control.Id, out var rect) ? rect : control.Bounds;

    private static Rect Shift(Rect rect, double dx, double dy) => new(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);

    private static Rect Inflate(Rect rect, double by) => new(rect.X - by, rect.Y - by, rect.Width + (2 * by), rect.Height + (2 * by));

    private static Rect Union(IEnumerable<Rect> rects)
    {
        var list = rects.ToList();
        var x = list.Min(static rect => rect.X);
        var y = list.Min(static rect => rect.Y);
        return new Rect(x, y, list.Max(static rect => rect.X + rect.Width) - x, list.Max(static rect => rect.Y + rect.Height) - y);
    }
}
