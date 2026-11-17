using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Models;
using DeckHost.Core.Rendering;
using DeckHost.Core.State;
using DeckHost.App.ViewModels;
using SkiaSharp;

namespace DeckHost.Tests;

/// <summary>Gradients with more than two colors, and radial gradients: what they resolve to, draw and save.</summary>
public sealed class GradientStopsTests
{
    private const string Red = "#FF0000";
    private const string Green = "#00FF00";
    private const string Blue = "#0000FF";
    private const string CpuVariable = "system.cpu";
    private const int ColorTolerance = 40;
    private const int KeyUnits = 10;
    private const int KeyPixels = 8;

    private static ProfileBackground Of(BackgroundLayer layer) => new() { Layers = [layer] };

    private static BackgroundLayer RedGreenBlue(string kind = BackgroundLayerKinds.Gradient) => new(kind)
    {
        Color = Red,
        ToColor = Blue,
        Angle = 0,
        Stops = [new GradientStop(Red, 0), new GradientStop(Green, 0.5), new GradientStop(Blue, 1)],
    };

    private static ResolvedLayer ResolveOnly(BackgroundLayer layer, VariableStore? store = null)
    {
        using var own = new VariableStore();
        return BackgroundLayers.Resolve(Of(layer), store ?? own).Single();
    }

    private static DeviceDefinition ThreeKeysInARow() => new(
        "gradient.device",
        "Gradient device",
        [.. Enumerable.Range(0, 3).Select(index => new PhysicalControl($"key.{index}", $"Key {index + 1}", ControlType.Key,
            new Rect(index * KeyUnits, 0, KeyUnits, KeyUnits), InputCapabilities.Press, OutputCapabilities.ColorImage, KeyPixels, KeyPixels))],
        true);

    private static SKColor CenterPixelOf(ControlFrame frame)
    {
        var offset = ((KeyPixels / 2 * KeyPixels) + (KeyPixels / 2)) * 4;
        return new SKColor(frame.Rgba[offset], frame.Rgba[offset + 1], frame.Rgba[offset + 2], frame.Rgba[offset + 3]);
    }

    private static IReadOnlyList<ControlFrame> RenderThreeKeys(BackgroundLayer layer)
    {
        using var resources = new SkiaDrawingResources();
        using var store = new VariableStore();
        return BackgroundPreview.Render(ThreeKeysInARow(), Of(layer), store, resources);
    }

    [Test]
    public void Resolve_GradientWithThreeStops_KeepsThemWithTheirPositions()
    {
        // Arrange
        var layer = RedGreenBlue();

        // Act
        var resolved = ResolveOnly(layer);

        // Assert
        Assert.That(resolved.Stops!.Positions, Is.EqualTo(new[] { 0f, 0.5f, 1f }));
        Assert.That(resolved.Stops.Colors, Is.EqualTo(new[] { SKColor.Parse(Red), SKColor.Parse(Green), SKColor.Parse(Blue) }));
    }

    [Test]
    public void Resolve_StopsGivenOutOfOrder_AreSortedByPosition()
    {
        // Arrange
        var layer = RedGreenBlue() with { Stops = [new GradientStop(Blue, 1), new GradientStop(Red, 0), new GradientStop(Green, 0.5)] };

        // Act
        var resolved = ResolveOnly(layer);

        // Assert
        Assert.That(resolved.Stops!.Colors, Is.EqualTo(new[] { SKColor.Parse(Red), SKColor.Parse(Green), SKColor.Parse(Blue) }));
    }

    [Test]
    public void Resolve_GradientWithoutStops_GoesFromColorToToColor()
    {
        // Arrange
        var layer = new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = Red, ToColor = Blue };

        // Act
        var resolved = ResolveOnly(layer);

        // Assert
        Assert.That(resolved.Stops!.Colors, Is.EqualTo(new[] { SKColor.Parse(Red), SKColor.Parse(Blue) }));
        Assert.That(resolved.Stops.Positions, Is.EqualTo(new[] { 0f, 1f }));
    }

    [Test]
    public void Resolve_ColorBindingOnAGradientWithStops_FollowsTheLastStop()
    {
        // Arrange
        using var store = new VariableStore();
        store.Publish(CpuVariable, 100.0);
        var layer = RedGreenBlue() with { Binding = new BackgroundBinding(CpuVariable) { FromColor = Blue, ToColor = "#FFFFFF" } };

        // Act
        var resolved = ResolveOnly(layer, store);

        // Assert
        Assert.That(resolved.Stops!.Colors[^1], Is.EqualTo(SKColor.Parse("#FFFFFF")));
        Assert.That(resolved.Stops.Colors[0], Is.EqualTo(SKColor.Parse(Red)));
    }

    [Test]
    public void Same_TwoResolutionsOfTheSameGradient_AreEqual()
    {
        // Arrange
        var first = new[] { ResolveOnly(RedGreenBlue()) };
        var second = new[] { ResolveOnly(RedGreenBlue()) };

        // Act
        var same = BackgroundLayers.Same(first, second);

        // Assert
        Assert.That(same, Is.True);
    }

    [Test]
    public void Same_GradientsWithAnotherMiddleColor_AreDifferent()
    {
        // Arrange
        var other = RedGreenBlue() with { Stops = [new GradientStop(Red, 0), new GradientStop(Blue, 0.5), new GradientStop(Blue, 1)] };
        var first = new[] { ResolveOnly(RedGreenBlue()) };
        var second = new[] { ResolveOnly(other) };

        // Act
        var same = BackgroundLayers.Same(first, second);

        // Assert
        Assert.That(same, Is.False);
    }

    [Test]
    public void Render_LinearGradientWithThreeStops_ShowsTheMiddleColorOnTheMiddleKey()
    {
        // Arrange
        var layer = RedGreenBlue();

        // Act
        var middle = CenterPixelOf(RenderThreeKeys(layer)[1]);

        // Assert
        Assert.That(middle.Green, Is.GreaterThan(255 - ColorTolerance));
        Assert.That(middle.Red + middle.Blue, Is.LessThan(ColorTolerance * 2));
    }

    [Test]
    public void Render_LinearGradientWithThreeStops_StartsRedAndEndsBlue()
    {
        // Arrange
        var layer = RedGreenBlue();

        // Act
        var frames = RenderThreeKeys(layer);

        // Assert
        Assert.That(CenterPixelOf(frames[0]).Red, Is.GreaterThan(CenterPixelOf(frames[0]).Blue));
        Assert.That(CenterPixelOf(frames[2]).Blue, Is.GreaterThan(CenterPixelOf(frames[2]).Red));
    }

    [Test]
    public void Render_RadialGradient_IsNearerTheFirstColorAtTheCenterKeyThanAtTheOuterKeys()
    {
        // Arrange
        var layer = new BackgroundLayer(BackgroundLayerKinds.Radial) { Color = Red, ToColor = Blue, CenterX = 0.5, CenterY = 0.5, Radius = 1 };

        // Act
        var frames = RenderThreeKeys(layer);

        // Assert
        Assert.That(CenterPixelOf(frames[1]).Red, Is.GreaterThan(CenterPixelOf(frames[0]).Red));
        Assert.That(CenterPixelOf(frames[1]).Red, Is.GreaterThan(CenterPixelOf(frames[2]).Red));
    }

    [Test]
    public void ToLayer_GradientRowWithTwoColorsAtTheEnds_KeepsTheOldTwoColorForm()
    {
        // Arrange
        var rows = new BackgroundLayersViewModel([new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = Red, ToColor = Blue, Angle = 45 }], _ => { });

        // Act
        var layer = rows.Layers.Single();

        // Assert
        Assert.That(layer.Stops, Is.Null);
        Assert.That((layer.Color, layer.ToColor), Is.EqualTo((Red, Blue)));
    }

    [Test]
    public void AddStop_GradientRow_SavesThreeStopsAndKeepsFirstAndLastInTheOldFields()
    {
        // Arrange
        var rows = new BackgroundLayersViewModel([new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = Red, ToColor = Blue, Angle = 45 }], _ => { });

        // Act
        rows.Rows[0].AddStop();

        // Assert
        var layer = rows.Layers.Single();
        Assert.That(layer.Stops, Has.Count.EqualTo(3));
        Assert.That((layer.Color, layer.ToColor), Is.EqualTo((Red, Blue)));
    }

    [Test]
    public void MoveStop_TwoColors_TradePlacesAndKeepTheirPositions()
    {
        // Arrange
        var rows = new BackgroundLayersViewModel([RedGreenBlue()], _ => { });
        var row = rows.Rows[0];

        // Act
        row.Stops[0].MoveDown();

        // Assert
        Assert.That(row.Stops.Select(static stop => (stop.Color, stop.PositionPercent)), Is.EqualTo(new[] { (Green, 0d), (Red, 50d), (Blue, 100d) }));
    }

    [Test]
    public void RemoveStop_GradientWithThreeStops_GoesBackToTwo()
    {
        // Arrange
        var rows = new BackgroundLayersViewModel([RedGreenBlue()], _ => { });
        var row = rows.Rows[0];

        // Act
        row.Stops[1].Remove();

        // Assert
        Assert.That(row.Stops, Has.Count.EqualTo(2));
    }

    [Test]
    public void RemoveStop_GradientWithTwoStops_KeepsBoth()
    {
        // Arrange
        var rows = new BackgroundLayersViewModel([new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = Red, ToColor = Blue }], _ => { });
        var row = rows.Rows[0];

        // Act
        row.Stops[0].Remove();

        // Assert
        Assert.That(row.Stops, Has.Count.EqualTo(2));
    }

    [Test]
    public void ToLayer_RadialRow_SavesItsCenterAndSize()
    {
        // Arrange
        var rows = new BackgroundLayersViewModel([new BackgroundLayer(BackgroundLayerKinds.Radial) { Color = Red, ToColor = Blue, CenterX = 0.25, CenterY = 0.75, Radius = 0.5 }], _ => { });

        // Act
        var layer = rows.Layers.Single();

        // Assert
        Assert.That((layer.CenterX, layer.CenterY, layer.Radius), Is.EqualTo((0.25, 0.75, 0.5)));
    }
}
