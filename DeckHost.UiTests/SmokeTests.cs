using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using DeckHost.App.Views;

namespace DeckHost.UiTests;

public sealed class SmokeTests
{
    [AvaloniaTest]
    public void CaptureRenderedFrame_AWindowWithText_ReturnsAFrameOfTheWindowsSize()
    {
        // Arrange
        const int width = 200;
        var window = new Window { Width = width, Height = 100, Content = new TextBlock { Text = "hello" } };
        window.Show();

        // Act
        using var frame = window.CaptureRenderedFrame();

        // Assert
        Assert.That(frame, Is.Not.Null);
        Assert.That(frame!.PixelSize.Width, Is.EqualTo(width));
    }
}
