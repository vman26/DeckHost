using DeckHost.Core.Rendering.Interfaces;
using SkiaSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DeckHost.Core.Rendering
{
    public sealed class SurfaceRenderer : ISurfaceRenderer, IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _frameBuffer;
        private readonly GCHandle _bufferHandle;
        private readonly SKSurface _surface;
        private bool _disposed;

        public SurfaceRenderer(int width, int height)
        {
            _width = width;
            _height = height;

            int bufferSize = width * height * 4; // RGBA32 format
            _frameBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            _bufferHandle = GCHandle.Alloc(_frameBuffer, GCHandleType.Pinned);

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            _surface = SKSurface.Create(info, _bufferHandle.AddrOfPinnedObject(), width * 4);
        }

        public ReadOnlySpan<byte> Render(Action<SKCanvas> drawAction)
        {
            var canvas = _surface.Canvas;
            canvas.Clear(SKColors.Black);

            drawAction(canvas);
            canvas.Flush();

            return _frameBuffer.AsSpan(0, _width * _height * 4);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _surface.Dispose();
            if (_bufferHandle.IsAllocated)
            {
                _bufferHandle.Free();
            }
            ArrayPool<byte>.Shared.Return(_frameBuffer);
            _disposed = true;
        }
    }
}
