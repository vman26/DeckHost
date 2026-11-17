using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using DeckHost.App.ViewModels;
using DeckHost.App.Views;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Models;
using DeckHost.Core.Workflows;
using Avalonia.Media.Imaging;

namespace DeckHost.UiTests;

/// <summary>What the custom-drawn views really draw: checked on rendered frames, so a change that blanks or recolors them fails.</summary>
public sealed class RenderingTests
{
    private static readonly PhysicalControl Key = new("key.0", "Key 1", ControlType.Key, new DeckHost.Contracts.Models.Rect(0, 0, 10, 10),
        InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage);

    private static (Color Color, int Count) Dominant(WriteableBitmap frame, int x, int y, int width, int height)
    {
        var counts = new Dictionary<Color, int>();
        for (var py = y; py < y + height; py++)
        {
            for (var px = x; px < x + width; px++)
            {
                var color = Pixel(frame, px, py);
                counts[color] = counts.GetValueOrDefault(color) + 1;
            }
        }

        var best = counts.MaxBy(static pair => pair.Value);
        return (best.Key, best.Value);
    }

    private static Color Pixel(WriteableBitmap frame, int x, int y)
    {
        using var locked = frame.Lock();
        var pixel = System.Runtime.InteropServices.Marshal.ReadInt32(locked.Address, (y * locked.RowBytes) + (x * 4));
        return locked.Format == Avalonia.Platform.PixelFormat.Bgra8888
            ? Color.FromArgb((byte)(pixel >> 24), (byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel)
            : Color.FromArgb((byte)(pixel >> 24), (byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16));
    }

    private static WorkflowEditorViewModel Editor(out WorkflowNode wait)
    {
        var editor = new WorkflowEditorViewModel("device", "layout", Key, "Key 1", new Workflow(), [], _ => true);
        wait = editor.AddNode(WorkflowNodeKinds.Wait, 100, 100);
        editor.SelectNode(null);
        return editor;
    }

    [AvaloniaTest]
    public void The_workflow_canvas_draws_its_nodes_and_a_wait_fills_over_its_time()
    {
        using var editor = Editor(out var wait);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        editor.Clock = () => now;
        var canvas = new WorkflowCanvas { DataContext = editor, Width = 500, Height = 300 };
        var window = new Window { Width = 500, Height = 300, Content = canvas };
        window.Show();

        using var idle = window.CaptureRenderedFrame()!;
        var accent = Dominant(idle, 110, 112, 8, 8).Color; // the node's header (tinted by its color)
        var emptyBody = Pixel(idle, 120, 150);
        var emptyBodyRight = Pixel(idle, 290, 150);
        Assert.That(emptyBody, Is.EqualTo(emptyBodyRight), "an idle node is one color from left to right");
        Assert.That(emptyBody, Is.Not.EqualTo(Pixel(idle, 20, 20)), "and the node stands out from the canvas");

        editor.ShowTrace(new WorkflowTrace("device", "layout", "key.0", 1, wait.Id, WorkflowTraceKind.Node, "Waiting 10 s", new DateTimeOffset(now)) { Wait = TimeSpan.FromSeconds(10) });
        now = now.AddSeconds(5);
        editor.Clock = () => now;
        canvas.InvalidateVisual();
        using var half = window.CaptureRenderedFrame()!;

        var left = Pixel(half, 120, 150);
        var right = Pixel(half, 290, 150);
        Assert.That(left, Is.Not.EqualTo(right), "half way: the left half is filled, the right half is not");
        Assert.That(right, Is.EqualTo(Pixel(half, 20, 20)).Or.EqualTo(emptyBody), "the right half is still the empty node");
        Assert.That(left, Is.Not.EqualTo(emptyBody));
        _ = accent;
    }

    [AvaloniaTest]
    public void A_breakpoint_is_a_red_dot_on_the_node_and_a_held_node_gets_an_amber_border()
    {
        using var editor = Editor(out var wait);
        var canvas = new WorkflowCanvas { DataContext = editor, Width = 500, Height = 300 };
        var window = new Window { Width = 500, Height = 300, Content = canvas };
        window.Show();
        using var plain = window.CaptureRenderedFrame()!;

        editor.ToggleBreakpoint(wait.Id);
        canvas.InvalidateVisual();
        using var withBreakpoint = window.CaptureRenderedFrame()!;
        var dot = Pixel(withBreakpoint, 102, 102);
        Assert.That(dot, Is.EqualTo(Color.Parse("#EF4444")), "the dot sits on the node's top left corner");
        Assert.That(Pixel(plain, 102, 102), Is.Not.EqualTo(dot));

        editor.ShowTrace(new WorkflowTrace("device", "layout", "key.0", 1, wait.Id, WorkflowTraceKind.Paused, "Paused at a breakpoint", DateTimeOffset.Now));
        canvas.InvalidateVisual();
        using var held = window.CaptureRenderedFrame()!;
        // The node's right edge (x = 100 + 210) is drawn with the amber border while the run is held.
        var edge = Pixel(held, 310, 130);
        Assert.That(edge, Is.EqualTo(Color.Parse("#F59E0B")), "amber, not the grey border");
    }

    [AvaloniaTest]
    public void A_switch_draws_one_port_per_case_and_else_with_their_own_colors()
    {
        using var editor = new WorkflowEditorViewModel("device", "layout", Key, "Key 1", new Workflow(), [], _ => true);
        var sw = editor.AddNode(WorkflowNodeKinds.Switch, 100, 60);
        editor.SelectNode(null);
        var canvas = new WorkflowCanvas { DataContext = editor, Width = 500, Height = 300 };
        var window = new Window { Width = 500, Height = 300, Content = canvas };
        window.Show();
        using var frame = window.CaptureRenderedFrame()!;

        var node = editor.Workflow.Find(sw.Id)!;
        Color RingOf(string port)
        {
            var (x, y) = WorkflowEditorViewModel.OutputPoint(node, port);
            return Pixel(frame, (int)Math.Round(x) + 6, (int)Math.Round(y));
        }

        // The ring is anti-aliased, so colors are compared with a little room.
        static bool Near(Color actual, string expected) =>
            Math.Abs(actual.R - Color.Parse(expected).R) < 40 && Math.Abs(actual.G - Color.Parse(expected).G) < 40 && Math.Abs(actual.B - Color.Parse(expected).B) < 40;
        Assert.That(Near(RingOf("case:c1"), "#06B6D4"), Is.True, "a case port is cyan");
        Assert.That(Near(RingOf("case:c2"), "#06B6D4"), Is.True);
        Assert.That(Near(RingOf(WorkflowPorts.Default), "#94A3B8"), Is.True, "Else is grey");
        var (_, below) = WorkflowEditorViewModel.OutputPoint(node, WorkflowPorts.Default);
        Assert.That(WorkflowEditorViewModel.HeightOf(node) + node.Y, Is.GreaterThan(below), "the node is tall enough for its third port");
    }

    [AvaloniaTest]
    public void A_link_between_two_nodes_is_drawn_in_the_gap_between_them()
    {
        using var editor = new WorkflowEditorViewModel("device", "layout", Key, "Key 1", new Workflow(), [], _ => true);
        var first = editor.AddNode(WorkflowNodeKinds.Wait, 20, 20);
        var last = editor.AddNode(WorkflowNodeKinds.Wait, 320, 20);
        editor.SelectNode(null);
        var before = RenderCanvas(editor);

        editor.Connect(first.Id, WorkflowPorts.Next, last.Id);
        editor.SelectNode(null);
        var linked = RenderCanvas(editor);

        // Somewhere in the gap between the two nodes the curve is drawn.
        var changed = 0;
        for (var x = 232; x < 318; x++)
        {
            for (var y = 20; y < 60; y++)
            {
                if (Pixel(before, x, y) != Pixel(linked, x, y))
                {
                    changed++;
                }
            }
        }

        Assert.That(changed, Is.GreaterThan(40), "the link is drawn");
    }

    private static WriteableBitmap RenderCanvas(WorkflowEditorViewModel editor)
    {
        var canvas = new WorkflowCanvas { DataContext = editor, Width = 560, Height = 120 };
        var window = new Window { Width = 560, Height = 120, Content = canvas };
        window.Show();
        var frame = window.CaptureRenderedFrame()!;
        window.Close();
        return frame;
    }

    [AvaloniaTest]
    public void A_device_body_draws_the_screens_in_the_given_color_and_not_a_blue_box()
    {
        var controls = Enumerable.Range(0, 3)
            .Select(static i => new PhysicalControl($"key.{i}", $"Key {i + 1}", ControlType.Key, new DeckHost.Contracts.Models.Rect(i * 12, 0, 10, 10),
                InputCapabilities.Press, OutputCapabilities.ColorImage))
            .ToList();
        var geometry = DeviceGeometry.Of(controls, []);
        var screen = new SolidColorBrush(Color.Parse("#112233"));
        var body = new DeviceBodyView
        {
            Geometry = geometry,
            Screen = screen,
            Body = new SolidColorBrush(Color.Parse("#2C313C")),
            Edge = new SolidColorBrush(Color.Parse("#445566")),
            Width = 600,
            Height = 400,
        };
        var window = new Window { Width = 600, Height = 400, Content = body };
        window.Show();

        using var frame = window.CaptureRenderedFrame()!;

        var colors = new HashSet<Color>();
        for (var y = 0; y < 400; y += 4)
        {
            for (var x = 0; x < 600; x += 4)
            {
                colors.Add(Pixel(frame, x, y));
            }
        }

        Assert.That(colors.Count, Is.GreaterThan(2), "a body, its edge and the screens, not one flat color");
        Assert.That(colors, Does.Not.Contain(Color.Parse("#2563EB")), "and not the blue box it used to be");
    }
}
