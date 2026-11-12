using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using SkiaSharp;

namespace DeckHost.Core.Rendering
{
    internal sealed class SkiaRenderContext : IRenderContext
    {
        private const float MinTextSize = 9f;
        private readonly SKCanvas _canvas;
        private readonly SkiaDrawingResources _resources;

        public SkiaRenderContext(SKCanvas canvas, int width, int height, string deviceId, string controlId, IVariableStore variables, SkiaDrawingResources resources, ActionSettings? settings = null, TimeSpan clock = default, string profileId = "")
        {
            _canvas = canvas;
            _resources = resources;
            Width = width;
            Height = height;
            DeviceId = deviceId;
            ControlId = controlId;
            Variables = variables;
            Settings = settings ?? ActionSettings.Empty;
            Clock = clock;
            ProfileId = profileId;
        }

        public string DeviceId { get; }

        public string ControlId { get; }

        public int Width { get; }

        public int Height { get; }

        public IVariableStore Variables { get; }

        public ActionSettings Settings { get; }

        public void Clear(uint argbColor) => _canvas.Clear(new SKColor(argbColor));

        public void DrawText(string text, double x, double y, float size, uint argbColor = 0xFFFFFFFF)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var paint = _resources.TextPaint;
            paint.Color = new SKColor(argbColor);
            _canvas.DrawText(text, (float)x, (float)y, SKTextAlign.Left, _resources.GetFont(size), paint);
        }

        public void DrawCenteredText(string text, double centerX, double centerY, float size, uint argbColor = 0xFFFFFFFF)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var paint = _resources.TextPaint;
            paint.Color = new SKColor(argbColor);

            var maxWidth = Width * 0.9f;
            var font = _resources.GetFont(size);
            var measured = font.MeasureText(text, out var bounds, paint);
            if (measured > maxWidth && size > MinTextSize)
            {
                size = Math.Max(MinTextSize, size * maxWidth / measured);
                font = _resources.GetFont(size);
                font.MeasureText(text, out bounds, paint);
            }

            var baselineX = (float)(centerX - (bounds.Left + bounds.Right) / 2d);
            var baselineY = (float)(centerY - (bounds.Top + bounds.Bottom) / 2d);
            _canvas.DrawText(text, baselineX, baselineY, SKTextAlign.Left, font, paint);
        }

        public void FillRoundedRect(double x, double y, double width, double height, double radius, uint argbColor)
        {
            var paint = _resources.FillPaint;
            paint.Color = new SKColor(argbColor);
            _canvas.DrawRoundRect(new SKRect((float)x, (float)y, (float)(x + width), (float)(y + height)), (float)radius, (float)radius, paint);
        }

        public void DrawImage(byte[] rgbaPixels, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(rgbaPixels);
            if (width <= 0 || height <= 0 || rgbaPixels.Length < width * height * 4)
            {
                return;
            }

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var image = SKImage.FromPixelCopy(info, rgbaPixels, width * 4);
            if (image is not null)
            {
                DrawCover(image);
            }
        }

        public bool DrawImageFile(string path)
        {
            var image = _resources.GetImage(path, Clock);
            if (image is null)
            {
                return false;
            }

            DrawCover(image);
            return true;
        }

        /// <summary>Time used to pick animated GIF frames.</summary>
        public TimeSpan Clock { get; }

        public string ProfileId { get; }

        /// <summary>The binding's text style (set by <see cref="ControlRenderer"/>).</summary>
        public TextStyle? TextStyle { get; set; }

        public void DrawLabel(string text, TextVerticalAlignment defaultVertical = TextVerticalAlignment.Middle, float? defaultSize = null, uint defaultColor = 0xFFFFFFFF)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var style = TextStyle;
            var padding = Math.Max(3f, Width * 0.06f);
            var maxWidth = Width - (2 * padding);
            var size = style?.Size ?? defaultSize ?? Math.Max(12f, Height * 0.17f);
            var color = style?.Color is { Length: > 0 } hex && SKColor.TryParse(hex.Trim(), out var parsed) ? parsed : new SKColor(defaultColor);

            // Shrink until the text fits the control (but not below a readable size). With an automatic
            // size, unwrapped lines also shrink until the longest one fits the width.
            var autoSize = style?.Size is null;
            List<string> lines;
            SKFont font;
            while (true)
            {
                font = _resources.GetFont(size, style?.FontFamily, style?.Bold ?? false, style?.Italic ?? false);
                lines = LayoutLines(text, font, maxWidth, style?.Wrap ?? true);
                var lineHeight = font.Spacing;
                var fitsHeight = lines.Count * lineHeight <= Height - padding;
                var fitsWidth = !autoSize || lines.TrueForAll(line => font.MeasureText(line, _resources.TextPaint) <= maxWidth);
                if ((fitsHeight && fitsWidth) || size <= MinTextSize)
                {
                    break;
                }

                size = Math.Max(MinTextSize, size * 0.9f);
            }

            var lineSpacing = font.Spacing;
            var metrics = font.Metrics;
            var blockHeight = (lines.Count - 1) * lineSpacing + (-metrics.Ascent + metrics.Descent);
            var top = (style?.Vertical ?? defaultVertical) switch
            {
                TextVerticalAlignment.Top => padding,
                TextVerticalAlignment.Bottom => Height - padding - blockHeight,
                _ => (Height - blockHeight) / 2f,
            };

            var paint = _resources.TextPaint;
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                var lineWidth = font.MeasureText(line, paint);
                var x = (style?.Horizontal ?? TextHorizontalAlignment.Center) switch
                {
                    TextHorizontalAlignment.Left => padding,
                    TextHorizontalAlignment.Right => Width - padding - lineWidth,
                    _ => (Width - lineWidth) / 2f,
                };
                var baseline = top - metrics.Ascent + (index * lineSpacing);

                // A soft shadow keeps text readable on images and light backgrounds.
                paint.Color = new SKColor(0, 0, 0, (byte)(color.Alpha * 0.6));
                _canvas.DrawText(line, x + 1, baseline + 1, SKTextAlign.Left, font, paint);
                paint.Color = color;
                _canvas.DrawText(line, x, baseline, SKTextAlign.Left, font, paint);
            }
        }

        private List<string> LayoutLines(string text, SKFont font, float maxWidth, bool wrap)
        {
            var paint = _resources.TextPaint;
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
            {
                if (!wrap || font.MeasureText(paragraph, paint) <= maxWidth)
                {
                    lines.Add(paragraph);
                    continue;
                }

                var current = string.Empty;
                foreach (var word in paragraph.Split(' '))
                {
                    var candidate = current.Length == 0 ? word : current + " " + word;
                    if (font.MeasureText(candidate, paint) <= maxWidth)
                    {
                        current = candidate;
                        continue;
                    }

                    if (current.Length > 0)
                    {
                        lines.Add(current);
                    }

                    // A single word wider than the control is broken by characters.
                    current = word;
                    while (current.Length > 1 && font.MeasureText(current, paint) > maxWidth)
                    {
                        var fit = current.Length - 1;
                        while (fit > 1 && font.MeasureText(current[..fit], paint) > maxWidth)
                        {
                            fit--;
                        }

                        lines.Add(current[..fit]);
                        current = current[fit..];
                    }
                }

                lines.Add(current);
            }

            return lines;
        }

        public bool DrawEncodedImage(byte[] encodedImage)
        {
            if (encodedImage is not { Length: > 0 })
            {
                return false;
            }

            using var image = SKImage.FromEncodedData(encodedImage);
            if (image is null)
            {
                return false;
            }

            DrawCover(image);
            return true;
        }

        /// <summary>Fills the control with a #RRGGBB / #AARRGGBB color. Returns false for an invalid color.</summary>
        public bool FillColor(string? color)
        {
            if (string.IsNullOrWhiteSpace(color) || !SKColor.TryParse(color.Trim(), out var parsed))
            {
                return false;
            }

            var paint = _resources.FillPaint;
            paint.Color = parsed;
            _canvas.DrawRect(0, 0, Width, Height, paint);
            return true;
        }

        /// <summary>
        /// Draws this control's slice of an image that covers the whole device layout, so one
        /// picture spans all controls. <paramref name="layout"/> and <paramref name="control"/>
        /// are in the device's layout coordinates.
        /// </summary>
        public bool DrawLayoutSlice(string path, Rect layout, Rect control)
        {
            var image = _resources.GetImage(path, Clock);
            if (image is null || layout.Width <= 0 || layout.Height <= 0 || control.Width <= 0 || control.Height <= 0)
            {
                return false;
            }

            // Scale the image to cover the layout, then map the control's area into image pixels.
            var cover = Math.Max(layout.Width / image.Width, layout.Height / image.Height);
            var offsetX = (layout.Width - (image.Width * cover)) / 2d;
            var offsetY = (layout.Height - (image.Height * cover)) / 2d;
            var source = SKRect.Create(
                (float)((control.X - layout.X - offsetX) / cover),
                (float)((control.Y - layout.Y - offsetY) / cover),
                (float)(control.Width / cover),
                (float)(control.Height / cover));

            _canvas.DrawImage(image, source, SKRect.Create(0, 0, Width, Height), _resources.ImageSampling, _resources.ImagePaint);
            return true;
        }

        private void DrawCover(SKImage image)
        {
            var scale = Math.Max(Width / (float)image.Width, Height / (float)image.Height);
            var drawWidth = image.Width * scale;
            var drawHeight = image.Height * scale;
            var destination = SKRect.Create((Width - drawWidth) / 2f, (Height - drawHeight) / 2f, drawWidth, drawHeight);
            _canvas.DrawImage(image, destination, _resources.ImageSampling, _resources.ImagePaint);
        }
    }
}
