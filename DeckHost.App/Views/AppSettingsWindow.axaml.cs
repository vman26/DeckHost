using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DeckHost.App.Views;

public partial class AppSettingsWindow : Window
{
    public AppSettingsWindow()
    {
        InitializeComponent();
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
}
