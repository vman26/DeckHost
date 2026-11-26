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

            if (_workflowSource is not null)
            {
                _workflowSource.PropertyChanged -= OnMainViewModelPropertyChanged;
                _workflowSource.OnlyCompatibleActionsChanged -= RememberOnlyCompatible;
            }

            _workflowSource = DataContext as MainViewModel;
            if (_workflowSource is not null)
            {
                _workflowSource.OnlyCompatibleActionsChanged += RememberOnlyCompatible;
                _workflowSource.WorkflowEditorRequested += OpenWorkflowEditor;
                _workflowSource.PropertyChanged += OnMainViewModelPropertyChanged;
            }

            ShowEditor();
            UpdatePreviewShown();
        };
        AppSettingsViewModel.Changed += settings => Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyDebugSetting(settings));
    }

    private MainViewModel? _workflowSource;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty || change.Property == WindowStateProperty)
        {
            UpdatePreviewShown();
        }
    }

    /// <summary>The device's pictures only go to the canvas while the window can show them (not in the tray, not minimized).</summary>
    private void UpdatePreviewShown()
    {
        if (DataContext is MainViewModel main)
        {
            main.PreviewShown = IsVisible && WindowState != WindowState.Minimized;
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Editor))
        {
            ShowEditor();
        }
    }

    /// <summary>
    /// A new editor view for each control, never a reused one: a reused view's choice boxes keep the
    /// previous control's selection while their items change, and wrote it into the new control
    /// (selecting a tile key after a transparent one turned its tile off).
    /// </summary>
    private void ShowEditor() =>
        EditorHost.Content = _workflowSource?.Editor is { } editor ? new ControlCustomizationView { DataContext = editor } : null;

    /// <summary>The workflow editor as a page (one at a time: a second one replaces the first).</summary>
    private void OpenWorkflowEditor(WorkflowEditorViewModel editor)
    {
        foreach (var open in _pages.Where(static entry => entry.Page is WorkflowEditorView).ToList())
        {
            _pages.Remove(open);
        }

        OpenPage(editor.Title, new WorkflowEditorView { DataContext = editor });
    }

    private static void RememberOnlyCompatible(object? sender, bool value) =>
        AppSettings.Save(AppSettings.Load() with { OnlyCompatibleActions = value });

    private void ApplyDebugSetting(AppSettingsData settings)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ShowDebugPanel = settings.ShowDebugPanel;
            vm.LoadOnlyCompatibleActions(settings.OnlyCompatibleActions);
            vm.ShowDeviceSimulator = settings.DeviceSimulator;
        }

        if (TryResolve<DeckHost.Core.Plugins.PluginCatalog>() is { } catalog)
        {
            var enabled = settings.DeviceSimulator;
            _ = System.Threading.Tasks.Task.Run(() => global::DeckHost.App.App.ApplyDeviceSimulator(catalog, enabled));
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

    /// <summary>Closes every page at once and shows the device layout (the Home button, Ctrl+Home).</summary>
    public void GoHome()
    {
        if (_pages.Count == 0)
        {
            return;
        }

        _pages.Clear();
        ShowTopPage();
    }

    private void PageHome_OnClick(object? sender, RoutedEventArgs e) => GoHome();

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
            return;
        }

        // Ctrl+Home (Cmd+Home) goes back to the layout from any number of pages.
        var homeModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.Home && e.KeyModifiers.HasFlag(homeModifier) && !e.Handled && _pages.Count > 0)
        {
            e.Handled = true;
            GoHome();
            return;
        }

        // Ctrl+Z / Ctrl+Y (Cmd on macOS) undo and redo on the device, unless a text box or a page
        // (the workflow editor has its own history) takes them.
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Handled || _pages.Count > 0 || !e.KeyModifiers.HasFlag(command) || DataContext is not ViewModels.MainViewModel main ||
            TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        var redo = e.Key == Key.Y || (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        if (redo)
        {
            e.Handled = true;
            main.Redo();
        }
        else if (e.Key == Key.Z)
        {
            e.Handled = true;
            main.Undo();
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
        if (DataContext is not MainViewModel { SelectedDevice: { } device })
        {
            return;
        }

        // One settings page per device: an open page of another device (the N4 Pro's, say) is
        // closed rather than shown again for this one.
        var index = _pages.FindIndex(entry => entry.Page is DeviceSettingsView);
        if (index >= 0 && _pages[index].Page.DataContext is DeviceSettingsViewModel open && string.Equals(open.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
        {
            OpenSingle($"{device.Name} settings", () => new DeviceSettingsView());
            return;
        }

        if (index >= 0)
        {
            _pages.RemoveAt(index);
        }

        if (TryResolve<DeviceSettingsService>()?.Create(device) is { } settings)
        {
            OpenPage($"{device.Name} settings", new DeviceSettingsView { DataContext = settings });
        }
        else
        {
            ShowTopPage();
        }
    }

    private void OpenSupport_OnClick(object? sender, RoutedEventArgs e) => OpenSupport();

    private void RestartNormally_OnClick(object? sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.RestartNormally();

    private void DismissRecovered_OnClick(object? sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.DismissRecoveredNotice();

    /// <summary>Help and support: documentation, reporting a problem, the support bundle, crash reports.</summary>
    public void OpenSupport() =>
        OpenSingle("Help and support", () => new SupportView
        {
            DataContext = new SupportViewModel(TryResolve<DeckHost.Core.Interfaces.IDeviceHostService>(), TryResolve<DeckHost.Core.Plugins.PluginCatalog>()),
        });

    private void OpenWelcome_OnClick(object? sender, RoutedEventArgs e) => OpenWelcome();

    private void OpenDeviceCreator_OnClick(object? sender, RoutedEventArgs e) => OpenDeviceCreator();

    /// <summary>Starts a device simulator window (this program with --simulator, its own process).</summary>
    private void OpenDesktopDeck_OnClick(object? sender, RoutedEventArgs e) =>
        DesktopDeckWindow.ShowDeck(Ioc.Default.GetRequiredService<DeckHost.Core.Interfaces.IDeviceHostService>());

    private void OpenSimulator_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Environment.ProcessPath is not { } program)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(program) { ArgumentList = { "--simulator" }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (Ioc.Default.GetService<Microsoft.Extensions.Logging.ILogger<MainWindow>>() is { } logger)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, ex, "Could not start the device simulator");
            }
        }
    }

    /// <summary>The device creator: a USB device DeckHost doesn't know, made into a deck.</summary>
    public void OpenDeviceCreator()
    {
        if (TryResolve<DeckHost.Device.Hid.IHidDeviceSource>() is { } source)
        {
            OpenSingle("Add a USB device", () => new DeviceCreatorView { DataContext = new DeviceCreatorViewModel(TryResolve<DeckHost.Device.Hid.IHidDescriptionStore>(), source) });
        }
    }

    /// <summary>The welcome tour (by itself on the first start).</summary>
    public void OpenWelcome() =>
        OpenSingle("Welcome", () =>
        {
            var welcome = new WelcomeViewModel(DataContext as MainViewModel, TryResolve<DeckHost.Core.Interfaces.IDeviceHostService>(), TryResolve<UpdateViewModel>());

            // On top of the tour: Back returns to it.
            welcome.DeviceCreatorRequested += (_, _) => OpenDeviceCreator();
            return new WelcomeView { DataContext = welcome };
        });

    /// <summary>On start: a crash since last time opens Help and support; the first start opens the welcome tour.</summary>
    public void ShowStartPages()
    {
        if (Platform.SafeMode.IsOn || SupportViewModel.HasUnseenCrashes())
        {
            OpenSupport();
        }
        else if (WelcomeViewModel.ShouldShow())
        {
            OpenWelcome();
        }
        else if (WhatsNewViewModel.ShouldShow())
        {
            WhatsNewViewModel.MarkSeen();
            OpenSingle("What's new", () => new WhatsNewView { DataContext = new WhatsNewViewModel() });
        }
    }

    private void OpenPluginManager_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TryResolve<PluginManagerViewModel>() is { } plugins)
        {
            OpenSingle("Plugins", () => new PluginManagerView { DataContext = plugins });
        }
    }

    private void OpenVariables_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TryResolve<DeckHost.Core.Interfaces.IDeviceHostService>() is { } host)
        {
            OpenSingle("Variables", () => new VariablesView { DataContext = new VariablesViewModel(host, (DataContext as MainViewModel)!.WantSystemValues) });
        }
    }

    private void OpenDevices_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TryResolve<DeckHost.Core.Interfaces.IDeviceHostService>() is { } host)
        {
            OpenSingle("Devices", () => new DeviceManagerView { DataContext = new DeviceManagerViewModel(host, TryResolve<DeviceSettingsService>()) });
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

    /// <summary>
    /// The device canvas from the keyboard: Enter customizes the focused control, Space presses it,
    /// + and − turn a knob, Delete removes its action and the arrow keys go to the next control.
    /// </summary>
    private void ControlCard_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not MainViewModel vm || sender is not Control { DataContext: ControlItemViewModel controlItem } control)
        {
            return;
        }

        var inputs = controlItem.Control.Inputs;
        switch (e.Key)
        {
            case Key.Enter:
                vm.SelectControl(controlItem.Control);
                break;
            case Key.Space when inputs.HasFlag(InputCapabilities.Press):
                vm.SelectControl(controlItem.Control);
                vm.Simulate(controlItem, InputCapabilities.Press, 1);
                if (inputs.HasFlag(InputCapabilities.Release))
                {
                    vm.Simulate(controlItem, InputCapabilities.Release, 0);
                }

                break;
            case Key.Space when inputs.HasFlag(InputCapabilities.Swipe):
                vm.Simulate(controlItem, InputCapabilities.Swipe, 1);
                break;
            case Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract when inputs.HasFlag(InputCapabilities.Rotate):
                vm.Simulate(controlItem, InputCapabilities.Rotate, e.Key is Key.OemPlus or Key.Add ? 1 : -1);
                break;
            case Key.Delete or Key.Back when controlItem.AssignedBinding is not null:
                vm.RemoveAssignment(controlItem.Control);
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                var items = vm.ControlItems;
                var rects = items.Select(static item => new Avalonia.Rect(item.X, item.Y, item.Width, item.Height)).ToList();
                var direction = e.Key switch
                {
                    Key.Left => CanvasDirection.Left,
                    Key.Right => CanvasDirection.Right,
                    Key.Up => CanvasDirection.Up,
                    _ => CanvasDirection.Down,
                };
                if (CanvasNavigation.Next(rects, items.IndexOf(controlItem), direction) is { } next)
                {
                    FocusControlCard(control, items[next]);
                }

                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Moves the keyboard focus to the card of <paramref name="target"/>, a sibling of <paramref name="from"/>.</summary>
    private static void FocusControlCard(Control from, ControlItemViewModel target)
    {
        var items = from.FindAncestorOfType<ItemsControl>();
        var card = items?.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => ReferenceEquals(border.DataContext, target) && border.Focusable);
        card?.Focus(NavigationMethod.Directional);
    }

    /// <summary>Enter on an action in the list puts it on the selected control, like dropping it there.</summary>
    private void ActionCard_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key is not (Key.Enter or Key.Space) || DataContext is not MainViewModel vm ||
            sender is not Control { DataContext: ActionItemViewModel actionItem } || vm.SelectedControl is not { } selected)
        {
            return;
        }

        e.Handled = true;
        vm.AssignActionToControl(selected.Control, actionItem.Action);
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

    /// <summary>Right-click → Try it: the action runs as if the control was pressed (a swipe bar: swiped right).</summary>
    private void ControlTry_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !TryGetMenuControl(sender, out var controlItem))
        {
            return;
        }

        var inputs = controlItem.Control.Inputs;
        if (inputs.HasFlag(InputCapabilities.Press))
        {
            vm.Simulate(controlItem, InputCapabilities.Press, 1);
            if (inputs.HasFlag(InputCapabilities.Release))
            {
                vm.Simulate(controlItem, InputCapabilities.Release, 0);
            }
        }
        else if (inputs.HasFlag(InputCapabilities.Swipe))
        {
            vm.Simulate(controlItem, InputCapabilities.Swipe, 1);
        }
    }

    private void ControlDocs_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TryGetMenuControl(sender, out var controlItem) && controlItem.AssignedBinding?.Model.Action.PluginId is { } pluginId)
        {
            OpenPluginDocs(pluginId);
        }
    }

    private void GroupDocs_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PluginActionGroupViewModel group })
        {
            OpenPluginDocs(group.PluginId);
        }
    }

    private void ActionDocs_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionItemViewModel action })
        {
            OpenPluginDocs(action.PluginId);
        }
    }

    /// <summary>The documentation page of a plugin (right-click on a control, an action or a plugin in the list).</summary>
    public void OpenPluginDocs(string pluginId)
    {
        if (TryResolve<PluginDocsService>()?.Create(pluginId) is { } docs)
        {
            OpenPage(docs.Name, new PluginDocsView { DataContext = docs });
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
