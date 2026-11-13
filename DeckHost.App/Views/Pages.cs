using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System;

namespace DeckHost.App.Views;

/// <summary>Shows pages (settings, plugins, profiles, updates) inside the main window instead of separate windows.</summary>
public interface IPageHost
{
    /// <summary>Shows a page on top of the current one; Back returns to the previous page.</summary>
    void OpenPage(string title, Control page);

    /// <summary>Closes the page on top.</summary>
    void ClosePage(Control page);
}

/// <summary>Opens and closes pages from inside a view, wherever the view is hosted.</summary>
public static class Pages
{
    /// <summary>
    /// Opens <paramref name="page"/> in the window hosting <paramref name="from"/> (the main
    /// window), or in a window of its own where there is no page host (tests, previews).
    /// </summary>
    public static void Open(Visual from, string title, Control page)
    {
        ArgumentNullException.ThrowIfNull(from);
        if (TopLevel.GetTopLevel(from) is IPageHost host)
        {
            host.OpenPage(title, page);
            return;
        }

        var window = new Window
        {
            Title = title,
            Content = page,
            Width = 760,
            Height = 720,
            Background = Avalonia.Media.Brush.Parse("#111827"),
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
        };
        if (TopLevel.GetTopLevel(from) is Window owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>Closes the page that contains <paramref name="from"/>.</summary>
    public static void Close(Visual from)
    {
        ArgumentNullException.ThrowIfNull(from);
        var page = from as Control ?? from.FindAncestorOfType<Control>();
        switch (TopLevel.GetTopLevel(from))
        {
            case IPageHost host when page is not null:
                host.ClosePage(page);
                break;
            case Window window:
                window.Close();
                break;
        }
    }
}
