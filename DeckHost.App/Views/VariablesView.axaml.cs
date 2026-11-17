using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

/// <summary>The Variables page; it reads the live values again every second while it is shown.</summary>
public partial class VariablesView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = System.TimeSpan.FromSeconds(1) };

    public VariablesView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => (DataContext as VariablesViewModel)?.Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as VariablesViewModel)?.Start();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        (DataContext as VariablesViewModel)?.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
