using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

/// <summary>The Devices page.</summary>
public partial class DeviceManagerView : UserControl
{
    public DeviceManagerView()
    {
        InitializeComponent();
    }

    private void DeviceSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DeviceRowViewModel row }
            && DataContext is DeviceManagerViewModel vm
            && vm.CreateDeviceSettings(row) is { } settings)
        {
            Pages.Open(this, $"{row.Name} settings", new DeviceSettingsView { DataContext = settings });
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as DeviceManagerViewModel)?.Resume();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as DeviceManagerViewModel)?.Dispose();
        base.OnDetachedFromVisualTree(e);
    }
}
