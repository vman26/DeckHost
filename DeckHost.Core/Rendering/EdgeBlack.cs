using SkiaSharp;

namespace DeckHost.Core.Rendering;

/// <summary>
/// Makes the black background of a Stream Deck key image transparent. Stream Deck plugins draw on
/// black (an unlit key), so their images would hide DeckHost's profile and control backgrounds.
/// Only black that is connected to the image's edge goes, so black inside a picture stays; the
/// anti-aliased rim next to it is un-blended from black so no dark halo remains.
/// </summary>
public static class EdgeBlack
{
    /// <summary>Darker than this in every channel counts as the black background.</summary>
    public const int Threshold = 16;

    /// <summary>The image with its edge-connected black made transparent; null when it can't be decoded.</summary>
    public static SKImage? Clear(byte[] encodedImage)
    {
        using var decoded = SKBitmap.Decode(encodedImage);
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
        {
            return null;
        }

        using var bitmap = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (!decoded.CopyTo(bitmap, SKColorType.Rgba8888))
        {
            return SKImage.FromBitmap(decoded);
        }

        Clear(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height);
        bitmap.NotifyPixelsChanged();
        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>The same on raw unpremultiplied RGBA pixels, in place.</summary>
    public static void Clear(Span<byte> rgba, int width, int height)
    {
        var cleared = new bool[width * height];
        var pending = new Stack<int>();

        for (var x = 0; x < width; x++)
        {
            pending.Push(x);
            pending.Push(((height - 1) * width) + x);
        }

        for (var y = 0; y < height; y++)
        {
            pending.Push(y * width);
            pending.Push((y * width) + width - 1);
        }

        while (pending.TryPop(out var index))
        {
            if (cleared[index] || !IsBackground(rgba, index))
            {
                continue;
            }

            cleared[index] = true;
            rgba[(index * 4) + 3] = 0;
            var x = index % width;
            var y = index / width;
            if (x > 0)
            {
                pending.Push(index - 1);
            }

            if (x < width - 1)
            {
                pending.Push(index + 1);
            }

            if (y > 0)
            {
                pending.Push(index - width);
            }

            if (y < height - 1)
            {
                pending.Push(index + width);
            }
        }

        // The ring next to the cleared area was blended with black: take the black back out
        // (color = icon * alpha), so the edge fades into the new background instead of a dark rim.
        for (var index = 0; index < cleared.Length; index++)
        {
            if (cleared[index] || !Touches(cleared, index, width, height))
            {
                continue;
            }

            var offset = index * 4;
            var brightest = Math.Max(rgba[offset], Math.Max(rgba[offset + 1], rgba[offset + 2]));
            if (brightest >= 160)
            {
                continue;
            }

            var alpha = Math.Max(brightest / 160f, 0f);
            if (alpha <= 0)
            {
                rgba[offset + 3] = 0;
                continue;
            }

            for (var channel = 0; channel < 3; channel++)
            {
                rgba[offset + channel] = (byte)Math.Min(255, MathF.Round(rgba[offset + channel] / alpha));
            }

            rgba[offset + 3] = (byte)MathF.Round(rgba[offset + 3] * alpha);
        }
    }

    private static bool IsBackground(ReadOnlySpan<byte> rgba, int index)
    {
        var offset = index * 4;
        return rgba[offset + 3] > 0 && rgba[offset] <= Threshold && rgba[offset + 1] <= Threshold && rgba[offset + 2] <= Threshold;
    }

    private static bool Touches(bool[] cleared, int index, int width, int height)
    {
        var x = index % width;
        var y = index / width;
        return (x > 0 && cleared[index - 1]) || (x < width - 1 && cleared[index + 1]) ||
               (y > 0 && cleared[index - width]) || (y < height - 1 && cleared[index + width]);
    }
}
