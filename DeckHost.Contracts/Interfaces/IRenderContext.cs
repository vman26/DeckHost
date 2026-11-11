using System;
using System.Collections.Generic;
using System.Text;

namespace DeckHost.Contracts.Interfaces
{
    public interface IRenderContext
    {
        string DeviceId { get; }
        string ControlId { get; }
        int Width { get; }
        int Height { get; }
        IVariableStore Variables { get; }

        void DrawText(string text, double x, double y, float size);
        void DrawCenteredText(string text, double centerX, double centerY, float size);
        void FillRoundedRect(double x, double y, double width, double height, double radius, uint argbColor);
        void DrawImage(byte[] pixelData, int width, int height);
    }
}
