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

    [Test]
    public void The_older_color_and_image_fields_are_the_bottom_layers()
    {
        var background = new ProfileBackground("#112233", "C:\\pic.png") { Layers = [new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = "#FF0000", ToColor = "#0000FF" }] };

        var layers = BackgroundLayers.Effective(background);

        Assert.That(layers.Select(static layer => layer.Kind), Is.EqualTo(new[] { BackgroundLayerKinds.Solid, BackgroundLayerKinds.Image, BackgroundLayerKinds.Gradient }));
        Assert.That(BackgroundLayers.Effective(null), Is.Empty);
        Assert.That(new ProfileBackground().IsEmpty, Is.True);
        Assert.That(new ProfileBackground { Layers = [] }.IsEmpty, Is.True);
        Assert.That(new ProfileBackground { Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid)] }.IsEmpty, Is.False);
    }

    [Test]
    public async Task Layers_are_drawn_bottom_to_top_with_their_opacity_and_hidden_or_unknown_ones_are_skipped()
    {
        var (host, store, frames) = await Start(new ProfileBackground("#0000FF")
        {
            Layers =
            [
                new BackgroundLayer(BackgroundLayerKinds.Solid, Opacity: 0.5) { Color = "#FF0000" },
                new BackgroundLayer(BackgroundLayerKinds.Solid, Visible: false) { Color = "#00FF00" },
                new BackgroundLayer("hologram") { Color = "#FFFFFF" },
            ],
        });
        await using var _ = host;
        using var __ = store;

        var pixel = Corner(frames["key.0"]);
        Assert.That(pixel[0], Is.InRange(120, 135), "half red over blue");
        Assert.That(pixel[1], Is.EqualTo(0), "the hidden green layer is not drawn");
        Assert.That(pixel[2], Is.InRange(120, 135), "half of the blue is left");
        Assert.That(pixel[3], Is.EqualTo(255));
    }

    [Test]
    public async Task A_gradient_runs_across_the_whole_layout_so_neighbouring_keys_continue_each_other()
    {
        var (host, store, frames) = await Start(new ProfileBackground { Layers = [new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = "#FF0000", ToColor = "#0000FF", Angle = 0 }] });
        await using var _ = host;
        using var __ = store;

        var left = Corner(frames["key.0"]);
        var right = frames["key.1"].Rgba[^4..];
        Assert.That(left[0], Is.GreaterThan(right[0]), "red fades from the left key to the right one");
        Assert.That(right[2], Is.GreaterThan(left[2]), "and blue grows");
        Assert.That(left[0] + left[2], Is.InRange(230, 275), "a mix of the two ends");
    }

    [TestCase(0.0, 255, 0)]
    [TestCase(100.0, 0, 255)]
    [TestCase(50.0, 128, 128)]
    public void A_color_binding_blends_between_its_two_colors_by_the_variables_value(double value, int red, int blue)
    {
        using var store = new VariableStore();
        store.Publish("system.cpu", value);
        var background = new ProfileBackground
        {
            Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = "#000000", Binding = new BackgroundBinding("system.cpu") { FromColor = "#FF0000", ToColor = "#0000FF" } }],
        };

        var layer = BackgroundLayers.Resolve(background, store).Single();

        Assert.That((int)layer.Color.Red, Is.InRange(red - 1, red + 1));
        Assert.That((int)layer.Color.Blue, Is.InRange(blue - 1, blue + 1));
    }

    [Test]
    public void Opacity_and_visibility_bindings_follow_the_value_and_unreadable_values_use_the_fallback()
    {
        using var store = new VariableStore();
        ProfileBackground Of(BackgroundBinding binding) => new() { Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid, Opacity: 0.8) { Color = "#FFFFFF", Binding = binding }] };

        store.Publish("vars.level", "50");
        Assert.That(BackgroundLayers.Resolve(Of(new BackgroundBinding("vars.level", BackgroundBindingTargets.Opacity)), store).Single().Opacity, Is.EqualTo(0.4).Within(0.001), "half of the layer's own 0.8");

        store.Publish("vars.on", "true");
        Assert.That(BackgroundLayers.Resolve(Of(new BackgroundBinding("vars.on", BackgroundBindingTargets.Visible, Min: 1)), store), Has.Count.EqualTo(1));
        store.Publish("vars.on", "false");
        Assert.That(BackgroundLayers.Resolve(Of(new BackgroundBinding("vars.on", BackgroundBindingTargets.Visible, Min: 1)), store), Is.Empty);

        store.Publish("vars.text", "not a number");
        Assert.That(BackgroundLayers.Resolve(Of(new BackgroundBinding("vars.text", BackgroundBindingTargets.Opacity) { Fallback = 100 }), store).Single().Opacity, Is.EqualTo(0.8).Within(0.001), "unreadable: the fallback (the top of the range)");
        Assert.That(BackgroundLayers.Resolve(Of(new BackgroundBinding("vars.missing", BackgroundBindingTargets.Opacity)), store), Is.Empty, "unset and no fallback: 0, so nothing shows");
        Assert.That(BackgroundLayers.BoundVariables(Of(new BackgroundBinding(" vars.level "))), Is.EquivalentTo(new[] { "vars.level" }));
    }

    private const string BoundVariable = "vars.level";
    private const string LayerColor = "#FFFFFF";

    private static ProfileBackground WithLayer(BackgroundLayer layer) => new() { Layers = [layer] };

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
        var background = WithLayer(new BackgroundLayer("hologram") { Binding = new BackgroundBinding(BoundVariable) });

        // Act
        var variables = BackgroundLayers.BoundVariables(background);

        // Assert
        Assert.That(variables, Is.Empty);
    }

    [Test]
    public async Task A_layer_that_follows_a_variable_redraws_the_device_when_the_variable_changes()
    {
        var (host, store, frames) = await Start(new ProfileBackground
        {
            Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = "#000000", Binding = new BackgroundBinding("system.cpu") { FromColor = "#000000", ToColor = "#FF0000" } }],
        });
        await using var _ = host;
        using var __ = store;
        Assert.That(Corner(frames["key.0"])[0], Is.EqualTo(0), "unset: the low end");

        store.Publish("system.cpu", 100.0);
        var end = DateTime.UtcNow.AddSeconds(5);
        while (Corner(frames["key.0"])[0] != 255 && DateTime.UtcNow < end)
        {
            await Task.Delay(20);
            await host.WhenIdleAsync();
        }

        Assert.That(Corner(frames["key.0"])[0], Is.EqualTo(255), "the key went red without any other change");
    }

    [Test]
    public void The_device_backdrop_uses_the_steady_layers_only()
    {
        var plain = ProfileBackdrop.GetColor(new ProfileBackground("#336699"));
        var layered = ProfileBackdrop.GetColor(new ProfileBackground
        {
            Layers =
            [
                new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = "#336699" },
                new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = "#FF0000", Binding = new BackgroundBinding("system.cpu") },
                new BackgroundLayer(BackgroundLayerKinds.Image) { ImagePath = "C:\\pic.png" },
            ],
        });
        var gradient = ProfileBackdrop.GetColor(new ProfileBackground { Layers = [new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = "#000000", ToColor = "#FFFFFF" }] });

        Assert.That(layered, Is.EqualTo(plain), "a layer that follows a variable and an image don't change the backdrop");
        Assert.That(gradient.Red, Is.InRange(120, 135), "a gradient counts as the color half way along it");
    }

    [Test]
    public void Layers_survive_a_save_with_unknown_kinds_and_old_files_without_layers_still_load()
    {
        var background = new ProfileBackground("#101010")
        {
            Layers =
            [
                new BackgroundLayer(BackgroundLayerKinds.Gradient, Opacity: 0.7) { Color = "#FF0000", ToColor = "#0000FF", Angle = 45 },
                new BackgroundLayer("hologram") { Color = "#FFFFFF", Binding = new BackgroundBinding("vars.x", BackgroundBindingTargets.Visible, 1, 5) { Fallback = 2 } },
            ],
        };

        var json = JsonSerializer.Serialize(background, Options);
        var back = JsonSerializer.Deserialize<ProfileBackground>(json, Options)!;

        Assert.That(back.Layers!.Select(static layer => layer.Kind), Is.EqualTo(new[] { "gradient", "hologram" }));
        Assert.That(back.Layers[1].Binding, Is.EqualTo(background.Layers![1].Binding));
        Assert.That(back.Layers[0].Angle, Is.EqualTo(45));
        Assert.That(JsonSerializer.Serialize(new ProfileBackground("#101010"), Options), Does.Not.Contain("layers"), "nothing is written for a background without layers");
        Assert.That(JsonSerializer.Deserialize<ProfileBackground>("{\"color\":\"#ABCDEF\"}", Options)!.Layers, Is.Null);
    }
}
