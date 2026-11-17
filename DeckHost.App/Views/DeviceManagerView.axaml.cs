using Avalonia;
using Avalonia.Controls;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

/// <summary>The Devices page.</summary>
public partial class DeviceManagerView : UserControl
{
    public DeviceManagerView()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as DeviceManagerViewModel)?.Dispose();
        base.OnDetachedFromVisualTree(e);
    }
}
