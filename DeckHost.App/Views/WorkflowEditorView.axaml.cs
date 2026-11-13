using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DeckHost.App.Platform;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

/// <summary>A page that shows the full window width (the main window lifts its usual maximum width).</summary>
public interface IWidePage;

public partial class WorkflowEditorView : UserControl, IWidePage
{
    private HotkeySettingField? _recordingField;

    public WorkflowEditorView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnRecorderKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnRecorderKeyUp, RoutingStrategies.Tunnel);
        Canvas.Message += (_, message) =>
        {
            if (DataContext is WorkflowEditorViewModel)
            {
                StatusText.Text = message;
            }
        };
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Canvas.FitToView();
            Canvas.Focus();
        }, DispatcherPriority.Loaded);
        DetachedFromVisualTree += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private void Fit_OnClick(object? sender, RoutedEventArgs e) => Canvas.FitToView();

    // ---------------------------------------------------------------- palette

    private async void PaletteNode_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: WorkflowPaletteItem item } source || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // A click adds the node after the selected one; a drag places it where it's dropped.
        e.Handled = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DragFormats.WorkflowNode, item.Kind));
        var result = await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        if (result == DragDropEffects.None && DataContext is WorkflowEditorViewModel vm)
        {
            vm.AddPaletteNodeCommand.Execute(item);
        }
    }

    private async void PaletteAction_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ActionItemViewModel action } source || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DragFormats.Action, action.Action));
        var result = await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        if (result == DragDropEffects.None && DataContext is WorkflowEditorViewModel vm)
        {
            vm.AddSelectedActionCommand.Execute(action);
        }
    }

    private void ActionSearch_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is not WorkflowEditorViewModel vm)
        {
            return;
        }

        var query = ActionSearch.Text?.Trim() ?? string.Empty;
        ActionList.ItemsSource = query.Length == 0
            ? vm.AvailableActions
            : vm.AvailableActions.Where(action =>
                action.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                action.PluginName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // ---------------------------------------------------------------- setting fields

    private async void BrowseSettingFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FilePathSettingField field } || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {field.Label.ToLowerInvariant()}",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Programs and shortcuts") { Patterns = ["*.exe", "*.lnk", "*.bat", "*.cmd", "*.ps1", "*.url", "*.app", "*.sh"] },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            field.Value = path;
        }
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

        if (e.Key != Key.PrintScreen)
        {
            Complete(field, e.Key, e.KeyModifiers);
        }
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
}
