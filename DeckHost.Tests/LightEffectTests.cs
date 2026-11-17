using DeckHost.App.ViewModels;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.State;
using DeckHost.Tests;

namespace DeckHost.Tests;

public class LightEffectTests
{
    private const string Orange = "FF8000";
    private const string White = "FFFFFF";
    private const string Black = "000000";
    private const string Red = "FF0000";
    private const string Green = "00FF00";
    private const string Blue = "0000FF";
    private const string DeviceId = "dev";
    private const string ProfileId = "profile";
    private const int SamplesPerEffect = 60;
    private const double SampleStepSeconds = 0.07;
    private const int FewestDistinctColors = 3;
    private const double BreathePeriodSeconds = 3;
    private const int Frames = 90;
    private const double FramesPerSecond = 30d;
    private const int LargestStep = 16;
    private const int NotNearlyRed = 50;
    private const int NotNearlyFull = 205;
    private static readonly string[] Knobs = ["Knob 1", "Knob 2"];

    private static IReadOnlyList<(byte R, byte G, byte B)> Samples(LightStyle style, int count, double stepSeconds, int phase = 0) =>
        [.. Enumerable.Range(0, count).Select(i => LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(i * stepSeconds), phase))];

    private static int LargestBrightnessStep(LightStyle style, int frames, double framesPerSecond)
    {
        var previous = LightEffectMath.ColorAt(style, TimeSpan.Zero).R;
        var largest = 0;
        for (var frame = 1; frame < frames; frame++)
        {
            var current = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(frame / framesPerSecond)).R;
            largest = Math.Max(largest, Math.Abs(current - previous));
            previous = current;
        }

        return largest;
    }

    private static bool IsBetween(byte first, byte second) => first is > NotNearlyRed and < NotNearlyFull && second is > NotNearlyRed and < NotNearlyFull;

    [TestCase(LightEffects.Pulse)]
    [TestCase(LightEffects.Blink)]
    [TestCase(LightEffects.ColorCycle)]
    [TestCase(LightEffects.Wave)]
    [TestCase(LightEffects.Chase)]
    [TestCase(LightEffects.Candle)]
    [TestCase(LightEffects.Sparkle)]
    public void ColorAt_AnimatedEffect_ChangesOverTime(string effect)
    {
        // Arrange
        var style = new LightStyle(Orange, effect, 2);

        // Act
        var samples = Samples(style, SamplesPerEffect, SampleStepSeconds, phase: 1);

        // Assert
        Assert.That(samples.Distinct().Count(), Is.GreaterThan(FewestDistinctColors), $"{effect} should change over time");
    }

    [TestCase(LightEffects.Pulse)]
    [TestCase(LightEffects.Blink)]
    [TestCase(LightEffects.ColorCycle)]
    [TestCase(LightEffects.Wave)]
    [TestCase(LightEffects.Chase)]
    [TestCase(LightEffects.Candle)]
    [TestCase(LightEffects.Sparkle)]
    public void IsAnimated_AnimatedEffect_IsTrue(string effect)
    {
        // Arrange (the effect)

        // Act
        var animated = LightEffects.IsAnimated(effect);

        // Assert
        Assert.That(animated, Is.True);
    }

    [TestCase(LightEffects.Pulse)]
    [TestCase(LightEffects.Blink)]
    [TestCase(LightEffects.ColorCycle)]
    [TestCase(LightEffects.Wave)]
    [TestCase(LightEffects.Chase)]
    [TestCase(LightEffects.Candle)]
    [TestCase(LightEffects.Sparkle)]
    public void ColorAt_AnimatedEffectAtTheSameTime_RepeatsExactly(string effect)
    {
        // Arrange
        var style = new LightStyle(Orange, effect, 2);
        var moment = TimeSpan.FromSeconds(1.3);
        var first = LightEffectMath.ColorAt(style, moment, 2);

        // Act
        var again = LightEffectMath.ColorAt(style, moment, 2);

        // Assert
        Assert.That(again, Is.EqualTo(first));
    }

    [Test]
    public void ColorAt_BreatheSampledAtThirtyFramesASecond_NeverJumpsMoreThanASmallStep()
    {
        // Arrange
        var style = new LightStyle(White, LightEffects.Breathe, BreathePeriodSeconds);

        // Act
        var largestStep = LargestBrightnessStep(style, Frames, FramesPerSecond);

        // Assert
        Assert.That(largestStep, Is.InRange(0, LargestStep));
    }

    [Test]
    public void ColorAt_ColorCycleAtTheFirstColor_IsThatColor()
    {
        // Arrange
        var style = new LightStyle(Black, LightEffects.ColorCycle, 3) { Colors = [Red, Green, Blue], Spread = 0 };

        // Act
        var color = LightEffectMath.ColorAt(style, TimeSpan.Zero);

        // Assert
        Assert.That(color, Is.EqualTo(((byte)255, (byte)0, (byte)0)));
    }

    [Test]
    public void ColorAt_ColorCycleAtTheSecondColor_IsThatColor()
    {
        // Arrange
        var style = new LightStyle(Black, LightEffects.ColorCycle, 3) { Colors = [Red, Green, Blue], Spread = 0 };

        // Act
        var color = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(color, Is.EqualTo(((byte)0, (byte)255, (byte)0)));
    }

    [Test]
    public void ColorAt_ColorCycleBetweenTwoColors_FadesBetweenThem()
    {
        // Arrange
        var style = new LightStyle(Black, LightEffects.ColorCycle, 3) { Colors = [Red, Green, Blue], Spread = 0 };

        // Act
        var halfway = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(0.5));

        // Assert
        Assert.That(IsBetween(halfway.R, halfway.G), Is.True, $"between red and green: {halfway}");
    }

    private static LightStyle RedThenFadeToBlue() => new(Black, LightEffects.Custom)
    {
        Steps = [new LightStep(Red, FadeSeconds: 0, HoldSeconds: 1), new LightStep(Blue, FadeSeconds: 1, HoldSeconds: 1)],
    };

    [Test]
    public void CustomLength_TwoSteps_AddsFadesAndHolds()
    {
        // Arrange
        var style = RedThenFadeToBlue();

        // Act
        var length = LightEffectMath.CustomLength(style);

        // Assert
        Assert.That(length, Is.EqualTo(3));
    }

    [Test]
    public void ColorAt_CustomEffectDuringTheFirstHold_IsTheFirstColor()
    {
        // Arrange
        var style = RedThenFadeToBlue();

        // Act
        var color = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(0.5));

        // Assert
        Assert.That(color, Is.EqualTo(((byte)255, (byte)0, (byte)0)));
    }

    [Test]
    public void ColorAt_CustomEffectDuringAFade_IsBetweenTheTwoColors()
    {
        // Arrange
        var style = RedThenFadeToBlue();

        // Act
        var fading = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1.5));

        // Assert
        Assert.That(IsBetween(fading.R, fading.B), Is.True, $"fading red to blue: {fading}");
    }

    [Test]
    public void ColorAt_CustomEffectDuringTheLastHold_IsTheLastColor()
    {
        // Arrange
        var style = RedThenFadeToBlue();

        // Act
        var color = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(2.5));

        // Assert
        Assert.That(color, Is.EqualTo(((byte)0, (byte)0, (byte)255)));
    }

    [Test]
    public void ColorAt_CustomEffectAfterItsLength_StartsFromTheBeginning()
    {
        // Arrange
        var style = RedThenFadeToBlue();

        // Act
        var color = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(3.5));

        // Assert
        Assert.That(color, Is.EqualTo(((byte)255, (byte)0, (byte)0)));
    }

    [Test]
    public void ColorAt_ChaseAQuarterIntoTheCycle_LightsTheSecondOfFourLeds()
    {
        // Arrange
        const int ledCount = 4;
        var style = new LightStyle(White, LightEffects.Chase, 4);
        var moment = TimeSpan.FromSeconds(1.1);

        // Act
        var leds = Enumerable.Range(0, ledCount).Select(i => LightEffectMath.ColorAt(style, moment, i, ledCount).Item1).ToList();

        // Assert
        Assert.That(leds.IndexOf(leds.Max()), Is.EqualTo(1));
    }

    [Test]
    public void Equals_StylesWithTheSameLists_AreEqual()
    {
        // Arrange
        var first = new LightStyle(Red, LightEffects.Custom) { Steps = [new LightStep(Green)], Spread = 0.1 };
        var second = new LightStyle(Red, LightEffects.Custom) { Steps = [new LightStep(Green)], Spread = 0.1 };

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.That(equal, Is.True);
    }

    [Test]
    public void Deserialize_AStyleSavedWithItsLists_IsEqualToTheOriginal()
    {
        // Arrange
        var style = new LightStyle(Red, LightEffects.Custom) { Steps = [new LightStep(Green)], Spread = 0.1 };
        var json = System.Text.Json.JsonSerializer.Serialize(new RgbFeedback { IdleLight = style });

        // Act
        var loaded = System.Text.Json.JsonSerializer.Deserialize<RgbFeedback>(json)!;

        // Assert
        Assert.That(loaded.IdleStyle, Is.EqualTo(style));
    }

    private static LightStyleEditorViewModel EditorComposingATrafficLightWithAnExtraStep(out Func<int> changes)
    {
        var editor = new LightStyleEditorViewModel(null);
        var count = 0;
        editor.Changed += (_, _) => count++;
        changes = () => count;
        editor.SelectedEffect = editor.Effects.First(option => option.Value == LightEffects.Custom);
        editor.SelectedPreset = LightStyleEditorViewModel.Presets.First(preset => preset.Name == "Traffic light");
        editor.Steps[0].HoldSeconds = 1;
        editor.AddStepCommand.Execute(null);
        editor.Steps[^1].Color = "#123456";
        editor.Steps[^1].MoveUpCommand.Execute(null);
        return editor;
    }

    [Test]
    public void SelectedEffect_Custom_ShowsTheStepsStartingFromAPreset()
    {
        // Arrange
        var editor = new LightStyleEditorViewModel(null);

        // Act
        editor.SelectedEffect = editor.Effects.First(option => option.Value == LightEffects.Custom);

        // Assert
        Assert.That(editor.ShowSteps, Is.True);
        Assert.That(editor.Steps, Is.Not.Empty);
    }

    [Test]
    public void ToStyle_CustomEffectComposedInTheEditor_HasTheStepsInTheirOrder()
    {
        // Arrange
        var editor = EditorComposingATrafficLightWithAnExtraStep(out _);

        // Act
        var style = editor.ToStyle()!;

        // Assert
        Assert.That(style.Effect, Is.EqualTo(LightEffects.Custom));
        Assert.That(style.Steps!.Select(step => step.Color), Is.EqualTo(new[] { "FF0000", "FFA500", "123456", "00FF00" }));
        Assert.That(style.Steps![0].HoldSeconds, Is.EqualTo(1));
    }

    [Test]
    public void Changed_EveryEditOfTheSteps_IsRaised()
    {
        // Arrange
        const int editsMade = 4;

        // Act
        EditorComposingATrafficLightWithAnExtraStep(out var changes);

        // Assert
        Assert.That(changes(), Is.GreaterThanOrEqualTo(editsMade));
    }

    [Test]
    public void ToStyle_OffInDeviceSettings_IsNoLight()
    {
        // Arrange
        var editor = new LightStyleEditorViewModel(null, KnobLightsViewModel.Off);

        // Act
        var style = editor.ToStyle();

        // Assert
        Assert.That(style, Is.Null);
    }

    [Test]
    public void ToStyle_OffOnAKnob_IsADarkLight()
    {
        // Arrange
        var editor = new LightStyleEditorViewModel(new LightStyle(Effect: LightEffects.Off));

        // Act
        var style = editor.ToStyle()!;

        // Assert
        Assert.That(style.Effect, Is.EqualTo(LightEffects.Off));
    }

    private static async Task<(DeviceHostService Host, HostLightTests.LightDevice Device, VariableStore Store)> StartedLightHostAsync()
    {
        var device = new HostLightTests.LightDevice();
        var store = new VariableStore();
        var host = new DeviceHostService([device], [], store);
        host.SetProfiles([new DeviceProfile(ProfileId, "Profile", HostLightTests.LightDevice.DeviceId, [])]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        return (host, device, store);
    }

    [Test]
    public async Task RequestRedraw_AfterTheDeviceResetItsLightsWithNoLightSet_SendsOffAgain()
    {
        // Arrange
        var (host, device, store) = await StartedLightHostAsync();
        await using var _ = host;
        using var __ = store;
        host.SetDeviceLight(HostLightTests.LightDevice.DeviceId, null);
        await host.WhenIdleAsync();
        device.Calls.Clear();

        // Act (the device plugin wrote its configuration, which put its own lighting back on)
        device.Context!.RequestRedraw(HostLightTests.LightDevice.DeviceId);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(device.Calls, Does.Contain("batch:encoder.0:0,0,0 encoder.1:0,0,0"));
    }

    [Test]
    public async Task SetDeviceLight_StyleWithALightForTheFirstLedOnly_GivesTheFirstItsOwnAndTheSecondTheSharedColor()
    {
        // Arrange
        var (host, device, store) = await StartedLightHostAsync();
        await using var _ = host;
        using var __ = store;
        device.Calls.Clear();

        // Act
        host.SetDeviceLight(HostLightTests.LightDevice.DeviceId, new LightStyle(Green) { Leds = [new LightStyle(Red), null] });
        await host.WhenIdleAsync();

        // Assert
        Assert.That(device.Calls, Does.Contain("batch:encoder.0:255,0,0 encoder.1:0,255,0"));
    }

    private static (KnobLightsViewModel Card, Dictionary<string, LightStyle?> Saved, LightStore Settings) CardWithTwoLeds()
    {
        var saved = new Dictionary<string, LightStyle?>();
        var settings = new LightStore(saved);
        return (new KnobLightsViewModel(settings, DeviceId, Knobs), saved, settings);
    }

    private static void GiveTheLedsABreatheAndAnOffEffect(KnobLightsViewModel card)
    {
        card.EachLedOwn = true;
        card.Leds[0].Editor.SelectedEffect = card.Leds[0].Editor.Effects.First(option => option.Value == LightEffects.Breathe);
        card.Leds[1].Editor.SelectedEffect = card.Leds[1].Editor.Effects.First(option => option.Value == LightEffects.Off);
    }

    [Test]
    public void EachLedOwn_LedsGivenTheirOwnEffects_SavesAStyleWithAnEffectPerLed()
    {
        // Arrange
        var (card, saved, _) = CardWithTwoLeds();

        // Act
        GiveTheLedsABreatheAndAnOffEffect(card);

        // Assert
        Assert.That(saved[DeviceId]!.Leds!.Select(led => led!.Effect), Is.EqualTo(new[] { LightEffects.Breathe, LightEffects.Off }));
    }

    [Test]
    public void Constructor_AfterEachLedWasGivenItsOwnLight_ShowsEachLedsLight()
    {
        // Arrange
        var (card, _, settings) = CardWithTwoLeds();
        GiveTheLedsABreatheAndAnOffEffect(card);

        // Act
        var reopened = new KnobLightsViewModel(settings, DeviceId, Knobs);

        // Assert
        Assert.That(reopened.EachLedOwn, Is.True);
        Assert.That(reopened.Leds[0].Editor.Effect, Is.EqualTo(LightEffects.Breathe));
    }

    private sealed class LightStore(Dictionary<string, LightStyle?> saved) : IDeviceLightSettings
    {
        public LightStyle? GetDeviceLight(string deviceId) => saved.GetValueOrDefault(deviceId);

        public void SetDeviceLight(string deviceId, LightStyle? style) => saved[deviceId] = style;
    }
}
