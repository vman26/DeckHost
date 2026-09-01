using SkiaSharp;

namespace DeckHost.Core.Rendering.Interfaces
{
    public interface ISurfaceRenderer
    {
        public ReadOnlySpan<byte> Render(Action<SKCanvas> drawAction);
    }
}