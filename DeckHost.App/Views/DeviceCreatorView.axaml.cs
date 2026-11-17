using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

/// <summary>The device creator page; dragging places the controls on the layout canvas.</summary>
public partial class DeviceCreatorView : UserControl, IWidePage
{
    private CreatorControlViewModel? _dragging;
    private Point _grab;
    private bool _moved;
    private bool _collapseOnRelease;

    public DeviceCreatorView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as DeviceCreatorViewModel)?.Resume();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Another page on top, or closed: stop reading the device.
        (DataContext as DeviceCreatorViewModel)?.Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    private void Control_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: CreatorControlViewModel control } border || DataContext is not DeviceCreatorViewModel creator)
        {
            return;
        }

        if (e.GetCurrentPoint(border).Properties.IsRightButtonPressed)
        {
            // The context menu is for what is selected: a control outside the selection becomes the selection, no drag starts.
            if (!control.IsSelected)
            {
                creator.SelectedControl = control;
            }

            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            creator.ToggleSelection(control);
        }
        else if (!control.IsSelected)
        {
            creator.SelectedControl = control;
        }

        _dragging = control;
        _moved = false;
        _collapseOnRelease = control.IsSelected && creator.HasSeveralSelected && e.KeyModifiers == KeyModifiers.None;
        _grab = e.GetPosition(border);
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void Control_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null || sender is not Border border || border.FindAncestorOfType<Canvas>() is not { } canvas)
        {
            return;
        }

        var position = e.GetPosition(canvas);
        _moved = true;
        // Alt held: place freely, without snapping to the other controls.
        (DataContext as DeviceCreatorViewModel)?.DragTo(_dragging, position.X - _grab.X, position.Y - _grab.Y, snap: !e.KeyModifiers.HasFlag(KeyModifiers.Alt));
    }

    private void Control_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging is { } control)
        {
            _dragging = null;
            e.Pointer.Capture(null);
            control.Dropped();

            // A plain click on one of several selected controls selects just that one (a drag moved them all).
            if (_collapseOnRelease && !_moved && DataContext is DeviceCreatorViewModel creator)
            {
                creator.SelectOnly(control);
            }
        }
    }

    // ---------------------------------------------------------------- the canvas: drag selection and right-click menus

    private Point? _marqueeStart;

    private void Canvas_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CanvasHost).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Shift and drag on the empty canvas selects what the rectangle touches; a plain click on it selects nothing.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _marqueeStart = e.GetPosition(ControlsCanvas);
            e.Pointer.Capture(CanvasHost);
            e.Handled = true;
        }
        else if (DataContext is DeviceCreatorViewModel creator)
        {
            creator.SelectWithin(new SnapRect(-1, -1, 0, 0), add: false);
        }
    }

    private void Canvas_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_marqueeStart is not { } start)
        {
            return;
        }

        var area = AreaBetween(start, e.GetPosition(ControlsCanvas));
        Canvas.SetLeft(MarqueeRect, area.X);
        Canvas.SetTop(MarqueeRect, area.Y);
        MarqueeRect.Width = area.Width;
        MarqueeRect.Height = area.Height;
        MarqueeRect.IsVisible = true;
    }

    private void Canvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_marqueeStart is not { } start)
        {
            return;
        }

        _marqueeStart = null;
        e.Pointer.Capture(null);
        MarqueeRect.IsVisible = false;
        (DataContext as DeviceCreatorViewModel)?.SelectWithin(AreaBetween(start, e.GetPosition(ControlsCanvas)), add: e.KeyModifiers.HasFlag(KeyModifiers.Control));
    }

    private static SnapRect AreaBetween(Point from, Point to) =>
        new(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));

    private void CanvasAddKey_OnClick(object? sender, RoutedEventArgs e) => (DataContext as DeviceCreatorViewModel)?.AddKeyCommand.Execute(null);

    private void CanvasAddKnob_OnClick(object? sender, RoutedEventArgs e) => (DataContext as DeviceCreatorViewModel)?.AddKnobCommand.Execute(null);

    private void CanvasArrange_OnClick(object? sender, RoutedEventArgs e) => (DataContext as DeviceCreatorViewModel)?.ArrangeCommand.Execute(null);

    private void CanvasSelectAll_OnClick(object? sender, RoutedEventArgs e) => (DataContext as DeviceCreatorViewModel)?.SelectAllCommand.Execute(null);

    private void ControlLearnPush_OnClick(object? sender, RoutedEventArgs e) => (DataContext as DeviceCreatorViewModel)?.LearnPressCommand.Execute(null);

    private void ControlRemove_OnClick(object? sender, RoutedEventArgs e) => (DataContext as DeviceCreatorViewModel)?.RemoveControlCommand.Execute(null);
}
