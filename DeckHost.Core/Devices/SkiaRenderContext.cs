using DeckHost.Contracts.Interfaces;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace DeckHost.Core.Devices
{
    internal sealed class SkiaRenderContext : IRenderContext
    {
        private readonly SKCanvas _canvas;
        private readonly int _width;
        private readonly int _height;

        public SkiaRenderContext(SKCanvas canvas, int width, int height)
        {
            _canvas = canvas;
            _width = width;
            _height = height;
        }

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

        public void DrawImage(byte[] pixelData, int width, int height)
        {
            // Draw raw pixel buffer or decoded bitmap onto the surface canvas
        }
    }
}
