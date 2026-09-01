using System;
using System.Collections.Generic;
using System.Text;

namespace DeckHost.Contracts.Interfaces
{
    public interface IRenderContext
    {
        void DrawText(string text, double x, double y, float size);
        void DrawImage(byte[] pixelData, int width, int height);
    }
}
