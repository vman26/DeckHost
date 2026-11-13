using System;
using System.IO;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeckHost.App.Platform;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

public partial class ControlCustomizationView : UserControl
{
    private HotkeySettingField? _recordingField;
    private bool _browserStarted;

    public ControlCustomizationView()
    {
        InitializeComponent();

        // Tunnel so Tab, Enter or Esc reach the recorder before focus navigation or dialog keys.
        AddHandler(KeyDownEvent, OnRecorderKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnRecorderKeyUp, RoutingStrategies.Tunnel);

        // The plugin's settings page, in the system's web view (WebView2, WKWebView, WebKitGTK).
        SettingsBrowser.EnvironmentRequested += (_, e) =>
        {
            e.EnableDevTools = true;
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckHost", "webview");
            switch (e)
            {
                case Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs windows:
                    windows.UserDataFolder = data;
                    break;
                case Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs gtk:
                    gtk.BaseDataDirectory = data;
                    gtk.BaseCacheDirectory = Path.Combine(data, "cache");
                    break;
            }
        };
        SettingsBrowser.NewWindowRequested += (_, e) =>
        {
            // Links that open a window go to the browser.
            e.Handled = true;
            if (e.Request is { Scheme: "http" or "https" } uri)
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            }
        };
        SettingsBrowser.AdapterCreated += (_, _) => _browserStarted = true;
        SettingsBrowser.AttachedToVisualTree += (_, _) => Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            // No web view on this system (Linux without WebKitGTK): offer the browser instead.
            if (!_browserStarted && SettingsBrowser.IsEffectivelyVisible)
            {
                SettingsBrowser.IsVisible = false;
                SettingsBrowserError.Text = OperatingSystem.IsLinux()
                    ? "The built-in browser needs WebKitGTK (libwebkit2gtk-4.1). Use Open in browser above; changes there are saved straight to the plugin."
                    : "The built-in browser couldn't start. Use Open in browser above; changes there are saved straight to the plugin.";
                SettingsBrowserError.IsVisible = true;
            }
        }, TimeSpan.FromSeconds(8));

        // Two columns when there is room, else one.
        Columns.SizeChanged += (_, e) => Arrange(e.NewSize.Width);

        StepsDropZone.AddHandler(DragDrop.DragOverEvent, Steps_OnDragOver);
        StepsDropZone.AddHandler(DragDrop.DragLeaveEvent, Steps_OnDragLeave);
        StepsDropZone.AddHandler(DragDrop.DropEvent, Steps_OnDrop);
    }

    /// <summary>Below this width the cards go in one column.</summary>
    private const double TwoColumnWidth = 820;

    private bool? _twoColumns;

    private void Arrange(double width)
    {
        var two = width >= TwoColumnWidth;
        if (_twoColumns == two)
        {
            return;
        }

        _twoColumns = two;
        Columns.ColumnDefinitions = new ColumnDefinitions(two ? "*,*" : "*");
        Columns.RowDefinitions = new RowDefinitions(two ? "Auto" : "Auto,Auto");
        Grid.SetColumn(SecondaryColumn, two ? 1 : 0);
        Grid.SetRow(SecondaryColumn, two ? 0 : 1);
        SecondaryColumn.Margin = two ? default : new Avalonia.Thickness(0, 14, 0, 0);
    }

    // ---------------------------------------------------------------- Multi Action steps: drag and drop

    private async void StepHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: MultiActionStepEditorViewModel step } ||
            DataContext is not ControlCustomizationViewModel vm ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DragFormats.Step, vm.Steps.IndexOf(step).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        DragGhost.Show(this, step.Action.Name, step.Action.Action);
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            DragGhost.Hide(this);
            vm.DropIndex = null;
        }
    }

    private void Steps_OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not ControlCustomizationViewModel vm)
        {
            return;
        }

        var isStep = e.DataTransfer.TryGetValue(DragFormats.Step) is not null;
        var action = e.DataTransfer.TryGetValue(DragFormats.Action);
        if (!isStep && (action is null || e.DataTransfer.TryGetValue(DragFormats.Control) is not null || !vm.CanInsertStep(action)))
        {
            return; // Not for the steps: the main window decides.
        }

        e.Handled = true;
        e.DragEffects = isStep ? DragDropEffects.Move : DragDropEffects.Copy;
        vm.DropIndex = DropIndexAt(e);
        DragGhost.Move(this, e);
    }

    private void Steps_OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is ControlCustomizationViewModel vm)
        {
            vm.DropIndex = null;
        }
    }

    private void Steps_OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ControlCustomizationViewModel vm)
        {
            return;
        }

        var index = DropIndexAt(e);
        vm.DropIndex = null;
        if (e.DataTransfer.TryGetValue(DragFormats.Step) is { } from &&
            int.TryParse(from, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var fromIndex))
        {
            e.Handled = true;
            vm.MoveStepTo(fromIndex, index);
        }
        else if (e.DataTransfer.TryGetValue(DragFormats.Control) is null &&
                 e.DataTransfer.TryGetValue(DragFormats.Action) is { } action && vm.CanInsertStep(action))
        {
            e.Handled = true;
            vm.InsertStep(action, index);
        }
    }

    /// <summary>The step the pointer is above (by the middle of each step), or the end.</summary>
    private int DropIndexAt(DragEventArgs e)
    {
        var count = StepsList.ItemCount;
        for (var index = 0; index < count; index++)
        {
            if (StepsList.ContainerFromIndex(index) is { } container &&
                e.GetPosition(container).Y < container.Bounds.Height / 2)
            {
                return index;
            }
        }

        return count;
    }

    private void HotkeyRecord_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: HotkeySettingField field } button)
        {
            return;
        }

        if (button.IsChecked == true)
        {
            if (_recordingField is not null && !ReferenceEquals(_recordingField, field))
            {
                _recordingField.IsRecording = false;
            }

            _recordingField = field;
            button.Focus();
        }
        else if (ReferenceEquals(_recordingField, field))
        {
            _recordingField = null;
        }
    }

    private void OnRecorderKeyDown(object? sender, KeyEventArgs e)
    {
        if (_recordingField is not { } field)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            StopRecording();
            return;
        }

        if (HotkeyRecorder.IsModifier(e.Key))
        {
            field.RecordingPreview = HotkeyRecorder.ModifierPrefix(e.KeyModifiers) + "…";
            return;
        }

        // Windows sends Print Screen only on key-up; that is handled there.
        if (e.Key == Key.PrintScreen)
        {
            return;
        }

        Complete(field, e.Key, e.KeyModifiers);
    }

    private void OnRecorderKeyUp(object? sender, KeyEventArgs e)
    {
        if (_recordingField is not { } field)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.PrintScreen)
        {
            Complete(field, e.Key, e.KeyModifiers);
        }
        else if (HotkeyRecorder.IsModifier(e.Key))
        {
            var prefix = HotkeyRecorder.ModifierPrefix(e.KeyModifiers);
            field.RecordingPreview = prefix.Length == 0 ? string.Empty : prefix + "…";
        }
    }

    private void Complete(HotkeySettingField field, Key key, KeyModifiers modifiers)
    {
        if (HotkeyRecorder.Describe(key, modifiers) is { } hotkey)
        {
            field.Value = hotkey;
            StopRecording();
        }
        else
        {
            field.RecordingPreview = $"{key} can't be used; try another key.";
        }
    }

    private void StopRecording()
    {
        if (_recordingField is { } field)
        {
            _recordingField = null;
            field.IsRecording = false;
        }
    }

    private async void ChooseIcon_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ControlCustomizationViewModel viewModel ||
            CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService(typeof(DeckHost.Core.Icons.IconLibrary)) is not DeckHost.Core.Icons.IconLibrary library)
        {
            return;
        }

        var iconify = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService(typeof(DeckHost.Core.Icons.IconifyClient)) as DeckHost.Core.Icons.IconifyClient;
        var picker = new IconPickerWindow { DataContext = new IconPickerViewModel(library, iconify) };
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (await picker.ShowDialog<IconChoice?>(owner) is { } choice)
        {
            if (choice.AsPicture)
            {
                viewModel.ImagePath = choice.Path;
            }
            else
            {
                viewModel.IconPath = choice.Path;
            }
        }
    }

    private async void BrowseImage_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select control image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        if (DataContext is ControlCustomizationViewModel vm && files.Count > 0)
        {
            vm.ImagePath = files[0].TryGetLocalPath() ?? files[0].Name;
        }
    }

    private async void BrowseSettingFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FilePathSettingField field })
        {
            return;
        }

        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {field.Label.ToLowerInvariant()}",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Programs and shortcuts") { Patterns = ["*.exe", "*.lnk", "*.bat", "*.cmd", "*.ps1", "*.url"] },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            field.Value = path;
        }
    }

    private void FontBox_OnGotFocus(object? sender, RoutedEventArgs e) => FontBox.IsDropDownOpen = true;

    /// <summary>Opens the whole font list (scroll), while typing in the box still filters it.</summary>
    private void FontList_OnClick(object? sender, RoutedEventArgs e)
    {
        if (FontBox.IsDropDownOpen)
        {
            FontBox.IsDropDownOpen = false;
            return;
        }

        FontBox.Focus();
        FontBox.IsDropDownOpen = true;
    }

}
