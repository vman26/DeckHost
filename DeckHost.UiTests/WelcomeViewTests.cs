using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using DeckHost.App.ViewModels;
using DeckHost.App.Views;

namespace DeckHost.UiTests;

/// <summary>The welcome tour for the keyboard: Next has the focus when it opens and after every step.</summary>
public sealed class WelcomeViewTests
{
    private static (Window Window, WelcomeView View, WelcomeViewModel Tour) ShowTour()
    {
        var tour = new WelcomeViewModel();
        var view = new WelcomeView { DataContext = tour };
        var window = new Window { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, tour);
    }

    [AvaloniaTest]
    public void Open_TheTour_FocusesNext()
    {
        // Arrange
        var (window, view, _) = ShowTour();

        // Act
        var next = view.FindControl<Button>("NextButton")!;

        // Assert
        Assert.That(next.IsFocused, Is.True);
        window.Close();
    }

    [AvaloniaTest]
    public void Next_ToTheSecondStep_KeepsTheFocusOnNext()
    {
        // Arrange
        var (window, view, tour) = ShowTour();
        var next = view.FindControl<Button>("NextButton")!;
        window.Focus();

        // Act
        tour.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.That(tour.IsStartStep, Is.True);
        Assert.That(next.IsFocused, Is.True);
        window.Close();
    }
}
