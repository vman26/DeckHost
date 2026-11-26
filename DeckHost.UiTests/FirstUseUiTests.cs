using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DeckHost.App.Views;

namespace DeckHost.UiTests;

/// <summary>The main window's first-use helpers: the tips over the window and the first close to the tray.</summary>
public sealed class FirstUseUiTests
{
    [AvaloniaTest]
    public void ShowCoachMarks_WorkspaceShown_ShowsTheTipNextToItsTarget()
    {
        // Arrange
        var window = new MainWindow();
        window.Show();

        // Act
        window.ShowCoachMarks();
        Dispatcher.UIThread.RunJobs();
        var layer = window.FindControl<Control>("CoachLayer")!;
        var card = window.FindControl<Control>("CoachCard")!;

        // Assert
        Assert.That((layer.IsVisible, Canvas.GetLeft(card) >= 0, Canvas.GetLeft(card) + card.Width <= window.Bounds.Width), Is.EqualTo((true, true, true)));
        window.Close();
    }

    [AvaloniaTest]
    public void ShowCoachMarks_PageOpen_WaitsUntilThePageCloses()
    {
        // Arrange
        var window = new MainWindow();
        window.Show();
        window.OpenPage("Welcome", new TextBlock { Text = "tour" });
        var layer = window.FindControl<Control>("CoachLayer")!;

        // Act
        window.ShowCoachMarks();
        Dispatcher.UIThread.RunJobs();
        var shownUnderThePage = layer.IsVisible;
        window.GoHome();
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.That((shownUnderThePage, layer.IsVisible), Is.EqualTo((false, true)));
        window.Close();
    }

    [AvaloniaTest]
    public void AskTrayNotice_Escape_KeepsRunningAndHidesTheNotice()
    {
        // Arrange
        var window = new MainWindow();
        window.Show();
        bool? keepRunning = null;
        window.AskTrayNotice(answer => keepRunning = answer);
        var notice = window.FindControl<Control>("TrayNotice")!;

        // Act
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        // Assert
        Assert.That((keepRunning, notice.IsVisible), Is.EqualTo(((bool?)true, false)));
        window.Close();
    }
}
