using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
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
}
