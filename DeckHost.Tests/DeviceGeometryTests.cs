using DeckHost.App.Views;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.MiraBox;

namespace DeckHost.Tests;

/// <summary>How the editor and the simulator place a device (body, screens, side controls).</summary>
public class DeviceGeometryTests
{
    private static DeviceDefinition Device(MiraBoxModel model) => model.Describe(model.DefaultDeviceId, model.ShortName, null, false, null);

    public static IEnumerable<TestCaseData> Models => MiraBoxModels.All.Select(model => new TestCaseData(model.OriginType).SetName($"{{m}}({model.ShortName})"));

    [TestCaseSource(nameof(Models))]
    public void The_drawing_starts_at_the_origin_and_holds_every_control(int originType)
    {
        var device = Device(MiraBoxModels.For(originType)!);
        var geometry = DeviceGeometry.Of(device);

        Assert.That(geometry.Width, Is.GreaterThan(0));
        foreach (var control in device.Controls)
        {
            var rect = geometry.Place(control);
            Assert.That(rect.X, Is.GreaterThanOrEqualTo(-0.001), control.Id);
            Assert.That(rect.Y, Is.GreaterThanOrEqualTo(-0.001), control.Id);
            Assert.That(rect.X + rect.Width, Is.LessThanOrEqualTo(geometry.Width + 0.001), control.Id);
            Assert.That(rect.Y + rect.Height, Is.LessThanOrEqualTo(geometry.Height + 0.001), control.Id);
        }
    }

    [Test]
    public void Front_controls_are_inside_the_body_and_the_XL_wheels_stick_out_of_its_sides()
    {
        var device = Device(MiraBoxModels.XL);
        var geometry = DeviceGeometry.Of(device);

        Expect.All(device.Controls.Where(static control => control.Type == ControlType.Key), key =>
        {
            var rect = geometry.Place(key);
            Assert.That(rect.X, Is.GreaterThan(geometry.Body.X));
            Assert.That(rect.X + rect.Width, Is.LessThan(geometry.Body.X + geometry.Body.Width));
        });
        var left = geometry.Place(device.FindControl("encoder.0")!);
        var right = geometry.Place(device.FindControl("encoder.1")!);
        Assert.That(left.X, Is.LessThan(geometry.Body.X));
        Assert.That(left.X + left.Width, Is.GreaterThan(geometry.Body.X));
        Assert.That(right.X, Is.LessThan(geometry.Body.X + geometry.Body.Width));
        Assert.That(right.X + right.Width, Is.GreaterThan(geometry.Body.X + geometry.Body.Width));
    }

    [Test]
    public void A_screen_covers_the_controls_on_it()
    {
        var device = Device(MiraBoxModels.N4Pro);
        var geometry = DeviceGeometry.Of(device);

        Assert.That(geometry.Displays, Has.Count.EqualTo(2));
        var key = geometry.Place(device.FindControl("key.0")!);
        Assert.That(geometry.Displays.Any(screen => screen.X <= key.X && screen.Y <= key.Y && screen.X + screen.Width >= key.X + key.Width && screen.Y + screen.Height >= key.Y + key.Height), Is.True);
    }

    [Test]
    public void A_device_without_controls_has_nothing_to_draw()
    {
        Assert.That(DeviceGeometry.Of(new DeviceDefinition("d", "D", [])).Width, Is.Zero);
    }
}
