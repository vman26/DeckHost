using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DeckHost.App.Views;

/// <summary>Turns a rendered control image (RGBA8888, premultiplied: what the host sends to a device) into a bitmap.</summary>
public static class FrameBitmaps
{
    /// <summary>Copies the pixels into <paramref name="reuse"/> when it has the right size, else into a new bitmap.</summary>
    public static WriteableBitmap Copy(int width, int height, byte[] rgba, WriteableBitmap? reuse = null)
    {
        var bitmap = reuse is not null && reuse.PixelSize.Width == width && reuse.PixelSize.Height == height
            ? reuse
            : new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);

        using var buffer = bitmap.Lock();
        for (var row = 0; row < height; row++)
        {
            System.Runtime.InteropServices.Marshal.Copy(rgba, row * width * 4, buffer.Address + (row * buffer.RowBytes), width * 4);
        }

        return bitmap;
    }
}
