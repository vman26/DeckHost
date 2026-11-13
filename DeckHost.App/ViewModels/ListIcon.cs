using Avalonia.Media;
using Avalonia.Media.Imaging;
using DeckHost.Core.Rendering;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace DeckHost.App.ViewModels;

/// <summary>
/// An icon in the action list: SVG path data on the 24×24 grid (DeckHost's icons) as a geometry,
/// or an image file (a plugin's icon) as a small bitmap. Files are read once and cached.
/// </summary>
public sealed class ListIcon
{
    private const int BitmapSize = 64;

    private static readonly ConcurrentDictionary<string, ListIcon> Cache = new(StringComparer.Ordinal);

    private ListIcon(Geometry? geometry, Bitmap? image, IBrush brush)
    {
        Geometry = geometry;
        Image = image;
        Brush = brush;
    }

    /// <summary>Path data on a 24×24 grid, even-odd filled.</summary>
    public Geometry? Geometry { get; }

    public Bitmap? Image { get; }

    public IBrush Brush { get; }

    public bool HasGeometry => Geometry is not null;

    public bool HasImage => Image is not null;

    /// <summary>
    /// The icon for <paramref name="source"/> (path data or an absolute file path) drawn in
    /// <paramref name="argb"/>; <paramref name="fallback"/> path data when there is none or it can't be read.
    /// </summary>
    public static ListIcon From(string? source, uint argb, string fallback)
    {
        var key = $"{argb:X8}|{source}|{fallback}";
        return Cache.GetOrAdd(key, _ =>
        {
            var brush = new SolidColorBrush(Color.FromUInt32(argb));
            if (!string.IsNullOrWhiteSpace(source))
            {
                if (Path.IsPathFullyQualified(source))
                {
                    if (LoadBitmap(source) is { } bitmap)
                    {
                        return new ListIcon(null, bitmap, brush);
                    }
                }
                else if (ParsePath(source) is { } geometry)
                {
                    return new ListIcon(geometry, null, brush);
                }
            }

            return new ListIcon(ParsePath(fallback), null, brush);
        });
    }

    private static Geometry? ParsePath(string data)
    {
        try
        {
            // F0: even-odd, as the icons are drawn on the device.
            return Avalonia.Media.Geometry.Parse("F0 " + data);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Bitmap? LoadBitmap(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            if (SvgRasterizer.IsSvg(bytes))
            {
                bytes = SvgRasterizer.ToPng(bytes, BitmapSize) ?? [];
            }

            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, BitmapSize);
        }
        catch (Exception)
        {
            // Not an image Avalonia can decode: the fallback icon shows instead.
            return null;
        }
    }
}
