using DeckHost.Core.Rendering.Interfaces;
using SkiaSharp;

namespace DeckHost.Core.Rendering
{
    /// <summary>
    /// Offscreen RGBA8888 surface backed by one pinned buffer that lives as long as the renderer.
    /// Create one per control size and reuse it; the returned span is overwritten by the next render.
    /// </summary>
    public sealed class SurfaceRenderer : ISurfaceRenderer, IDisposable
    {
        private readonly byte[] _frameBuffer;
        private readonly SKSurface _surface;
        private bool _disposed;

        public SurfaceRenderer(int width, int height)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

            Width = width;
            Height = height;

            // Allocated on the pinned object heap: no GCHandle, and the buffer never moves
            // while Skia holds a pointer to it.
            _frameBuffer = GC.AllocateUninitializedArray<byte>(width * height * 4, pinned: true);

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            unsafe
            {
                fixed (byte* pixels = _frameBuffer)
                {
                    _surface = SKSurface.Create(info, (nint)pixels, width * 4)
                        ?? throw new InvalidOperationException($"Could not create a {width}x{height} Skia surface.");
                }
            }
        }

        public int Width { get; }

        public int Height { get; }

        public ReadOnlySpan<byte> Render(Action<SKCanvas> drawAction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var canvas = _surface.Canvas;
            canvas.Clear(SKColors.Black);
            drawAction(canvas);
            canvas.Flush();

            return _frameBuffer;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _surface.Dispose();
            _disposed = true;
        }
    }
}
