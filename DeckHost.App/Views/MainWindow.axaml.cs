using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System.ComponentModel;
using System.Threading.Tasks;

namespace DeckHost.App.Views;

public partial class MainWindow : Window
{
    private static readonly DataFormat<IDeckAction> ActionPayloadFormat =
        DataFormat.CreateInProcessFormat<IDeckAction>("ActionPayload");

    private static readonly DataFormat<string> ControlPayloadFormat =
        DataFormat.CreateInProcessFormat<string>("AssignedControlPayload");

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnControlDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnControlDrop, handledEventsToo: true);
    }

    private async void ActionCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Control { DataContext: ActionItemViewModel actionItem })
        {
            return;
        }

        var dataTransfer = new DataTransfer();
        dataTransfer.Add(DataTransferItem.Create(ActionPayloadFormat, actionItem.Action));

        e.Handled = true;
        await DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Copy);
    }

    private async void ControlCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm ||
            sender is not Control { DataContext: ControlItemViewModel controlItem })
        {
            return;
        }

        vm.SelectControl(controlItem.Control);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || controlItem.AssignedBinding is null)
        {
            return;
        }

        var dataTransfer = new DataTransfer();
        dataTransfer.Add(DataTransferItem.Create(ControlPayloadFormat, controlItem.Id));
        dataTransfer.Add(DataTransferItem.Create(ActionPayloadFormat, controlItem.AssignedBinding.PrimaryAction.Action));

        e.Handled = true;
        var result = await DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Copy | DragDropEffects.Move);
        if (result == DragDropEffects.None)
        {
            vm.RemoveAssignment(controlItem.Control);
        }
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

    private async void CustomizeSelectedControl_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedControl is null)
        {
            return;
        }

        await OpenCustomizationDialogAsync(vm, vm.SelectedControl);
    }

    private async void ControlCustomize_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !TryGetMenuControl(sender, out var controlItem))
        {
            return;
        }

        vm.SelectControl(controlItem.Control);
        await OpenCustomizationDialogAsync(vm, controlItem);
    }

    private void ControlRemove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !TryGetMenuControl(sender, out var controlItem))
        {
            return;
        }

        vm.RemoveAssignment(controlItem.Control);
    }

    private void OpenPluginManager_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var window = new PluginManagerWindow
        {
            DataContext = vm,
        };

        window.Show(this);
    }

    private async Task OpenCustomizationDialogAsync(MainViewModel vm, ControlItemViewModel controlItem)
    {
        var customization = vm.CreateCustomizationViewModel(controlItem.Id);
        var window = new ControlCustomizationWindow
        {
            DataContext = customization,
        };

        void OnCustomizationChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ControlCustomizationViewModel.DisplayText) or nameof(ControlCustomizationViewModel.ImagePath))
            {
                vm.ApplyCustomization(customization);
            }
        }

        customization.PropertyChanged += OnCustomizationChanged;

        try
        {
            var saved = await window.ShowDialog<bool?>(this);
            if (saved == true)
            {
                vm.ApplyCustomization(customization);
            }
        }
        finally
        {
            customization.PropertyChanged -= OnCustomizationChanged;
        }
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
                if (ancestor is Control { DataContext: ControlItemViewModel controlItem })
                {
                    targetControl = controlItem.Control;
                    return true;
                }
            }
        }

        targetControl = null!;
        return false;
    }

    private static bool TryGetMenuControl(object? sender, out ControlItemViewModel controlItem)
    {
        if (sender is StyledElement { DataContext: ControlItemViewModel item })
        {
            controlItem = item;
            return true;
        }

        controlItem = null!;
        return false;
    }
}
