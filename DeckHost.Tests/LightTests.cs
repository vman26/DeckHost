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
    [Fact]
    public void Solid_and_off()
    {
        Assert.Equal(((byte)255, (byte)0, (byte)128), LightEffectMath.ColorAt(new LightStyle("FF0080"), TimeSpan.FromSeconds(7)));
        Assert.Equal(((byte)0, (byte)0, (byte)0), LightEffectMath.ColorAt(new LightStyle("FF0080", LightEffects.Off), TimeSpan.Zero));
    }

    [Fact]
    public void Breathing_fades_between_a_glow_and_the_full_color()
    {
        var style = new LightStyle("FF0000", LightEffects.Breathe, 2);

        var start = LightEffectMath.ColorAt(style, TimeSpan.Zero);
        var middle = LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1));

        Assert.InRange(start.R, (byte)5, (byte)15); // never fully dark
        Assert.Equal((byte)255, middle.R);
        Assert.Equal(start, LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(2))); // one cycle later
    }

    [Fact]
    public void Rainbow_moves_on_and_each_light_is_ahead_of_the_previous_one()
    {
        var style = new LightStyle("000000", LightEffects.Rainbow, 4);

        Assert.NotEqual(LightEffectMath.ColorAt(style, TimeSpan.Zero), LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1)));
        Assert.Equal(LightEffectMath.ColorAt(style, TimeSpan.FromSeconds(1)), LightEffectMath.ColorAt(style, TimeSpan.Zero, phase: 1));
    }
}

public class HostLightTests
{
    [Fact]
    public async Task The_device_light_shows_on_every_knob_and_a_knob_can_have_its_own()
    {
        var device = new LightDevice();
        await using var fixture = await Fixture.StartAsync(device,
            Knob("encoder.0", new RgbFeedback("FFFFFF", "00FF00")));

        fixture.Host.SetDeviceLight(LightDevice.DeviceId, new LightStyle("FF0000"));
        await fixture.Host.WhenIdleAsync();

        Assert.Equal((0, 255, 0), fixture.Shown("encoder.0")); // its own idle color
        Assert.Equal((255, 0, 0), fixture.Shown("encoder.1")); // the device's
        Assert.Contains("encoder.1:255,0,0", device.Calls);
    }

    [Fact]
    public async Task An_unplugged_device_still_shows_its_lights_in_the_editor()
    {
        var device = new LightDevice(connected: false);
        await using var fixture = await Fixture.StartAsync(device);

        fixture.Host.SetDeviceLight(LightDevice.DeviceId, new LightStyle("0000FF"));
        await fixture.Host.WhenIdleAsync();

        Assert.Equal((0, 0, 255), fixture.Shown("encoder.0"));
        Assert.Empty(device.Calls);
    }

    [Fact]
    public async Task A_press_shows_the_active_color_then_the_idle_light_again()
    {
        var device = new LightDevice();
        await using var fixture = await Fixture.StartAsync(device, Knob("encoder.0", new RgbFeedback("FFFFFF", "00FF00", ActiveDuration: 60)));

        fixture.Host.SimulateInput(new DeviceInputEvent(LightDevice.DeviceId, "encoder.0", InputCapabilities.Press, 1));
        await fixture.Host.WhenIdleAsync();
        Assert.Equal((255, 255, 255), fixture.Shown("encoder.0"));

        await WaitUntil(() => fixture.Shown("encoder.0") == (0, 255, 0));
        Assert.Equal((0, 255, 0), fixture.Shown("encoder.0"));
    }

    [Fact]
    public async Task An_effect_keeps_changing_the_light_in_one_command_for_all_knobs()
    {
        var device = new LightDevice();
        await using var fixture = await Fixture.StartAsync(device);
        fixture.Host.SetDeviceLight(LightDevice.DeviceId, new LightStyle("FF0000", LightEffects.Rainbow, 1));
        await fixture.Host.WhenIdleAsync();
        var first = fixture.Shown("encoder.0");

        await WaitUntil(() => fixture.Shown("encoder.0") != first);

        Assert.NotEqual(first, fixture.Shown("encoder.0"));
        Assert.Contains(device.Calls, call => call.StartsWith("batch:", StringComparison.Ordinal));
    }

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
    private sealed class Store : DeckHost.App.ViewModels.IDeviceLightSettings
    {
        public Dictionary<string, LightStyle?> Lights { get; } = [];

        public LightStyle? GetDeviceLight(string deviceId) => Lights.GetValueOrDefault(deviceId);

        public void SetDeviceLight(string deviceId, LightStyle? style) => Lights[deviceId] = style;
    }

    [Fact]
    public void The_knob_lights_card_sets_the_devices_light_right_away()
    {
        var store = new Store();
        var card = new DeckHost.App.ViewModels.KnobLightsViewModel(store, "dev");

        Assert.Equal(DeckHost.App.ViewModels.KnobLightsViewModel.Off, card.SelectedEffect.Value); // dark until chosen
        card.SelectedEffect = card.Effects.First(option => option.Value == LightEffects.Breathe);
        card.Color = "#FF8800";
        card.PeriodSeconds = 4.2;

        Assert.Equal(new LightStyle("FF8800", LightEffects.Breathe, 4.2), store.Lights["dev"]);
        Assert.True(card.ShowColor && card.ShowSpeed);
        card.SelectedEffect = card.Effects.First(option => option.Value == DeckHost.App.ViewModels.KnobLightsViewModel.Off);
        Assert.Null(store.Lights["dev"]);
    }

    [Fact]
    public void A_knob_can_have_its_own_idle_effect()
    {
        var builtIns = DeckHost.App.SystemPlugins.SystemPluginSet.Create().First(plugin => plugin.Id == DeckHost.App.SystemPlugins.SystemPluginSet.MediaPluginId);
        var item = DeckHost.App.ViewModels.ActionItemViewModel.Create(builtIns.Id, builtIns.Name, builtIns.Actions.First(action => action.Id == "media.volume-knob"));
        var knob = new DeckHost.App.ViewModels.ControlItemViewModel(new PhysicalControl("encoder.0", "Knob 1", ControlType.Encoder, new Rect(0, 0, 10, 10),
            InputCapabilities.Rotate | InputCapabilities.Press, OutputCapabilities.None, DisplayControlId: "touch.0", LedIndex: 0));
        var binding = new DeckHost.App.ViewModels.ControlBindingViewModel(
            new ControlBinding("encoder.0", ProfileActionKind.Single, item.ToActionReference(), new ControlAppearance(), []), item, []);
        var customize = new DeckHost.App.ViewModels.ControlCustomizationViewModel(knob, binding, [item], null);

        Assert.Null(customize.BuildBinding()!.Appearance.RgbFeedback); // follows the device's knob lights
        customize.LightEnabled = true;
        customize.SelectedIdleEffect = customize.IdleEffects.First(option => option.Value == LightEffects.Rainbow);
        customize.EffectPeriod = 6;

        var light = customize.BuildBinding()!.Appearance.RgbFeedback!;
        Assert.Equal((LightEffects.Rainbow, 6d), (light.IdleEffect, light.EffectPeriodSeconds));
        Assert.Equal(new LightStyle(light.IdleColor, LightEffects.Rainbow, 6), light.IdleStyle);
    }

    [Fact]
    public void Device_lights_and_a_knobs_effect_are_saved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}", "profiles.json");
        try
        {
            var store = new DeckHost.Core.Profiles.ProfileStore(path);
            var light = new RgbFeedback("FFFFFF", "00FF00") { IdleEffect = LightEffects.Breathe, EffectPeriodSeconds = 2.5 };
            var binding = new ControlBinding("encoder.0", ProfileActionKind.Single, Bindings.Reference("p", "a"), new ControlAppearance(RgbFeedback: light), []);
            store.Save(new DeckHost.Core.Profiles.ProfileDocument(DeckHost.Core.Profiles.ProfileDocument.CurrentVersion, "p", [new DeviceProfile("p", "P", "dev", [binding])],
                DeviceLights: new Dictionary<string, LightStyle> { ["dev"] = new("FF0000", LightEffects.Rainbow, 5) }));

            var loaded = store.Load()!;

            Assert.Equal(new LightStyle("FF0000", LightEffects.Rainbow, 5), loaded.DeviceLights!["dev"]);
            Assert.Equal(light, Assert.Single(Assert.Single(loaded.Profiles).Bindings).Appearance.RgbFeedback);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
