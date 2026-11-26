using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

public partial class WelcomeView : UserControl
{
    private WelcomeViewModel? _vm;

    public WelcomeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind(DataContext as WelcomeViewModel);
    }

    private void Bind(WelcomeViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.Finished -= OnFinished;
            _vm.PropertyChanged -= OnViewModelChanged;
            _vm.StopWatching();
        }

        _vm = vm;
        if (vm is not null)
        {
            vm.Finished += OnFinished;
            vm.PropertyChanged += OnViewModelChanged;
            if (TopLevel.GetTopLevel(this) is not null)
            {
                vm.StartWatching();
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Follows plugged-in devices only while shown (a page on top, the device creator, detaches it).
        _vm?.StartWatching();
        FocusNext();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _vm?.StopWatching();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WelcomeViewModel.Step))
        {
            FocusNext();
        }
    }

    /// <summary>The keyboard starts on Next, on every step.</summary>
    private void FocusNext() => Dispatcher.UIThread.Post(() => NextButton?.Focus(), DispatcherPriority.Loaded);

    private void OnFinished(object? sender, EventArgs e) => Pages.Close(this);

    private async void CopyCommands_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var copied = false;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetTextAsync(_vm.HelpCommands);
                copied = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                copied = false;
            }
        }

        _vm.CommandsCopied(copied);
    }

    private async void ImportProfile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a Stream Deck profile",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Stream Deck profiles") { Patterns = ["*.streamDeckProfile"] },
                new FilePickerFileType("DeckHost profiles") { Patterns = ["*.deckhostprofile"] },
            ],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            _vm.ImportProfile(path);
        }
    }
}
