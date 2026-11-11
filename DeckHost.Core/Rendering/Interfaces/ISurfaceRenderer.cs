using SkiaSharp;

namespace DeckHost.Core.Rendering.Interfaces
{
    public interface ISurfaceRenderer
    {
        int Width { get; }

        int Height { get; }

        /// <summary>Renders into the renderer's buffer and returns it. The span stays valid until the next call.</summary>
        ReadOnlySpan<byte> Render(Action<SKCanvas> drawAction);
    }
}
