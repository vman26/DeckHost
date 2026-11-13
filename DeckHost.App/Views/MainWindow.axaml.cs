using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.DependencyInjection;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace DeckHost.App.Views;

public partial class MainWindow : Window
{
    private static readonly DataFormat<IDeckAction> ActionPayloadFormat =
        DataFormat.CreateInProcessFormat<IDeckAction>("ActionPayload");

    private static readonly DataFormat<string> ControlPayloadFormat =
        DataFormat.CreateInProcessFormat<string>("AssignedControlPayload");

    private const double DragThreshold = 6;

    private PluginManagerWindow? _pluginManagerWindow;
    private ProfileManagerWindow? _profileManagerWindow;
    private (ControlItemViewModel Control, Point Start, PointerPressedEventArgs Press)? _pendingControlDrag;
    private ControlItemViewModel? _simulatedPress;

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnControlDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnControlDrop, handledEventsToo: true);

        if (TryResolve<UpdateViewModel>() is { } updates)
        {
            UpdateBanner.DataContext = updates;
            UpdateBanner.Bind(IsVisibleProperty, new Avalonia.Data.Binding(nameof(UpdateViewModel.ShowBanner)) { Source = updates });
            updates.DetailsRequested += () => UpdateWindow.ShowFor(this, updates);
        }
    }

    /// <summary>A service from the app's container, or null when there is none (tests, previews).</summary>
    private static T? TryResolve<T>()
        where T : class
    {
        try
        {
            return Ioc.Default.GetService<T>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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
            sender is not Control { DataContext: ControlItemViewModel controlItem } control)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            vm.SelectControl(controlItem.Control);
            return;
        }

        e.Handled = true;

        if (vm.IsTestMode)
        {
            SimulatePress(vm, controlItem, control, e);
            return;
        }

        vm.SelectControl(controlItem.Control);

        if (e.ClickCount >= 2)
        {
            // Double-click: shortcut to Customize.
            _pendingControlDrag = null;
            await OpenCustomizationDialogAsync(vm, controlItem);
            return;
        }

        // Only start dragging once the pointer actually moves, so clicks and double-clicks work.
        _pendingControlDrag = controlItem.AssignedBinding is null ? null : (controlItem, e.GetPosition(control), e);
    }

    private async void ControlCard_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingControlDrag is not { } pending ||
            sender is not Control control ||
            !ReferenceEquals(control.DataContext, pending.Control) ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var offset = e.GetPosition(control) - pending.Start;
        if (Math.Abs(offset.X) < DragThreshold && Math.Abs(offset.Y) < DragThreshold)
        {
            return;
        }

        _pendingControlDrag = null;
        var controlItem = pending.Control;
        if (controlItem.AssignedBinding is null)
        {
            return;
        }

        var dataTransfer = new DataTransfer();
        dataTransfer.Add(DataTransferItem.Create(ControlPayloadFormat, controlItem.Id));
        dataTransfer.Add(DataTransferItem.Create(ActionPayloadFormat, controlItem.AssignedBinding.PrimaryAction.Action));

        // The drop handler moves (or with Ctrl copies/swaps) the binding, or removes it when it's
        // dropped where there is no control. A cancelled drag (Esc, outside the window) changes nothing.
        await DragDrop.DoDragDropAsync(pending.Press, dataTransfer, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void ControlCard_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pendingControlDrag = null;

        if (_simulatedPress is { } pressed && DataContext is MainViewModel vm)
        {
            _simulatedPress = null;
            vm.Simulate(pressed, InputCapabilities.Release, 0);
        }
    }

    private void ControlCard_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainViewModel { IsTestMode: true } vm ||
            sender is not Control { DataContext: ControlItemViewModel controlItem } ||
            !controlItem.Control.Inputs.HasFlag(InputCapabilities.Rotate) ||
            e.Delta.Y == 0)
        {
            return;
        }

        e.Handled = true;
        vm.Simulate(controlItem, InputCapabilities.Rotate, e.Delta.Y > 0 ? 1 : -1);
    }

    /// <summary>Test mode: turns a click into the input the hardware would send for this control.</summary>
    private void SimulatePress(MainViewModel vm, ControlItemViewModel controlItem, Control control, PointerPressedEventArgs e)
    {
        var inputs = controlItem.Control.Inputs;
        if (inputs.HasFlag(InputCapabilities.Swipe) && !inputs.HasFlag(InputCapabilities.Press))
        {
            var leftHalf = e.GetPosition(control).X < control.Bounds.Width / 2;
            vm.Simulate(controlItem, InputCapabilities.Swipe, leftHalf ? -1 : 1);
            return;
        }

        if (!inputs.HasFlag(InputCapabilities.Press))
        {
            return;
        }

        vm.Simulate(controlItem, InputCapabilities.Press, 1);

        // Hold until the mouse button is released, for controls that report a release.
        if (inputs.HasFlag(InputCapabilities.Release))
        {
            _simulatedPress = controlItem;
            e.Pointer.Capture(control);
        }
    }

    private void OnControlDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        e.Handled = true;

        if (DataContext is not MainViewModel vm || !TryGetDraggedAction(e, out var action))
        {
            return;
        }

        var fromControl = TryGetDraggedSourceControl(e, out var sourceId);
        if (!TryGetDropTargetControl(e, out var targetControl))
        {
            // An assignment dragged off its control can be dropped anywhere to remove it.
            e.DragEffects = fromControl ? DragDropEffects.Move : DragDropEffects.None;
            return;
        }

        if (fromControl)
        {
            if (!string.Equals(sourceId, targetControl.Id, StringComparison.OrdinalIgnoreCase) && vm.CanMoveBinding(sourceId, targetControl.Id))
            {
                e.DragEffects = IsCopyRequested(e) ? DragDropEffects.Copy : DragDropEffects.Move;
            }

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

        if (DataContext is not MainViewModel vm || !TryGetDraggedAction(e, out var action))
        {
            return;
        }

        var fromControl = TryGetDraggedSourceControl(e, out var sourceId);
        if (!TryGetDropTargetControl(e, out var targetControl))
        {
            if (fromControl)
            {
                vm.RemoveAssignment(sourceId);
            }

            return;
        }

        if (fromControl)
        {
            if (!string.Equals(sourceId, targetControl.Id, StringComparison.OrdinalIgnoreCase))
            {
                vm.MoveBinding(sourceId, targetControl.Id, copy: IsCopyRequested(e));
            }

            return;
        }

        vm.AssignActionToControl(targetControl, action);
    }

    private static bool IsCopyRequested(DragEventArgs e) => e.KeyModifiers.HasFlag(KeyModifiers.Control);

    private static bool TryGetDraggedSourceControl(DragEventArgs e, out string controlId)
    {
        controlId = e.DataTransfer.TryGetValue(ControlPayloadFormat) ?? string.Empty;
        return controlId.Length > 0;
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

    private AppSettingsWindow? _appSettingsWindow;

    private void OpenAppSettings_OnClick(object? sender, RoutedEventArgs e) => OpenAppSettings();

    /// <summary>Opens the DeckHost settings window (also from the tray, when this window is hidden).</summary>
    public void OpenAppSettings()
    {
        if (_appSettingsWindow is { IsVisible: true })
        {
            _appSettingsWindow.Activate();
            return;
        }

        _appSettingsWindow = new AppSettingsWindow { DataContext = new AppSettingsViewModel(TryResolve<UpdateViewModel>()) };
        _appSettingsWindow.Closed += (_, _) => _appSettingsWindow = null;
        if (IsVisible)
        {
            _appSettingsWindow.Show(this);
        }
        else
        {
            _appSettingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _appSettingsWindow.Show();
        }
    }

    private void OpenDeviceSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedDevice: { } device }
            && Ioc.Default.GetRequiredService<DeviceSettingsService>().Create(device) is { } settings)
        {
            new DeviceSettingsWindow { DataContext = settings }.Show(this);
        }
    }

    private void OpenPluginManager_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel)
        {
            return;
        }

        if (_pluginManagerWindow is { IsVisible: true })
        {
            _pluginManagerWindow.Activate();
            return;
        }

        _pluginManagerWindow = new PluginManagerWindow
        {
            DataContext = Ioc.Default.GetRequiredService<PluginManagerViewModel>(),
        };
        _pluginManagerWindow.Closed += (_, _) => _pluginManagerWindow = null;
        _pluginManagerWindow.Show(this);
    }

    private void OpenProfileManager_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel)
        {
            return;
        }

        if (_profileManagerWindow is { IsVisible: true })
        {
            _profileManagerWindow.Activate();
            return;
        }

        _profileManagerWindow = new ProfileManagerWindow
        {
            DataContext = Ioc.Default.GetRequiredService<ProfileManagerViewModel>(),
        };
        _profileManagerWindow.Closed += (_, _) => _profileManagerWindow = null;
        _profileManagerWindow.Show(this);
    }

    private async Task OpenCustomizationDialogAsync(MainViewModel vm, ControlItemViewModel controlItem)
    {
        var customization = vm.CreateCustomizationViewModel(controlItem.Id);
        if (customization.HasSettingsPage && vm.SelectedDevice is { } device)
        {
            // Plugin settings pages are shown inside the Customize window.
            customization.SettingsPageUri = await vm.DeviceHost.GetActionSettingsPageAsync(device.Id, controlItem.Id);
        }

        var window = new ControlCustomizationWindow
        {
            DataContext = customization,
        };

        // Every edit previews live on the device; Cancel puts the original binding back.
        void OnCustomizationChanged(object? sender, EventArgs e) => vm.ApplyBinding(customization.ControlId, customization.BuildBinding());

        customization.Changed += OnCustomizationChanged;
        try
        {
            var saved = await window.ShowDialog<bool?>(this);
            vm.ApplyBinding(customization.ControlId, saved == true ? customization.BuildBinding() : customization.OriginalBinding);
        }
        finally
        {
            customization.Changed -= OnCustomizationChanged;
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
