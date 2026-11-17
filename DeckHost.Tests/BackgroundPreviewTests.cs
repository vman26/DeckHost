using System.Collections.Concurrent;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.Rendering;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>The Studio preview draws a background exactly as the host draws it behind a control's content.</summary>
public sealed class BackgroundPreviewTests
{
    private const string Red = "#FF0000";
    private const string Blue = "#0000FF";
    private const string Black = "#000000";
    private const string CpuVariable = "system.cpu";
    private const string FirstKey = "key.0";
    private const string SecondKey = "key.1";
    private const double FullLoad = 100.0;
    private const double Tolerance = 2;
    private const int HalfOpaqueBlend = 128;
    private const double HalfOpacity = 0.5;
    private static readonly byte[] BaseColor = [0x0B, 0x12, 0x20, 0xFF];
    private static readonly byte[] RedPixel = [0xFF, 0x00, 0x00, 0xFF];

    private static DeviceDefinition DeviceWithTwoKeysAndAKnob() => new FakeDevicePlugin().Devices.Single();

    private static ProfileBackground SolidBackground(string color) => new() { Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = color }] };

    private static ProfileBackground GradientBackground() => new()
    {
        Layers = [new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = Red, ToColor = Blue, Angle = 45 }],
    };

    private static IReadOnlyList<ControlFrame> Render(DeviceDefinition device, ProfileBackground? background, IVariableStore? store = null)
    {
        using var resources = new SkiaDrawingResources();
        return BackgroundPreview.Render(device, background, store ?? new VariableStore(), resources);
    }

    private static byte[] Corner(ControlFrame frame) => frame.Rgba[..4];

    private static async Task<ControlFrame> HostFrameAsync(ProfileBackground background, string controlId)
    {
        var frames = new ConcurrentDictionary<string, ControlFrame>();
        using var store = new VariableStore();
        await using var host = new DeviceHostService([new FakeDevicePlugin()], [new TestApplicationPlugin("p", new RecordingAction("a"))], store);
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        host.SetProfiles([new DeviceProfile("one", "One", FakeDevicePlugin.DeviceId, [], Background: background)]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        return frames[controlId];
    }

    [Test]
    public void Render_SolidLayer_FillsEveryDisplayWithTheLayerColor()
    {
        // Arrange
        var device = DeviceWithTwoKeysAndAKnob();

        // Act
        var frames = Render(device, SolidBackground(Red));

        // Assert
        Assert.That(frames.Select(Corner), Is.All.EqualTo(RedPixel));
    }

    [Test]
    public void Render_DeviceWithAKnob_ReturnsFramesOnlyForControlsWithDisplays()
    {
        // Arrange
        var device = DeviceWithTwoKeysAndAKnob();

        // Act
        var frames = Render(device, SolidBackground(Red));

        // Assert
        Assert.That(frames.Select(static frame => frame.ControlId), Is.EqualTo(new[] { FirstKey, SecondKey }));
    }

    [Test]
    public async Task Render_GradientBackground_MatchesTheFrameTheHostDrawsByteForByte()
    {
        // Arrange
        var device = DeviceWithTwoKeysAndAKnob();
        var hostFrame = await HostFrameAsync(GradientBackground(), SecondKey);

        // Act
        var previewFrame = Render(device, GradientBackground()).Single(frame => frame.ControlId == SecondKey);

        // Assert
        Assert.That(previewFrame.Rgba, Is.EqualTo(hostFrame.Rgba));
    }

    [Test]
    public void Render_ColorLayerFollowingAVariable_UsesTheVariablesCurrentValue()
    {
        // Arrange
        var device = DeviceWithTwoKeysAndAKnob();
        using var store = new VariableStore();
        store.Publish(CpuVariable, FullLoad);
        var background = new ProfileBackground
        {
            Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = Black, Binding = new BackgroundBinding(CpuVariable) { FromColor = Black, ToColor = Red } }],
        };

        // Act
        var frames = Render(device, background, store);

        // Assert
        Assert.That(Corner(frames[0]), Is.EqualTo(RedPixel));
    }

    [Test]
    public void Render_NoBackground_FillsTheBaseColor()
    {
        // Arrange
        var device = DeviceWithTwoKeysAndAKnob();

        // Act
        var frames = Render(device, null);

        // Assert
        Assert.That(Corner(frames[0]), Is.EqualTo(BaseColor));
    }

    [Test]
    public void Render_HalfOpaqueLayerOverTheOlderColorField_BlendsBoth()
    {
        // Arrange
        var device = DeviceWithTwoKeysAndAKnob();
        var background = new ProfileBackground
        {
            Color = Blue,
            Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid, Opacity: HalfOpacity) { Color = Red }],
        };

        // Act
        var corner = Corner(Render(device, background)[0]);

        // Assert
        Assert.That(corner[0], Is.EqualTo(HalfOpaqueBlend).Within(Tolerance));
        Assert.That(corner[2], Is.EqualTo(HalfOpaqueBlend).Within(Tolerance));
    }
}
