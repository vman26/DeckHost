using SkiaSharp;

namespace DeckHost.Core.Rendering;

/// <summary>
/// Paints, fonts and decoded images (including animated GIF frames) shared across renders, so
/// drawing a frame doesn't allocate native Skia objects. Not thread-safe: owned by the host's
/// render worker.
/// </summary>
public sealed class SkiaDrawingResources : IDisposable
{
    private const int MaxCachedImages = 64;
    private const int MaxAnimationFrames = 240;
    private const long MaxAnimationBytes = 96L * 1024 * 1024;
    private const int MinFrameDuration = 20;

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

    private readonly Dictionary<(string Family, bool Bold, bool Italic, float Size), SKFont> _styledFonts = [];

    /// <summary>A font for a family/weight/slant; falls back to the default typeface when the family isn't installed.</summary>
    public SKFont GetFont(float size, string? family, bool bold, bool italic)
    {
        if (string.IsNullOrWhiteSpace(family) && !bold && !italic)
        {
            return GetFont(size);
        }

        size = MathF.Round(size * 2f) / 2f;
        var key = (family ?? string.Empty, bold, italic, size);
        if (!_styledFonts.TryGetValue(key, out var font))
        {
            var typeface = SKTypeface.FromFamilyName(
                string.IsNullOrWhiteSpace(family) ? null : family,
                bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default;
            font = new SKFont(typeface, size) { Subpixel = true, Embolden = bold && !typeface.IsBold };
            _styledFonts[key] = font;
        }

        return font;
    }

    /// <summary>Whether the image is an animated GIF (more than one frame).</summary>
    public bool IsAnimated(string? path) => !string.IsNullOrWhiteSpace(path) && Load(path) is { Frames.Count: > 1 };

    /// <summary>
    /// The image, or for an animated GIF the frame shown at <paramref name="clock"/> (looping).
    /// Decoded once and kept until the file changes on disk.
    /// </summary>
    public SKImage? GetImage(string path, TimeSpan clock = default)
    {
        if (Load(path) is not { Frames.Count: > 0 } cached)
        {
            return null;
        }

        if (cached.Frames.Count == 1 || cached.TotalMilliseconds <= 0)
        {
            return cached.Frames[0].Image;
        }

        var position = (long)clock.TotalMilliseconds % cached.TotalMilliseconds;
        foreach (var (image, duration) in cached.Frames)
        {
            if (position < duration)
            {
                return image;
            }

            position -= duration;
        }

        return cached.Frames[^1].Image;
    }

    public void Dispose()
    {
        foreach (var font in _fonts.Values.Concat(_styledFonts.Values))
        {
            font.Dispose();
        }

        _styledFonts.Clear();

        foreach (var entry in _images.Values)
        {
            entry.Dispose();
        }

        _fonts.Clear();
        _images.Clear();
        FillPaint.Dispose();
        TextPaint.Dispose();
        ImagePaint.Dispose();
    }

    private CachedImage? Load(string path)
    {
        DateTime lastWrite;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            lastWrite = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        if (_images.TryGetValue(path, out var cached) && cached.LastWriteUtc == lastWrite)
        {
            return cached;
        }

        cached?.Dispose();
        _images.Remove(path);

        if (_images.Count >= MaxCachedImages)
        {
            foreach (var entry in _images.Values)
            {
                entry.Dispose();
            }

            _images.Clear();
        }

        var loaded = new CachedImage(Decode(path), lastWrite);
        _images[path] = loaded;
        return loaded;
    }

    /// <summary>All frames with their durations (ms); one frame for still images.</summary>
    private static List<(SKImage Image, int Duration)> Decode(string path)
    {
        var frames = new List<(SKImage, int)>();
        try
        {
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                if (SvgRasterizer.ToImage(File.ReadAllBytes(path), 288) is { } svg)
                {
                    frames.Add((svg, 0));
                }

                return frames;
            }

            using var codec = SKCodec.Create(path);
            if (codec is null)
            {
                return frames;
            }

            var frameInfos = codec.FrameInfo;
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            if (frameInfos.Length <= 1)
            {
                using var still = SKBitmap.Decode(codec, info);
                if (still is not null)
                {
                    frames.Add((SKImage.FromBitmap(still), 0));
                }

                return frames;
            }

            // Keep memory bounded for long or large animations: skip frames evenly when needed.
            var frameBytes = Math.Max(1L, (long)info.Width * info.Height * 4);
            var maxFrames = (int)Math.Clamp(MaxAnimationBytes / frameBytes, 1, MaxAnimationFrames);
            var step = Math.Max(1, (int)Math.Ceiling(frameInfos.Length / (double)maxFrames));

            var decoded = new SKBitmap?[frameInfos.Length];
            var pendingDuration = 0;
            for (var index = 0; index < frameInfos.Length; index++)
            {
                var bitmap = new SKBitmap(info);
                var required = frameInfos[index].RequiredFrame;
                if (required >= 0 && decoded[required] is { } previous)
                {
                    previous.CopyTo(bitmap);
                }
                else
                {
                    bitmap.Erase(SKColors.Transparent);
                }

                codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(index, required));
                decoded[index] = bitmap;
                pendingDuration += Math.Max(MinFrameDuration, frameInfos[index].Duration);

                if (index % step == 0 || index == frameInfos.Length - 1)
                {
                    frames.Add((SKImage.FromBitmap(bitmap), pendingDuration));
                    pendingDuration = 0;
                }

                // Release frames no later frame builds on.
                for (var candidate = 0; candidate < index; candidate++)
                {
                    if (decoded[candidate] is { } old && !IsRequiredLater(frameInfos, candidate, index))
                    {
                        old.Dispose();
                        decoded[candidate] = null;
                    }
                }
            }

            foreach (var bitmap in decoded)
            {
                bitmap?.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return frames;
    }

    private static bool IsRequiredLater(SKCodecFrameInfo[] frames, int frame, int current)
    {
        if (frame == current)
        {
            return true;
        }

        for (var index = current + 1; index < frames.Length; index++)
        {
            if (frames[index].RequiredFrame == frame)
            {
                return true;
            }
        }

        // The next frame may build on the current one even when not declared yet.
        return frame == current - 1 && current + 1 < frames.Length && frames[current + 1].RequiredFrame == frame;
    }

    private sealed class CachedImage(List<(SKImage Image, int Duration)> frames, DateTime lastWriteUtc) : IDisposable
    {
        public List<(SKImage Image, int Duration)> Frames { get; } = frames;

        public DateTime LastWriteUtc { get; } = lastWriteUtc;

        public long TotalMilliseconds { get; } = frames.Sum(static frame => (long)frame.Duration);

        public void Dispose()
        {
            foreach (var (image, _) in Frames)
            {
                image.Dispose();
            }
        }
    }
}
