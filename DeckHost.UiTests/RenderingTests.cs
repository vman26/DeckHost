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

    private const int CanvasWidth = 500;
    private const int CanvasHeight = 300;
    private const int NodeLeftBodyX = 120;
    private const int NodeRightBodyX = 290;
    private const int NodeBodyY = 150;

    private static (Window Window, WorkflowCanvas Canvas) ShowCanvas(WorkflowEditorViewModel editor)
    {
        var canvas = new WorkflowCanvas { DataContext = editor, Width = CanvasWidth, Height = CanvasHeight };
        var window = new Window { Width = CanvasWidth, Height = CanvasHeight, Content = canvas };
        window.Show();
        return (window, canvas);
    }

    [AvaloniaTest]
    public void Render_AnIdleWaitNode_IsOneColorFromLeftToRightAndStandsOutFromTheCanvas()
    {
        // Arrange
        using var editor = Editor(out _);
        var (window, _) = ShowCanvas(editor);

        // Act
        using var idle = window.CaptureRenderedFrame()!;

        // Assert
        var emptyBody = Pixel(idle, NodeLeftBodyX, NodeBodyY);
        Assert.That(emptyBody, Is.EqualTo(Pixel(idle, NodeRightBodyX, NodeBodyY)), "an idle node is one color from left to right");
        Assert.That(emptyBody, Is.Not.EqualTo(Pixel(idle, 20, 20)), "and the node stands out from the canvas");
    }

    [AvaloniaTest]
    public void Render_AWaitHalfWayThrough_FillsTheLeftHalfOfTheNodeOnly()
    {
        // Arrange
        using var editor = Editor(out var wait);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        editor.Clock = () => now;
        var (window, canvas) = ShowCanvas(editor);
        using var idle = window.CaptureRenderedFrame()!;
        var emptyBody = Pixel(idle, NodeLeftBodyX, NodeBodyY);
        editor.ShowTrace(new WorkflowTrace("device", "layout", "key.0", 1, wait.Id, WorkflowTraceKind.Node, "Waiting 10 s", new DateTimeOffset(now)) { Wait = TimeSpan.FromSeconds(10) });

        // Act
        now = now.AddSeconds(5);
        editor.Clock = () => now;
        canvas.InvalidateVisual();
        using var half = window.CaptureRenderedFrame()!;

        // Assert
        var left = Pixel(half, NodeLeftBodyX, NodeBodyY);
        var right = Pixel(half, NodeRightBodyX, NodeBodyY);
        Assert.That(left, Is.Not.EqualTo(right), "half way: the left half is filled, the right half is not");
        Assert.That(right, Is.EqualTo(Pixel(half, 20, 20)).Or.EqualTo(emptyBody), "the right half is still the empty node");
        Assert.That(left, Is.Not.EqualTo(emptyBody));
    }

    private const string BreakpointRed = "#EF4444";
    private const string HeldAmber = "#F59E0B";

    [AvaloniaTest]
    public void Render_ABreakpoint_IsARedDotOnTheNodesTopLeftCorner()
    {
        // Arrange
        using var editor = Editor(out var wait);
        var (window, canvas) = ShowCanvas(editor);
        using var plain = window.CaptureRenderedFrame()!;

        // Act
        editor.ToggleBreakpoint(wait.Id);
        canvas.InvalidateVisual();
        using var withBreakpoint = window.CaptureRenderedFrame()!;

        // Assert
        var dot = Pixel(withBreakpoint, 102, 102);
        Assert.That(dot, Is.EqualTo(Color.Parse(BreakpointRed)));
        Assert.That(Pixel(plain, 102, 102), Is.Not.EqualTo(dot));
    }

    [AvaloniaTest]
    public void Render_ANodeAtWhichARunIsHeld_GetsAnAmberBorder()
    {
        // Arrange
        using var editor = Editor(out var wait);
        var (window, canvas) = ShowCanvas(editor);

        // Act
        editor.ShowTrace(new WorkflowTrace("device", "layout", "key.0", 1, wait.Id, WorkflowTraceKind.Paused, "Paused at a breakpoint", DateTimeOffset.Now));
        canvas.InvalidateVisual();
        using var held = window.CaptureRenderedFrame()!;

        // Assert (the node's right edge, x = 100 + 210, is drawn amber while the run is held)
        Assert.That(Pixel(held, 310, 130), Is.EqualTo(Color.Parse(HeldAmber)), "amber, not the grey border");
    }

    private const string CasePortColor = "#06B6D4";
    private const string ElsePortColor = "#94A3B8";
    private const int ColorRoom = 40;

    private static (WriteableBitmap Frame, WorkflowNode Node) RenderedSwitch()
    {
        var editor = new WorkflowEditorViewModel("device", "layout", Key, "Key 1", new Workflow(), [], _ => true);
        var sw = editor.AddNode(WorkflowNodeKinds.Switch, 100, 60);
        editor.SelectNode(null);
        var (window, _) = ShowCanvas(editor);
        var frame = window.CaptureRenderedFrame()!;
        return (frame, editor.Workflow.Find(sw.Id)!);
    }

    // The ring is anti-aliased, so colors are compared with a little room.
    private static bool Near(Color actual, string expected) =>
        Math.Abs(actual.R - Color.Parse(expected).R) < ColorRoom && Math.Abs(actual.G - Color.Parse(expected).G) < ColorRoom && Math.Abs(actual.B - Color.Parse(expected).B) < ColorRoom;

    private static Color RingOf(WriteableBitmap frame, WorkflowNode node, string port)
    {
        var (x, y) = WorkflowEditorViewModel.OutputPoint(node, port);
        return Pixel(frame, (int)Math.Round(x) + 6, (int)Math.Round(y));
    }

    [AvaloniaTest]
    public void Render_ASwitch_DrawsEachCasePortInCyan()
    {
        // Arrange
        var (frame, node) = RenderedSwitch();

        // Act
        var first = RingOf(frame, node, "case:c1");
        var second = RingOf(frame, node, "case:c2");

        // Assert
        Assert.That(Near(first, CasePortColor), Is.True, "a case port is cyan");
        Assert.That(Near(second, CasePortColor), Is.True);
    }

    [AvaloniaTest]
    public void Render_ASwitch_DrawsTheElsePortInGrey()
    {
        // Arrange
        var (frame, node) = RenderedSwitch();

        // Act
        var elsePort = RingOf(frame, node, WorkflowPorts.Default);

        // Assert
        Assert.That(Near(elsePort, ElsePortColor), Is.True, "Else is grey");
    }

    [AvaloniaTest]
    public void HeightOf_ASwitch_IsTallEnoughForItsThirdPort()
    {
        // Arrange
        var (_, node) = RenderedSwitch();

        // Act
        var (_, below) = WorkflowEditorViewModel.OutputPoint(node, WorkflowPorts.Default);

        // Assert
        Assert.That(WorkflowEditorViewModel.HeightOf(node) + node.Y, Is.GreaterThan(below));
    }

    private const int StudioMinimumColors = 5;
    private const int SamplingStep = 8;

    [AvaloniaTest]
    public void Render_TheBackgroundStudioWithFourLayers_DrawsCardsTextAndColorFields()
    {
        // Arrange
        var studio = new DeckHost.App.ViewModels.BackgroundLayersViewModel(
        [
            new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = "#1E293B" },
            new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = "#FF0000", ToColor = "#0000FF", Angle = 45, Binding = new BackgroundBinding("system.cpu") { FromColor = "#000000", ToColor = "#FF0000" } },
            new BackgroundLayer(BackgroundLayerKinds.Image),
            new BackgroundLayer("hologram"),
        ], _ => { });
        var window = new Window { Width = 760, Height = 1600, Content = new ScrollViewer { Content = new BackgroundLayersEditor { DataContext = studio } } };
        window.Show();

        // Act
        using var frame = window.CaptureRenderedFrame()!;
        window.Close();

        // Assert
        var colors = new HashSet<Color>();
        for (var y = 0; y < frame.PixelSize.Height; y += SamplingStep)
        {
            for (var x = 0; x < frame.PixelSize.Width; x += SamplingStep)
            {
                colors.Add(Pixel(frame, x, y));
            }
        }

        Assert.That(colors.Count, Is.GreaterThan(StudioMinimumColors), "cards, text and color fields were drawn");
        Assert.That(studio.Rows, Has.Count.EqualTo(4));
    }

    private const int LinkGapStartX = 232;
    private const int LinkGapEndX = 318;
    private const int LinkRowTop = 20;
    private const int LinkRowBottom = 60;
    private const int MinimumLinkPixels = 40;

    [AvaloniaTest]
    public void Render_ALinkBetweenTwoNodes_IsDrawnInTheGapBetweenThem()
    {
        // Arrange
        using var editor = new WorkflowEditorViewModel("device", "layout", Key, "Key 1", new Workflow(), [], _ => true);
        var first = editor.AddNode(WorkflowNodeKinds.Wait, 20, 20);
        var last = editor.AddNode(WorkflowNodeKinds.Wait, 320, 20);
        editor.SelectNode(null);
        var before = RenderCanvas(editor);

        // Act
        editor.Connect(first.Id, WorkflowPorts.Next, last.Id);
        editor.SelectNode(null);
        var linked = RenderCanvas(editor);

        // Assert
        var changed = 0;
        for (var x = LinkGapStartX; x < LinkGapEndX; x++)
        {
            for (var y = LinkRowTop; y < LinkRowBottom; y++)
            {
                if (Pixel(before, x, y) != Pixel(linked, x, y))
                {
                    changed++;
                }
            }
        }

        Assert.That(changed, Is.GreaterThan(MinimumLinkPixels), "the link is drawn");
    }

    private const string NodeFillColor = "#111827";
    private const string CanvasColor = "#0B1120";
    private const string CanvasDotColor = "#1E293B";
    private const int SpacingWindowWidth = 800;
    private const int SpacingWindowHeight = 200;
    private const double FirstNodeX = 20;
    private const double TargetNodeX = 260;
    private const double RowY = 20;
    private const int PixelsOffTheEdge = 5;
    private const int BodyInset = 8;
    private const int BottomInset = 6;

    private static WriteableBitmap RenderCanvasOf(WorkflowEditorViewModel editor, int width, int height)
    {
        var canvas = new WorkflowCanvas { DataContext = editor, Width = width, Height = height };
        var window = new Window { Width = width, Height = height, Content = canvas };
        window.Show();
        var frame = window.CaptureRenderedFrame()!;
        window.Close();
        return frame;
    }

    private static bool EveryPixelIsCanvas(WriteableBitmap frame, int fromX, int toX, int y)
    {
        var canvas = new[] { Color.Parse(CanvasColor), Color.Parse(CanvasDotColor) };
        for (var x = fromX; x <= toX; x++)
        {
            if (!canvas.Contains(Pixel(frame, x, y)))
            {
                return false;
            }
        }

        return true;
    }

    [AvaloniaTest]
    public void InsertIntoLink_NodeDroppedBetweenCloseNeighbours_LeavesTheGapBeforeTheNextNodeFreeOfNodes()
    {
        // Arrange
        using var editor = new WorkflowEditorViewModel("device", "layout", Key, "Key 1", new Workflow(), [], _ => true);
        var first = editor.AddNode(WorkflowNodeKinds.Wait, FirstNodeX, RowY);
        var target = editor.AddNode(WorkflowNodeKinds.Wait, TargetNodeX, RowY);
        editor.Connect(first.Id, WorkflowPorts.Next, target.Id);
        var link = editor.Workflow.Links.Single();
        var dropped = editor.AddNode(WorkflowNodeKinds.Wait, TargetNodeX, SpacingWindowHeight / 2d);

        // Act
        editor.InsertIntoLink(dropped.Id, link, TargetNodeX, RowY);
        editor.SelectNode(null);
        using var frame = RenderCanvasOf(editor, SpacingWindowWidth, SpacingWindowHeight);

        // Assert
        var inserted = editor.Workflow.Find(dropped.Id)!;
        var moved = editor.Workflow.Find(target.Id)!;
        var belowTheLink = (int)(RowY + WorkflowEditorViewModel.HeightOf(inserted)) - BottomInset;
        var gapStart = (int)(inserted.X + WorkflowEditorViewModel.NodeWidth) + PixelsOffTheEdge;
        var gapEnd = (int)moved.X - PixelsOffTheEdge;
        Assert.That(gapEnd, Is.GreaterThan(gapStart), "the next node starts after the inserted one ends");
        Assert.That(EveryPixelIsCanvas(frame, gapStart, gapEnd, belowTheLink), Is.True, "nothing but canvas between them");
    }

    [AvaloniaTest]
    public void InsertIntoLink_NodeDroppedBetweenCloseNeighbours_DrawsBothNodesWithTheirOwnBodies()
    {
        // Arrange
        using var editor = new WorkflowEditorViewModel("device", "layout", Key, "Key 1", new Workflow(), [], _ => true);
        var first = editor.AddNode(WorkflowNodeKinds.Wait, FirstNodeX, RowY);
        var target = editor.AddNode(WorkflowNodeKinds.Wait, TargetNodeX, RowY);
        editor.Connect(first.Id, WorkflowPorts.Next, target.Id);
        var link = editor.Workflow.Links.Single();
        var dropped = editor.AddNode(WorkflowNodeKinds.Wait, TargetNodeX, SpacingWindowHeight / 2d);

        // Act
        editor.InsertIntoLink(dropped.Id, link, TargetNodeX, RowY);
        editor.SelectNode(null);
        using var frame = RenderCanvasOf(editor, SpacingWindowWidth, SpacingWindowHeight);

        // Assert
        var inserted = editor.Workflow.Find(dropped.Id)!;
        var moved = editor.Workflow.Find(target.Id)!;
        var bodyY = (int)(RowY + WorkflowEditorViewModel.HeightOf(inserted)) - BottomInset;
        Assert.That(Pixel(frame, (int)inserted.X + BodyInset, bodyY), Is.EqualTo(Color.Parse(NodeFillColor)), "the inserted node");
        Assert.That(Pixel(frame, (int)moved.X + BodyInset, bodyY), Is.EqualTo(Color.Parse(NodeFillColor)), "the node that moved");
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

    private const string OldBlueBox = "#2563EB";

    [AvaloniaTest]
    public void Render_ADeviceBody_ShowsABodyItsEdgeAndTheScreensNotOneFlatColor()
    {
        // Arrange
        var controls = Enumerable.Range(0, 3)
            .Select(static i => new PhysicalControl($"key.{i}", $"Key {i + 1}", ControlType.Key, new DeckHost.Contracts.Models.Rect(i * 12, 0, 10, 10),
                InputCapabilities.Press, OutputCapabilities.ColorImage))
            .ToList();
        var body = new DeviceBodyView
        {
            Geometry = DeviceGeometry.Of(controls, []),
            Screen = new SolidColorBrush(Color.Parse("#112233")),
            Body = new SolidColorBrush(Color.Parse("#2C313C")),
            Edge = new SolidColorBrush(Color.Parse("#445566")),
            Width = 600,
            Height = 400,
        };
        var window = new Window { Width = 600, Height = 400, Content = body };
        window.Show();

        // Act
        using var frame = window.CaptureRenderedFrame()!;

        // Assert
        var colors = new HashSet<Color>();
        for (var y = 0; y < 400; y += 4)
        {
            for (var x = 0; x < 600; x += 4)
            {
                colors.Add(Pixel(frame, x, y));
            }
        }

        Assert.That(colors.Count, Is.GreaterThan(2), "a body, its edge and the screens, not one flat color");
        Assert.That(colors, Does.Not.Contain(Color.Parse(OldBlueBox)), "and not the blue box it used to be");
    }

    private const string PreviewRed = "#FF0000";
    private const string PreviewBlack = "#000000";
    private const string PreviewCpu = "system.cpu";
    private const double PreviewFullLoad = 100.0;

    private static DeviceDefinition TwoKeyDevice() => new(
        "preview.device",
        "Preview device",
        [
            new PhysicalControl("key.0", "Key 1", ControlType.Key, new DeckHost.Contracts.Models.Rect(0, 0, 10, 10), InputCapabilities.Press, OutputCapabilities.ColorImage, 8, 8),
            new PhysicalControl("key.1", "Key 2", ControlType.Key, new DeckHost.Contracts.Models.Rect(12, 0, 10, 10), InputCapabilities.Press, OutputCapabilities.ColorImage, 8, 8),
        ],
        true);

    private static ProfileBackground FollowingCpu() => new()
    {
        Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = PreviewBlack, Binding = new BackgroundBinding(PreviewCpu) { FromColor = PreviewBlack, ToColor = PreviewRed } }],
    };

    private static Window ShowPreview(BackgroundPreviewViewModel preview)
    {
        var view = new BackgroundPreviewView { DataContext = preview };
        var window = new Window { Width = preview.Geometry.Width, Height = preview.Geometry.Height, Content = view };
        window.Show();
        return window;
    }

    private static Color TileCenter(WriteableBitmap frame, BackgroundPreviewViewModel preview, string controlId)
    {
        var bounds = preview.Tiles.Single(tile => tile.Frame.ControlId == controlId).Bounds;
        return Pixel(frame, (int)(bounds.X + (bounds.Width / 2)), (int)(bounds.Y + (bounds.Height / 2)));
    }

    [AvaloniaTest]
    public void BackgroundPreviewView_SolidLayer_DrawsTheLayerColorOnEachScreenTile()
    {
        // Arrange
        using var store = new DeckHost.Core.State.VariableStore();
        var background = new ProfileBackground { Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = PreviewRed }] };
        using var preview = new BackgroundPreviewViewModel(store, TwoKeyDevice, () => background, renderInline: true);
        var window = ShowPreview(preview);

        // Act
        using var frame = window.CaptureRenderedFrame()!;
        window.Close();

        // Assert
        Assert.That(TileCenter(frame, preview, "key.0"), Is.EqualTo(Color.Parse(PreviewRed)));
        Assert.That(TileCenter(frame, preview, "key.1"), Is.EqualTo(Color.Parse(PreviewRed)));
    }

    [AvaloniaTest]
    public void BackgroundPreviewView_BoundVariableChanged_RedrawsTheTileInTheNewColor()
    {
        // Arrange
        using var store = new DeckHost.Core.State.VariableStore();
        using var preview = new BackgroundPreviewViewModel(store, TwoKeyDevice, FollowingCpu, renderInline: true);
        var window = ShowPreview(preview);
        using var before = window.CaptureRenderedFrame()!;

        // Act
        store.Publish(PreviewCpu, PreviewFullLoad);
        using var after = window.CaptureRenderedFrame()!;
        window.Close();

        // Assert
        Assert.That(TileCenter(before, preview, "key.0"), Is.Not.EqualTo(Color.Parse(PreviewRed)), "dark while the CPU is idle");
        Assert.That(TileCenter(after, preview, "key.0"), Is.EqualTo(Color.Parse(PreviewRed)), "red at full load");
    }
}
