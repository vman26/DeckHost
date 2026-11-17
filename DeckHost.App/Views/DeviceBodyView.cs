using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DeckHost.App.Views;

/// <summary>
/// The device behind the controls on the editor canvas: its body and, in the profile's background
/// color, the screens the controls sit on (<see cref="DeviceGeometry"/>, as the device simulator
/// draws it). The controls are drawn on top.
/// </summary>
public sealed class DeviceBodyView : Control
{
    public static readonly StyledProperty<DeviceGeometry?> GeometryProperty =
        AvaloniaProperty.Register<DeviceBodyView, DeviceGeometry?>(nameof(Geometry));

    public static readonly StyledProperty<IBrush?> ScreenProperty =
        AvaloniaProperty.Register<DeviceBodyView, IBrush?>(nameof(Screen));

    public static readonly StyledProperty<IBrush?> BodyProperty =
        AvaloniaProperty.Register<DeviceBodyView, IBrush?>(nameof(Body));

    public static readonly StyledProperty<IBrush?> EdgeProperty =
        AvaloniaProperty.Register<DeviceBodyView, IBrush?>(nameof(Edge));

    static DeviceBodyView() => AffectsRender<DeviceBodyView>(GeometryProperty, ScreenProperty, BodyProperty, EdgeProperty);

    public DeviceGeometry? Geometry
    {
        get => GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    /// <summary>The screens' color: the profile's background.</summary>
    public IBrush? Screen
    {
        get => GetValue(ScreenProperty);
        set => SetValue(ScreenProperty, value);
    }

    public IBrush? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public IBrush? Edge
    {
        get => GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Geometry is not { Width: > 0 } geometry)
        {
            return;
        }

        var body = geometry.Body;
        context.DrawRectangle(Body, Edge is null ? null : new Pen(Edge, 2), new Rect(body.X, body.Y, body.Width, body.Height), 18, 18);
        foreach (var display in geometry.Displays)
        {
            context.DrawRectangle(Screen, null, new Rect(display.X, display.Y, display.Width, display.Height), 6, 6);
        }
    }
}
