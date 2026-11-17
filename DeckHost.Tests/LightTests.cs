using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.State;
using System.Collections.Concurrent;

namespace DeckHost.Tests;

public class LightEffectMathTests
{
    private const string Pink = "FF0080";
    private const string Red = "FF0000";
    private const string Black = "000000";
    private const double BreathePeriodSeconds = 2;
    private const double RainbowPeriodSeconds = 4;
    private const int SomeSecond = 7;
    private const byte DimestRed = 5;
    private const byte BrightestOfTheDim = 15;

    [Test]
    public void ColorAt_SolidStyle_IsTheColorAtAnyTime()
    {
        // Arrange
        var style = new LightStyle(Pink);

        // Act
        var color = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(SomeSecond));

        // Assert
        Assert.That(color, Is.EqualTo(((byte)255, (byte)0, (byte)128)));
    }

    [Test]
    public void ColorAt_OffEffect_IsBlack()
    {
        // Arrange
        var style = new LightStyle(Pink, LightEffects.Off);

        // Act
        var color = LightEffectMath.ColorAt(style, TimeSpan.Zero);

        // Assert
        Assert.That(color, Is.EqualTo(((byte)0, (byte)0, (byte)0)));
    }

    [Test]
    public void ColorAt_BreatheAtTheStartOfACycle_IsAGlowThatIsNeverFullyDark()
    {
        // Arrange
        var style = new LightStyle(Red, LightEffects.Breathe, BreathePeriodSeconds);

        // Act
        var start = LightEffectMath.ColorAt(style, TimeSpan.Zero);

        // Assert
        Assert.That(start.R, Is.InRange(DimestRed, BrightestOfTheDim));
    }

    [Test]
    public void ColorAt_BreatheHalfWayThroughACycle_IsTheFullColor()
    {
        // Arrange
        var style = new LightStyle(Red, LightEffects.Breathe, BreathePeriodSeconds);

        // Act
        var middle = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(BreathePeriodSeconds / 2));

        // Assert
        Assert.That(middle.R, Is.EqualTo((byte)255));
    }

    [Test]
    public void ColorAt_BreatheOneCycleLater_IsTheStartColorAgain()
    {
        // Arrange
        var style = new LightStyle(Red, LightEffects.Breathe, BreathePeriodSeconds);
        var start = LightEffectMath.ColorAt(style, TimeSpan.Zero);

        // Act
        var later = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(BreathePeriodSeconds));

        // Assert
        Assert.That(later, Is.EqualTo(start));
    }

    [Test]
    public void ColorAt_RainbowAQuarterCycleLater_IsAnotherColor()
    {
        // Arrange
        var style = new LightStyle(Black, LightEffects.Rainbow, RainbowPeriodSeconds);
        var start = LightEffectMath.ColorAt(style, TimeSpan.Zero);

        // Act
        var later = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(later, Is.Not.EqualTo(start));
    }

    [Test]
    public void ColorAt_RainbowOnTheNextLight_IsAheadByTheSameQuarterCycle()
    {
        // Arrange
        var style = new LightStyle(Black, LightEffects.Rainbow, RainbowPeriodSeconds);
        var aQuarterLater = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1));

        // Act
        var nextLight = LightEffectMath.ColorAt(style, TimeSpan.Zero, phase: 1);

        // Assert
        Assert.That(nextLight, Is.EqualTo(aQuarterLater));
    }
}

public class HostLightTests
{
    private const string FirstKnob = "encoder.0";
    private const string SecondKnob = "encoder.1";
    private const string White = "FFFFFF";
    private const string Green = "00FF00";
    private const string Red = "FF0000";
    private const string Blue = "0000FF";
    private const int PressDurationMilliseconds = 60;
    private const int OneRainbowSecond = 1;
    private static readonly (int, int, int) GreenLight = (0, 255, 0);
    private static readonly (int, int, int) RedLight = (255, 0, 0);
    private static readonly (int, int, int) BlueLight = (0, 0, 255);
    private static readonly (int, int, int) WhiteLight = (255, 255, 255);

    private static async Task<Fixture> StartedWithAGreenFirstKnobAsync(LightDevice device) =>
        await Fixture.StartAsync(device, Knob(FirstKnob, new RgbFeedback(White, Green)));

    [Test]
    public async Task SetDeviceLight_KnobWithItsOwnIdleColor_KeepsItsOwnColor()
    {
        // Arrange
        await using var fixture = await StartedWithAGreenFirstKnobAsync(new LightDevice());

        // Act
        fixture.Host.SetDeviceLight(LightDevice.DeviceId, new LightStyle(Red));
        await fixture.Host.WhenIdleAsync();

        // Assert
        Assert.That(fixture.Shown(FirstKnob), Is.EqualTo(GreenLight));
    }

    [Test]
    public async Task SetDeviceLight_KnobWithoutItsOwnColor_ShowsTheDevicesLight()
    {
        // Arrange
        var device = new LightDevice();
        await using var fixture = await StartedWithAGreenFirstKnobAsync(device);

        // Act
        fixture.Host.SetDeviceLight(LightDevice.DeviceId, new LightStyle(Red));
        await fixture.Host.WhenIdleAsync();

        // Assert
        Assert.That(fixture.Shown(SecondKnob), Is.EqualTo(RedLight));
        Assert.That(device.Calls, Does.Contain($"{SecondKnob}:255,0,0"));
    }

    [Test]
    public async Task SetDeviceLight_UnpluggedDevice_ShowsTheLightInTheEditor()
    {
        // Arrange
        await using var fixture = await Fixture.StartAsync(new LightDevice(connected: false));

        // Act
        fixture.Host.SetDeviceLight(LightDevice.DeviceId, new LightStyle(Blue));
        await fixture.Host.WhenIdleAsync();

        // Assert
        Assert.That(fixture.Shown(FirstKnob), Is.EqualTo(BlueLight));
    }

    [Test]
    public async Task SetDeviceLight_UnpluggedDevice_SendsNothingToTheDevice()
    {
        // Arrange
        var device = new LightDevice(connected: false);
        await using var fixture = await Fixture.StartAsync(device);

        // Act
        fixture.Host.SetDeviceLight(LightDevice.DeviceId, new LightStyle(Blue));
        await fixture.Host.WhenIdleAsync();

        // Assert
        Assert.That(device.Calls, Is.Empty);
    }

    [Test]
    public async Task SimulateInput_PressOnAKnobWithLightFeedback_ShowsTheActiveColor()
    {
        // Arrange
        await using var fixture = await Fixture.StartAsync(new LightDevice(), Knob(FirstKnob, new RgbFeedback(White, Green, ActiveDuration: PressDurationMilliseconds)));

        // Act
        fixture.Host.SimulateInput(new DeviceInputEvent(LightDevice.DeviceId, FirstKnob, InputCapabilities.Press, 1));
        await fixture.Host.WhenIdleAsync();

        // Assert
        Assert.That(fixture.Shown(FirstKnob), Is.EqualTo(WhiteLight));
    }

    [Test]
    public async Task SimulateInput_PressOnAKnobWithLightFeedback_ShowsTheIdleLightAgainAfterTheActiveDuration()
    {
        // Arrange
        await using var fixture = await Fixture.StartAsync(new LightDevice(), Knob(FirstKnob, new RgbFeedback(White, Green, ActiveDuration: PressDurationMilliseconds)));
        fixture.Host.SimulateInput(new DeviceInputEvent(LightDevice.DeviceId, FirstKnob, InputCapabilities.Press, 1));
        await fixture.Host.WhenIdleAsync();

        // Act
        await WaitUntil(() => fixture.Shown(FirstKnob) == GreenLight);

        // Assert
        Assert.That(fixture.Shown(FirstKnob), Is.EqualTo(GreenLight));
    }

    [Test]
    public async Task SetDeviceLight_RainbowEffect_KeepsChangingTheLight()
    {
        // Arrange
        await using var fixture = await Fixture.StartAsync(new LightDevice());
        fixture.Host.SetDeviceLight(LightDevice.DeviceId, new LightStyle(Red, LightEffects.Rainbow, OneRainbowSecond));
        await fixture.Host.WhenIdleAsync();
        var first = fixture.Shown(FirstKnob);

        // Act
        await WaitUntil(() => fixture.Shown(FirstKnob) != first);

        // Assert
        Assert.That(fixture.Shown(FirstKnob), Is.Not.EqualTo(first));
    }

    [Test]
    public async Task SetDeviceLight_RainbowEffect_SendsOneCommandForAllKnobs()
    {
        // Arrange
        var device = new LightDevice();
        await using var fixture = await Fixture.StartAsync(device);

        // Act
        fixture.Host.SetDeviceLight(LightDevice.DeviceId, new LightStyle(Red, LightEffects.Rainbow, OneRainbowSecond));
        await fixture.Host.WhenIdleAsync();
        await WaitUntil(() => device.Calls.Any(IsABatch));

        // Assert
        Assert.That(device.Calls.Any(IsABatch), Is.True);
    }

    private static bool IsABatch(string call) => call.StartsWith("batch:", StringComparison.Ordinal);

    private static ControlBinding Knob(string controlId, RgbFeedback light) =>
        new(controlId, ProfileActionKind.Single, Bindings.Reference("p", "a"), new ControlAppearance(RgbFeedback: light), []);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < end)
        {
            await Task.Delay(20);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly VariableStore _store = new();
        private readonly ConcurrentDictionary<string, (int, int, int)> _shown = new();

        private Fixture(LightDevice device, ControlBinding[] bindings)
        {
            Host = new DeviceHostService([device], [new TestApplicationPlugin("p", new RecordingAction("a"))], _store);
            Host.LightChanged += light => _shown[light.ControlId] = (light.Red, light.Green, light.Blue);
            Host.SetProfiles([new DeviceProfile("profile", "Profile", LightDevice.DeviceId, bindings)]);
        }

        public DeviceHostService Host { get; }

        public static async Task<Fixture> StartAsync(LightDevice device, params ControlBinding[] bindings)
        {
            var fixture = new Fixture(device, bindings);
            await fixture.Host.StartAsync(CancellationToken.None);
            await fixture.Host.WhenIdleAsync();
            return fixture;
        }

        public (int R, int G, int B) Shown(string controlId) => _shown.TryGetValue(controlId, out var color) ? color : (-1, -1, -1);

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            _store.Dispose();
        }
    }

    internal sealed class LightDevice(bool connected = true) : IDevicePlugin, IDeviceOutput, IDeviceLightBatchOutput
    {
        public const string DeviceId = "light.device";

        public event EventHandler? DevicesChanged { add { } remove { } }

        public string Id => "light.plugin";

        public string Name => "Lights";

        public IReadOnlyCollection<DeviceDefinition> Devices { get; } =
        [
            new(DeviceId, "Light device",
            [
                new("encoder.0", "Knob 1", ControlType.Encoder, new Rect(0, 0, 10, 10), InputCapabilities.Press | InputCapabilities.Rotate, OutputCapabilities.None, LedIndex: 0),
                new("encoder.1", "Knob 2", ControlType.Encoder, new Rect(20, 0, 10, 10), InputCapabilities.Press | InputCapabilities.Rotate, OutputCapabilities.None, LedIndex: 1),
            ],
            IsConnected: connected),
        ];

        public IDeviceOutput Output => this;

        public ConcurrentQueue<string> Calls { get; } = new();

        public IDevicePluginContext? Context { get; private set; }

        public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
        {
            Context = context;
            return Task.CompletedTask;
        }

        public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
        {
            error = null;
            return true;
        }

        public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error)
        {
            Calls.Enqueue($"{controlId}:{red},{green},{blue}");
            error = null;
            return true;
        }

        public bool TrySetLights(string deviceId, IReadOnlyList<LightColor> lights, out string? error)
        {
            Calls.Enqueue("batch:" + string.Join(" ", lights.Select(light => $"{light.ControlId}:{light.Red},{light.Green},{light.Blue}")));
            error = null;
            return true;
        }

        public void Clear(string deviceId)
        {
        }

        public void Dispose()
        {
        }
    }
}
public class LightSettingsTests
{
    private const string DeviceId = "dev";
    private const string Orange = "#FF8800";
    private const string OrangeHex = "FF8800";
    private const double Period = 4.2;
    private const double RainbowPeriod = 6;

    private sealed class Store : DeckHost.App.ViewModels.IDeviceLightSettings
    {
        public Dictionary<string, LightStyle?> Lights { get; } = [];

        public LightStyle? GetDeviceLight(string deviceId) => Lights.GetValueOrDefault(deviceId);

        public void SetDeviceLight(string deviceId, LightStyle? style) => Lights[deviceId] = style;
    }

    private sealed class TempProfilesFolder : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}");

        public string ProfilesFile => Path.Combine(Folder, "profiles.json");

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    private static ActionSettingOption Effect(DeckHost.App.ViewModels.KnobLightsViewModel card, string value) =>
        card.Effects.First(option => option.Value == value);

    [Test]
    public void Constructor_NoLightSet_ShowsTheDeviceDark()
    {
        // Arrange
        var store = new Store();

        // Act
        var card = new DeckHost.App.ViewModels.KnobLightsViewModel(store, DeviceId);

        // Assert
        Assert.That(card.SelectedEffect.Value, Is.EqualTo(DeckHost.App.ViewModels.KnobLightsViewModel.Off));
    }

    [Test]
    public void SelectedEffect_ChosenWithAColorAndPeriod_SetsTheDevicesLightRightAway()
    {
        // Arrange
        var store = new Store();
        var card = new DeckHost.App.ViewModels.KnobLightsViewModel(store, DeviceId);

        // Act
        card.SelectedEffect = Effect(card, LightEffects.Breathe);
        card.Color = Orange;
        card.PeriodSeconds = Period;

        // Assert
        Assert.That(store.Lights[DeviceId], Is.EqualTo(new LightStyle(OrangeHex, LightEffects.Breathe, Period)));
    }

    [Test]
    public void SelectedEffect_Breathe_ShowsTheColorAndSpeedFields()
    {
        // Arrange
        var card = new DeckHost.App.ViewModels.KnobLightsViewModel(new Store(), DeviceId);

        // Act
        card.SelectedEffect = Effect(card, LightEffects.Breathe);

        // Assert
        Assert.That(card.ShowColor && card.ShowSpeed, Is.True);
    }

    [Test]
    public void SelectedEffect_BackToOff_ClearsTheDevicesLight()
    {
        // Arrange
        var store = new Store();
        var card = new DeckHost.App.ViewModels.KnobLightsViewModel(store, DeviceId);
        card.SelectedEffect = Effect(card, LightEffects.Breathe);

        // Act
        card.SelectedEffect = Effect(card, DeckHost.App.ViewModels.KnobLightsViewModel.Off);

        // Assert
        Assert.That(store.Lights[DeviceId], Is.Null);
    }

    private static DeckHost.App.ViewModels.ControlCustomizationViewModel CustomizeAKnob()
    {
        var builtIns = DeckHost.App.SystemPlugins.SystemPluginSet.Create().First(plugin => plugin.Id == DeckHost.App.SystemPlugins.SystemPluginSet.MediaPluginId);
        var item = DeckHost.App.ViewModels.ActionItemViewModel.Create(builtIns.Id, builtIns.Name, builtIns.Actions.First(action => action.Id == "media.volume-knob"));
        var knob = new DeckHost.App.ViewModels.ControlItemViewModel(new PhysicalControl("encoder.0", "Knob 1", ControlType.Encoder, new Rect(0, 0, 10, 10),
            InputCapabilities.Rotate | InputCapabilities.Press, OutputCapabilities.None, DisplayControlId: "touch.0", LedIndex: 0));
        var binding = new DeckHost.App.ViewModels.ControlBindingViewModel(
            new ControlBinding("encoder.0", ProfileActionKind.Single, item.ToActionReference(), new ControlAppearance(), []), item, []);
        return new DeckHost.App.ViewModels.ControlCustomizationViewModel(knob, binding, [item], null);
    }

    [Test]
    public void BuildBinding_KnobWithoutItsOwnLight_FollowsTheDevicesKnobLights()
    {
        // Arrange
        var customize = CustomizeAKnob();

        // Act
        var light = customize.BuildBinding()!.Appearance.RgbFeedback;

        // Assert
        Assert.That(light, Is.Null);
    }

    [Test]
    public void BuildBinding_KnobWithItsOwnIdleEffect_KeepsTheEffectAndPeriod()
    {
        // Arrange
        var customize = CustomizeAKnob();
        customize.LightEnabled = true;
        customize.SelectedIdleEffect = customize.IdleEffects.First(option => option.Value == LightEffects.Rainbow);
        customize.EffectPeriod = RainbowPeriod;

        // Act
        var light = customize.BuildBinding()!.Appearance.RgbFeedback!;

        // Assert
        Assert.That((light.IdleEffect, light.EffectPeriodSeconds), Is.EqualTo((LightEffects.Rainbow, RainbowPeriod)));
    }

    [Test]
    public void BuildBinding_KnobWithItsOwnIdleEffect_BuildsTheFullIdleStyle()
    {
        // Arrange
        var customize = CustomizeAKnob();
        customize.LightEnabled = true;
        customize.SelectedIdleEffect = customize.IdleEffects.First(option => option.Value == LightEffects.Rainbow);
        customize.EffectPeriod = RainbowPeriod;

        // Act
        var light = customize.BuildBinding()!.Appearance.RgbFeedback!;

        // Assert
        Assert.That(light.IdleStyle, Is.EqualTo(new LightStyle(light.IdleColor, LightEffects.Rainbow, RainbowPeriod)));
    }

    private static DeckHost.Core.Profiles.ProfileDocument DocumentWithKnobEffectAndDeviceLight(RgbFeedback light)
    {
        var binding = new ControlBinding("encoder.0", ProfileActionKind.Single, Bindings.Reference("p", "a"), new ControlAppearance(RgbFeedback: light), []);
        return new DeckHost.Core.Profiles.ProfileDocument(DeckHost.Core.Profiles.ProfileDocument.CurrentVersion, "p", [new DeviceProfile("p", "P", DeviceId, [binding])],
            DeviceLights: new Dictionary<string, LightStyle> { [DeviceId] = new("FF0000", LightEffects.Rainbow, 5) });
    }

    [Test]
    public void Load_AfterSavingADeviceLight_ReturnsIt()
    {
        // Arrange
        using var folder = new TempProfilesFolder();
        var store = new DeckHost.Core.Profiles.ProfileStore(folder.ProfilesFile);
        store.Save(DocumentWithKnobEffectAndDeviceLight(new RgbFeedback("FFFFFF", "00FF00")));

        // Act
        var loaded = store.Load()!;

        // Assert
        Assert.That(loaded.DeviceLights![DeviceId], Is.EqualTo(new LightStyle("FF0000", LightEffects.Rainbow, 5)));
    }

    [Test]
    public void Load_AfterSavingAKnobsIdleEffect_ReturnsIt()
    {
        // Arrange
        using var folder = new TempProfilesFolder();
        var store = new DeckHost.Core.Profiles.ProfileStore(folder.ProfilesFile);
        var light = new RgbFeedback("FFFFFF", "00FF00") { IdleEffect = LightEffects.Breathe, EffectPeriodSeconds = 2.5 };
        store.Save(DocumentWithKnobEffectAndDeviceLight(light));

        // Act
        var loaded = store.Load()!;

        // Assert
        Assert.That(Expect.Single(Expect.Single(loaded.Profiles).Bindings).Appearance.RgbFeedback, Is.EqualTo(light));
    }
}
