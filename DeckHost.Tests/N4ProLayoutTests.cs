using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.MiraBox;

namespace DeckHost.Tests;

public class N4ProLayoutTests
{
    [Test]
    public void Layout_matches_the_N4_Pro_hardware()
    {
        var device = N4ProLayout.Create("dev", "N4 Pro", info: null, isConnected: false, detail: null);

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
        var device = N4ProLayout.Create("dev", "N4 Pro", info: null, isConnected: false, detail: null);

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
        Assert.That(N4ProLayout.GetImageKeyId(controlId), Is.EqualTo(sdkKeyId));
    }

    [TestCase("encoder.0")]
    [TestCase("touch.swipe")]
    [TestCase("key.10")]
    public void Controls_without_images_have_no_image_key(string controlId)
    {
        Assert.That(N4ProLayout.GetImageKeyId(controlId), Is.Null);
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
        Assert.That(N4ProLayout.TryMapInput((MiraBoxEventType)eventType, sdkId, out var mappedControl, out var mappedEvent, out var mappedValue), Is.True);
        Assert.That((mappedControl, mappedEvent, mappedValue), Is.EqualTo((controlId, inputEvent, value)));
    }

    [TestCase((int)MiraBoxEventType.KeyPress, 2)]
    [TestCase((int)MiraBoxEventType.KnobLeft, 17)]
    [TestCase((int)MiraBoxEventType.ToggleUp, 1)]
    public void Unused_events_are_ignored(int eventType, int sdkId)
    {
        Assert.That(N4ProLayout.TryMapInput((MiraBoxEventType)eventType, sdkId, out _, out _, out _), Is.False);
    }

    [Test]
    public void Device_info_json_from_the_bridge_parses()
    {
        const string json = """{"deviceKey":"\\\\?\\hid#1","originType":7,"vendorId":21832,"productId":4104,"serialNumber":"ABC","firmwareVersion":"1.0","width":800,"height":480,"keyWidth":112,"keyHeight":112,"minKey":6,"maxKey":15,"hasSecondScreen":true,"minSecondScreenKey":1,"maxSecondScreenKey":4,"secondScreenWidth":176,"secondScreenHeight":112,"hasRgbLed":true,"ledCount":4}""";

        var info = MiraBoxDeviceInfo.FromJson(json);

        Assert.That(info, Is.Not.Null);
        Assert.That(info.IsN4Pro, Is.True);
        Assert.That(info.DeviceKey, Is.EqualTo(@"\\?\hid#1"));
        Assert.That(info.LedCount, Is.EqualTo(4));
    }
}
