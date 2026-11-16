using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using SkiaSharp;

namespace DeckHost.Core.Rendering
{
    internal sealed class SkiaRenderContext : IRenderContext
    {
        /// <summary>
        /// The control's own text (Customize → Text) while the action draws; whether the action drew
        /// it is in <see cref="ExpectedTextDrawn"/>, so the host can add it when the action doesn't.
        /// </summary>
        internal string? ExpectedText
        {
            get => _expectedText;
            set
            {
                _expectedText = value;
                ExpectedTextDrawn = false;
            }
        }

        private string? _expectedText;

        internal bool ExpectedTextDrawn { get; private set; }

        private void Note(string text)
        {
            if (!ExpectedTextDrawn && ExpectedText is { Length: > 0 } expected &&
                (text.Contains(expected, StringComparison.Ordinal) || Squash(text).Contains(Squash(expected), StringComparison.Ordinal)))
            {
                ExpectedTextDrawn = true;
            }
        }

        /// <summary>Lines joined for a strip: compare without line breaks and repeated spaces.</summary>
        private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
            Note(text ?? string.Empty);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var paint = _resources.TextPaint;
            paint.Color = new SKColor(argbColor);
            _canvas.DrawText(text, (float)x, (float)y, SKTextAlign.Left, StyledFont(size), paint);
        }

        /// <summary>The font for text an action places itself: the binding's family, bold and italic at the action's size.</summary>
        private SKFont StyledFont(float size) =>
            _resources.GetFont(size, TextStyle?.FontFamily, TextStyle?.Bold ?? false, TextStyle?.Italic ?? false);

        public void DrawCenteredText(string text, double centerX, double centerY, float size, uint argbColor = 0xFFFFFFFF)
        {
            Note(text ?? string.Empty);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var paint = _resources.TextPaint;
            paint.Color = new SKColor(argbColor);

            var maxWidth = Width * 0.9f;
            var font = StyledFont(size);
            var measured = font.MeasureText(text, out var bounds, paint);
            if (measured > maxWidth && size > MinTextSize)
            {
                size = Math.Max(MinTextSize, size * maxWidth / measured);
                font = StyledFont(size);
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

        public void DrawIcon(string svgPathData, double centerX, double centerY, double size, uint argbColor = 0xFFFFFFFF)
        {
            if (size <= 0 || string.IsNullOrWhiteSpace(svgPathData))
            {
                return;
            }

            if (Path.IsPathRooted(svgPathData))
            {
                DrawIconFile(svgPathData, centerX, centerY, size, argbColor);
                return;
            }

            if (_resources.GetIconPath(svgPathData) is not { } path)
            {
                return;
            }

            var paint = _resources.FillPaint;
            paint.Color = new SKColor(argbColor);
            _canvas.Save();
            _canvas.Translate((float)(centerX - (size / 2d)), (float)(centerY - (size / 2d)));
            _canvas.Scale((float)(size / SkiaDrawingResources.IconGrid));
            _canvas.DrawPath(path, paint);
            _canvas.Restore();
        }

        /// <summary>An icon file fitted into the square; a single-color SVG in <paramref name="argbColor"/>.</summary>
        private void DrawIconFile(string file, double centerX, double centerY, double size, uint argbColor)
        {
            var image = _resources.GetImage(file, Clock);
            if (image is null)
            {
                return;
            }

            var scale = size / Math.Max(image.Width, image.Height);
            var width = image.Width * scale;
            var height = image.Height * scale;
            var target = SKRect.Create((float)(centerX - (width / 2d)), (float)(centerY - (height / 2d)), (float)width, (float)height);
            using var paint = new SKPaint { IsAntialias = true };
            if (_resources.IsSingleColor(file))
            {
                paint.ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(argbColor), SKBlendMode.SrcIn);
            }

            _canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }

        public void FillGradientRect(double x, double y, double width, double height, double radius, uint fromArgb, uint toArgb)
        {
            var rect = new SKRect((float)x, (float)y, (float)(x + width), (float)(y + height));
            using var shader = SKShader.CreateLinearGradient(new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
                [new SKColor(fromArgb), new SKColor(toArgb)], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Shader = shader };
            _canvas.DrawRoundRect(rect, (float)radius, (float)radius, paint);
        }

        public void DrawRightAlignedText(string text, double right, double baselineY, float size, uint argbColor = 0xFFFFFFFF)
        {
            Note(text ?? string.Empty);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var paint = _resources.TextPaint;
            paint.Color = new SKColor(argbColor);
            var font = StyledFont(size);
            _canvas.DrawText(text, (float)(right - font.MeasureText(text, paint)), (float)baselineY, SKTextAlign.Left, font, paint);
        }

        /// <summary>Time used to pick animated GIF frames.</summary>
        public TimeSpan Clock { get; }

        public string ProfileId { get; }

        public bool IsCompact { get; private init; }

        /// <summary>The binding's text style (set by <see cref="ControlRenderer"/>).</summary>
        public TextStyle? TextStyle { get; set; }

        /// <summary>
        /// Draws into one rectangle of this control's image, clipped to it, with a context that is
        /// as large as the rectangle. <paramref name="controlId"/> and <paramref name="settings"/>
        /// are those of the binding drawn there, which can belong to another control (a knob shown
        /// on its touch zone), so the action reads its own variables and settings.
        /// </summary>
        public void DrawRegion(int x, int y, int width, int height, string controlId, ActionSettings? settings, bool compact, Action<SkiaRenderContext> draw)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            _canvas.Save();
            try
            {
                _canvas.ClipRect(SKRect.Create(x, y, width, height));
                _canvas.Translate(x, y);
                draw(new SkiaRenderContext(_canvas, width, height, DeviceId, controlId, Variables, _resources, settings, Clock, ProfileId) { IsCompact = compact });
            }
            finally
            {
                _canvas.Restore();
            }
        }

        public void DrawLabel(string text, TextVerticalAlignment defaultVertical = TextVerticalAlignment.Middle, float? defaultSize = null, uint defaultColor = 0xFFFFFFFF)
        {
            Note(text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var style = TextStyle;

            // A strip (see IsCompact) has room for one line: join the lines and shrink that to fit.
            var singleLine = IsCompact;
            if (singleLine)
            {
                text = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            var padding = singleLine ? Math.Max(2f, Height * 0.08f) : Math.Max(3f, Width * 0.06f);
            var sidePadding = Math.Max(3f, Width * 0.06f);
            var maxWidth = Width - (2 * sidePadding);
            var size = style?.Size ?? defaultSize ?? (singleLine ? Height * 0.5f : Math.Max(12f, Height * 0.17f));
            var color = style?.Color is { Length: > 0 } hex && SKColor.TryParse(hex.Trim(), out var parsed) ? parsed : new SKColor(defaultColor);

            // Shrink until the text fits the control (but not below a readable size). With an automatic
            // size, unwrapped lines also shrink until the longest one fits the width.
            var autoSize = style?.Size is null || singleLine;
            List<string> lines;
            SKFont font;
            while (true)
            {
                font = _resources.GetFont(size, style?.FontFamily, style?.Bold ?? false, style?.Italic ?? false);
                lines = LayoutLines(text, font, maxWidth, !singleLine && (style?.Wrap ?? true));
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
            var top = (singleLine ? TextVerticalAlignment.Middle : style?.Vertical ?? defaultVertical) switch
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
                    TextHorizontalAlignment.Left => sidePadding,
                    TextHorizontalAlignment.Right => Width - sidePadding - lineWidth,
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

        public bool DrawEncodedImage(byte[] encodedImage, bool clearEdgeBlack)
        {
            if (!clearEdgeBlack)
            {
                return DrawEncodedImage(encodedImage);
            }

            if (encodedImage is not { Length: > 0 } || _resources.GetEdgeClearedImage(encodedImage) is not { } image)
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
