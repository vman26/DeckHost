using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using DeckHost.App.Views;

namespace DeckHost.UiTests;

/// <summary>The main window's pages: the Home button closes every page at once.</summary>
public sealed class PageNavigationTests
{
    [AvaloniaTest]
    public void GoHome_WithTwoPagesOpen_ShowsTheWorkspaceAndHidesThePages()
    {
        var window = new MainWindow();
        window.Show();
        window.OpenPage("First", new TextBlock { Text = "one" });
        window.OpenPage("Second", new TextBlock { Text = "two" });
        var pageHost = window.FindControl<Control>("PageHost")!;
        var workspace = window.FindControl<Control>("Workspace")!;

        window.GoHome();

        Assert.That(pageHost.IsVisible, Is.False);
        Assert.That(workspace.IsVisible, Is.True);
        window.Close();
    }

    [AvaloniaTest]
    public void GoHome_WithNoPageOpen_LeavesTheWorkspaceShown()
    {
        var window = new MainWindow();
        window.Show();
        var workspace = window.FindControl<Control>("Workspace")!;

        window.GoHome();

        Assert.That(workspace.IsVisible, Is.True);
        window.Close();
    }
}
