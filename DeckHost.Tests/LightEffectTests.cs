using DeckHost.App.ViewModels;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.State;
using DeckHost.Tests;

namespace DeckHost.Tests;

public class LightEffectTests
{
    [TestCase(LightEffects.Pulse)]
    [TestCase(LightEffects.Blink)]
    [TestCase(LightEffects.ColorCycle)]
    [TestCase(LightEffects.Wave)]
    [TestCase(LightEffects.Chase)]
    [TestCase(LightEffects.Candle)]
    [TestCase(LightEffects.Sparkle)]
    public void Every_effect_moves_and_repeats_exactly(string effect)
    {
        var style = new LightStyle("FF8000", effect, 2);
        var samples = Enumerable.Range(0, 60).Select(i => LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(i * 0.07), phase: 1)).ToList();

        Assert.That(samples.Distinct().Count() > 3, Is.True, $"{effect} should change over time");
        Assert.That(LightEffects.IsAnimated(effect), Is.True);
        Assert.That(LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1.3), 2), Is.EqualTo(LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1.3), 2)));
    }

    [Test]
    public void Fades_are_smooth_steps_of_brightness()
    {
        // Sampled at 30 frames a second, breathing never jumps more than a small step.
        var style = new LightStyle("FFFFFF", LightEffects.Breathe, 3);
        var previous = LightEffectMath.ColorAt(style, TimeSpan.Zero).R;
        for (var frame = 1; frame < 90; frame++)
        {
            var current = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(frame / 30d)).R;
            Assert.That(Math.Abs(current - previous), Is.InRange(0, 16));
            previous = current;
        }
    }

    [Test]
    public void Color_cycle_fades_through_its_colors()
    {
        var style = new LightStyle("000000", LightEffects.ColorCycle, 3) { Colors = ["FF0000", "00FF00", "0000FF"], Spread = 0 };

        Assert.That(LightEffectMath.ColorAt(style, TimeSpan.Zero), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
        Assert.That(LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1)), Is.EqualTo(((byte)0, (byte)255, (byte)0)));
        var halfway = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(0.5));
        Assert.That(halfway.R is > 50 and < 205 && halfway.G is > 50 and < 205, Is.True, $"between red and green: {halfway}");
    }

    [Test]
    public void A_custom_effect_plays_its_steps()
    {
        var style = new LightStyle("000000", LightEffects.Custom)
        {
            Steps = [new LightStep("FF0000", FadeSeconds: 0, HoldSeconds: 1), new LightStep("0000FF", FadeSeconds: 1, HoldSeconds: 1)],
        };

        Assert.That(LightEffectMath.CustomLength(style), Is.EqualTo(3));
        Assert.That(LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(0.5)), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
        var fading = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1.5));
        Assert.That(fading.R is > 50 and < 205 && fading.B is > 50 and < 205, Is.True, $"fading red to blue: {fading}");
        Assert.That(LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(2.5)), Is.EqualTo(((byte)0, (byte)0, (byte)255)));
        Assert.That(LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(3.5)), Is.EqualTo(((byte)255, (byte)0, (byte)0))); // from the start
    }

    [Test]
    public void Chase_lights_one_led_at_a_time()
    {
        var style = new LightStyle("FFFFFF", LightEffects.Chase, 4);

        // A quarter into the cycle the head is on the second of four LEDs.
        var leds = Enumerable.Range(0, 4).Select(i => LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1.1), i, 4).Item1).ToList();
        Assert.That(leds.IndexOf(leds.Max()), Is.EqualTo(1));
    }

    [Test]
    public void Styles_with_the_same_lists_are_equal_and_saved_with_them()
    {
        var a = new LightStyle("FF0000", LightEffects.Custom) { Steps = [new LightStep("00FF00")], Spread = 0.1 };
        var b = new LightStyle("FF0000", LightEffects.Custom) { Steps = [new LightStep("00FF00")], Spread = 0.1 };
        Assert.That(b, Is.EqualTo(a));

        var json = System.Text.Json.JsonSerializer.Serialize(new RgbFeedback { IdleLight = a });
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<RgbFeedback>(json)!.IdleStyle, Is.EqualTo(a));
    }

    [Test]
    public void The_editor_composes_a_custom_effect()
    {
        var editor = new LightStyleEditorViewModel(null);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.SelectedEffect = editor.Effects.First(option => option.Value == LightEffects.Custom);
        Assert.That(editor.ShowSteps, Is.True);
        Assert.That(editor.Steps, Is.Not.Empty); // starts from a preset

        editor.SelectedPreset = LightStyleEditorViewModel.Presets.First(preset => preset.Name == "Traffic light");
        editor.Steps[0].HoldSeconds = 1;
        editor.AddStepCommand.Execute(null);
        editor.Steps[^1].Color = "#123456";
        editor.Steps[^1].MoveUpCommand.Execute(null);

        var style = editor.ToStyle()!;
        Assert.That(style.Effect, Is.EqualTo(LightEffects.Custom));
        Assert.That(style.Steps!.Select(step => step.Color), Is.EqualTo(new[] { "FF0000", "FFA500", "123456", "00FF00" }));
        Assert.That(style.Steps![0].HoldSeconds, Is.EqualTo(1));
        Assert.That(changes >= 4, Is.True);
    }

    [Test]
    public void Off_in_device_settings_means_no_light_and_off_on_a_knob_means_dark()
    {
        Assert.That(new LightStyleEditorViewModel(null, KnobLightsViewModel.Off).ToStyle(), Is.Null);
        Assert.That(new LightStyleEditorViewModel(new LightStyle(Effect: LightEffects.Off)).ToStyle()!.Effect, Is.EqualTo(LightEffects.Off));
    }

    [Test]
    public async Task Off_is_sent_again_after_the_device_resets_its_lights()
    {
        var device = new HostLightTests.LightDevice();
        var store = new VariableStore();
        await using var host = new DeviceHostService([device], [], store);
        host.SetProfiles([new DeviceProfile("profile", "Profile", HostLightTests.LightDevice.DeviceId, [])]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        host.SetDeviceLight(HostLightTests.LightDevice.DeviceId, null);
        await host.WhenIdleAsync();
        device.Calls.Clear();

        // The device plugin wrote its configuration, which put its own lighting back on.
        device.Context!.RequestRedraw(HostLightTests.LightDevice.DeviceId);
        await host.WhenIdleAsync();

        Assert.That(device.Calls.Any(call => call == "batch:encoder.0:0,0,0 encoder.1:0,0,0"), Is.True);
        store.Dispose();
    }

    [Test]
    public async Task Each_led_can_have_its_own_light()
    {
        var device = new HostLightTests.LightDevice();
        var store = new VariableStore();
        await using var host = new DeviceHostService([device], [], store);
        host.SetProfiles([new DeviceProfile("profile", "Profile", HostLightTests.LightDevice.DeviceId, [])]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        device.Calls.Clear();

        host.SetDeviceLight(HostLightTests.LightDevice.DeviceId, new LightStyle("00FF00") { Leds = [new LightStyle("FF0000"), null] });
        await host.WhenIdleAsync();

        // The first LED its own red; the second (no style of its own) the shared green.
        Assert.That(device.Calls.Any(call => call == "batch:encoder.0:255,0,0 encoder.1:0,255,0"), Is.True);
        store.Dispose();
    }

    [Test]
    public void The_knob_lights_card_composes_each_led()
    {
        var saved = new Dictionary<string, LightStyle?>();
        var settings = new LightStore(saved);
        var card = new KnobLightsViewModel(settings, "dev", ["Knob 1", "Knob 2"]);

        card.EachLedOwn = true;
        card.Leds[0].Editor.SelectedEffect = card.Leds[0].Editor.Effects.First(option => option.Value == LightEffects.Breathe);
        card.Leds[1].Editor.SelectedEffect = card.Leds[1].Editor.Effects.First(option => option.Value == LightEffects.Off);

        var style = saved["dev"]!;
        Assert.That(style.Leds!.Select(led => led!.Effect), Is.EqualTo(new[] { LightEffects.Breathe, LightEffects.Off }));

        // Opened again: the card shows each LED's own light.
        var reopened = new KnobLightsViewModel(settings, "dev", ["Knob 1", "Knob 2"]);
        Assert.That(reopened.EachLedOwn, Is.True);
        Assert.That(reopened.Leds[0].Editor.Effect, Is.EqualTo(LightEffects.Breathe));
    }

    private sealed class LightStore(Dictionary<string, LightStyle?> saved) : IDeviceLightSettings
    {
        public LightStyle? GetDeviceLight(string deviceId) => saved.GetValueOrDefault(deviceId);

        public void SetDeviceLight(string deviceId, LightStyle? style) => saved[deviceId] = style;
    }
}
