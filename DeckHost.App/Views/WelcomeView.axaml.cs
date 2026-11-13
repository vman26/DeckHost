using System;
using Avalonia.Controls;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WelcomeViewModel vm)
            {
                vm.Finished -= OnFinished;
                vm.Finished += OnFinished;
            }
        };
    }

    private void OnFinished(object? sender, EventArgs e) => Pages.Close(this);
}
