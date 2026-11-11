using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.MiraBox;

namespace DeckHost.Tests;

public class N4ProLayoutTests
{
    [Fact]
    public void Layout_matches_the_N4_Pro_hardware()
    {
        var device = N4ProLayout.Create("dev", "N4 Pro", info: null, isConnected: false, detail: null);

        Assert.Equal(10, device.Controls.Count(control => control.Type == ControlType.Key));
        Assert.Equal(4, device.Controls.Count(control => control.Type == ControlType.Encoder));
        Assert.Equal(5, device.Controls.Count(control => control.Type == ControlType.TouchSurface));
        Assert.All(device.Controls.Where(control => control.Type == ControlType.Key), key => Assert.Equal((112, 112), (key.RenderWidth, key.RenderHeight)));
        Assert.Equal((176, 112), (device.FindControl("touch.0")!.RenderWidth, device.FindControl("touch.0")!.RenderHeight));
        Assert.Equal([0, 1, 2, 3], device.Controls.Where(control => control.HasLight).Select(control => control.LedIndex!.Value));
    }

    [Theory]
    [InlineData("key.0", 11)]
    [InlineData("key.4", 15)]
    [InlineData("key.5", 6)]
    [InlineData("key.9", 10)]
    [InlineData("touch.0", 1)]
    [InlineData("touch.3", 4)]
    public void Image_key_ids_follow_the_SDK_numbering(string controlId, int sdkKeyId)
    {
        Assert.Equal(sdkKeyId, N4ProLayout.GetImageKeyId(controlId));
    }

    [Theory]
    [InlineData("encoder.0")]
    [InlineData("touch.swipe")]
    [InlineData("key.10")]
    public void Controls_without_images_have_no_image_key(string controlId)
    {
        Assert.Null(N4ProLayout.GetImageKeyId(controlId));
    }

    [Theory]
    [InlineData((int)MiraBoxEventType.KeyPress, 11, "key.0", InputCapabilities.Press, 1)]
    [InlineData((int)MiraBoxEventType.KeyRelease, 6, "key.5", InputCapabilities.Release, 0)]
    [InlineData((int)MiraBoxEventType.KeyRelease, 2, "touch.1", InputCapabilities.Press, 1)]
    [InlineData((int)MiraBoxEventType.KnobLeft, 16, "encoder.0", InputCapabilities.Rotate, -1)]
    [InlineData((int)MiraBoxEventType.KnobRight, 23, "encoder.3", InputCapabilities.Rotate, 1)]
    [InlineData((int)MiraBoxEventType.KnobPress, 25, "encoder.1", InputCapabilities.Press, 1)]
    [InlineData((int)MiraBoxEventType.SwipeLeft, 28, "touch.swipe", InputCapabilities.Swipe, -1)]
    [InlineData((int)MiraBoxEventType.SwipeRight, 29, "touch.swipe", InputCapabilities.Swipe, 1)]
    public void Input_events_map_to_controls(int eventType, int sdkId, string controlId, InputCapabilities inputEvent, int value)
    {
        Assert.True(N4ProLayout.TryMapInput((MiraBoxEventType)eventType, sdkId, out var mappedControl, out var mappedEvent, out var mappedValue));
        Assert.Equal((controlId, inputEvent, value), (mappedControl, mappedEvent, mappedValue));
    }

    [Theory]
    [InlineData((int)MiraBoxEventType.KeyPress, 2)]
    [InlineData((int)MiraBoxEventType.KnobLeft, 17)]
    [InlineData((int)MiraBoxEventType.ToggleUp, 1)]
    public void Unused_events_are_ignored(int eventType, int sdkId)
    {
        Assert.False(N4ProLayout.TryMapInput((MiraBoxEventType)eventType, sdkId, out _, out _, out _));
    }

    [Fact]
    public void Device_info_json_from_the_bridge_parses()
    {
        const string json = """{"deviceKey":"\\\\?\\hid#1","originType":7,"vendorId":21832,"productId":4104,"serialNumber":"ABC","firmwareVersion":"1.0","width":800,"height":480,"keyWidth":112,"keyHeight":112,"minKey":6,"maxKey":15,"hasSecondScreen":true,"minSecondScreenKey":1,"maxSecondScreenKey":4,"secondScreenWidth":176,"secondScreenHeight":112,"hasRgbLed":true,"ledCount":4}""";

        var info = MiraBoxDeviceInfo.FromJson(json);

        Assert.NotNull(info);
        Assert.True(info.IsN4Pro);
        Assert.Equal(@"\\?\hid#1", info.DeviceKey);
        Assert.Equal(4, info.LedCount);
    }
}
