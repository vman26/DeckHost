using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DeckHost.App.Platform;

namespace DeckHost.App.Views;

/// <summary>
/// Shows a plugin's settings page in the system's web view. The web view is only created when there is a page to show
/// and the computer can run one (<see cref="WebViewSupport"/>); otherwise, and when the web view fails, a message
/// points to "Open in browser" instead. A web view that can't start used to end DeckHost.
/// </summary>
public sealed class SettingsBrowserHost : ContentControl
{
    public static readonly StyledProperty<Uri?> SourceProperty = AvaloniaProperty.Register<SettingsBrowserHost, Uri?>(nameof(Source));

    private NativeWebView? _browser;
    private bool _browserStarted;
    private bool _attached;

    public Uri? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>The control currently shown, null when there is no page or the fallback message is shown (tests).</summary>
    internal NativeWebView? Browser => _browser;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        WebViewSupport.Failed += OnWebViewFailed;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        WebViewSupport.Failed -= OnWebViewFailed;
        Release();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty && _attached)
        {
            Refresh();
        }
    }

    private void OnWebViewFailed() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        Release();
        ShowMessage();
    });

    private void Refresh()
    {
        if (Source is not { } source)
        {
            Release();
            return;
        }

        if (!WebViewSupport.IsAvailable)
        {
            Release();
            ShowMessage();
            return;
        }

        if (_browser is null)
        {
            _browser = CreateBrowser();
            Content = _browser;
        }

        _browser.Source = source;
    }

    /// <summary>Takes the web view out of the window (a half-started one is aborted by its host, quietly) before the page goes away.</summary>
    private void Release()
    {
        if (_browser is null)
        {
            Content = null;
            return;
        }

        var browser = _browser;
        _browser = null;
        Content = null;
        browser.Source = null;
    }

    private void ShowMessage()
    {
        var text = SafeMode.IsOn
            ? "Safe mode: the built-in browser is off."
            : OperatingSystem.IsLinux()
                ? "The built-in browser needs WebKitGTK (libwebkit2gtk-4.1)."
                : "The built-in browser isn't available on this computer.";
        var details = SafeMode.IsOn ? null : WebViewSupport.Reason;
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#FCA5A5")),
            Text = $"{text} Use Open in browser above; changes there are saved straight to the plugin.",
        });
        if (!string.IsNullOrEmpty(details))
        {
            panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#94A3B8")), Text = details });
        }

        if (!SafeMode.IsOn)
        {
            var retry = new Button { Content = "Try the built-in browser again", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            retry.Click += (_, _) =>
            {
                WebViewSupport.Retry();
                Release();
                Refresh();
            };
            panel.Children.Add(retry);
        }

        Content = panel;
    }

    private NativeWebView CreateBrowser()
    {
        var browser = new NativeWebView();
        _browserStarted = false;
        browser.EnvironmentRequested += (_, e) =>
        {
            e.EnableDevTools = true;
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckHost", "webview");
            switch (e)
            {
                case Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs windows:
                    windows.UserDataFolder = data;
                    break;
                case Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs gtk:
                    gtk.BaseDataDirectory = data;
                    gtk.BaseCacheDirectory = Path.Combine(data, "cache");
                    break;
            }
        };
        browser.NewWindowRequested += (_, e) =>
        {
            // Links that open a window go to the browser.
            e.Handled = true;
            if (e.Request is { Scheme: "http" or "https" } uri)
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            }
        };
        browser.AdapterCreated += (_, _) => _browserStarted = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            // No web view on this system (Linux without WebKitGTK): the message instead.
            if (!_browserStarted && _browser == browser && browser.IsEffectivelyVisible)
            {
                Release();
                ShowMessage();
            }
        }, TimeSpan.FromSeconds(8));
        return browser;
    }
}
