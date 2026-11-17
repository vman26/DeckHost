using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using DeckHost.App.Views;

namespace DeckHost.UiTests;

public sealed class SmokeTests
{
    [AvaloniaTest]
    public void The_headless_platform_renders_a_window()
    {
        var window = new Window { Width = 200, Height = 100, Content = new TextBlock { Text = "hello" } };
        window.Show();

        using var frame = window.CaptureRenderedFrame();

        Assert.That(frame, Is.Not.Null);
        Assert.That(frame!.PixelSize.Width, Is.EqualTo(200));
    }
}
