using SkiaSharp;

namespace DeckHost.Core.Rendering;

/// <summary>
/// Paints, fonts and decoded images shared across renders, so drawing a frame doesn't allocate
/// native Skia objects. Not thread-safe: owned by the host's render worker.
/// </summary>
public sealed class SkiaDrawingResources : IDisposable
{
    private const int MaxCachedImages = 64;

    private readonly Dictionary<float, SKFont> _fonts = [];
    private readonly Dictionary<string, CachedImage> _images = new(StringComparer.OrdinalIgnoreCase);

    public SKPaint FillPaint { get; } = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    public SKPaint TextPaint { get; } = new() { IsAntialias = true };

    public SKPaint ImagePaint { get; } = new() { IsAntialias = true };

    public SKSamplingOptions ImageSampling { get; } = new(SKCubicResampler.Mitchell);

    public SKFont GetFont(float size)
    {
        size = MathF.Round(size * 2f) / 2f;
        if (!_fonts.TryGetValue(size, out var font))
        {
            font = new SKFont(SKTypeface.Default, size) { Subpixel = true };
            _fonts[size] = font;
        }

        return font;
    }

    /// <summary>Decodes an image once and keeps it until the file changes on disk.</summary>
    public SKImage? GetImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        DateTime lastWrite;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            lastWrite = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (_images.TryGetValue(path, out var cached) && cached.LastWriteUtc == lastWrite)
        {
            return cached.Image;
        }

        cached?.Image?.Dispose();
        _images.Remove(path);

        if (_images.Count >= MaxCachedImages)
        {
            foreach (var entry in _images.Values)
            {
                entry.Image?.Dispose();
            }

            _images.Clear();
        }

        SKImage? image = null;
        try
        {
            using var data = SKData.Create(path);
            image = data is null ? null : SKImage.FromEncodedData(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _images[path] = new CachedImage(image, lastWrite);
        return image;
    }

    public void Dispose()
    {
        foreach (var font in _fonts.Values)
        {
            font.Dispose();
        }

        foreach (var entry in _images.Values)
        {
            entry.Image?.Dispose();
        }

        _fonts.Clear();
        _images.Clear();
        FillPaint.Dispose();
        TextPaint.Dispose();
        ImagePaint.Dispose();
    }

    private sealed record CachedImage(SKImage? Image, DateTime LastWriteUtc);
}
