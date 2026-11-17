using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.MiraBox;

namespace DeckHost.Tests;

public class MiraBoxModelTests
{
    private const string DeviceId = "dev";
    private const string N4ProName = "N4 Pro";
    private const string FirstTouchZone = "touch.0";
    private const int KeysOnTheN4Pro = 10;
    private const int KnobsOnTheN4Pro = 4;
    private const int TouchControlsOnTheN4Pro = 5;
    private const int KeySize = 112;
    private const int ZoneWidth = 176;
    private const int ZoneHeight = 112;
    private const int KnobCount = 4;

    private static DeviceDefinition N4Pro() => MiraBoxModels.N4Pro.Describe(DeviceId, N4ProName, info: null, isConnected: false, detail: null);

    private static DeviceDefinition Describe(MiraBoxModel model) => model.Describe(model.DefaultDeviceId, model.ShortName, info: null, isConnected: false, detail: null);

    private static int Count(DeviceDefinition device, ControlType type) => device.Controls.Count(control => control.Type == type);

    private static IReadOnlyList<string> KnobsNotShownOnTheZoneAboveThem(DeviceDefinition device) =>
    [
        .. Enumerable.Range(0, KnobCount)
            .Where(index => device.FindControl($"encoder.{index}")!.DisplayControlId != $"touch.{index}")
            .Select(static index => $"encoder.{index}"),
    ];

    private static IReadOnlyList<string> KnobsWithoutTheirZoneDirectlyAbove(DeviceDefinition device) =>
    [
        .. Enumerable.Range(0, KnobCount).Where(index =>
        {
            var knob = device.FindControl($"encoder.{index}")!;
            var zone = device.FindControl($"touch.{index}")!;
            var knobCenter = knob.Bounds.X + (knob.Bounds.Width / 2);
            return !(zone.Bounds.Y < knob.Bounds.Y && knobCenter >= zone.Bounds.X && knobCenter <= zone.Bounds.X + zone.Bounds.Width);
        }).Select(static index => $"encoder.{index}"),
    ];

    [Test]
    public void Describe_N4Pro_HasTenKeys()
    {
        // Arrange
        var device = N4Pro();

        // Act
        var keys = Count(device, ControlType.Key);

        // Assert
        Assert.That(keys, Is.EqualTo(KeysOnTheN4Pro));
    }

    [Test]
    public void Describe_N4Pro_HasFourKnobs()
    {
        // Arrange
        var device = N4Pro();

        // Act
        var knobs = Count(device, ControlType.Encoder);

        // Assert
        Assert.That(knobs, Is.EqualTo(KnobsOnTheN4Pro));
    }

    [Test]
    public void Describe_N4Pro_HasFiveTouchControls()
    {
        // Arrange
        var device = N4Pro();

        // Act
        var touch = Count(device, ControlType.TouchSurface);

        // Assert
        Assert.That(touch, Is.EqualTo(TouchControlsOnTheN4Pro));
    }

    [Test]
    public void Describe_N4Pro_GivesEveryKeyASquareImage()
    {
        // Arrange
        var device = N4Pro();

        // Act
        var sizes = device.Controls.Where(static control => control.Type == ControlType.Key).Select(static key => (key.RenderWidth, key.RenderHeight));

        // Assert
        Assert.That(sizes, Is.All.EqualTo((KeySize, KeySize)));
    }

    [Test]
    public void Describe_N4Pro_GivesTheTouchZoneAWideImage()
    {
        // Arrange
        var device = N4Pro();

        // Act
        var zone = device.FindControl(FirstTouchZone)!;

        // Assert
        Assert.That((zone.RenderWidth, zone.RenderHeight), Is.EqualTo((ZoneWidth, ZoneHeight)));
    }

    [Test]
    public void Describe_N4Pro_TiesOneLedToEachKnob()
    {
        // Arrange
        var device = N4Pro();

        // Act
        var leds = device.Controls.Where(static control => control.HasLight).Select(static control => control.LedIndex!.Value);

        // Assert
        Assert.That(leds, Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void Describe_N4Pro_ShowsEachKnobOnTheTouchZoneAboveIt()
    {
        // Arrange
        var device = N4Pro();

        // Act
        var wrong = KnobsNotShownOnTheZoneAboveThem(device);

        // Assert
        Assert.That(wrong, Is.Empty);
    }

    [Test]
    public void Describe_N4Pro_PlacesEachTouchZoneDirectlyAboveItsKnob()
    {
        // Arrange
        var device = N4Pro();

        // Act
        var wrong = KnobsWithoutTheirZoneDirectlyAbove(device);

        // Assert
        Assert.That(wrong, Is.Empty);
    }

    [Test]
    public void Describe_N4Pro_LinksNoControlButKnobsToADisplay()
    {
        // Arrange
        var device = N4Pro();

        // Act
        var linked = device.Controls.Where(static control => control.Type != ControlType.Encoder && control.DisplayControlId is not null);

        // Assert
        Assert.That(linked, Is.Empty);
    }

    [TestCase("key.0", 11)]
    [TestCase("key.4", 15)]
    [TestCase("key.5", 6)]
    [TestCase("key.9", 10)]
    [TestCase("touch.0", 1)]
    [TestCase("touch.3", 4)]
    public void GetImageKeyId_ControlWithAnImage_FollowsTheSdkNumbering(string controlId, int sdkKeyId)
    {
        // Arrange
        var model = MiraBoxModels.N4Pro;

        // Act
        var id = model.GetImageKeyId(controlId);

        // Assert
        Assert.That(id, Is.EqualTo(sdkKeyId));
    }

    [TestCase("encoder.0")]
    [TestCase("touch.swipe")]
    [TestCase("key.10")]
    public void GetImageKeyId_ControlWithoutAnImage_IsNull(string controlId)
    {
        // Arrange
        var model = MiraBoxModels.N4Pro;

        // Act
        var id = model.GetImageKeyId(controlId);

        // Assert
        Assert.That(id, Is.Null);
    }

    [TestCase((int)MiraBoxEventType.KeyPress, 11, "key.0", InputCapabilities.Press, 1)]
    [TestCase((int)MiraBoxEventType.KeyRelease, 6, "key.5", InputCapabilities.Release, 0)]
    [TestCase((int)MiraBoxEventType.KeyRelease, 2, "touch.1", InputCapabilities.Press, 1)]
    [TestCase((int)MiraBoxEventType.KnobLeft, 16, "encoder.0", InputCapabilities.Rotate, -1)]
    [TestCase((int)MiraBoxEventType.KnobRight, 23, "encoder.3", InputCapabilities.Rotate, 1)]
    [TestCase((int)MiraBoxEventType.KnobPress, 25, "encoder.1", InputCapabilities.Press, 1)]
    [TestCase((int)MiraBoxEventType.SwipeLeft, 28, "touch.swipe", InputCapabilities.Swipe, -1)]
    [TestCase((int)MiraBoxEventType.SwipeRight, 29, "touch.swipe", InputCapabilities.Swipe, 1)]
    public void TryMapInput_N4ProEvent_MapsToItsControl(int eventType, int sdkId, string controlId, InputCapabilities inputEvent, int value)
    {
        // Arrange
        var model = MiraBoxModels.N4Pro;

        // Act
        var mapped = model.TryMapInput((MiraBoxEventType)eventType, sdkId, out var mappedControl, out var mappedEvent, out var mappedValue);

        // Assert
        Assert.That(mapped, Is.True);
        Assert.That((mappedControl, mappedEvent, mappedValue), Is.EqualTo((controlId, inputEvent, value)));
    }

    [TestCase((int)MiraBoxEventType.KeyPress, 2)]
    [TestCase((int)MiraBoxEventType.KnobLeft, 17)]
    [TestCase((int)MiraBoxEventType.ToggleUp, 1)]
    public void TryMapInput_UnusedEvent_IsIgnored(int eventType, int sdkId)
    {
        // Arrange
        var model = MiraBoxModels.N4Pro;

        // Act
        var mapped = model.TryMapInput((MiraBoxEventType)eventType, sdkId, out _, out _, out _);

        // Assert
        Assert.That(mapped, Is.False);
    }

    private const string BridgeInfoJson = """{"deviceKey":"\\\\?\\hid#1","originType":7,"vendorId":21832,"productId":4104,"serialNumber":"ABC","firmwareVersion":"1.0","width":800,"height":480,"keyWidth":112,"keyHeight":112,"minKey":6,"maxKey":15,"hasSecondScreen":true,"minSecondScreenKey":1,"maxSecondScreenKey":4,"secondScreenWidth":176,"secondScreenHeight":112,"hasRgbLed":true,"ledCount":4}""";

    [Test]
    public void FromJson_DeviceInfoFromTheBridge_IsTheN4ProModel()
    {
        // Arrange
        var json = BridgeInfoJson;

        // Act
        var info = MiraBoxDeviceInfo.FromJson(json);

        // Assert
        Assert.That(info, Is.Not.Null);
        Assert.That(MiraBoxModels.For(info.OriginType), Is.SameAs(MiraBoxModels.N4Pro));
    }

    [Test]
    public void FromJson_DeviceInfoFromTheBridge_KeepsTheDeviceKeyAndLedCount()
    {
        // Arrange
        var json = BridgeInfoJson;

        // Act
        var info = MiraBoxDeviceInfo.FromJson(json)!;

        // Assert
        Assert.That(info.DeviceKey, Is.EqualTo(@"\\?\hid#1"));
        Assert.That(info.LedCount, Is.EqualTo(4));
    }

    // ---------------------------------------------------------------- every model

    public static IEnumerable<TestCaseData> Models => MiraBoxModels.All.Select(model => new TestCaseData(model.OriginType).SetName($"{{m}}({model.ShortName})"));

    private static IReadOnlyList<string> ControlsWithAnImageButNoImageKeyId(MiraBoxModel model, DeviceDefinition device) =>
        [.. device.Controls.Where(static control => control.HasImageOutput).Where(control => model.GetImageKeyId(control.Id) is null).Select(static control => control.Id)];

    private static IReadOnlyList<string> EventsThatDoNotMapBackToTheirControl(MiraBoxModel model) =>
    [
        .. model.Controls.SelectMany(control => control.Events.Where(input =>
        {
            var mapped = model.TryMapInput(input.Event, input.SdkId, out var controlId, out var inputEvent, out var value);
            return !(mapped && (controlId, inputEvent, value) == (control.Id, input.Input, input.Value));
        }).Select(input => $"{control.Id}:{input.Event}")),
    ];

    private static IReadOnlyList<string> EventsOfInputsTheControlDoesNotHave(MiraBoxModel model) =>
        [.. model.Controls.SelectMany(control => control.Events.Where(input => !control.Inputs.HasFlag(input.Input)).Select(input => $"{control.Id}:{input.Input}"))];

    private static IReadOnlyList<string> KeysOutOfReadingOrder(MiraBoxModel model)
    {
        var keys = model.Controls.Where(static control => control.Id.StartsWith("key.", StringComparison.Ordinal)).ToList();
        return [.. Enumerable.Range(1, Math.Max(0, keys.Count - 1)).Where(index =>
        {
            var (previous, current) = (keys[index - 1].Bounds, keys[index].Bounds);
            return !(current.Y > previous.Y || (current.Y == previous.Y && current.X > previous.X));
        }).Select(index => keys[index].Id)];
    }

    [TestCaseSource(nameof(Models))]
    public void FindDescriptorProblems_EveryModel_FindsNone(int originType)
    {
        // Arrange
        var device = Describe(MiraBoxModels.For(originType)!);

        // Act
        var problems = device.FindDescriptorProblems();

        // Assert
        Assert.That(problems, Is.Empty);
    }

    [TestCaseSource(nameof(Models))]
    public void Describe_EveryModel_NamesTheModelInItsIdentity(int originType)
    {
        // Arrange
        var model = MiraBoxModels.For(originType)!;

        // Act
        var device = Describe(model);

        // Assert
        Assert.That(device.Identity!.Model, Is.EqualTo(model.ModelName));
    }

    [TestCaseSource(nameof(Models))]
    public void Describe_EveryModel_GivesEveryControlItsOwnId(int originType)
    {
        // Arrange
        var model = MiraBoxModels.For(originType)!;

        // Act
        var device = Describe(model);

        // Assert
        Assert.That(device.Controls.Select(static control => control.Id), Is.Unique);
    }

    [TestCaseSource(nameof(Models))]
    public void GetImageKeyId_EveryControlWithAnImage_HasAnImageKey(int originType)
    {
        // Arrange
        var model = MiraBoxModels.For(originType)!;
        var device = Describe(model);

        // Act
        var missing = ControlsWithAnImageButNoImageKeyId(model, device);

        // Assert
        Assert.That(missing, Is.Empty);
    }

    [TestCaseSource(nameof(Models))]
    public void TryMapInput_EveryEventOfEveryModel_MapsBackToOneControl(int originType)
    {
        // Arrange
        var model = MiraBoxModels.For(originType)!;

        // Act
        var broken = EventsThatDoNotMapBackToTheirControl(model);

        // Assert
        Assert.That(broken, Is.Empty);
    }

    [TestCaseSource(nameof(Models))]
    public void Events_EveryEventOfEveryModel_IsOneTheControlCanReport(int originType)
    {
        // Arrange
        var model = MiraBoxModels.For(originType)!;

        // Act
        var impossible = EventsOfInputsTheControlDoesNotHave(model);

        // Assert
        Assert.That(impossible, Is.Empty);
    }

    [TestCaseSource(nameof(Models))]
    public void Controls_EveryModel_HasEachImageKeyIdOnce(int originType)
    {
        // Arrange
        var model = MiraBoxModels.For(originType)!;

        // Act
        var imageIds = model.Controls.Where(static control => control.ImageKeyId is not null).Select(static control => control.ImageKeyId!.Value);

        // Assert
        Assert.That(imageIds, Is.Unique);
    }

    [TestCaseSource(nameof(Models))]
    public void Controls_EveryModel_NumbersItsKeysInReadingOrder(int originType)
    {
        // Arrange
        var model = MiraBoxModels.For(originType)!;

        // Act
        var outOfOrder = KeysOutOfReadingOrder(model);

        // Assert
        Assert.That(outOfOrder, Is.Empty);
    }

    [Test]
    public void BottomLeftRows_FiveColumnsAndThreeRows_PutsTheHighestRowFirst()
    {
        // Arrange
        var expected = new[] { 11, 12, 13, 14, 15, 6, 7, 8, 9, 10, 1, 2, 3, 4, 5 };

        // Act
        var numbers = MiraBoxModels.BottomLeftRows(5, 3, 1);

        // Assert
        Assert.That(numbers, Is.EqualTo(expected));
    }

    [Test]
    public void TopRightColumns_FiveColumnsAndThreeRows_CountsFromTheTopRightDownTheColumns()
    {
        // Arrange (top-right is 1, the key below it 2, the top-left key is 13)
        var expected = new[] { 13, 10, 7, 4, 1, 14, 11, 8, 5, 2, 15, 12, 9, 6, 3 };

        // Act
        var numbers = MiraBoxModels.TopRightColumns(5, 3, 1);

        // Assert
        Assert.That(numbers, Is.EqualTo(expected));
    }

    [Test]
    public void GetImageKeyId_293sTopRightKey_IsOne()
    {
        // Arrange
        var model = MiraBoxModels.Sd293s;

        // Act
        var id = model.GetImageKeyId("key.4");

        // Assert
        Assert.That(id, Is.EqualTo(1));
    }

    [Test]
    public void GetImageKeyId_293sSecondScreenTile_IsSixteen()
    {
        // Arrange
        var model = MiraBoxModels.Sd293s;

        // Act
        var id = model.GetImageKeyId("screen.0");

        // Assert
        Assert.That(id, Is.EqualTo(16));
    }

    [Test]
    public void Inputs_293sSecondScreenTile_AreNone()
    {
        // Arrange
        var model = MiraBoxModels.Sd293s;

        // Act
        var inputs = model.Find("screen.0")!.Inputs;

        // Assert
        Assert.That(inputs, Is.EqualTo(InputCapabilities.None));
    }

    [TestCase(MiraBoxModels.Origin.N4, 4, 0, true)]
    [TestCase(MiraBoxModels.Origin.SD293, 0, 0, false)]
    [TestCase(MiraBoxModels.Origin.XL, 2, 0, false)]
    [TestCase(MiraBoxModels.Origin.M3, 3, 0, false)]
    [TestCase(MiraBoxModels.Origin.N3, 3, 3, true)]
    [TestCase(MiraBoxModels.Origin.N1, 1, 0, false)]
    [TestCase(MiraBoxModels.Origin.K1Pro, 3, 0, false)]
    [TestCase(MiraBoxModels.Origin.Mini, 2, 0, false)]
    [TestCase(MiraBoxModels.Origin.M18, 0, 3, false)]
    public void Controls_Model_HasItsKnobsAndButtons(int originType, int knobs, int buttons, bool addsKnobRelease)
    {
        // Arrange
        var model = MiraBoxModels.For(originType)!;

        // Act
        var (knobCount, buttonCount) = (model.Controls.Count(static control => control.Type == ControlType.Encoder), model.Controls.Count(static control => control.Type == ControlType.Button));

        // Assert
        Assert.That((knobCount, buttonCount), Is.EqualTo((knobs, buttons)));
        Assert.That(model.KnobsReportNoRelease, Is.EqualTo(addsKnobRelease));
    }

    [TestCase(MiraBoxModels.Origin.N4)]
    [TestCase(MiraBoxModels.Origin.SD293)]
    [TestCase(MiraBoxModels.Origin.XL)]
    [TestCase(MiraBoxModels.Origin.M3)]
    [TestCase(MiraBoxModels.Origin.N3)]
    [TestCase(MiraBoxModels.Origin.N1)]
    [TestCase(MiraBoxModels.Origin.K1Pro)]
    [TestCase(MiraBoxModels.Origin.Mini)]
    [TestCase(MiraBoxModels.Origin.M18)]
    public void LedCount_ModelOtherThanTheN4Pro_IsZeroAsOnlyTheN4ProsLedsAreTiedToControls(int originType)
    {
        // Arrange
        var model = MiraBoxModels.For(originType)!;

        // Act
        var leds = model.LedCount;

        // Assert
        Assert.That(leds, Is.Zero);
    }

    [TestCase(MiraBoxModels.Origin.XL, (int)MiraBoxEventType.KeyPress, 25, "key.0", InputCapabilities.Press, 1)]
    [TestCase(MiraBoxModels.Origin.XL, (int)MiraBoxEventType.KeyRelease, 8, "key.31", InputCapabilities.Release, 0)]
    [TestCase(MiraBoxModels.Origin.XL, (int)MiraBoxEventType.DipLeft, 33, "encoder.0", InputCapabilities.Rotate, 1)]
    [TestCase(MiraBoxModels.Origin.XL, (int)MiraBoxEventType.DipRight, 34, "encoder.0", InputCapabilities.Rotate, -1)]
    [TestCase(MiraBoxModels.Origin.XL, (int)MiraBoxEventType.DipLeft, 35, "encoder.1", InputCapabilities.Rotate, -1)]
    [TestCase(MiraBoxModels.Origin.XL, (int)MiraBoxEventType.DipRight, 36, "encoder.1", InputCapabilities.Rotate, 1)]
    [TestCase(MiraBoxModels.Origin.XL, (int)MiraBoxEventType.DipPress, 37, "encoder.0", InputCapabilities.Press, 1)]
    [TestCase(MiraBoxModels.Origin.M18, (int)MiraBoxEventType.KeyPress, 17, "button.1", InputCapabilities.Press, 1)]
    [TestCase(MiraBoxModels.Origin.M3, (int)MiraBoxEventType.KnobRight, 21, "encoder.0", InputCapabilities.Rotate, 1)]
    [TestCase(MiraBoxModels.Origin.M3, (int)MiraBoxEventType.KnobRelease, 22, "encoder.2", InputCapabilities.Release, 0)]
    [TestCase(MiraBoxModels.Origin.N3, (int)MiraBoxEventType.KnobPress, 12, "encoder.0", InputCapabilities.Press, 1)]
    [TestCase(MiraBoxModels.Origin.N3, (int)MiraBoxEventType.KeyPress, 7, "button.0", InputCapabilities.Press, 1)]
    [TestCase(MiraBoxModels.Origin.N1, (int)MiraBoxEventType.KeyPress, 16, "screen.0", InputCapabilities.Press, 1)]
    [TestCase(MiraBoxModels.Origin.N1, (int)MiraBoxEventType.KnobPress, 18, "encoder.0", InputCapabilities.Press, 1)]
    [TestCase(MiraBoxModels.Origin.N1, (int)MiraBoxEventType.KnobLeft, 19, "encoder.0", InputCapabilities.Rotate, -1)]
    [TestCase(MiraBoxModels.Origin.K1Pro, (int)MiraBoxEventType.KnobLeft, 12, "encoder.2", InputCapabilities.Rotate, -1)]
    [TestCase(MiraBoxModels.Origin.Mini, (int)MiraBoxEventType.DipRight, 11, "encoder.1", InputCapabilities.Rotate, 1)]
    public void TryMapInput_ModelEvent_MapsToItsControl(int originType, int eventType, int sdkId, string controlId, InputCapabilities inputEvent, int value)
    {
        // Arrange
        var model = MiraBoxModels.For(originType)!;

        // Act
        var mapped = model.TryMapInput((MiraBoxEventType)eventType, sdkId, out var mappedControl, out var mappedEvent, out var mappedValue);

        // Assert
        Assert.That(mapped, Is.True);
        Assert.That((mappedControl, mappedEvent, mappedValue), Is.EqualTo((controlId, inputEvent, value)));
    }

    private static DeviceDefinition Xl() => MiraBoxModels.XL.Describe("mirabox.xl", "XL", null, false, null);

    [Test]
    public void Describe_XL_PutsAWheelOnEachSideOfItsKeys()
    {
        // Arrange
        var device = Xl();

        // Act
        var (left, right) = (device.FindControl("encoder.0")!, device.FindControl("encoder.1")!);

        // Assert
        Assert.That((left.Side, right.Side), Is.EqualTo((DeviceSide.Left, DeviceSide.Right)));
    }

    [Test]
    public void Describe_XL_PlacesTheWheelsOutsideTheKeys()
    {
        // Arrange
        var device = Xl();
        var keys = device.Controls.Where(static control => control.Type == ControlType.Key).ToList();

        // Act
        var (left, right) = (device.FindControl("encoder.0")!, device.FindControl("encoder.1")!);

        // Assert
        Assert.That(left.Bounds.X + left.Bounds.Width, Is.LessThan(keys.Min(static key => key.Bounds.X)));
        Assert.That(right.Bounds.X, Is.GreaterThan(keys.Max(static key => key.Bounds.X + key.Bounds.Width)));
    }

    [Test]
    public void Describe_XL_KeepsEveryControlButTheWheelsOnTheFront()
    {
        // Arrange
        var device = Xl();

        // Act
        var sides = device.Controls.Where(static control => control.Type != ControlType.Encoder).Select(static control => control.Side);

        // Assert
        Assert.That(sides, Is.All.EqualTo(DeviceSide.Front));
    }

    [Test]
    public void Describe_N3_PutsItsKnobsToTheRightOfItsKeys()
    {
        // Arrange
        var device = MiraBoxModels.N3.Describe("mirabox.n3", "N3", null, false, null);
        var rightOfKeys = device.Controls.Where(static control => control.Type is ControlType.Key or ControlType.Button).Max(static control => control.Bounds.X + control.Bounds.Width);

        // Act
        var knobLefts = device.Controls.Where(static control => control.Type == ControlType.Encoder).Select(static knob => knob.Bounds.X);

        // Assert
        Assert.That(knobLefts, Is.All.GreaterThan(rightOfKeys));
    }

    private static DeviceDefinition N1() => MiraBoxModels.N1.Describe("mirabox.n1", "N1", null, false, null);

    [Test]
    public void Describe_N1_ShowsTheKnobOnTheThirdSmallDisplay()
    {
        // Arrange
        var device = N1();

        // Act
        var knob = device.FindControl("encoder.0")!;

        // Assert
        Assert.That(knob.DisplayControlId, Is.EqualTo("screen.2"));
    }

    [Test]
    public void Describe_N1_PutsTheSmallDisplaysOnTheSecondScreen()
    {
        // Arrange
        var device = N1();

        // Act
        var tile = device.FindControl("screen.2")!;

        // Assert
        Assert.That(tile.DisplayId, Is.EqualTo("second"));
    }

    [Test]
    public void Describe_N1_GivesTheSmallDisplaysASmallerImageThanTheKeys()
    {
        // Arrange
        var device = N1();

        // Act
        var widths = (device.FindControl("screen.2")!.RenderWidth, device.FindControl("key.0")!.RenderWidth);

        // Assert
        Assert.That(widths, Is.EqualTo((64, 96)));
    }

    [Test]
    public void Describe_SizesTheSdkReports_WinOverTheModelDefaults()
    {
        // Arrange (the 293 V2 has 100 px keys, the V3 112 px; both are origin type SD293)
        var info = MiraBoxDeviceInfo.FromJson("""{"deviceKey":"k","originType":1,"keyWidth":100,"keyHeight":100,"width":800,"height":480}""")!;

        // Act
        var device = MiraBoxModels.Sd293.Describe("mirabox.293", "293", info, true, null);

        // Assert
        var key = device.FindControl("key.0")!;
        Assert.That((key.RenderWidth, key.RenderHeight), Is.EqualTo((100, 100)));
    }

    private static IReadOnlyList<string> SettingKeys(MiraBoxModel model) => [.. MiraBoxDeviceSettings.For(model).Select(static setting => setting.Key)];

    [Test]
    public void For_N4Pro_OffersVibrationAndLedBrightness()
    {
        // Arrange
        var model = MiraBoxModels.N4Pro;

        // Act
        var keys = SettingKeys(model);

        // Assert
        Assert.That(keys, Does.Contain(MiraBoxDeviceSettings.Vibration).And.Contain(MiraBoxDeviceSettings.LedBrightness));
    }

    [Test]
    public void For_XL_OffersNeitherVibrationNorLedBrightness()
    {
        // Arrange
        var model = MiraBoxModels.XL;

        // Act
        var keys = SettingKeys(model);

        // Assert
        Assert.That(keys, Does.Not.Contain(MiraBoxDeviceSettings.Vibration).And.Not.Contain(MiraBoxDeviceSettings.LedBrightness));
    }

    [Test]
    public void For_XL_OffersKeyBrightnessAndTheIdleScreen()
    {
        // Arrange
        var model = MiraBoxModels.XL;

        // Act
        var keys = SettingKeys(model);

        // Assert
        Assert.That(keys, Does.Contain(MiraBoxDeviceSettings.KeyBrightness).And.Contain(MiraBoxDeviceSettings.IdleScreen));
    }
}
