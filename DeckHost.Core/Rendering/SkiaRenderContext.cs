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

        public SkiaRenderContext(SKCanvas canvas, int width, int height, string deviceId, string controlId, IVariableStore variables, SkiaDrawingResources resources, ActionSettings? settings = null)
        {
            _canvas = canvas;
            _resources = resources;
            Width = width;
            Height = height;
            DeviceId = deviceId;
            ControlId = controlId;
            Variables = variables;
            Settings = settings ?? ActionSettings.Empty;
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
            var image = _resources.GetImage(path);
            if (image is null)
            {
                return false;
            }

            DrawCover(image);
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
