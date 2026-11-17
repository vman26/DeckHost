using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using DeckHost.App.Platform;
using DeckHost.App.Views;

namespace DeckHost.UiTests;

/// <summary>The plugin settings page only gets a web view when the computer can run one; otherwise a message points to the browser.</summary>
public sealed class SettingsBrowserHostTests
{
    private static readonly Uri Page = new("http://127.0.0.1:1/pi/test");

    private static void NoWebViewRuntime()
    {
        WebViewSupport.RuntimeVersion = static () => null;
        WebViewSupport.Reset();
    }

    private static Window Show(SettingsBrowserHost host)
    {
        var window = new Window { Width = 400, Height = 300, Content = host };
        window.Show();
        return window;
    }

    [AvaloniaTest]
    public void Source_ComputerWithoutAWebViewRuntime_CreatesNoWebView()
    {
        // Arrange
        NoWebViewRuntime();
        var host = new SettingsBrowserHost { Source = Page };

        // Act
        var window = Show(host);
        window.Close();
        WebViewSupport.RestoreDefaults();

        // Assert
        Assert.That(host.Browser, Is.Null);
    }

    [AvaloniaTest]
    public void Source_ComputerWithoutAWebViewRuntime_ShowsAMessageAboutTheBrowser()
    {
        // Arrange
        NoWebViewRuntime();
        var host = new SettingsBrowserHost { Source = Page };

        // Act
        var window = Show(host);
        var content = host.Content;
        window.Close();
        WebViewSupport.RestoreDefaults();

        // Assert
        Assert.That(content, Is.TypeOf<StackPanel>());
        Assert.That(((TextBlock)((StackPanel)content!).Children[0]).Text, Does.Contain("Open in browser"));
    }

    [AvaloniaTest]
    public void Source_NoPage_ShowsNothingAndCreatesNoWebView()
    {
        // Arrange
        var host = new SettingsBrowserHost();

        // Act
        var window = Show(host);
        window.Close();

        // Assert
        Assert.That(host.Content, Is.Null);
        Assert.That(host.Browser, Is.Null);
    }

    [AvaloniaTest]
    public void Failed_WebViewThatFailsWhileRunning_SwitchesToTheMessage()
    {
        // Arrange (the marker goes to a scratch folder, never the real one)
        NoWebViewRuntime();
        WebViewSupport.MarkerPath = Path.Combine(Path.GetTempPath(), "deckhost-webview-ui-" + Guid.NewGuid().ToString("N"), "webview-disabled.txt");
        var host = new SettingsBrowserHost();
        var window = Show(host);
        host.Source = Page;

        // Act
        WebViewSupport.ReportFailure(new System.Runtime.InteropServices.COMException("Class not registered", unchecked((int)0x80040154)));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var content = host.Content;
        window.Close();
        WebViewSupport.RestoreDefaults();

        // Assert
        Assert.That(content, Is.TypeOf<StackPanel>());
    }

    [AvaloniaTest]
    public void Message_WhenTheBrowserIsUnavailable_OffersToTryAgainAndSaysWhy()
    {
        // Arrange
        NoWebViewRuntime();
        var host = new SettingsBrowserHost { Source = Page };

        // Act
        var window = Show(host);
        var content = (StackPanel)host.Content!;
        window.Close();
        WebViewSupport.RestoreDefaults();

        // Assert
        Assert.That(content.Children.OfType<Button>().Single().Content, Is.EqualTo("Try the built-in browser again"));
        Assert.That(content.Children.OfType<TextBlock>().Count(), Is.EqualTo(2), "the message and the reason");
    }
}
