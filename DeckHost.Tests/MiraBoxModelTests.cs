using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.MiraBox;

namespace DeckHost.Tests;

public class MiraBoxModelTests
{
    [Test]
    public void Layout_matches_the_N4_Pro_hardware()
    {
        var device = MiraBoxModels.N4Pro.Describe("dev", "N4 Pro", info: null, isConnected: false, detail: null);

        Assert.That(device.Controls.Count(control => control.Type == ControlType.Key), Is.EqualTo(10));
        Assert.That(device.Controls.Count(control => control.Type == ControlType.Encoder), Is.EqualTo(4));
        Assert.That(device.Controls.Count(control => control.Type == ControlType.TouchSurface), Is.EqualTo(5));
        Expect.All(device.Controls.Where(control => control.Type == ControlType.Key), key => Assert.That((key.RenderWidth, key.RenderHeight), Is.EqualTo((112, 112))));
        Assert.That((device.FindControl("touch.0")!.RenderWidth, device.FindControl("touch.0")!.RenderHeight), Is.EqualTo((176, 112)));
        Assert.That(device.Controls.Where(control => control.HasLight).Select(control => control.LedIndex!.Value), Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void Each_knob_shows_on_the_touch_zone_above_it()
    {
        var device = MiraBoxModels.N4Pro.Describe("dev", "N4 Pro", info: null, isConnected: false, detail: null);

        for (var index = 0; index < 4; index++)
        {
            var knob = device.FindControl($"encoder.{index}")!;
            var zone = device.FindControl($"touch.{index}")!;
            Assert.That(knob.DisplayControlId, Is.EqualTo(zone.Id));

            // The zone is directly above the knob, which is what the strip placement relies on.
            Assert.That(zone.Bounds.Y < knob.Bounds.Y, Is.True);
            Assert.That(knob.Bounds.X + (knob.Bounds.Width / 2), Is.InRange(zone.Bounds.X, zone.Bounds.X + zone.Bounds.Width));
        }

        Expect.All(device.Controls.Where(control => control.Type != ControlType.Encoder), control => Assert.That(control.DisplayControlId, Is.Null));
    }

    [TestCase("key.0", 11)]
    [TestCase("key.4", 15)]
    [TestCase("key.5", 6)]
    [TestCase("key.9", 10)]
    [TestCase("touch.0", 1)]
    [TestCase("touch.3", 4)]
    public void Image_key_ids_follow_the_SDK_numbering(string controlId, int sdkKeyId)
    {
        Assert.That(MiraBoxModels.N4Pro.GetImageKeyId(controlId), Is.EqualTo(sdkKeyId));
    }

    [TestCase("encoder.0")]
    [TestCase("touch.swipe")]
    [TestCase("key.10")]
    public void Controls_without_images_have_no_image_key(string controlId)
    {
        Assert.That(MiraBoxModels.N4Pro.GetImageKeyId(controlId), Is.Null);
    }

    [TestCase((int)MiraBoxEventType.KeyPress, 11, "key.0", InputCapabilities.Press, 1)]
    [TestCase((int)MiraBoxEventType.KeyRelease, 6, "key.5", InputCapabilities.Release, 0)]
    [TestCase((int)MiraBoxEventType.KeyRelease, 2, "touch.1", InputCapabilities.Press, 1)]
    [TestCase((int)MiraBoxEventType.KnobLeft, 16, "encoder.0", InputCapabilities.Rotate, -1)]
    [TestCase((int)MiraBoxEventType.KnobRight, 23, "encoder.3", InputCapabilities.Rotate, 1)]
    [TestCase((int)MiraBoxEventType.KnobPress, 25, "encoder.1", InputCapabilities.Press, 1)]
    [TestCase((int)MiraBoxEventType.SwipeLeft, 28, "touch.swipe", InputCapabilities.Swipe, -1)]
    [TestCase((int)MiraBoxEventType.SwipeRight, 29, "touch.swipe", InputCapabilities.Swipe, 1)]
    public void Input_events_map_to_controls(int eventType, int sdkId, string controlId, InputCapabilities inputEvent, int value)
    {
        Assert.That(MiraBoxModels.N4Pro.TryMapInput((MiraBoxEventType)eventType, sdkId, out var mappedControl, out var mappedEvent, out var mappedValue), Is.True);
        Assert.That((mappedControl, mappedEvent, mappedValue), Is.EqualTo((controlId, inputEvent, value)));
    }

    [TestCase((int)MiraBoxEventType.KeyPress, 2)]
    [TestCase((int)MiraBoxEventType.KnobLeft, 17)]
    [TestCase((int)MiraBoxEventType.ToggleUp, 1)]
    public void Unused_events_are_ignored(int eventType, int sdkId)
    {
        Assert.That(MiraBoxModels.N4Pro.TryMapInput((MiraBoxEventType)eventType, sdkId, out _, out _, out _), Is.False);
    }

    [Test]
    public void Device_info_json_from_the_bridge_parses()
    {
        const string json = """{"deviceKey":"\\\\?\\hid#1","originType":7,"vendorId":21832,"productId":4104,"serialNumber":"ABC","firmwareVersion":"1.0","width":800,"height":480,"keyWidth":112,"keyHeight":112,"minKey":6,"maxKey":15,"hasSecondScreen":true,"minSecondScreenKey":1,"maxSecondScreenKey":4,"secondScreenWidth":176,"secondScreenHeight":112,"hasRgbLed":true,"ledCount":4}""";

        var info = MiraBoxDeviceInfo.FromJson(json);

        Assert.That(info, Is.Not.Null);
        Assert.That(MiraBoxModels.For(info.OriginType), Is.SameAs(MiraBoxModels.N4Pro));
        Assert.That(info.DeviceKey, Is.EqualTo(@"\\?\hid#1"));
        Assert.That(info.LedCount, Is.EqualTo(4));
    }

    // ---------------------------------------------------------------- every model

    public static IEnumerable<TestCaseData> Models => MiraBoxModels.All.Select(model => new TestCaseData(model.OriginType).SetName($"{{m}}({model.ShortName})"));

    [TestCaseSource(nameof(Models))]
    public void Every_model_has_a_consistent_descriptor(int originType)
    {
        var model = MiraBoxModels.For(originType)!;
        var device = model.Describe(model.DefaultDeviceId, model.ShortName, info: null, isConnected: false, detail: null);

        Assert.That(device.FindDescriptorProblems(), Is.Empty);
        Assert.That(device.Identity!.Model, Is.EqualTo(model.ModelName));
        Assert.That(device.Controls.Select(static control => control.Id), Is.Unique);
        Expect.All(device.Controls.Where(static control => control.HasImageOutput), control => Assert.That(model.GetImageKeyId(control.Id), Is.Not.Null));
    }

    [TestCaseSource(nameof(Models))]
    public void Every_model_maps_each_SDK_event_to_one_control_and_back(int originType)
    {
        var model = MiraBoxModels.For(originType)!;

        foreach (var control in model.Controls)
        {
            foreach (var input in control.Events)
            {
                Assert.That(model.TryMapInput(input.Event, input.SdkId, out var controlId, out var inputEvent, out var value), Is.True);
                Assert.That((controlId, inputEvent, value), Is.EqualTo((control.Id, input.Input, input.Value)));
                Assert.That(control.Inputs.HasFlag(input.Input), Is.True, $"{control.Id} reports {input.Input}");
            }
        }

        var imageIds = model.Controls.Where(static control => control.ImageKeyId is not null).Select(static control => control.ImageKeyId!.Value).ToList();
        Assert.That(imageIds, Is.Unique);
    }

    [TestCaseSource(nameof(Models))]
    public void Keys_are_numbered_in_reading_order(int originType)
    {
        var keys = MiraBoxModels.For(originType)!.Controls.Where(static control => control.Id.StartsWith("key.", StringComparison.Ordinal)).ToList();

        for (var index = 1; index < keys.Count; index++)
        {
            var (previous, current) = (keys[index - 1].Bounds, keys[index].Bounds);
            Assert.That(current.Y > previous.Y || (current.Y == previous.Y && current.X > previous.X), Is.True, $"{keys[index].Id} after {keys[index - 1].Id}");
        }
    }

    [TestCase(1, 5, 3, new[] { 11, 12, 13, 14, 15, 6, 7, 8, 9, 10, 1, 2, 3, 4, 5 })]
    public void Bottom_left_numbering_puts_the_highest_row_first(int first, int columns, int rows, int[] expected)
    {
        Assert.That(MiraBoxModels.BottomLeftRows(columns, rows, first), Is.EqualTo(expected));
    }

    [Test]
    public void The_293s_counts_from_the_top_right_down_the_columns()
    {
        // Top-right is 1, the key below it 2; the top-left key is 13.
        Assert.That(MiraBoxModels.TopRightColumns(5, 3, 1), Is.EqualTo(new[] { 13, 10, 7, 4, 1, 14, 11, 8, 5, 2, 15, 12, 9, 6, 3 }));
        Assert.That(MiraBoxModels.Sd293s.GetImageKeyId("key.4"), Is.EqualTo(1));
        Assert.That(MiraBoxModels.Sd293s.GetImageKeyId("screen.0"), Is.EqualTo(16));
        Assert.That(MiraBoxModels.Sd293s.Find("screen.0")!.Inputs, Is.EqualTo(InputCapabilities.None));
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
    public void Models_have_their_knobs_and_buttons(int originType, int knobs, int buttons, bool addsKnobRelease)
    {
        var model = MiraBoxModels.For(originType)!;

        Assert.That(model.Controls.Count(static control => control.Type == ControlType.Encoder), Is.EqualTo(knobs));
        Assert.That(model.Controls.Count(static control => control.Type == ControlType.Button), Is.EqualTo(buttons));
        Assert.That(model.KnobsReportNoRelease, Is.EqualTo(addsKnobRelease));
        Assert.That(model.LedCount, Is.Zero, "only the N4 Pro's LEDs are tied to controls");
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
    public void Model_events_map_to_controls(int originType, int eventType, int sdkId, string controlId, InputCapabilities inputEvent, int value)
    {
        Assert.That(MiraBoxModels.For(originType)!.TryMapInput((MiraBoxEventType)eventType, sdkId, out var mappedControl, out var mappedEvent, out var mappedValue), Is.True);
        Assert.That((mappedControl, mappedEvent, mappedValue), Is.EqualTo((controlId, inputEvent, value)));
    }

    [Test]
    public void The_XL_has_a_wheel_on_each_side_of_its_keys()
    {
        var device = MiraBoxModels.XL.Describe("mirabox.xl", "XL", null, false, null);
        var keys = device.Controls.Where(static control => control.Type == ControlType.Key).ToList();
        var (left, right) = (device.FindControl("encoder.0")!, device.FindControl("encoder.1")!);

        Assert.That((left.Side, right.Side), Is.EqualTo((DeviceSide.Left, DeviceSide.Right)));
        Assert.That(left.Bounds.X + left.Bounds.Width, Is.LessThan(keys.Min(static key => key.Bounds.X)));
        Assert.That(right.Bounds.X, Is.GreaterThan(keys.Max(static key => key.Bounds.X + key.Bounds.Width)));
        Expect.All(device.Controls.Where(static control => control.Type != ControlType.Encoder), control => Assert.That(control.Side, Is.EqualTo(DeviceSide.Front)));
    }

    [Test]
    public void The_N1_knob_shows_on_the_third_small_display()
    {
        var device = MiraBoxModels.N1.Describe("mirabox.n1", "N1", null, false, null);

        Assert.That(device.FindControl("encoder.0")!.DisplayControlId, Is.EqualTo("screen.2"));
        Assert.That(device.FindControl("screen.2")!.DisplayId, Is.EqualTo("second"));
        Assert.That((device.FindControl("screen.2")!.RenderWidth, device.FindControl("key.0")!.RenderWidth), Is.EqualTo((64, 96)));
    }

    [Test]
    public void Sizes_the_SDK_reports_win_over_the_model_defaults()
    {
        // The 293 V2 has 100 px keys, the V3 112 px; both are origin type SD293.
        var info = MiraBoxDeviceInfo.FromJson("""{"deviceKey":"k","originType":1,"keyWidth":100,"keyHeight":100,"width":800,"height":480}""")!;

        var device = MiraBoxModels.Sd293.Describe("mirabox.293", "293", info, true, null);

        Assert.That((device.FindControl("key.0")!.RenderWidth, device.FindControl("key.0")!.RenderHeight), Is.EqualTo((100, 100)));
    }

    [Test]
    public void Settings_offer_what_the_model_has()
    {
        var n4Pro = MiraBoxDeviceSettings.For(MiraBoxModels.N4Pro).Select(static setting => setting.Key).ToList();
        var xl = MiraBoxDeviceSettings.For(MiraBoxModels.XL).Select(static setting => setting.Key).ToList();

        Assert.That(n4Pro, Does.Contain(MiraBoxDeviceSettings.Vibration).And.Contain(MiraBoxDeviceSettings.LedBrightness));
        Assert.That(xl, Does.Not.Contain(MiraBoxDeviceSettings.Vibration).And.Not.Contain(MiraBoxDeviceSettings.LedBrightness));
        Assert.That(xl, Does.Contain(MiraBoxDeviceSettings.KeyBrightness).And.Contain(MiraBoxDeviceSettings.IdleScreen));
    }
}
