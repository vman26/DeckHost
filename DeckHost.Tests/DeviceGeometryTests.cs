using DeckHost.App.Views;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.MiraBox;

namespace DeckHost.Tests;

/// <summary>How the editor and the simulator place a device (body, screens, side controls).</summary>
public class DeviceGeometryTests
{
    private const double Tolerance = 0.001;
    private const string LeftWheel = "encoder.0";
    private const string RightWheel = "encoder.1";
    private const string FirstKey = "key.0";

    private static DeviceDefinition Device(MiraBoxModel model) => model.Describe(model.DefaultDeviceId, model.ShortName, null, false, null);

    public static IEnumerable<TestCaseData> Models => MiraBoxModels.All.Select(model => new TestCaseData(model.OriginType).SetName($"{{m}}({model.ShortName})"));

    private static IReadOnlyList<string> ControlsOutsideTheDrawing(DeviceDefinition device, DeviceGeometry geometry) =>
    [
        .. device.Controls.Where(control =>
        {
            var rect = geometry.Place(control);
            return rect.X < -Tolerance || rect.Y < -Tolerance ||
                   rect.X + rect.Width > geometry.Width + Tolerance || rect.Y + rect.Height > geometry.Height + Tolerance;
        }).Select(static control => control.Id),
    ];

    private static IReadOnlyList<string> KeysNotInsideTheBody(DeviceDefinition device, DeviceGeometry geometry) =>
    [
        .. device.Controls.Where(static control => control.Type == ControlType.Key).Where(key =>
        {
            var rect = geometry.Place(key);
            return rect.X <= geometry.Body.X || rect.X + rect.Width >= geometry.Body.X + geometry.Body.Width;
        }).Select(static key => key.Id),
    ];

    [TestCaseSource(nameof(Models))]
    public void Of_EveryMiraBoxModel_HasAWidth(int originType)
    {
        // Arrange
        var device = Device(MiraBoxModels.For(originType)!);

        // Act
        var geometry = DeviceGeometry.Of(device);

        // Assert
        Assert.That(geometry.Width, Is.GreaterThan(0));
    }

    [TestCaseSource(nameof(Models))]
    public void Of_EveryMiraBoxModel_HoldsEveryControlInsideTheDrawing(int originType)
    {
        // Arrange
        var device = Device(MiraBoxModels.For(originType)!);

        // Act
        var geometry = DeviceGeometry.Of(device);

        // Assert
        Assert.That(ControlsOutsideTheDrawing(device, geometry), Is.Empty);
    }

    [Test]
    public void Of_XLDevice_PutsEveryKeyInsideTheBody()
    {
        // Arrange
        var device = Device(MiraBoxModels.XL);

        // Act
        var geometry = DeviceGeometry.Of(device);

        // Assert
        Assert.That(KeysNotInsideTheBody(device, geometry), Is.Empty);
    }

    [Test]
    public void Of_XLDevice_LetsTheLeftWheelStickOutOfTheLeftSideOfTheBody()
    {
        // Arrange
        var device = Device(MiraBoxModels.XL);

        // Act
        var geometry = DeviceGeometry.Of(device);

        // Assert
        var left = geometry.Place(device.FindControl(LeftWheel)!);
        Assert.That(left.X, Is.LessThan(geometry.Body.X));
        Assert.That(left.X + left.Width, Is.GreaterThan(geometry.Body.X));
    }

    [Test]
    public void Of_XLDevice_LetsTheRightWheelStickOutOfTheRightSideOfTheBody()
    {
        // Arrange
        var device = Device(MiraBoxModels.XL);

        // Act
        var geometry = DeviceGeometry.Of(device);

        // Assert
        var right = geometry.Place(device.FindControl(RightWheel)!);
        Assert.That(right.X, Is.LessThan(geometry.Body.X + geometry.Body.Width));
        Assert.That(right.X + right.Width, Is.GreaterThan(geometry.Body.X + geometry.Body.Width));
    }

    [Test]
    public void Of_N4Pro_HasTwoScreens()
    {
        // Arrange
        var device = Device(MiraBoxModels.N4Pro);

        // Act
        var geometry = DeviceGeometry.Of(device);

        // Assert
        Assert.That(geometry.Displays, Has.Count.EqualTo(2));
    }

    [Test]
    public void Of_N4Pro_PutsAScreenBehindTheFirstKey()
    {
        // Arrange
        var device = Device(MiraBoxModels.N4Pro);

        // Act
        var geometry = DeviceGeometry.Of(device);

        // Assert
        var key = geometry.Place(device.FindControl(FirstKey)!);
        Assert.That(geometry.Displays.Any(screen => screen.X <= key.X && screen.Y <= key.Y && screen.X + screen.Width >= key.X + key.Width && screen.Y + screen.Height >= key.Y + key.Height), Is.True);
    }

    [Test]
    public void Of_DeviceWithoutControls_HasNothingToDraw()
    {
        // Arrange
        var device = new DeviceDefinition("d", "D", []);

        // Act
        var geometry = DeviceGeometry.Of(device);

        // Assert
        Assert.That(geometry.Width, Is.Zero);
    }
}
