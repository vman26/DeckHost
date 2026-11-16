using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.Rendering;
using DeckHost.Core.State;
using System.Collections.Concurrent;

namespace DeckHost.Tests;

/// <summary>
/// A knob shown on the touch zone above it (<see cref="PhysicalControl.DisplayControlId"/>), and
/// the screen backdrop behind the controls.
/// </summary>
public class LinkedDisplayTests
{
    private const uint Red = 0xFFFF0000;
    private const uint Blue = 0xFF0000FF;
    private static readonly byte[] RedPixel = [255, 0, 0, 255];
    private static readonly byte[] BluePixel = [0, 0, 255, 255];
    private static readonly byte[] BasePixel = [0x0B, 0x12, 0x20, 255];

    private sealed class Fixture : IAsyncDisposable
    {
        public StripDevicePlugin Plugin { get; } = new();

        public SolidAction KnobAction { get; } = new("knob", Red);

        public SolidAction ZoneAction { get; } = new("zone", Blue);

        public AppearanceOnlyAction DisplayOnly { get; } = new("display");

        public VariableStore Store { get; } = new();

        public DeviceHostService Host { get; }

        public ConcurrentDictionary<string, ControlFrame> Frames { get; } = new();

        public Fixture()
        {
            Host = new DeviceHostService([Plugin], [new TestApplicationPlugin("p", KnobAction, ZoneAction, DisplayOnly)], Store);
            Host.ControlFrameRendered += frame => Frames[frame.ControlId] = frame;
        }

        public async Task ShowAsync(params DeviceProfile[] profiles)
        {
            Host.SetProfiles(profiles);
            await Host.StartAsync(CancellationToken.None);
            await Host.WhenIdleAsync();
        }

        public byte[] ZonePixel(int x, int y)
        {
            var frame = Frames[StripDevicePlugin.ZoneId];
            return frame.Rgba.AsSpan(((y * frame.Width) + x) * 4, 4).ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            Store.Dispose();
        }
    }

    private static DeviceProfile Profile(string id, ProfileBackground? background, params ControlBinding[] bindings) =>
        new(id, id, StripDevicePlugin.DeviceId, bindings, Background: background);

    private static ControlBinding Knob(bool showOnDisplay = true) =>
        Bindings.Single(StripDevicePlugin.KnobId, "p", "knob") with { Appearance = new ControlAppearance { ShowOnLinkedDisplay = showOnDisplay } };

    private static ControlBinding Zone() => Bindings.Single(StripDevicePlugin.ZoneId, "p", "zone");

    [Test]
    public async Task A_knob_fills_its_touch_zone_when_the_zone_has_no_action()
    {
        await using var fixture = new Fixture();
        await fixture.ShowAsync(Profile("one", null, Knob()));

        Assert.That(fixture.ZonePixel(2, 2), Is.EqualTo(RedPixel));
        Assert.That(fixture.ZonePixel(StripDevicePlugin.ZoneWidth - 3, StripDevicePlugin.ZoneHeight - 3), Is.EqualTo(RedPixel));

        // Drawn as the knob (its own variables and settings), at the zone's full size.
        Assert.That(fixture.KnobAction.Renders, Does.Contain((StripDevicePlugin.KnobId, StripDevicePlugin.ZoneWidth, StripDevicePlugin.ZoneHeight, false)));
    }

    [Test]
    public async Task A_knob_and_a_zone_action_share_the_zone()
    {
        await using var fixture = new Fixture();
        await fixture.ShowAsync(Profile("one", null, Knob(), Zone()));

        var strip = ControlRenderer.LinkedStripHeight(StripDevicePlugin.ZoneHeight);
        var own = StripDevicePlugin.ZoneHeight - strip;
        Assert.That(strip, Is.InRange(20, StripDevicePlugin.ZoneHeight / 2));

        // The zone's action keeps the top; the knob (which sits below the zone) gets the bottom strip.
        Assert.That(fixture.ZonePixel(2, 2), Is.EqualTo(BluePixel));
        Assert.That(fixture.ZonePixel(2, own - 1), Is.EqualTo(BluePixel));
        Assert.That(fixture.ZonePixel(2, own), Is.EqualTo(RedPixel));
        Assert.That(fixture.ZonePixel(2, StripDevicePlugin.ZoneHeight - 1), Is.EqualTo(RedPixel));

        Assert.That(fixture.ZoneAction.Renders, Does.Contain((StripDevicePlugin.ZoneId, StripDevicePlugin.ZoneWidth, own, false)));
        Assert.That(fixture.KnobAction.Renders, Does.Contain((StripDevicePlugin.KnobId, StripDevicePlugin.ZoneWidth, strip, true)));
    }

    [Test]
    public async Task A_zone_with_only_a_background_color_is_still_free_for_the_knob()
    {
        await using var fixture = new Fixture();
        var greenZone = Bindings.Single(StripDevicePlugin.ZoneId, "p", "display") with { Appearance = new ControlAppearance(BackgroundColor: "#00FF00") };
        await fixture.ShowAsync(Profile("one", null, Knob(), greenZone));

        // The knob fills the whole zone: no strip, the zone's own action area is empty.
        Assert.That(fixture.KnobAction.Renders, Does.Contain((StripDevicePlugin.KnobId, StripDevicePlugin.ZoneWidth, StripDevicePlugin.ZoneHeight, false)));
        Assert.That(fixture.KnobAction.Renders.Any(render => render.Compact), Is.False);
        Assert.That(fixture.ZonePixel(2, StripDevicePlugin.ZoneHeight - 1), Is.EqualTo(RedPixel));

        // And a tap there is the knob's.
        fixture.Plugin.Context!.ReportInputEvent(StripDevicePlugin.DeviceId, StripDevicePlugin.ZoneId, InputCapabilities.Press, 1);
        await fixture.Host.WhenIdleAsync();
        Assert.That(fixture.KnobAction.Executions, Does.Contain((StripDevicePlugin.KnobId, InputCapabilities.Press)));
        Assert.That(fixture.DisplayOnly.Executions, Is.Empty);
    }

    [Test]
    public async Task The_zone_color_shows_under_a_knob_that_draws_nothing_there()
    {
        await using var fixture = new Fixture();
        var greenZone = Bindings.Single(StripDevicePlugin.ZoneId, "p", "display") with { Appearance = new ControlAppearance(BackgroundColor: "#00FF00") };
        var knobShowingNothing = Bindings.Single(StripDevicePlugin.KnobId, "p", "display");
        await fixture.ShowAsync(Profile("one", null, knobShowingNothing, greenZone));

        Assert.That(fixture.ZonePixel(2, 2), Is.EqualTo(new byte[] { 0, 255, 0, 255 }));
    }

    [Test]
    public async Task A_zone_with_its_own_text_shares_with_the_knob()
    {
        await using var fixture = new Fixture();
        var labelledZone = Bindings.Single(StripDevicePlugin.ZoneId, "p", "display") with { Appearance = new ControlAppearance(Text: "Mute", BackgroundColor: "#00FF00") };
        await fixture.ShowAsync(Profile("one", null, Knob(), labelledZone));

        Assert.That(fixture.KnobAction.Renders.Any(render => render.Compact), Is.True);
        Assert.That(fixture.ZonePixel(2, 2), Is.EqualTo(new byte[] { 0, 255, 0, 255 }));
    }

    [Test]
    public async Task A_knob_can_stay_off_the_zone()
    {
        await using var fixture = new Fixture();
        await fixture.ShowAsync(Profile("one", null, Knob(showOnDisplay: false)));

        Assert.That(fixture.ZonePixel(2, 2), Is.EqualTo(BasePixel));
        Assert.That(fixture.KnobAction.Renders, Is.Empty);
    }

    [Test]
    public async Task Tapping_a_zone_that_only_shows_the_knob_presses_the_knob()
    {
        await using var fixture = new Fixture();
        await fixture.ShowAsync(Profile("one", null, Knob()));

        fixture.Plugin.Context!.ReportInputEvent(StripDevicePlugin.DeviceId, StripDevicePlugin.ZoneId, InputCapabilities.Press, 1);
        await fixture.Host.WhenIdleAsync();

        Assert.That(
            fixture.KnobAction.Executions,
            Is.EqualTo(new[] { (StripDevicePlugin.KnobId, InputCapabilities.Press), (StripDevicePlugin.KnobId, InputCapabilities.Release) }));
    }

    [TestCase(true, true)]   // the zone has its own action: the tap is the zone's
    [TestCase(false, false)] // the knob isn't shown there: the tap does nothing
    public async Task Other_taps_never_reach_the_knob(bool zoneHasAction, bool showKnobOnDisplay)
    {
        await using var fixture = new Fixture();
        await fixture.ShowAsync(zoneHasAction
            ? Profile("one", null, Knob(showKnobOnDisplay), Zone())
            : Profile("one", null, Knob(showKnobOnDisplay)));

        fixture.Plugin.Context!.ReportInputEvent(StripDevicePlugin.DeviceId, StripDevicePlugin.ZoneId, InputCapabilities.Press, 1);
        await fixture.Host.WhenIdleAsync();

        Assert.That(fixture.KnobAction.Executions, Is.Empty);
        Assert.That(fixture.ZoneAction.Executions.Count, Is.EqualTo(zoneHasAction ? 1 : 0));
    }

    [Test]
    public async Task The_backdrop_follows_the_profile_and_is_sent_before_the_images()
    {
        await using var fixture = new Fixture();
        await fixture.ShowAsync(
            Profile("plain", null),
            Profile("red", new ProfileBackground("#FF0000")),
            Profile("also-red", new ProfileBackground("#FF0000")));

        // No background color: the base color the controls start with, then the control images.
        Assert.That(fixture.Plugin.Calls, Is.EqualTo(new[] { "backdrop:11,18,32", $"image:{StripDevicePlugin.ZoneId}" }));

        // Another color: the backdrop first, then every image again (the device may have drawn over them).
        fixture.Plugin.Calls.Clear();
        fixture.Host.SetActiveProfile("red");
        await fixture.Host.WhenIdleAsync();
        Assert.That(fixture.Plugin.Calls, Is.EqualTo(new[] { "backdrop:255,0,0", $"image:{StripDevicePlugin.ZoneId}" }));

        // The same color and the same pixels: nothing is sent.
        fixture.Plugin.Calls.Clear();
        fixture.Host.SetActiveProfile("also-red");
        await fixture.Host.WhenIdleAsync();
        Assert.That(fixture.Plugin.Calls, Is.Empty);
    }

    [Test]
    public async Task A_redraw_request_sends_the_backdrop_and_the_images_again()
    {
        await using var fixture = new Fixture();
        await fixture.ShowAsync(Profile("plain", null));
        fixture.Plugin.Calls.Clear();

        // What a device plugin asks for after it overwrote the screen (the N4 Pro's idle screen).
        fixture.Plugin.Context!.RequestRedraw(StripDevicePlugin.DeviceId);
        await fixture.Host.WhenIdleAsync();

        Assert.That(fixture.Plugin.Calls, Is.EqualTo(new[] { "backdrop:11,18,32", $"image:{StripDevicePlugin.ZoneId}" }));
    }

    [Test]
    public void The_knob_display_choice_is_saved_with_the_profile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deckhost-profiles-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DeckHost.Core.Profiles.ProfileStore(path);
            store.Save(new DeckHost.Core.Profiles.ProfileDocument(1, "one", [Profile("one", null, Knob(showOnDisplay: false), Zone())]));

            var bindings = store.Load()!.Profiles.Single().Bindings;
            Assert.That(bindings.Single(binding => binding.ControlId == StripDevicePlugin.KnobId).Appearance.ShowOnLinkedDisplay, Is.False);

            // Older files have no such field: knobs show on their zone.
            Assert.That(bindings.Single(binding => binding.ControlId == StripDevicePlugin.ZoneId).Appearance.ShowOnLinkedDisplay, Is.True);
            var old = System.Text.Json.JsonSerializer.Deserialize<ControlAppearance>("""{ "Text": "Hi" }""")!;
            Assert.That(old.ShowOnLinkedDisplay, Is.True);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase(null, 0x0B, 0x12, 0x20)]
    [TestCase("#FF8000", 0xFF, 0x80, 0x00)]
    [TestCase("not a color", 0x0B, 0x12, 0x20)]
    [TestCase("#80FFFFFF", 0x85, 0x89, 0x90)] // half-transparent white over the base color
    public void The_backdrop_color_is_the_profile_color_over_the_base(string? color, byte red, byte green, byte blue) =>
        Assert.That(ProfileBackdrop.GetColor(color is null ? null : new ProfileBackground(color)), Is.EqualTo((red, green, blue)));
}
