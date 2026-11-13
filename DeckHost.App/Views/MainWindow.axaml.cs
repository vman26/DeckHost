using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.DependencyInjection;
using DeckHost.App.Platform;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DeckHost.App.Views;

/// <summary>
/// The main window, laid out like the Stream Deck app: the device on top (a fixed size, so it
/// doesn't move while you work), the selected control's editor below it, the actions on the right.
/// Settings, plugins, profiles and updates open as pages inside the window (<see cref="IPageHost"/>).
/// </summary>
public partial class MainWindow : Window, IPageHost, IDragGhostHost
{
    private const double DragThreshold = 6;

    private readonly List<(string Title, Control Page)> _pages = [];
    private (ControlItemViewModel Control, Point Start, PointerPressedEventArgs Press)? _pendingControlDrag;
    private ControlItemViewModel? _simulatedPress;

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnControlDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnControlDrop, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);

        if (TryResolve<UpdateViewModel>() is { } updates)
        {
            UpdateBanner.DataContext = updates;
            UpdateBanner.Bind(IsVisibleProperty, new Avalonia.Data.Binding(nameof(UpdateViewModel.ShowBanner)) { Source = updates });
            updates.DetailsRequested += OpenUpdates;
        }

        // The debug panel is a DeckHost setting.
        DataContextChanged += (_, _) => ApplyDebugSetting(AppSettings.Load());
        DataContextChanged += (_, _) =>
        {
            if (_workflowSource is not null)
            {
                _workflowSource.WorkflowEditorRequested -= OpenWorkflowEditor;
            }

            _workflowSource = DataContext as MainViewModel;
            if (_workflowSource is not null)
            {
                _workflowSource.WorkflowEditorRequested += OpenWorkflowEditor;
            }
        };
        AppSettingsViewModel.Changed += settings => Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyDebugSetting(settings));
    }

    private MainViewModel? _workflowSource;

    /// <summary>The workflow editor as a page (one at a time: a second one replaces the first).</summary>
    private void OpenWorkflowEditor(WorkflowEditorViewModel editor)
    {
        foreach (var open in _pages.Where(static entry => entry.Page is WorkflowEditorView).ToList())
        {
            _pages.Remove(open);
        }

        OpenPage(editor.Title, new WorkflowEditorView { DataContext = editor });
    }

    private void ApplyDebugSetting(AppSettingsData settings)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ShowDebugPanel = settings.ShowDebugPanel;
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

    // ---------------------------------------------------------------- pages

    public void OpenPage(string title, Control page)
    {
        _pages.Add((title, page));
        ShowTopPage();
    }

    public void ClosePage(Control page)
    {
        var index = _pages.FindIndex(entry => ReferenceEquals(entry.Page, page) || entry.Page.IsVisualAncestorOf(page));
        if (index < 0)
        {
            return;
        }

        _pages.RemoveRange(index, _pages.Count - index);
        ShowTopPage();
    }

    /// <summary>Shows the page of type <typeparamref name="T"/> if it's open (on top), else opens a new one.</summary>
    private void OpenSingle<T>(string title, Func<T> create)
        where T : Control
    {
        var index = _pages.FindIndex(entry => entry.Page is T);
        if (index >= 0)
        {
            var existing = _pages[index];
            _pages.RemoveAt(index);
            _pages.Add(existing);
            ShowTopPage();
            return;
        }

        OpenPage(title, create());
    }

    private void ShowTopPage()
    {
        // The workspace is hidden under a page: a plugin's settings page is a native web view,
        // which would otherwise stay on top of the page (native windows draw above Avalonia).
        Workspace.IsVisible = _pages.Count == 0;
        if (_pages.Count == 0)
        {
            PageContent.Content = null;
            PageHost.IsVisible = false;
            return;
        }

        var (title, page) = _pages[^1];
        PageTitle.Text = title;
        PageContent.MaxWidth = page is IWidePage ? double.PositiveInfinity : 1100;
        PageContent.Content = page;
        PageHost.IsVisible = true;
    }

    private void PageBack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_pages.Count > 0)
        {
            _pages.RemoveAt(_pages.Count - 1);
            ShowTopPage();
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Esc leaves a page (unless something inside used it, like a hotkey recorder).
        if (e.Key == Key.Escape && !e.Handled && _pages.Count > 0)
        {
            e.Handled = true;
            PageBack_OnClick(sender, e);
        }
    }

    private void OpenAppSettings_OnClick(object? sender, RoutedEventArgs e) => OpenAppSettings();

    /// <summary>Opens DeckHost's settings (also from the tray, which shows this window first).</summary>
    public void OpenAppSettings() =>
        OpenSingle("DeckHost settings", () => new AppSettingsView { DataContext = new AppSettingsViewModel(TryResolve<UpdateViewModel>()) });

    /// <summary>Opens the update page (the banner's What's new, the tray's update item).</summary>
    public void OpenUpdates()
    {
        if (TryResolve<UpdateViewModel>() is { } updates)
        {
            OpenSingle("Update", () => new UpdateView { DataContext = updates });
        }
    }

    private void OpenDeviceSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedDevice: { } device } &&
            TryResolve<DeviceSettingsService>()?.Create(device) is { } settings)
        {
            OpenSingle($"{device.Name} settings", () => new DeviceSettingsView { DataContext = settings });
        }
    }

    private void OpenSupport_OnClick(object? sender, RoutedEventArgs e) => OpenSupport();

    /// <summary>Help and support: documentation, reporting a problem, the support bundle, crash reports.</summary>
    public void OpenSupport() =>
        OpenSingle("Help and support", () => new SupportView
        {
            DataContext = new SupportViewModel(TryResolve<DeckHost.Core.Interfaces.IDeviceHostService>(), TryResolve<DeckHost.Core.Plugins.PluginCatalog>()),
        });

    private void OpenWelcome_OnClick(object? sender, RoutedEventArgs e) => OpenWelcome();

    /// <summary>The welcome tour (by itself on the first start).</summary>
    public void OpenWelcome() =>
        OpenSingle("Welcome", () => new WelcomeView
        {
            DataContext = new WelcomeViewModel(DataContext as MainViewModel, TryResolve<DeckHost.Core.Interfaces.IDeviceHostService>(), TryResolve<UpdateViewModel>()),
        });

    /// <summary>On start: a crash since last time opens Help and support; the first start opens the welcome tour.</summary>
    public void ShowStartPages()
    {
        if (SupportViewModel.HasUnseenCrashes())
        {
            OpenSupport();
        }
        else if (WelcomeViewModel.ShouldShow())
        {
            OpenWelcome();
        }
    }

    private void OpenPluginManager_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TryResolve<PluginManagerViewModel>() is { } plugins)
        {
            OpenSingle("Plugins", () => new PluginManagerView { DataContext = plugins });
        }
    }

    private void OpenProfileManager_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TryResolve<ProfileManagerViewModel>() is { } profiles)
        {
            OpenSingle("Profiles", () => new ProfileManagerView { DataContext = profiles });
        }
    }

    // ---------------------------------------------------------------- pages and folders

    private void AddPage_OnClick(object? sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.AddPage();

    private void CloseFolder_OnClick(object? sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.CloseFolder();

    // ---------------------------------------------------------------- drag ghost

    public void ShowGhost(string text, IDeckAction? action)
    {
        DragGhostText.Text = text;
        var icon = action is not null && DataContext is MainViewModel vm ? vm.FindActionItem(action)?.Icon : null;
        DragGhostPath.Data = icon?.Geometry;
        DragGhostPath.Fill = icon?.Brush ?? Brushes.White;
        DragGhostIconBox.IsVisible = icon?.HasGeometry == true;
        DragGhostImage.Source = icon?.Image;
        DragGhostImage.IsVisible = icon?.HasImage == true;
        DragGhostVisual.IsVisible = true;
    }

    public void MoveGhost(Point position)
    {
        Canvas.SetLeft(DragGhostVisual, position.X + 14);
        Canvas.SetTop(DragGhostVisual, position.Y + 10);
    }

    public void HideGhost() => DragGhostVisual.IsVisible = false;

    // ---------------------------------------------------------------- dragging actions and controls

    private async void ActionCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Control { DataContext: ActionItemViewModel actionItem })
        {
            return;
        }

        var dataTransfer = new DataTransfer();
        dataTransfer.Add(DataTransferItem.Create(DragFormats.Action, actionItem.Action));

        e.Handled = true;
        ShowGhost(actionItem.Name, actionItem.Action);
        MoveGhost(e.GetPosition(this));
        try
        {
            await DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Copy);
        }
        finally
        {
            HideGhost();
        }
    }

    private void ControlCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
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

        // Selecting a control opens its editor below the device; a double-click presses it
        // (it runs the real action, as the device would).
        vm.SelectControl(controlItem.Control);
        if (e.ClickCount >= 2)
        {
            _pendingControlDrag = null;
            SimulatePress(vm, controlItem, control, e);
            return;
        }

        // Only start dragging once the pointer actually moves, so clicks still select.
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
        if (controlItem.AssignedBinding is not { } binding)
        {
            return;
        }

        var dataTransfer = new DataTransfer();
        dataTransfer.Add(DataTransferItem.Create(DragFormats.Control, controlItem.Id));
        dataTransfer.Add(DataTransferItem.Create(DragFormats.Action, binding.PrimaryAction.Action));

        // The drop handler moves (or with Ctrl copies/swaps) the binding, or removes it when it's
        // dropped where there is no control. A cancelled drag (Esc, outside the window) changes nothing.
        ShowGhost(binding.DisplayName, binding.PrimaryAction.Action);
        MoveGhost(e.GetPosition(this));
        try
        {
            await DragDrop.DoDragDropAsync(pending.Press, dataTransfer, DragDropEffects.Copy | DragDropEffects.Move);
        }
        finally
        {
            HideGhost();
        }
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
        if (DataContext is not MainViewModel vm ||
            sender is not Control { DataContext: ControlItemViewModel controlItem } ||
            !controlItem.Control.Inputs.HasFlag(InputCapabilities.Rotate) ||
            e.Delta.Y == 0)
        {
            return;
        }

        e.Handled = true;
        vm.Simulate(controlItem, InputCapabilities.Rotate, e.Delta.Y > 0 ? 1 : -1);
    }

    /// <summary>A double-click: the input the hardware would send for this control.</summary>
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
        MoveGhost(e.GetPosition(this));

        // Handled already: a drop zone inside the editor (Multi Action steps) took it.
        if (e.Handled)
        {
            return;
        }

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
        HideGhost();
        if (e.Handled)
        {
            return;
        }

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
        controlId = e.DataTransfer.TryGetValue(DragFormats.Control) ?? string.Empty;
        return controlId.Length > 0;
    }

    private void ControlCustomize_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && TryGetMenuControl(sender, out var controlItem))
        {
            // The editor below the device shows the selected control.
            vm.SelectControl(controlItem.Control);
        }
    }

    private void ControlRemove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !TryGetMenuControl(sender, out var controlItem))
        {
            return;
        }

        vm.RemoveAssignment(controlItem.Control);
    }

    private static bool TryGetDraggedAction(DragEventArgs e, out IDeckAction action)
    {
        if (e.DataTransfer.TryGetValue(DragFormats.Action) is { } draggedAction)
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
