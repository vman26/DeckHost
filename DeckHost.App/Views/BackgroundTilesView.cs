using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

/// <summary>
/// Draws the background of each display (<see cref="BackgroundPreviewTile"/>) at the display's place, in the
/// device's layout units; a <see cref="Viewbox"/> around it scales it.
/// </summary>
public sealed class BackgroundTilesView : Control
{
    public static readonly StyledProperty<IReadOnlyList<BackgroundPreviewTile>?> TilesProperty =
        AvaloniaProperty.Register<BackgroundTilesView, IReadOnlyList<BackgroundPreviewTile>?>(nameof(Tiles));

    private readonly Dictionary<string, WriteableBitmap> _bitmaps = [];

    static BackgroundTilesView() => AffectsRender<BackgroundTilesView>(TilesProperty);

    public IReadOnlyList<BackgroundPreviewTile>? Tiles
    {
        get => GetValue(TilesProperty);
        set => SetValue(TilesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Tiles is not { } tiles)
        {
            return;
        }

        foreach (var tile in tiles)
        {
            var frame = tile.Frame;
            _bitmaps.TryGetValue(frame.ControlId, out var reuse);
            var bitmap = FrameBitmaps.Copy(frame.Width, frame.Height, frame.Rgba, reuse);
            _bitmaps[frame.ControlId] = bitmap;
            context.DrawImage(bitmap, new Avalonia.Rect(0, 0, frame.Width, frame.Height), new Avalonia.Rect(tile.Bounds.X, tile.Bounds.Y, tile.Bounds.Width, tile.Bounds.Height));
        }

        // Drop what is no longer shown.
        foreach (var gone in _bitmaps.Keys.Except(tiles.Select(static tile => tile.Frame.ControlId)).ToList())
        {
            _bitmaps.Remove(gone);
        }
    }
}
