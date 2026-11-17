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
    private const string PluginId = "p";
    private const string ProfileId = "one";
    private const string GreenBackground = "#00FF00";
    private const string DisplayOnlyAction = "display";
    private const int PixelInsideTheTopLeft = 2;
    private const int CornerInset = 3;
    private const int ShortestStrip = 20;
    private static readonly byte[] GreenPixel = [0, 255, 0, 255];
    private static readonly string BaseBackdrop = "backdrop:11,18,32";
    private static readonly string RedBackdrop = "backdrop:255,0,0";
    private static readonly string ZoneImage = $"image:{StripDevicePlugin.ZoneId}";
    private static readonly byte[] RedPixel = [255, 0, 0, 255];
    private static readonly byte[] BluePixel = [0, 0, 255, 255];
    private static readonly byte[] BasePixel = [0x0B, 0x12, 0x20, 255];

    private sealed class TempProfilesFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"deckhost-profiles-{Guid.NewGuid():N}.json");

        public void Dispose() => File.Delete(Path);
    }

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
            Host = new DeviceHostService([Plugin], [new TestApplicationPlugin(PluginId, KnobAction, ZoneAction, DisplayOnly)], Store);
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
        Bindings.Single(StripDevicePlugin.KnobId, PluginId, "knob") with { Appearance = new ControlAppearance { ShowOnLinkedDisplay = showOnDisplay } };

    private static ControlBinding Zone() => Bindings.Single(StripDevicePlugin.ZoneId, PluginId, "zone");

    private static ControlBinding GreenZone() =>
        Bindings.Single(StripDevicePlugin.ZoneId, PluginId, DisplayOnlyAction) with { Appearance = new ControlAppearance(BackgroundColor: GreenBackground) };

    private static async Task TapTheZoneAsync(Fixture fixture)
    {
        fixture.Plugin.Context!.ReportInputEvent(StripDevicePlugin.DeviceId, StripDevicePlugin.ZoneId, InputCapabilities.Press, 1);
        await fixture.Host.WhenIdleAsync();
    }

    [Test]
    public async Task ShowAsync_KnobWithoutAZoneAction_FillsTheZoneWithTheKnobsDrawing()
    {
        // Arrange
        await using var fixture = new Fixture();

        // Act
        await fixture.ShowAsync(Profile(ProfileId, null, Knob()));

        // Assert
        Assert.That(fixture.ZonePixel(PixelInsideTheTopLeft, PixelInsideTheTopLeft), Is.EqualTo(RedPixel));
        Assert.That(fixture.ZonePixel(StripDevicePlugin.ZoneWidth - CornerInset, StripDevicePlugin.ZoneHeight - CornerInset), Is.EqualTo(RedPixel));
    }

    [Test]
    public async Task ShowAsync_KnobWithoutAZoneAction_DrawsTheKnobAsItselfAtTheZonesFullSize()
    {
        // Arrange
        await using var fixture = new Fixture();

        // Act
        await fixture.ShowAsync(Profile(ProfileId, null, Knob()));

        // Assert
        Assert.That(fixture.KnobAction.Renders, Does.Contain((StripDevicePlugin.KnobId, StripDevicePlugin.ZoneWidth, StripDevicePlugin.ZoneHeight, false)));
    }

    [Test]
    public void LinkedStripHeight_TouchZone_IsBetweenTheMinimumAndHalfTheZone()
    {
        // Arrange
        var zoneHeight = StripDevicePlugin.ZoneHeight;

        // Act
        var strip = ControlRenderer.LinkedStripHeight(zoneHeight);

        // Assert
        Assert.That(strip, Is.InRange(ShortestStrip, zoneHeight / 2));
    }

    [Test]
    public async Task ShowAsync_KnobAndZoneAction_GiveTheTopToTheZoneAndTheBottomStripToTheKnob()
    {
        // Arrange
        await using var fixture = new Fixture();
        var own = StripDevicePlugin.ZoneHeight - ControlRenderer.LinkedStripHeight(StripDevicePlugin.ZoneHeight);

        // Act
        await fixture.ShowAsync(Profile(ProfileId, null, Knob(), Zone()));

        // Assert
        Assert.That(fixture.ZonePixel(2, 2), Is.EqualTo(BluePixel));
        Assert.That(fixture.ZonePixel(2, own - 1), Is.EqualTo(BluePixel));
        Assert.That(fixture.ZonePixel(2, own), Is.EqualTo(RedPixel));
        Assert.That(fixture.ZonePixel(2, StripDevicePlugin.ZoneHeight - 1), Is.EqualTo(RedPixel));
    }

    [Test]
    public async Task ShowAsync_KnobAndZoneAction_DrawTheZoneFullAndTheKnobCompact()
    {
        // Arrange
        await using var fixture = new Fixture();
        var strip = ControlRenderer.LinkedStripHeight(StripDevicePlugin.ZoneHeight);
        var own = StripDevicePlugin.ZoneHeight - strip;

        // Act
        await fixture.ShowAsync(Profile(ProfileId, null, Knob(), Zone()));

        // Assert
        Assert.That(fixture.ZoneAction.Renders, Does.Contain((StripDevicePlugin.ZoneId, StripDevicePlugin.ZoneWidth, own, false)));
        Assert.That(fixture.KnobAction.Renders, Does.Contain((StripDevicePlugin.KnobId, StripDevicePlugin.ZoneWidth, strip, true)));
    }

    [Test]
    public async Task ShowAsync_ZoneWithOnlyABackgroundColor_LetsTheKnobFillTheWholeZone()
    {
        // Arrange
        await using var fixture = new Fixture();

        // Act
        await fixture.ShowAsync(Profile(ProfileId, null, Knob(), GreenZone()));

        // Assert
        Assert.That(fixture.KnobAction.Renders, Does.Contain((StripDevicePlugin.KnobId, StripDevicePlugin.ZoneWidth, StripDevicePlugin.ZoneHeight, false)));
        Assert.That(fixture.KnobAction.Renders.Any(render => render.Compact), Is.False);
        Assert.That(fixture.ZonePixel(2, StripDevicePlugin.ZoneHeight - 1), Is.EqualTo(RedPixel));
    }

    [Test]
    public async Task ReportInput_TapOnAZoneWithOnlyABackgroundColor_PressesTheKnob()
    {
        // Arrange
        await using var fixture = new Fixture();
        await fixture.ShowAsync(Profile(ProfileId, null, Knob(), GreenZone()));

        // Act
        await TapTheZoneAsync(fixture);

        // Assert
        Assert.That(fixture.KnobAction.Executions, Does.Contain((StripDevicePlugin.KnobId, InputCapabilities.Press)));
        Assert.That(fixture.DisplayOnly.Executions, Is.Empty);
    }

    [Test]
    public async Task ShowAsync_KnobDrawingNothingOnAZoneWithABackgroundColor_ShowsTheZoneColor()
    {
        // Arrange
        await using var fixture = new Fixture();
        var knobShowingNothing = Bindings.Single(StripDevicePlugin.KnobId, PluginId, DisplayOnlyAction);

        // Act
        await fixture.ShowAsync(Profile(ProfileId, null, knobShowingNothing, GreenZone()));

        // Assert
        Assert.That(fixture.ZonePixel(2, 2), Is.EqualTo(GreenPixel));
    }

    [Test]
    public async Task ShowAsync_ZoneWithItsOwnText_SharesTheZoneWithTheKnob()
    {
        // Arrange
        await using var fixture = new Fixture();
        var labelledZone = Bindings.Single(StripDevicePlugin.ZoneId, PluginId, DisplayOnlyAction) with { Appearance = new ControlAppearance(Text: "Mute", BackgroundColor: GreenBackground) };

        // Act
        await fixture.ShowAsync(Profile(ProfileId, null, Knob(), labelledZone));

        // Assert
        Assert.That(fixture.KnobAction.Renders.Any(render => render.Compact), Is.True);
        Assert.That(fixture.ZonePixel(2, 2), Is.EqualTo(GreenPixel));
    }

    [Test]
    public async Task ShowAsync_KnobNotShownOnTheDisplay_LeavesTheZoneAtTheBaseColor()
    {
        // Arrange
        await using var fixture = new Fixture();

        // Act
        await fixture.ShowAsync(Profile(ProfileId, null, Knob(showOnDisplay: false)));

        // Assert
        Assert.That(fixture.ZonePixel(2, 2), Is.EqualTo(BasePixel));
        Assert.That(fixture.KnobAction.Renders, Is.Empty);
    }

    [Test]
    public async Task ReportInput_TapOnAZoneThatOnlyShowsTheKnob_PressesAndReleasesTheKnob()
    {
        // Arrange
        await using var fixture = new Fixture();
        await fixture.ShowAsync(Profile(ProfileId, null, Knob()));

        // Act
        await TapTheZoneAsync(fixture);

        // Assert
        Assert.That(
            fixture.KnobAction.Executions,
            Is.EqualTo(new[] { (StripDevicePlugin.KnobId, InputCapabilities.Press), (StripDevicePlugin.KnobId, InputCapabilities.Release) }));
    }

    [Test]
    public async Task ReportInput_TapOnAZoneWithItsOwnAction_RunsTheZonesActionAndNotTheKnobs()
    {
        // Arrange
        await using var fixture = new Fixture();
        await fixture.ShowAsync(Profile(ProfileId, null, Knob(), Zone()));

        // Act
        await TapTheZoneAsync(fixture);

        // Assert
        Assert.That(fixture.KnobAction.Executions, Is.Empty);
        Assert.That(fixture.ZoneAction.Executions.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ReportInput_TapOnAZoneWhereTheKnobIsNotShown_DoesNothing()
    {
        // Arrange
        await using var fixture = new Fixture();
        await fixture.ShowAsync(Profile(ProfileId, null, Knob(showOnDisplay: false)));

        // Act
        await TapTheZoneAsync(fixture);

        // Assert
        Assert.That(fixture.KnobAction.Executions, Is.Empty);
        Assert.That(fixture.ZoneAction.Executions, Is.Empty);
    }

    private static DeviceProfile[] ThreeProfilesWithBackdrops() =>
    [
        Profile("plain", null),
        Profile("red", new ProfileBackground("#FF0000")),
        Profile("also-red", new ProfileBackground("#FF0000")),
    ];

    [Test]
    public async Task ShowAsync_ProfileWithoutABackgroundColor_SendsTheBaseBackdropBeforeTheImages()
    {
        // Arrange
        await using var fixture = new Fixture();

        // Act
        await fixture.ShowAsync(ThreeProfilesWithBackdrops());

        // Assert
        Assert.That(fixture.Plugin.Calls, Is.EqualTo(new[] { BaseBackdrop, ZoneImage }));
    }

    [Test]
    public async Task SetActiveProfile_ProfileWithAnotherColor_SendsTheBackdropThenEveryImageAgain()
    {
        // Arrange
        await using var fixture = new Fixture();
        await fixture.ShowAsync(ThreeProfilesWithBackdrops());
        fixture.Plugin.Calls.Clear();

        // Act
        fixture.Host.SetActiveProfile("red");
        await fixture.Host.WhenIdleAsync();

        // Assert
        Assert.That(fixture.Plugin.Calls, Is.EqualTo(new[] { RedBackdrop, ZoneImage }));
    }

    [Test]
    public async Task SetActiveProfile_ProfileWithTheSameColorAndPixels_SendsNothing()
    {
        // Arrange
        await using var fixture = new Fixture();
        await fixture.ShowAsync(ThreeProfilesWithBackdrops());
        fixture.Host.SetActiveProfile("red");
        await fixture.Host.WhenIdleAsync();
        fixture.Plugin.Calls.Clear();

        // Act
        fixture.Host.SetActiveProfile("also-red");
        await fixture.Host.WhenIdleAsync();

        // Assert
        Assert.That(fixture.Plugin.Calls, Is.Empty);
    }

    [Test]
    public async Task RequestRedraw_AfterTheDeviceOverwroteItsScreen_SendsTheBackdropAndTheImagesAgain()
    {
        // Arrange
        await using var fixture = new Fixture();
        await fixture.ShowAsync(Profile("plain", null));
        fixture.Plugin.Calls.Clear();

        // Act (what a device plugin asks for after it overwrote the screen: the N4 Pro's idle screen)
        fixture.Plugin.Context!.RequestRedraw(StripDevicePlugin.DeviceId);
        await fixture.Host.WhenIdleAsync();

        // Assert
        Assert.That(fixture.Plugin.Calls, Is.EqualTo(new[] { BaseBackdrop, ZoneImage }));
    }

    private static DeckHost.Core.Profiles.ProfileStore SavedProfileWithAHiddenKnob(TempProfilesFile file)
    {
        var store = new DeckHost.Core.Profiles.ProfileStore(file.Path);
        store.Save(new DeckHost.Core.Profiles.ProfileDocument(1, ProfileId, [Profile(ProfileId, null, Knob(showOnDisplay: false), Zone())]));
        return store;
    }

    [Test]
    public void Load_KnobHiddenFromItsDisplay_KeepsTheChoice()
    {
        // Arrange
        using var file = new TempProfilesFile();
        var store = SavedProfileWithAHiddenKnob(file);

        // Act
        var bindings = store.Load()!.Profiles.Single().Bindings;

        // Assert
        Assert.That(bindings.Single(binding => binding.ControlId == StripDevicePlugin.KnobId).Appearance.ShowOnLinkedDisplay, Is.False);
    }

    [Test]
    public void Load_ZoneBindingWithoutTheChoice_ShowsOnTheLinkedDisplay()
    {
        // Arrange
        using var file = new TempProfilesFile();
        var store = SavedProfileWithAHiddenKnob(file);

        // Act
        var bindings = store.Load()!.Profiles.Single().Bindings;

        // Assert
        Assert.That(bindings.Single(binding => binding.ControlId == StripDevicePlugin.ZoneId).Appearance.ShowOnLinkedDisplay, Is.True);
    }

    [Test]
    public void Deserialize_AppearanceFromAnOlderFileWithoutTheField_ShowsOnTheLinkedDisplay()
    {
        // Arrange
        const string oldFile = """{ "Text": "Hi" }""";

        // Act
        var appearance = System.Text.Json.JsonSerializer.Deserialize<ControlAppearance>(oldFile)!;

        // Assert
        Assert.That(appearance.ShowOnLinkedDisplay, Is.True);
    }

    [TestCase(null, 0x0B, 0x12, 0x20)]
    [TestCase("#FF8000", 0xFF, 0x80, 0x00)]
    [TestCase("not a color", 0x0B, 0x12, 0x20)]
    [TestCase("#80FFFFFF", 0x85, 0x89, 0x90)] // half-transparent white over the base color
    public void GetColor_ProfileColor_IsTheColorOverTheBase(string? color, byte red, byte green, byte blue)
    {
        // Arrange
        var background = color is null ? null : new ProfileBackground(color);

        // Act
        var result = ProfileBackdrop.GetColor(background);

        // Assert
        Assert.That(result, Is.EqualTo((red, green, blue)));
    }
}
