using SkiaSharp;
using Svg.Skia;
using System.Text;

namespace DeckHost.Core.Rendering;

/// <summary>Turns SVG (files or data: URLs from Stream Deck plugins) into bitmaps.</summary>
public static class SvgRasterizer
{
    /// <summary>Default size: Stream Deck @2x key images are 144×144.</summary>
    public const int DefaultSize = 144;

    public static bool IsSvg(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data[..Math.Min(data.Length, 512)]).TrimStart('﻿', ' ', '\r', '\n', '\t');
        return text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && text.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Renders an SVG to an image that fits <paramref name="size"/>×<paramref name="size"/> (aspect kept).</summary>
    public static SKImage? ToImage(byte[] svgData, int size = DefaultSize)
    {
        try
        {
            using var svg = new SKSvg();
            using var stream = new MemoryStream(svgData);
            var picture = svg.Load(stream);
            if (picture is null)
            {
                return null;
            }

            var bounds = picture.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return null;
            }

            var scale = size / Math.Max(bounds.Width, bounds.Height);
            var width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bounds.Height * scale));
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();
            return surface.Snapshot();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Xml.XmlException or FormatException or NullReferenceException)
        {
            return null;
        }
    }

    /// <summary>Renders an SVG to PNG bytes, or null when it can't be parsed.</summary>
    public static byte[]? ToPng(byte[] svgData, int size = DefaultSize)
    {
        using var image = ToImage(svgData, size);
        using var encoded = image?.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }
}
