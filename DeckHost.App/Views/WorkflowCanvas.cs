using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Models;
using Rect = Avalonia.Rect;
using DeckHost.Core.Workflows;

namespace DeckHost.App.Views;

/// <summary>
/// Draws a workflow (<see cref="WorkflowEditorViewModel"/>) as nodes with ports and curved links,
/// and edits it with the pointer: drag a node to move it, drag from an output port (the dots on the
/// right) onto another node to link them, click a link to select it, Delete removes the selection.
/// Drag the empty canvas to pan; Ctrl + wheel zooms. Actions dropped on it become action nodes.
/// </summary>
public sealed class WorkflowCanvas : Control
{
    private const double PortRadius = 6;
    private const double Grid = 20;

    private static readonly Typeface Regular = new(FontFamily.Default);
    private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#0B1120"));
    private static readonly IBrush GridDot = new SolidColorBrush(Color.Parse("#1E293B"));
    private static readonly IBrush NodeFill = new SolidColorBrush(Color.Parse("#111827"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush HintBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.Parse("#FCA5A5"));
    private static readonly IPen NodeBorder = new Pen(new SolidColorBrush(Color.Parse("#334155")), 1);
    private static readonly IPen SelectedBorder = new Pen(Brushes.White, 2);
    private static readonly IPen FailedBorder = new Pen(new SolidColorBrush(Color.Parse("#EF4444")), 3);
    private static readonly Dictionary<string, Geometry?> Icons = [];

    private WorkflowEditorViewModel? _viewModel;
    private Vector _offset = new(0, 0);
    private double _zoom = 1;

    private Drag _drag = Drag.None;
    private Point _dragStart;            // screen
    private Vector _dragOffsetAtStart;
    private string? _dragNodeId;
    private Point _dragNodeOrigin;       // canvas
    private Vector _dragNodeDelta;       // canvas
    private string? _linkFrom;
    private string? _linkPort;
    private Point _linkEnd;              // canvas
    private string? _hoverNodeId;
    private WorkflowLink? _insertLink;   // the link a dragged or dropped node would go into

    private enum Drag
    {
        None,
        Pan,
        Node,
        Link,
    }

    public WorkflowCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>Raised with a message for the user (e.g. why a link can't be made).</summary>
    public event EventHandler<string>? Message;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.Changed -= OnViewModelChanged;
        }

        _viewModel = DataContext as WorkflowEditorViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Changed += OnViewModelChanged;
        }

        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewModel is not null)
        {
            _viewModel.Changed -= OnViewModelChanged;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_viewModel is not null)
        {
            _viewModel.Changed -= OnViewModelChanged;
            _viewModel.Changed += OnViewModelChanged;
        }
    }

    private void OnViewModelChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>Where a screen point is on the canvas (before pan and zoom).</summary>
    public Point ToCanvas(Point screen) => new((screen.X - _offset.X) / _zoom, (screen.Y - _offset.Y) / _zoom);

    /// <summary>Pans and zooms so every node is in view.</summary>
    public void FitToView()
    {
        if (_viewModel is not { Workflow.Nodes.Count: > 0 } vm || Bounds.Width < 10 || Bounds.Height < 10)
        {
            _offset = new Vector(0, 0);
            _zoom = 1;
            InvalidateVisual();
            return;
        }

        var left = vm.Workflow.Nodes.Min(static node => node.X) - 40;
        var top = vm.Workflow.Nodes.Min(static node => node.Y) - 40;
        var right = vm.Workflow.Nodes.Max(static node => node.X + WorkflowEditorViewModel.NodeWidth) + 40;
        var bottom = vm.Workflow.Nodes.Max(static node => node.Y + WorkflowEditorViewModel.HeightOf(node)) + 40;
        _zoom = Math.Clamp(Math.Min(Bounds.Width / (right - left), Bounds.Height / (bottom - top)), 0.6, 1.25);
        _offset = new Vector(-left * _zoom, -top * _zoom);
        InvalidateVisual();
    }

    public void ResetZoom()
    {
        _zoom = 1;
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- drawing

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        DrawGrid(context, bounds);
        if (_viewModel is not { } vm)
        {
            return;
        }

        using (context.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset.X, _offset.Y)))
        {
            var nodes = vm.Workflow.Nodes.ToDictionary(static node => node.Id, node => Shown(node), StringComparer.Ordinal);

            foreach (var link in vm.Workflow.Links)
            {
                if (nodes.TryGetValue(link.From, out var from) && nodes.TryGetValue(link.To, out var to))
                {
                    DrawLink(context, vm, link, from, to, link == _insertLink);
                }
            }

            if (_drag == Drag.Link && _linkFrom is not null && _linkPort is not null && nodes.TryGetValue(_linkFrom, out var source))
            {
                var (x, y) = WorkflowEditorViewModel.OutputPoint(source, _linkPort);
                var pen = new Pen(new SolidColorBrush(PortColor(_linkPort)), 2, new DashStyle([4, 3], 0));
                context.DrawGeometry(null, pen, Curve(new Point(x, y), _linkEnd));
            }

            foreach (var node in nodes.Values)
            {
                DrawNode(context, vm, node);
            }
        }

        if (vm.IsEmpty)
        {
            var hint = Text("Add a When node from the palette to start, then link actions to it.", 14, HintBrush, Regular);
            context.DrawText(hint, new Point((bounds.Width - hint.Width) / 2, (bounds.Height - hint.Height) / 2));
        }
    }

    /// <summary>A node where it's drawn: moved along while it's dragged.</summary>
    private WorkflowNode Shown(WorkflowNode node) =>
        _drag == Drag.Node && node.Id == _dragNodeId
            ? node with { X = _dragNodeOrigin.X + _dragNodeDelta.X, Y = _dragNodeOrigin.Y + _dragNodeDelta.Y }
            : node;

    private void DrawGrid(DrawingContext context, Rect bounds)
    {
        var step = Grid * _zoom;
        if (step < 8)
        {
            return;
        }

        var startX = _offset.X % step;
        var startY = _offset.Y % step;
        for (var x = startX; x < bounds.Width; x += step)
        {
            for (var y = startY; y < bounds.Height; y += step)
            {
                context.FillRectangle(GridDot, new Rect(x - 0.75, y - 0.75, 1.5, 1.5));
            }
        }
    }

    private static void DrawLink(DrawingContext context, WorkflowEditorViewModel vm, WorkflowLink link, WorkflowNode from, WorkflowNode to, bool target)
    {
        var (x1, y1) = WorkflowEditorViewModel.OutputPoint(from, link.Port);
        var (x2, y2) = WorkflowEditorViewModel.InputPoint(to);
        var color = PortColor(link.Port);
        var active = vm.IsActive(link);
        var selected = vm.IsSelected(link);
        var pen = new Pen(
            new SolidColorBrush(active ? Color.Parse("#FDE047") : selected || target ? Colors.White : color),
            selected || active || target ? 3.5 : 2.2);
        if (target)
        {
            // The link a dropped node would be put into: a soft halo under it.
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Colors.White, 0.25), 12), Curve(new Point(x1, y1), new Point(x2, y2)));
        }

        context.DrawGeometry(null, pen, Curve(new Point(x1, y1), new Point(x2, y2)));

        // An arrow head at the input.
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(new Point(x2, y2), true);
            g.LineTo(new Point(x2 - 9, y2 - 5));
            g.LineTo(new Point(x2 - 9, y2 + 5));
            g.EndFigure(true);
        }

        context.DrawGeometry(pen.Brush, null, head);
    }

    private void DrawNode(DrawingContext context, WorkflowEditorViewModel vm, WorkflowNode node)
    {
        var type = WorkflowEditorViewModel.TypeOf(node);
        var accent = Color.FromUInt32(type.Color);
        var width = WorkflowEditorViewModel.NodeWidth;
        var height = WorkflowEditorViewModel.HeightOf(node);
        var rect = new Rect(node.X, node.Y, width, height);
        var missing = vm.IsMissing(node);

        // A glow while a run passes through.
        if (vm.IsActive(node))
        {
            context.DrawRectangle(new SolidColorBrush(accent, 0.35), null, rect.Inflate(6), 14, 14);
        }

        context.DrawRectangle(NodeFill, vm.HasFailed(node) ? FailedBorder : vm.IsSelected(node) ? SelectedBorder : _hoverNodeId == node.Id && _drag == Drag.Link ? new Pen(new SolidColorBrush(accent), 2) : NodeBorder, rect, 10, 10);

        // Header: the kind's color, icon and name (an action's plugin).
        var header = new Rect(node.X, node.Y, width, WorkflowEditorViewModel.HeaderHeight);
        using (context.PushClip(new RoundedRect(rect, 10)))
        {
            context.FillRectangle(new SolidColorBrush(accent, 0.22), header);

            // A run waiting here fills the node from the left over the time the wait takes.
            if (vm.WaitProgress(node) is { } progress)
            {
                context.FillRectangle(new SolidColorBrush(accent, 0.40), new Rect(node.X, node.Y, width * progress, height));
                context.FillRectangle(new SolidColorBrush(accent), new Rect(node.X + (width * progress) - 1.5, node.Y, 3, height));
            }
        }

        if (IconOf(type.Icon) is { } icon)
        {
            using (context.PushTransform(Matrix.CreateScale(0.62, 0.62) * Matrix.CreateTranslation(node.X + 9, node.Y + 7.5)))
            {
                context.DrawGeometry(new SolidColorBrush(accent), null, icon);
            }
        }

        var headerText = Text(Fit(vm.HeaderOf(node).ToUpperInvariant(), 26), 11, new SolidColorBrush(accent), Bold);
        context.DrawText(headerText, new Point(node.X + 30, node.Y + ((WorkflowEditorViewModel.HeaderHeight - headerText.Height) / 2)));

        // The node in one line.
        var summary = Text(Fit(vm.SummaryOf(node), 28), 13, missing ? MissingBrush : TextBrush, Bold);
        context.DrawText(summary, new Point(node.X + 12, node.Y + WorkflowEditorViewModel.HeaderHeight + 3));
        if (missing)
        {
            var note = Text("not loaded", 10, MissingBrush, Regular);
            context.DrawText(note, new Point(node.X + width - note.Width - 10, node.Y + WorkflowEditorViewModel.HeaderHeight + 6));
        }

        // Input on the left (not on a trigger).
        if (WorkflowNodeKinds.HasInput(node.Kind))
        {
            var (ix, iy) = WorkflowEditorViewModel.InputPoint(node);
            context.DrawEllipse(NodeFill, new Pen(new SolidColorBrush(Color.Parse("#94A3B8")), 2), new Point(ix, iy), PortRadius, PortRadius);
        }

        // Outputs on the right, each with its label.
        foreach (var port in WorkflowNodeKinds.OutputsOf(node.Kind))
        {
            var (ox, oy) = WorkflowEditorViewModel.OutputPoint(node, port);
            var color = PortColor(port);
            var label = Text(WorkflowPorts.Describe(port), 11, HintBrush, Regular);
            context.DrawText(label, new Point(ox - label.Width - 14, oy - (label.Height / 2)));
            var linked = vm.Workflow.LinksFrom(node.Id, port).Any();
            context.DrawEllipse(linked ? new SolidColorBrush(color) : NodeFill, new Pen(new SolidColorBrush(color), 2), new Point(ox, oy), PortRadius, PortRadius);
        }

        if (WorkflowNodeKinds.OutputsOf(node.Kind).Count == 0)
        {
            var end = Text("ends the run", 11, HintBrush, Regular);
            context.DrawText(end, new Point(node.X + 12, node.Y + WorkflowEditorViewModel.HeaderHeight + WorkflowEditorViewModel.SummaryHeight + 2));
        }
    }

    private static Geometry Curve(Point from, Point to)
    {
        var bend = Math.Max(40, Math.Abs(to.X - from.X) / 2);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(from, false);
            g.CubicBezierTo(new Point(from.X + bend, from.Y), new Point(to.X - bend, to.Y), to);
            g.EndFigure(false);
        }

        return geometry;
    }

    private static Color PortColor(string port) => port switch
    {
        WorkflowPorts.Yes => Color.Parse("#22C55E"),
        WorkflowPorts.No => Color.Parse("#EF4444"),
        WorkflowPorts.First => Color.Parse("#2DD4BF"),
        WorkflowPorts.Second => Color.Parse("#F472B6"),
        WorkflowPorts.Each => Color.Parse("#EC4899"),
        WorkflowPorts.Done => Color.Parse("#A78BFA"),
        _ => Color.Parse("#64748B"),
    };

    private static FormattedText Text(string text, double size, IBrush brush, Typeface typeface) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);

    private static string Fit(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static Geometry? IconOf(string data)
    {
        if (!Icons.TryGetValue(data, out var geometry))
        {
            try
            {
                geometry = Geometry.Parse("F0 " + data);
            }
            catch (Exception)
            {
                geometry = null;
            }

            Icons[data] = geometry;
        }

        return geometry;
    }

    // ---------------------------------------------------------------- hit testing

    private WorkflowNode? NodeAt(Point canvas) =>
        _viewModel?.Workflow.Nodes.LastOrDefault(node =>
            new Rect(node.X, node.Y, WorkflowEditorViewModel.NodeWidth, WorkflowEditorViewModel.HeightOf(node)).Contains(canvas));

    private (WorkflowNode Node, string Port)? PortAt(Point canvas)
    {
        if (_viewModel is null)
        {
            return null;
        }

        var reach = (PortRadius + 5) / Math.Min(1, _zoom);
        foreach (var node in _viewModel.Workflow.Nodes.Reverse())
        {
            foreach (var port in WorkflowNodeKinds.OutputsOf(node.Kind))
            {
                var (x, y) = WorkflowEditorViewModel.OutputPoint(node, port);
                if (Math.Abs(canvas.X - x) <= reach && Math.Abs(canvas.Y - y) <= reach)
                {
                    return (node, port);
                }
            }
        }

        return null;
    }

    private WorkflowLink? LinkAt(Point canvas)
    {
        if (_viewModel is null)
        {
            return null;
        }

        var reach = 7 / Math.Min(1, _zoom);
        foreach (var link in _viewModel.Workflow.Links.Reverse())
        {
            if (_viewModel.Workflow.Find(link.From) is not { } from || _viewModel.Workflow.Find(link.To) is not { } to)
            {
                continue;
            }

            var (x1, y1) = WorkflowEditorViewModel.OutputPoint(from, link.Port);
            var (x2, y2) = WorkflowEditorViewModel.InputPoint(to);
            var bend = Math.Max(40, Math.Abs(x2 - x1) / 2);
            for (var i = 0; i <= 40; i++)
            {
                var t = i / 40.0;
                var point = Bezier(new Point(x1, y1), new Point(x1 + bend, y1), new Point(x2 - bend, y2), new Point(x2, y2), t);
                if (Math.Abs(point.X - canvas.X) <= reach && Math.Abs(point.Y - canvas.Y) <= reach)
                {
                    return link;
                }
            }
        }

        return null;
    }

    /// <summary>The link whose curve runs through a rectangle (the one nearest its center), for dropping a node into it.</summary>
    private WorkflowLink? LinkThrough(Rect area)
    {
        if (_viewModel is null)
        {
            return null;
        }

        WorkflowLink? best = null;
        var bestDistance = double.MaxValue;
        var middle = area.Center;
        foreach (var link in _viewModel.Workflow.Links)
        {
            if (_viewModel.Workflow.Find(link.From) is not { } from || _viewModel.Workflow.Find(link.To) is not { } to)
            {
                continue;
            }

            var (x1, y1) = WorkflowEditorViewModel.OutputPoint(from, link.Port);
            var (x2, y2) = WorkflowEditorViewModel.InputPoint(to);
            var bend = Math.Max(40, Math.Abs(x2 - x1) / 2);
            for (var i = 0; i <= 40; i++)
            {
                var point = Bezier(new Point(x1, y1), new Point(x1 + bend, y1), new Point(x2 - bend, y2), new Point(x2, y2), i / 40.0);
                if (!area.Contains(point))
                {
                    continue;
                }

                var distance = ((point.X - middle.X) * (point.X - middle.X)) + ((point.Y - middle.Y) * (point.Y - middle.Y));
                if (distance < bestDistance)
                {
                    best = link;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    private static Point Bezier(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var u = 1 - t;
        return new Point(
            (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X),
            (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y));
    }

    // ---------------------------------------------------------------- pointer

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_viewModel is not { } vm)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var screen = point.Position;
        var canvas = ToCanvas(screen);
        _dragStart = screen;

        if (point.Properties.IsMiddleButtonPressed || (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            StartPan(e);
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            // Select what is under the pointer and offer what can be done with it.
            WorkflowNode? onNode = NodeAt(canvas);
            WorkflowLink? onLink = onNode is null ? LinkAt(canvas) : null;
            if (onNode is not null)
            {
                vm.SelectNode(onNode.Id);
            }
            else if (onLink is not null)
            {
                vm.SelectLink(onLink);
            }

            ShowMenu(onNode, onLink);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (PortAt(canvas) is { } port)
        {
            _drag = Drag.Link;
            _linkFrom = port.Node.Id;
            _linkPort = port.Port;
            _linkEnd = canvas;
            vm.SelectNode(port.Node.Id);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (NodeAt(canvas) is { } node)
        {
            vm.SelectNode(node.Id);
            _drag = Drag.Node;
            _dragNodeId = node.Id;
            _dragNodeOrigin = new Point(node.X, node.Y);
            _dragNodeDelta = default;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (LinkAt(canvas) is { } link)
        {
            vm.SelectLink(link);
            e.Handled = true;
            return;
        }

        vm.SelectNode(null);
        StartPan(e);
    }

    private void StartPan(PointerPressedEventArgs e)
    {
        _drag = Drag.Pan;
        _dragOffsetAtStart = _offset;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_viewModel is not { } vm)
        {
            return;
        }

        var screen = e.GetPosition(this);
        var canvas = ToCanvas(screen);
        switch (_drag)
        {
            case Drag.Pan:
                _offset = _dragOffsetAtStart + (screen - _dragStart);
                InvalidateVisual();
                break;
            case Drag.Node:
                _dragNodeDelta = (screen - _dragStart) / _zoom;
                _insertLink = _dragNodeId is { } moving && vm.CanInsertIntoLink(moving) && vm.Workflow.Find(moving) is { } shown
                    ? LinkThrough(new Rect(_dragNodeOrigin.X + _dragNodeDelta.X, _dragNodeOrigin.Y + _dragNodeDelta.Y, WorkflowEditorViewModel.NodeWidth, WorkflowEditorViewModel.HeightOf(shown)))
                    : null;
                InvalidateVisual();
                break;
            case Drag.Link:
                _linkEnd = canvas;
                _hoverNodeId = NodeAt(canvas)?.Id;
                InvalidateVisual();
                break;
            default:
                Cursor = PortAt(canvas) is not null ? new Cursor(StandardCursorType.Cross)
                    : NodeAt(canvas) is not null ? new Cursor(StandardCursorType.SizeAll)
                    : LinkAt(canvas) is not null ? new Cursor(StandardCursorType.Hand)
                    : Cursor.Default;
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var drag = _drag;
        _drag = Drag.None;
        e.Pointer.Capture(null);
        if (_viewModel is not { } vm)
        {
            return;
        }

        var canvas = ToCanvas(e.GetPosition(this));
        switch (drag)
        {
            case Drag.Node when _dragNodeId is { } id:
                if (Math.Abs(_dragNodeDelta.X) > 0.5 || Math.Abs(_dragNodeDelta.Y) > 0.5)
                {
                    var (nx, ny) = (_dragNodeOrigin.X + _dragNodeDelta.X, _dragNodeOrigin.Y + _dragNodeDelta.Y);
                    if (_insertLink is { } into)
                    {
                        // Moved and put into the link: one undo step.
                        if (vm.InsertIntoLink(id, into, nx, ny) is { } why)
                        {
                            Message?.Invoke(this, why);
                        }
                    }
                    else
                    {
                        vm.MoveNode(id, nx, ny);
                    }
                }

                break;
            case Drag.Link when _linkFrom is { } from && _linkPort is { } port:
                if (NodeAt(canvas) is { } target && target.Id != from)
                {
                    if (vm.Connect(from, port, target.Id) is { } problem)
                    {
                        Message?.Invoke(this, problem);
                    }
                }

                break;
        }

        _dragNodeId = null;
        _linkFrom = null;
        _linkPort = null;
        _hoverNodeId = null;
        _insertLink = null;
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag != Drag.None)
        {
            _drag = Drag.None;
            _insertLink = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var screen = e.GetPosition(this);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            // Zoom around the pointer.
            var before = ToCanvas(screen);
            _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), 0.3, 2.0);
            _offset = new Vector(screen.X - (before.X * _zoom), screen.Y - (before.Y * _zoom));
        }
        else
        {
            var delta = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? new Vector(e.Delta.Y, 0) : new Vector(e.Delta.X, e.Delta.Y);
            _offset += delta * 40;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_viewModel is not { } vm)
        {
            return;
        }

        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.Delete or Key.Back when vm.HasSelection:
                vm.DeleteSelection();
                e.Handled = true;
                break;
            case Key.Escape when _drag != Drag.None:
                _drag = Drag.None;
                _insertLink = null;
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Z when command && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
            case Key.Y when command:
                vm.RedoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Z when command:
                vm.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D when command:
                vm.DuplicateSelectionCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ---------------------------------------------------------------- dropping actions

    /// <summary>The rectangle a node dropped at a pointer position takes (it is centered on the pointer).</summary>
    private Rect DropArea(Point canvas) =>
        new(canvas.X - (WorkflowEditorViewModel.NodeWidth / 2), canvas.Y - 20, WorkflowEditorViewModel.NodeWidth, WorkflowEditorViewModel.HeaderHeight + WorkflowEditorViewModel.SummaryHeight + 6);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var kind = e.DataTransfer.TryGetValue(DragFormats.WorkflowNode);
        var action = e.DataTransfer.TryGetValue(DragFormats.Action);
        e.DragEffects = action is not null || kind is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        // A node that can pass a run on lights the link it would be put into.
        var link = (kind is null || WorkflowEditorViewModel.CanBeInserted(kind)) && (kind is not null || action is not null)
            ? LinkThrough(DropArea(ToCanvas(e.GetPosition(this))))
            : null;
        if (link != _insertLink)
        {
            _insertLink = link;
            InvalidateVisual();
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (_insertLink is not null)
        {
            _insertLink = null;
            InvalidateVisual();
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var into = _insertLink;
        _insertLink = null;
        if (_viewModel is not { } vm)
        {
            return;
        }

        var at = ToCanvas(e.GetPosition(this));
        var x = at.X - (WorkflowEditorViewModel.NodeWidth / 2);
        var y = at.Y - 20;
        if (e.DataTransfer.TryGetValue(DragFormats.WorkflowNode) is { } kind)
        {
            vm.AddNode(kind, x, y, into);
        }
        else if (e.DataTransfer.TryGetValue(DragFormats.Action) is { } action &&
                 vm.AvailableActions.FirstOrDefault(item => ReferenceEquals(item.Action, action)) is { } item)
        {
            vm.AddAction(item, x, y, into);
        }

        InvalidateVisual();
        Focus();
    }

    // ---------------------------------------------------------------- context menus

    private void ShowMenu(WorkflowNode? node, WorkflowLink? link)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        var items = new List<Control>();
        if (node is not null)
        {
            if (node.Kind == WorkflowNodeKinds.Trigger)
            {
                items.Add(Item("▶ Run from here", () => vm.RunCommand.Execute(null)));
                items.Add(new Separator());
            }

            items.Add(Item("Duplicate", () => vm.DuplicateSelection(), "Ctrl+D"));
            items.Add(Item("Take off its links", () => vm.DisconnectSelection(), enabled: vm.SelectedHasLinks));
            items.Add(new Separator());
            items.Add(Item("Remove", () => vm.DeleteSelection(), "Delete"));
        }
        else if (link is not null)
        {
            items.Add(Item("Remove link", () => vm.DeleteSelection(), "Delete"));
        }
        else
        {
            items.Add(Item("Fit to view", FitToView));
            items.Add(Item("Arrange", () => vm.ArrangeCommand.Execute(null)));
            items.Add(Item("Undo", () => vm.UndoCommand.Execute(null), "Ctrl+Z", vm.CanUndo));
            items.Add(Item("Redo", () => vm.RedoCommand.Execute(null), "Ctrl+Y", vm.CanRedo));
        }

        new ContextMenu { ItemsSource = items }.Open(this);
    }

    private static MenuItem Item(string header, Action click, string? gesture = null, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (gesture is not null)
        {
            item.InputGesture = KeyGesture.Parse(gesture);
        }

        item.Click += (_, _) => click();
        return item;
    }
}
