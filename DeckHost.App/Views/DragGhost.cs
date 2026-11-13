using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using DeckHost.Contracts.Interfaces;

namespace DeckHost.App.Views;

/// <summary>A window that shows what is being dragged (the action's icon and name) under the pointer.</summary>
internal interface IDragGhostHost
{
    void ShowGhost(string text, IDeckAction? action);

    void MoveGhost(Point position);

    void HideGhost();
}

/// <summary>
/// The drag ghost. The platform's drag and drop shows no picture of what is dragged, so the main
/// window draws one in an overlay that follows the pointer while the drag is over it.
/// </summary>
internal static class DragGhost
{
    public static void Show(Visual from, string text, IDeckAction? action) =>
        (TopLevel.GetTopLevel(from) as IDragGhostHost)?.ShowGhost(text, action);

    public static void Move(Visual from, DragEventArgs e)
    {
        if (TopLevel.GetTopLevel(from) is { } top and IDragGhostHost host)
        {
            host.MoveGhost(e.GetPosition(top));
        }
    }

    public static void Hide(Visual from) => (TopLevel.GetTopLevel(from) as IDragGhostHost)?.HideGhost();
}
