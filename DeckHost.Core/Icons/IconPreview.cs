using DeckHost.Core.Rendering;
using SkiaSharp;
using System.Text;

namespace DeckHost.Core.Icons;

/// <summary>Small PNG previews of icons for the icon picker, single-color SVGs in the given color.</summary>
public static class IconPreview
{
    /// <summary>A preview of an icon file; null when it can't be read.</summary>
    public static byte[]? FromFile(string path, int size, uint singleColor = 0xFFFFFFFF)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            var isSvg = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
            return Render(data, isSvg, isSvg && Encoding.UTF8.GetString(data).Contains("currentColor", StringComparison.OrdinalIgnoreCase), size, singleColor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A preview of an SVG document (an Iconify icon).</summary>
    public static byte[]? FromSvg(string svg, int size, bool isSingleColor, uint singleColor = 0xFFFFFFFF) =>
        Render(Encoding.UTF8.GetBytes(svg), isSvg: true, isSingleColor, size, singleColor);

    private static byte[]? Render(byte[] data, bool isSvg, bool tint, int size, uint singleColor)
    {
        using var image = isSvg ? SvgRasterizer.ToImage(data, size * 2) : SKImage.FromEncodedData(data);
        if (image is null || image.Width <= 0 || image.Height <= 0)
        {
            return null;
        }

        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var scale = (float)size / Math.Max(image.Width, image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        using var paint = new SKPaint { IsAntialias = true };
        if (tint)
        {
            paint.ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(singleColor), SKBlendMode.SrcIn);
        }

        canvas.DrawImage(image, SKRect.Create((size - width) / 2f, (size - height) / 2f, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }
}
