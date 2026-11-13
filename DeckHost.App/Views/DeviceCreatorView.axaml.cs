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

        creator.SelectedControl = control;
        _dragging = control;
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
        _dragging.MoveTo(position.X - _grab.X, position.Y - _grab.Y);
    }

    private void Control_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging is { } control)
        {
            _dragging = null;
            e.Pointer.Capture(null);
            control.Dropped();
        }
    }
}
