using DeckHost.App.ViewModels;
using DeckHost.Contracts.Models;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>The Studio preview redraws when the background or a value it follows changes, and not for anything else.</summary>
public sealed class BackgroundPreviewViewModelTests
{
    private const string Red = "#FF0000";
    private const string Blue = "#0000FF";
    private const string Black = "#000000";
    private const string CpuVariable = "system.cpu";
    private const string UnrelatedVariable = "vars.unrelated";
    private const double FullLoad = 100.0;
    private static readonly byte[] RedPixel = [0xFF, 0x00, 0x00, 0xFF];
    private static readonly byte[] BluePixel = [0x00, 0x00, 0xFF, 0xFF];

    private static DeviceDefinition Device() => new FakeDevicePlugin().Devices.Single();

    private static ProfileBackground Solid(string color) => new() { Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = color }] };

    private static ProfileBackground FollowingCpu() => new()
    {
        Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = Black, Binding = new BackgroundBinding(CpuVariable) { FromColor = Black, ToColor = Red } }],
    };

    private static byte[] FirstPixel(BackgroundPreviewViewModel preview) => preview.Tiles[0].Frame.Rgba[..4];

    [Test]
    public void Invalidate_BackgroundChanged_RendersTheNewColor()
    {
        // Arrange
        var background = Solid(Red);
        using var store = new VariableStore();
        using var preview = new BackgroundPreviewViewModel(store, Device, () => background, renderInline: true);
        background = Solid(Blue);

        // Act
        preview.Invalidate();

        // Assert
        Assert.That(FirstPixel(preview), Is.EqualTo(BluePixel));
    }

    [Test]
    public void Publish_VariableALayerFollows_RendersTheTilesAgain()
    {
        // Arrange
        using var store = new VariableStore();
        using var preview = new BackgroundPreviewViewModel(store, Device, FollowingCpu, renderInline: true);

        // Act
        store.Publish(CpuVariable, FullLoad);

        // Assert
        Assert.That(FirstPixel(preview), Is.EqualTo(RedPixel));
    }

    [Test]
    public void Publish_VariableNoLayerFollows_KeepsTheSameTiles()
    {
        // Arrange
        using var store = new VariableStore();
        using var preview = new BackgroundPreviewViewModel(store, Device, FollowingCpu, renderInline: true);
        var before = preview.Tiles;

        // Act
        store.Publish(UnrelatedVariable, FullLoad);

        // Assert
        Assert.That(preview.Tiles, Is.SameAs(before));
    }

    [Test]
    public void Constructor_NoDevice_HasNoTilesAndNoDevice()
    {
        // Arrange
        using var store = new VariableStore();

        // Act
        using var preview = new BackgroundPreviewViewModel(store, () => null, () => Solid(Red), renderInline: true);

        // Assert
        Assert.That(preview.Tiles, Is.Empty);
        Assert.That(preview.HasDevice, Is.False);
    }

    [Test]
    public void Constructor_Device_HasATilePerDisplay()
    {
        // Arrange
        using var store = new VariableStore();

        // Act
        using var preview = new BackgroundPreviewViewModel(store, Device, () => Solid(Red), renderInline: true);

        // Assert
        Assert.That(preview.Tiles, Has.Count.EqualTo(2));
        Assert.That(preview.HasDevice, Is.True);
    }

    [Test]
    public void Publish_VariableALayerFollowsAfterDispose_KeepsTheSameTiles()
    {
        // Arrange
        using var store = new VariableStore();
        var preview = new BackgroundPreviewViewModel(store, Device, FollowingCpu, renderInline: true);
        var before = preview.Tiles;
        preview.Dispose();

        // Act
        store.Publish(CpuVariable, FullLoad);

        // Assert
        Assert.That(preview.Tiles, Is.SameAs(before));
    }
}
