using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace DeckHost.Core.Devices
{
    internal sealed class SkiaRenderContext : IRenderContext
    {
        private readonly SKCanvas _canvas;
        private readonly ActionContext _actionContext;

        public SkiaRenderContext(SKCanvas canvas, int width, int height, ActionContext actionContext)
        {
            _canvas = canvas;
            Width = width;
            Height = height;
            _actionContext = actionContext;
        }

        public string DeviceId => _actionContext.DeviceId;

        public string ControlId => _actionContext.ControlId;

        public int Width { get; }

        public int Height { get; }

        public IVariableStore Variables => _actionContext.Variables;

        public void DrawText(string text, double x, double y, float size)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
            };
            using var font = new SKFont
            {
                Size = size,
            };
            _canvas.DrawText(text, (float)x, (float)y, SKTextAlign.Left, font, paint);
        }

        public void DrawCenteredText(string text, double centerX, double centerY, float size)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
            };
            using var font = new SKFont
            {
                Size = size,
            };

            var bounds = new SKRect();
            font.MeasureText(text, out bounds, paint);
            var baselineX = (float)(centerX - (bounds.Left + bounds.Right) / 2d);
            var baselineY = (float)(centerY - (bounds.Top + bounds.Bottom) / 2d);
            _canvas.DrawText(text, baselineX, baselineY, SKTextAlign.Left, font, paint);
        }

        public void FillRoundedRect(double x, double y, double width, double height, double radius, uint argbColor)
        {
            using var paint = new SKPaint
            {
                Color = new SKColor(argbColor),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            _canvas.DrawRoundRect(new SKRoundRect(new SKRect((float)x, (float)y, (float)(x + width), (float)(y + height)), (float)radius, (float)radius), paint);
        }

        public void DrawImage(byte[] pixelData, int width, int height)
        {
            // Draw raw pixel buffer or decoded bitmap onto the surface canvas
        }
    }
}
