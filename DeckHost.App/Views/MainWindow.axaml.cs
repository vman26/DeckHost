using Avalonia;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.App.Views;

public partial class MainWindow : Window
{
    private static readonly DataFormat<IDeckAction> ActionPayloadFormat =
        DataFormat.CreateInProcessFormat<IDeckAction>("ActionPayload");

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnControlDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnControlDrop, handledEventsToo: true);
    }

    private void OnControlDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        e.Handled = true;

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (!TryGetDropTargetControl(e, out var targetControl) ||
            !TryGetDraggedAction(e, out var action))
        {
            return;
        }

        if (vm.CanDropAction(action, targetControl))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnControlDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (!TryGetDropTargetControl(e, out var targetControl) ||
            !TryGetDraggedAction(e, out var action))
        {
            return;
        }

        vm.AssignActionToControl(targetControl, action);
    }

    private static bool TryGetDraggedAction(DragEventArgs e, out IDeckAction action)
    {
        if (e.DataTransfer.TryGetValue(ActionPayloadFormat) is { } draggedAction)
        {
            action = draggedAction;
            return true;
        }

        action = null!;
        return false;
    }

    private static bool TryGetDropTargetControl(DragEventArgs e, out PhysicalControl targetControl)
    {
        if (e.Source is Visual visual)
        {
            foreach (var ancestor in visual.GetSelfAndVisualAncestors())
            {
                if (ancestor is Control { DataContext: PhysicalControl physicalControl })
                {
                    targetControl = physicalControl;
                    return true;
                }
            }
        }

        targetControl = null!;
        return false;
    }
}