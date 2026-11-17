using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.Rendering;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>Profile backgrounds as layers: solid, gradient, image, and layers that follow a variable.</summary>
public sealed class BackgroundLayerTests
{
    private const string BoundVariable = "vars.level";
    private const string CpuVariable = "system.cpu";
    private const string LayerColor = "#FFFFFF";
    private const string Black = "#000000";
    private const string Red = "#FF0000";
    private const string Green = "#00FF00";
    private const string Blue = "#0000FF";
    private const string UnknownKind = "hologram";
    private const string Pic = "C:\\pic.png";
    private const double HalfOpacity = 0.5;
    private const int ColorTolerance = 1;
    private const double Tolerance = 0.001;
    private const int GradientAngle = 45;
    private const int RedPixelLow = 120;
    private const int RedPixelHigh = 135;
    private const int OpaqueAlpha = 255;
    private const int WaitSeconds = 5;
    private const int PollMilliseconds = 20;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task<(DeviceHostService Host, VariableStore Store, ConcurrentDictionary<string, ControlFrame> Frames)> Start(ProfileBackground background)
    {
        var store = new VariableStore();
        var host = new DeviceHostService([new FakeDevicePlugin()], [new TestApplicationPlugin("p", new RecordingAction("a"))], store);
        var frames = new ConcurrentDictionary<string, ControlFrame>();
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        host.SetProfiles([new DeviceProfile("one", "One", FakeDevicePlugin.DeviceId, [], Background: background)]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        return (host, store, frames);
    }

    private static byte[] Corner(ControlFrame frame) => frame.Rgba[..4];

    private static ProfileBackground WithLayer(BackgroundLayer layer) => new() { Layers = [layer] };

    private static ProfileBackground OpaqueSolidFollowing(BackgroundBinding binding) =>
        WithLayer(new BackgroundLayer(BackgroundLayerKinds.Solid, Opacity: 0.8) { Color = LayerColor, Binding = binding });

    // ---------------------------------------------------------------- which layers there are

    [Test]
    public void Effective_OlderColorAndImageFields_AreTheBottomLayers()
    {
        // Arrange
        var background = new ProfileBackground("#112233", Pic) { Layers = [new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = Red, ToColor = Blue }] };

        // Act
        var layers = BackgroundLayers.Effective(background);

        // Assert
        Assert.That(layers.Select(static layer => layer.Kind), Is.EqualTo(new[] { BackgroundLayerKinds.Solid, BackgroundLayerKinds.Image, BackgroundLayerKinds.Gradient }));
    }

    [Test]
    public void Effective_NoBackground_HasNoLayers()
    {
        // Act
        var layers = BackgroundLayers.Effective(null);

        // Assert
        Assert.That(layers, Is.Empty);
    }

    [Test]
    public void IsEmpty_WithNothingSetOrOnlyEmptyLayers_IsTrue()
    {
        // Act
        var plain = new ProfileBackground().IsEmpty;
        var emptyLayers = new ProfileBackground { Layers = [] }.IsEmpty;

        // Assert
        Assert.That(plain, Is.True);
        Assert.That(emptyLayers, Is.True);
    }

    [Test]
    public void IsEmpty_WithALayer_IsFalse()
    {
        // Act
        var isEmpty = new ProfileBackground { Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid)] }.IsEmpty;

        // Assert
        Assert.That(isEmpty, Is.False);
    }

    // ---------------------------------------------------------------- what is drawn

    [Test]
    public async Task Render_LayersWithOpacityAndHiddenAndUnknownOnes_DrawsBottomToTopSkippingTheOthers()
    {
        // Arrange
        var (host, store, frames) = await Start(new ProfileBackground(Blue)
        {
            Layers =
            [
                new BackgroundLayer(BackgroundLayerKinds.Solid, Opacity: HalfOpacity) { Color = Red },
                new BackgroundLayer(BackgroundLayerKinds.Solid, Visible: false) { Color = Green },
                new BackgroundLayer(UnknownKind) { Color = LayerColor },
            ],
        });
        await using var _ = host;
        using var __ = store;

        // Act
        var pixel = Corner(frames["key.0"]);

        // Assert
        Assert.That(pixel[0], Is.InRange(RedPixelLow, RedPixelHigh), "half red over blue");
        Assert.That(pixel[1], Is.EqualTo(0), "the hidden green layer is not drawn");
        Assert.That(pixel[2], Is.InRange(RedPixelLow, RedPixelHigh), "half of the blue is left");
        Assert.That(pixel[3], Is.EqualTo(OpaqueAlpha));
    }

    [Test]
    public async Task Render_AGradient_RunsAcrossTheWholeLayoutSoNeighbouringKeysContinueEachOther()
    {
        // Arrange
        var (host, store, frames) = await Start(WithLayer(new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = Red, ToColor = Blue, Angle = 0 }));
        await using var _ = host;
        using var __ = store;

        // Act
        var left = Corner(frames["key.0"]);
        var right = frames["key.1"].Rgba[^4..];

        // Assert
        Assert.That(left[0], Is.GreaterThan(right[0]), "red fades from the left key to the right one");
        Assert.That(right[2], Is.GreaterThan(left[2]), "and blue grows");
        Assert.That(left[0] + left[2], Is.InRange(230, 275), "a mix of the two ends");
    }

    // ---------------------------------------------------------------- bindings

    [TestCase(0.0, 255, 0)]
    [TestCase(100.0, 0, 255)]
    [TestCase(50.0, 128, 128)]
    public void Resolve_AColorBinding_BlendsBetweenItsTwoColorsByTheValue(double value, int red, int blue)
    {
        // Arrange
        using var store = new VariableStore();
        store.Publish(CpuVariable, value);
        var background = WithLayer(new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = Black, Binding = new BackgroundBinding(CpuVariable) { FromColor = Red, ToColor = Blue } });

        // Act
        var layer = BackgroundLayers.Resolve(background, store).Single();

        // Assert
        Assert.That((int)layer.Color.Red, Is.InRange(red - ColorTolerance, red + ColorTolerance));
        Assert.That((int)layer.Color.Blue, Is.InRange(blue - ColorTolerance, blue + ColorTolerance));
    }

    [Test]
    public void Resolve_AnOpacityBinding_ScalesTheLayersOwnOpacity()
    {
        // Arrange
        using var store = new VariableStore();
        store.Publish(BoundVariable, "50");

        // Act
        var layer = BackgroundLayers.Resolve(OpaqueSolidFollowing(new BackgroundBinding(BoundVariable, BackgroundBindingTargets.Opacity)), store).Single();

        // Assert
        Assert.That(layer.Opacity, Is.EqualTo(0.4).Within(Tolerance), "half of the layer's own 0.8");
    }

    [Test]
    public void Resolve_AVisibilityBindingWhileTheVariableIsTrue_KeepsTheLayer()
    {
        // Arrange
        using var store = new VariableStore();
        store.Publish("vars.on", "true");

        // Act
        var layers = BackgroundLayers.Resolve(OpaqueSolidFollowing(new BackgroundBinding("vars.on", BackgroundBindingTargets.Visible, Min: 1)), store);

        // Assert
        Assert.That(layers, Has.Count.EqualTo(1));
    }

    [Test]
    public void Resolve_AVisibilityBindingWhileTheVariableIsFalse_DropsTheLayer()
    {
        // Arrange
        using var store = new VariableStore();
        store.Publish("vars.on", "false");

        // Act
        var layers = BackgroundLayers.Resolve(OpaqueSolidFollowing(new BackgroundBinding("vars.on", BackgroundBindingTargets.Visible, Min: 1)), store);

        // Assert
        Assert.That(layers, Is.Empty);
    }

    [Test]
    public void Resolve_AnUnreadableValue_UsesTheBindingsFallback()
    {
        // Arrange
        using var store = new VariableStore();
        store.Publish("vars.text", "not a number");

        // Act
        var layer = BackgroundLayers.Resolve(OpaqueSolidFollowing(new BackgroundBinding("vars.text", BackgroundBindingTargets.Opacity) { Fallback = 100 }), store).Single();

        // Assert
        Assert.That(layer.Opacity, Is.EqualTo(0.8).Within(Tolerance), "the fallback is the top of the range");
    }

    [Test]
    public void Resolve_AnUnsetVariableWithoutAFallback_ShowsNothing()
    {
        // Arrange
        using var store = new VariableStore();

        // Act
        var layers = BackgroundLayers.Resolve(OpaqueSolidFollowing(new BackgroundBinding("vars.missing", BackgroundBindingTargets.Opacity)), store);

        // Assert
        Assert.That(layers, Is.Empty, "0, so nothing shows");
    }

    [Test]
    public void BoundVariables_AVariableWithSpaces_IsListedTrimmed()
    {
        // Arrange
        var background = OpaqueSolidFollowing(new BackgroundBinding(" vars.level "));

        // Act
        var variables = BackgroundLayers.BoundVariables(background);

        // Assert
        Assert.That(variables, Is.EquivalentTo(new[] { BoundVariable }));
    }

    [Test]
    public void BoundVariables_HiddenBoundLayer_IsEmpty()
    {
        // Arrange
        var background = WithLayer(new BackgroundLayer(BackgroundLayerKinds.Solid, Visible: false) { Color = LayerColor, Binding = new BackgroundBinding(BoundVariable) });

        // Act
        var variables = BackgroundLayers.BoundVariables(background);

        // Assert
        Assert.That(variables, Is.Empty);
    }

    [Test]
    public void BoundVariables_BoundLayerWithoutOpacity_IsEmpty()
    {
        // Arrange
        var background = WithLayer(new BackgroundLayer(BackgroundLayerKinds.Solid, Opacity: 0) { Color = LayerColor, Binding = new BackgroundBinding(BoundVariable) });

        // Act
        var variables = BackgroundLayers.BoundVariables(background);

        // Assert
        Assert.That(variables, Is.Empty);
    }

    [Test]
    public void BoundVariables_BoundLayerOfAnUnknownKind_IsEmpty()
    {
        // Arrange
        var background = WithLayer(new BackgroundLayer(UnknownKind) { Binding = new BackgroundBinding(BoundVariable) });

        // Act
        var variables = BackgroundLayers.BoundVariables(background);

        // Assert
        Assert.That(variables, Is.Empty);
    }

    [Test]
    public async Task Render_ALayerThatFollowsAVariable_RedrawsTheDeviceWhenTheVariableChanges()
    {
        // Arrange
        var (host, store, frames) = await Start(WithLayer(new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = Black, Binding = new BackgroundBinding(CpuVariable) { FromColor = Black, ToColor = Red } }));
        await using var _ = host;
        using var __ = store;
        var before = Corner(frames["key.0"])[0];

        // Act
        store.Publish(CpuVariable, 100.0);
        var end = DateTime.UtcNow.AddSeconds(WaitSeconds);
        while (Corner(frames["key.0"])[0] != OpaqueAlpha && DateTime.UtcNow < end)
        {
            await Task.Delay(PollMilliseconds);
            await host.WhenIdleAsync();
        }

        // Assert
        Assert.That(before, Is.EqualTo(0), "unset: the low end");
        Assert.That(Corner(frames["key.0"])[0], Is.EqualTo(OpaqueAlpha), "the key went red without any other change");
    }

    // ---------------------------------------------------------------- the device backdrop

    [Test]
    public void GetColor_LayersThatFollowAVariableOrShowAnImage_GiveTheSameBackdropAsThePlainColor()
    {
        // Arrange
        var plain = ProfileBackdrop.GetColor(new ProfileBackground("#336699"));
        var layered = new ProfileBackground
        {
            Layers =
            [
                new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = "#336699" },
                new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = Red, Binding = new BackgroundBinding(CpuVariable) },
                new BackgroundLayer(BackgroundLayerKinds.Image) { ImagePath = Pic },
            ],
        };

        // Act
        var backdrop = ProfileBackdrop.GetColor(layered);

        // Assert
        Assert.That(backdrop, Is.EqualTo(plain), "a layer that follows a variable and an image don't change the backdrop");
    }

    [Test]
    public void GetColor_AGradient_IsTheColorHalfWayAlongIt()
    {
        // Arrange
        var background = WithLayer(new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = Black, ToColor = LayerColor });

        // Act
        var gradient = ProfileBackdrop.GetColor(background);

        // Assert
        Assert.That(gradient.Red, Is.InRange(RedPixelLow, RedPixelHigh));
    }

    // ---------------------------------------------------------------- saving

    [Test]
    public void Serialize_LayersWithAnUnknownKind_SurviveASave()
    {
        // Arrange
        var background = new ProfileBackground("#101010")
        {
            Layers =
            [
                new BackgroundLayer(BackgroundLayerKinds.Gradient, Opacity: 0.7) { Color = Red, ToColor = Blue, Angle = GradientAngle },
                new BackgroundLayer(UnknownKind) { Color = LayerColor, Binding = new BackgroundBinding("vars.x", BackgroundBindingTargets.Visible, 1, 5) { Fallback = 2 } },
            ],
        };

        // Act
        var back = JsonSerializer.Deserialize<ProfileBackground>(JsonSerializer.Serialize(background, Options), Options)!;

        // Assert
        Assert.That(back.Layers!.Select(static layer => layer.Kind), Is.EqualTo(new[] { "gradient", UnknownKind }));
        Assert.That(back.Layers[1].Binding, Is.EqualTo(background.Layers![1].Binding));
        Assert.That(back.Layers[0].Angle, Is.EqualTo(GradientAngle));
    }

    [Test]
    public void Serialize_ABackgroundWithoutLayers_WritesNoLayers()
    {
        // Act
        var json = JsonSerializer.Serialize(new ProfileBackground("#101010"), Options);

        // Assert
        Assert.That(json, Does.Not.Contain("layers"));
    }

    [Test]
    public void Deserialize_AnOldFileWithoutLayers_StillLoads()
    {
        // Act
        var background = JsonSerializer.Deserialize<ProfileBackground>("{\"color\":\"#ABCDEF\"}", Options)!;

        // Assert
        Assert.That(background.Layers, Is.Null);
    }
}
