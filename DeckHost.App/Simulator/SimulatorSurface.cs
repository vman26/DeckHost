using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LayoutRect = DeckHost.Contracts.Models.Rect;
using Rect = Avalonia.Rect;

namespace DeckHost.App.Simulator;

/// <summary>
/// Draws the simulated device from its description (every control where the device plugin puts it,
/// with the images and lights DeckHost sends) and turns the mouse into the device's input:
/// click = press and release, wheel = turn a knob or switch, drag across a swipe surface = swipe.
/// </summary>
public sealed class SimulatorSurface : Control
{
    private const double SwipeDistance = 24;

    private static readonly IBrush Body = new SolidColorBrush(Color.Parse("#1B1E25"));
    private static readonly IPen BodyEdge = new Pen(new SolidColorBrush(Color.Parse("#2C313C")), 2);
    private static readonly IBrush Empty = new SolidColorBrush(Color.Parse("#0E1014"));
    private static readonly IBrush Cap = new SolidColorBrush(Color.Parse("#2A2F3A"));
    private static readonly IBrush Label = new SolidColorBrush(Color.Parse("#8A93A6"));
    private static readonly IBrush PressedOverlay = new SolidColorBrush(Colors.White, 0.18);
    private static readonly IPen Rib = new Pen(new SolidColorBrush(Color.Parse("#5C6576")), 1.5);
    private static readonly IPen Bezel = new Pen(new SolidColorBrush(Color.Parse("#2C313C")), 1);
    private static readonly IPen IdleRing = new Pen(new SolidColorBrush(Color.Parse("#3B4252")), 5);
    private static readonly IPen Indicator = new Pen(new SolidColorBrush(Color.Parse("#C8CEDA")), 3, lineCap: PenLineCap.Round);
    private static readonly Typeface Font = new(FontFamily.Default);

    private readonly SimulatorViewModel _viewModel;
    private readonly Dictionary<string, double> _knobAngles = new(StringComparer.Ordinal);
    private PhysicalControl? _pressed;
    private Point _pressPoint;

    public SimulatorSurface(SimulatorViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.Changed += (_, _) => InvalidateVisual();
        ClipToBounds = true;
        Focusable = true;
    }

    // ---------------------------------------------------------------- layout

    /// <summary>Where things go on screen: <see cref="Views.DeviceGeometry"/> (shared with the editor), scaled to fit.</summary>
    private sealed record Geometry(double Scale, Point Offset, Views.DeviceGeometry Device)
    {
        public Rect Body => ToScreen(Device.Body);

        public Rect ToScreen(LayoutRect rect) =>
            new((rect.X * Scale) + Offset.X, (rect.Y * Scale) + Offset.Y, rect.Width * Scale, rect.Height * Scale);

        public Rect Place(PhysicalControl control) => ToScreen(Device.Place(control));
    }

    private Geometry? Layout()
    {
        if (_viewModel.Device is not { Controls.Count: > 0 } device)
        {
            return null;
        }

        var shape = Views.DeviceGeometry.Of(device);
        var scale = Math.Max(0.1, Math.Min((Bounds.Width - 32) / shape.Width, (Bounds.Height - 32) / shape.Height));
        var offset = new Point((Bounds.Width - (shape.Width * scale)) / 2, (Bounds.Height - (shape.Height * scale)) / 2);
        return new Geometry(scale, offset, shape);
    }

    private PhysicalControl? HitTest(Point point) =>
        Layout() is { } geometry
            ? _viewModel.Device?.Controls.LastOrDefault(control => geometry.Place(control).Inflate(4).Contains(point))
            : null;

    // ---------------------------------------------------------------- drawing

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_viewModel.Device is not { } device)
        {
            var text = Text(_viewModel.Status, 14, Label);
            context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
            return;
        }

        if (Layout() is not { } geometry)
        {
            return;
        }

        // Side controls first: the body covers their inner part, so they stick out of its edge.
        foreach (var control in device.Controls.Where(static control => !Views.DeviceGeometry.IsFront(control)))
        {
            DrawSideWheel(context, control, geometry.Place(control));
        }

        context.DrawRectangle(Body, BodyEdge, geometry.Body, 18, 18);

        // The screens behind the keys, in the color DeckHost sets (its backdrop).
        var backdrop = new SolidColorBrush(_viewModel.Backdrop);
        foreach (var screen in geometry.Device.Displays)
        {
            context.DrawRectangle(backdrop, null, geometry.ToScreen(screen), 6, 6);
        }

        var dim = new SolidColorBrush(Colors.Black, 1 - (Math.Clamp(_viewModel.Brightness, 0, 100) / 100.0));
        foreach (var control in device.Controls)
        {
            var rect = geometry.Place(control);
            if (!Views.DeviceGeometry.IsFront(control))
            {
                // Drawn before the body; only the press shows on top.
                if (ReferenceEquals(control, _pressed))
                {
                    context.DrawRectangle(PressedOverlay, null, rect, 6, 6);
                }

                continue;
            }

            if (control.HasImageOutput)
            {
                DrawScreen(context, control, rect, dim);
            }
            else if (control.Type == ControlType.Encoder)
            {
                DrawKnob(context, control, rect);
            }
            else
            {
                DrawPlain(context, control, rect);
            }

            if (ReferenceEquals(control, _pressed))
            {
                context.DrawRectangle(PressedOverlay, null, rect, control.Shape == ControlShape.Round ? rect.Width / 2 : 6, control.Shape == ControlShape.Round ? rect.Height / 2 : 6);
            }
        }
    }

    private void DrawScreen(DrawingContext context, PhysicalControl control, Rect rect, IBrush dim)
    {
        var radius = Math.Min(rect.Width, rect.Height) * 0.08;
        if (_viewModel.Images.TryGetValue(control.Id, out var image))
        {
            using (context.PushClip(new RoundedRect(rect, radius)))
            {
                context.DrawImage(image, new Rect(image.Size), rect);
            }
        }
        else
        {
            context.DrawRectangle(Empty, null, rect, radius, radius);
            var name = Text(control.DisplayName, Math.Max(9, rect.Height * 0.12), Label);
            context.DrawText(name, new Point(rect.X + ((rect.Width - name.Width) / 2), rect.Y + ((rect.Height - name.Height) / 2)));
        }

        context.DrawRectangle(dim, Bezel, rect, radius, radius);
    }

    private void DrawKnob(DrawingContext context, PhysicalControl control, Rect rect)
    {
        var ring = control.HasLight && _viewModel.Lights.TryGetValue(control.Id, out var light) && light != Colors.Black
            ? new Pen(new SolidColorBrush(light), 5)
            : IdleRing;
        if (control.Shape == ControlShape.Round)
        {
            var center = rect.Center;
            var radius = Math.Min(rect.Width, rect.Height) / 2;
            context.DrawEllipse(Cap, ring, center, radius - 3, radius - 3);
            var angle = (_knobAngles.GetValueOrDefault(control.Id) - 90) * Math.PI / 180;
            context.DrawLine(Indicator, center + new Point(Math.Cos(angle) * radius * 0.25, Math.Sin(angle) * radius * 0.25), center + new Point(Math.Cos(angle) * radius * 0.7, Math.Sin(angle) * radius * 0.7));
        }
        else
        {
            // A switch: pushed one way or the other, pressed in. Tall on the side of a device (the
            // wheel up moves it up), wide below the keys.
            context.DrawRectangle(Cap, ring, rect, 8, 8);
            var step = Math.Clamp(_knobAngles.GetValueOrDefault(control.Id) / 15, -1, 1);
            var upright = rect.Height > rect.Width;
            var size = Math.Max(9, Math.Min(rect.Width, rect.Height) * 0.3);
            var dot = Math.Min(rect.Width, rect.Height) * 0.14;
            if (upright)
            {
                context.DrawEllipse(Label, null, rect.Center - new Point(0, step * rect.Height * 0.07), dot, dot);
                var up = Text("▲", size, Label);
                var down = Text("▼", size, Label);
                context.DrawText(up, new Point(rect.X + ((rect.Width - up.Width) / 2), rect.Y + (rect.Height * 0.08)));
                context.DrawText(down, new Point(rect.X + ((rect.Width - down.Width) / 2), rect.Bottom - (rect.Height * 0.08) - down.Height));
            }
            else
            {
                context.DrawEllipse(Label, null, rect.Center + new Point(step * rect.Width * 0.12, 0), dot, dot);
                var left = Text("◀", size, Label);
                var right = Text("▶", size, Label);
                context.DrawText(left, new Point(rect.X + (rect.Width * 0.12), rect.Y + ((rect.Height - left.Height) / 2)));
                context.DrawText(right, new Point(rect.Right - (rect.Width * 0.12) - right.Width, rect.Y + ((rect.Height - right.Height) / 2)));
            }
        }
    }

    /// <summary>A wheel on the side of the device, seen from the front: ribs that move as it turns.</summary>
    private void DrawSideWheel(DrawingContext context, PhysicalControl control, Rect rect)
    {
        var horizontal = control.Side is DeviceSide.Left or DeviceSide.Right;
        var ring = control.HasLight && _viewModel.Lights.TryGetValue(control.Id, out var light) && light != Colors.Black
            ? new Pen(new SolidColorBrush(light), 2)
            : new Pen(new SolidColorBrush(Color.Parse("#4B5263")), 1.5);
        context.DrawRectangle(Cap, ring, rect, 6, 6);
        var spacing = (horizontal ? rect.Height : rect.Width) / 9;
        var turn = (_knobAngles.GetValueOrDefault(control.Id) / 15 % 3 + 3) % 3 * (spacing / 3);
        for (var line = spacing / 2; line < (horizontal ? rect.Height : rect.Width); line += spacing)
        {
            var at = line + turn;
            if (at >= (horizontal ? rect.Height : rect.Width) - 2)
            {
                continue;
            }

            if (horizontal)
            {
                context.DrawLine(Rib, new Point(rect.X + 3, rect.Y + at), new Point(rect.Right - 3, rect.Y + at));
            }
            else
            {
                context.DrawLine(Rib, new Point(rect.X + at, rect.Y + 3), new Point(rect.X + at, rect.Bottom - 3));
            }
        }
    }

    private static void DrawPlain(DrawingContext context, PhysicalControl control, Rect rect)
    {
        context.DrawRectangle(Cap, null, rect, 8, 8);
        var caption = control.Inputs.HasFlag(InputCapabilities.Swipe) && !control.Inputs.HasFlag(InputCapabilities.Press)
            ? "◀  swipe  ▶"
            : control.DisplayName;
        var text = Text(caption, Math.Max(9, Math.Min(14, rect.Height * 0.35)), Label);
        context.DrawText(text, new Point(rect.X + ((rect.Width - text.Width) / 2), rect.Y + ((rect.Height - text.Height) / 2)));
    }

    private static FormattedText Text(string text, double size, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, brush);

    // ---------------------------------------------------------------- input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || HitTest(e.GetPosition(this)) is not { } control)
        {
            return;
        }

        _pressed = control;
        _pressPoint = e.GetPosition(this);
        e.Pointer.Capture(this);
        if (control.Inputs.HasFlag(InputCapabilities.Press))
        {
            _viewModel.SendInput(control.Id, InputCapabilities.Press, 1);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed is not { } control)
        {
            return;
        }

        _pressed = null;
        e.Pointer.Capture(null);
        var distance = e.GetPosition(this).X - _pressPoint.X;
        if (control.Inputs.HasFlag(InputCapabilities.Swipe) && Math.Abs(distance) >= SwipeDistance)
        {
            _viewModel.SendInput(control.Id, InputCapabilities.Swipe, distance < 0 ? -1 : 1);
        }

        if (control.Inputs.HasFlag(InputCapabilities.Release))
        {
            _viewModel.SendInput(control.Id, InputCapabilities.Release, 0);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (HitTest(e.GetPosition(this)) is not { } control || !control.Inputs.HasFlag(InputCapabilities.Rotate))
        {
            return;
        }

        var steps = e.Delta.Y > 0 ? 1 : e.Delta.Y < 0 ? -1 : 0;
        if (steps == 0)
        {
            return;
        }

        // Wheel up turns clockwise (right), like the scroll wheel of most knob simulators.
        _viewModel.SendInput(control.Id, InputCapabilities.Rotate, steps);
        _knobAngles[control.Id] = control.Shape == ControlShape.Round || control.Side != DeviceSide.Front
            ? (_knobAngles.GetValueOrDefault(control.Id) + (steps * 15)) % 360
            : steps * 15;
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>The control under a point in this surface (for tests and tooltips).</summary>
    internal PhysicalControl? ControlAt(Point point) => HitTest(point);

    /// <summary>Where a control is drawn (for tests).</summary>
    internal Rect ScreenBounds(PhysicalControl control) => Layout()?.Place(control) ?? default;
}
