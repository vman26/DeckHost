using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices.Interfaces;
using DeckHost.Core.Rendering;
using DeckHost.Device.MiraBox;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DeckHost.Core.Devices
{
    public class DeviceDisplaySession : IDeviceDisplaySession, IDisposable
    {
        private readonly int _deviceId;
        private readonly SurfaceRenderer _renderer;
        private readonly int _width;
        private readonly int _height;

        public DeviceDisplaySession(int deviceId, int width, int height)
        {
            _deviceId = deviceId;
            _width = width;
            _height = height;
            _renderer = new SurfaceRenderer(width, height);
        }

        public void RenderAndPush(IDeckAction action, ActionContext actionContext)
        {
            // 1. Render the action's visual layout to the zero-allocation offscreen surface
            ReadOnlySpan<byte> pixelData = _renderer.Render(canvas =>
            {
                var renderContext = new SkiaRenderContext(canvas, _width, _height);
                action.Render(renderContext);
            });

            // 2. Push the raw framebuffer across the native C-ABI boundary without heap allocation
            if (!pixelData.IsEmpty)
            {
                ref byte bufferRef = ref MemoryMarshal.GetReference(pixelData);
                MiraBoxNative.SendDisplayBuffer(_deviceId, in bufferRef, pixelData.Length, _width, _height);
            }
        }

        public void Dispose() => _renderer.Dispose();
    }
}
