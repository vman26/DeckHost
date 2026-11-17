using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Models;

namespace DeckHost.Core.Rendering;

/// <summary>
/// A profile background on a device's displays, drawn the way the host draws it behind the controls' content:
/// the same base color, layer resolution and layout slices (<see cref="ControlRenderer.DrawBackground"/>), so
/// the Background Studio's preview matches the device.
/// </summary>
public static class BackgroundPreview
{
    /// <summary>One frame per control with a display, in the device's control order.</summary>
    /// <param name="clock">The time an animated image is shown at (the first frame by default).</param>
    public static IReadOnlyList<ControlFrame> Render(
        DeviceDefinition device,
        ProfileBackground? background,
        IVariableStore variables,
        SkiaDrawingResources resources,
        TimeSpan clock = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(resources);

        var layers = background is null ? [] : BackgroundLayers.Resolve(background, variables);
        var layout = ControlRenderer.LayoutBounds(device);
        var frames = new List<ControlFrame>();
        var renderers = new Dictionary<(int Width, int Height), SurfaceRenderer>();
        try
        {
            foreach (var control in device.Controls.Where(static control => control.HasImageOutput))
            {
                var size = (control.RenderWidth, control.RenderHeight);
                if (!renderers.TryGetValue(size, out var renderer))
                {
                    renderer = new SurfaceRenderer(size.RenderWidth, size.RenderHeight);
                    renderers[size] = renderer;
                }

                var pixels = renderer.Render(canvas =>
                {
                    var context = new SkiaRenderContext(canvas, renderer.Width, renderer.Height, device.Id, control.Id, variables, resources, clock: clock);
                    ControlRenderer.DrawBackground(context, layers, layout, control.Bounds);
                });
                frames.Add(new ControlFrame(device.Id, control.Id, renderer.Width, renderer.Height, pixels.ToArray()));
            }
        }
        finally
        {
            foreach (var renderer in renderers.Values)
            {
                renderer.Dispose();
            }
        }

        return frames;
    }
}
