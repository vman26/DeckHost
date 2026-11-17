using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.Elgato;
using DeckHost.Device.MiraBox;
using DeckHost.Device.Web;

namespace DeckHost.Tests;

/// <summary>The device descriptors (master plan 7.1) every built-in device plugin gives.</summary>
public sealed class DeviceDescriptorTests : IDisposable
{
    private const string N4ProId = "mirabox.n4pro";
    private const string N4ProName = "MiraBox N4 Pro";
    private const string Pedal = "Stream Deck Pedal";

    private readonly string _directory = Directory.CreateTempSubdirectory("deckhost-descriptor").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private IEnumerable<DeviceDefinition> EveryBuiltInDevice()
    {
        foreach (var model in MiraBoxModels.All)
        {
            yield return model.Describe(model.DefaultDeviceId, $"MiraBox {model.ShortName}", null, isConnected: false, detail: null);
        }

        foreach (var model in StreamDeckModel.All)
        {
            yield return model.Describe($"elgato.{model.ProductId:x4}.test", false, "TEST", null, null);
        }

        using var web = new WebDeckPlugin(_directory);
        yield return web.Devices.Single();
    }

    private IReadOnlyList<string> DescriptorProblemsOfEveryDevice() =>
    [
        .. EveryBuiltInDevice().SelectMany(device => device.FindDescriptorProblems().Select(problem => $"{device.Name}: {problem}")),
    ];

    private IReadOnlyList<string> DevicesOnAnotherDescriptorVersion() =>
        [.. EveryBuiltInDevice().Where(static device => device.DescriptorVersion != DeviceDefinition.CurrentDescriptorVersion).Select(static device => device.Name)];

    private IReadOnlyList<string> DevicesWithoutAModelIdentity() =>
        [.. EveryBuiltInDevice().Where(static device => string.IsNullOrEmpty(device.Identity?.Model)).Select(static device => device.Name)];

    private IReadOnlyList<string> ScreenDevicesThatDoNotSayTheyHaveImages() =>
        [.. EveryBuiltInDevice().Where(HasScreens).Where(static device => !device.Has(DeviceCapabilities.Images)).Select(static device => device.Name)];

    private IReadOnlyList<string> ScreenDevicesWithoutDisplays() =>
        [.. EveryBuiltInDevice().Where(HasScreens).Where(static device => device.Displays.Count == 0).Select(static device => device.Name)];

    private IReadOnlyList<string> PicturesOnNoDisplay() =>
    [
        .. EveryBuiltInDevice().Where(HasScreens).SelectMany(device => device.Controls
            .Where(static control => control.HasImageOutput)
            .Where(control => device.FindDisplay(control.DisplayId) is null)
            .Select(control => $"{device.Name}: {control.Id}")),
    ];

    private static bool HasScreens(DeviceDefinition device) => device.Controls.Any(static control => control.HasImageOutput);

    [Test]
    public void FindDescriptorProblems_EveryBuiltInDevice_FindsNone()
    {
        // Arrange (the built-in devices)

        // Act
        var problems = DescriptorProblemsOfEveryDevice();

        // Assert
        Assert.That(problems, Is.Empty);
    }

    [Test]
    public void DescriptorVersion_EveryBuiltInDevice_IsTheCurrentVersion()
    {
        // Arrange (the built-in devices)

        // Act
        var others = DevicesOnAnotherDescriptorVersion();

        // Assert
        Assert.That(others, Is.Empty);
    }

    [Test]
    public void Identity_EveryBuiltInDevice_NamesTheModel()
    {
        // Arrange (the built-in devices)

        // Act
        var without = DevicesWithoutAModelIdentity();

        // Assert
        Assert.That(without, Is.Empty);
    }

    [Test]
    public void Has_EveryDeviceWithScreens_SaysItHasImages()
    {
        // Arrange (the built-in devices)

        // Act
        var silent = ScreenDevicesThatDoNotSayTheyHaveImages();

        // Assert
        Assert.That(silent, Is.Empty);
    }

    [Test]
    public void Displays_EveryDeviceWithScreens_ListsAtLeastOne()
    {
        // Arrange (the built-in devices)

        // Act
        var without = ScreenDevicesWithoutDisplays();

        // Assert
        Assert.That(without, Is.Empty);
    }

    [Test]
    public void FindDisplay_EveryPictureOnABuiltInDevice_IsOnAListedDisplay()
    {
        // Arrange (the built-in devices)

        // Act
        var stray = PicturesOnNoDisplay();

        // Assert
        Assert.That(stray, Is.Empty);
    }

    private static DeviceDefinition N4Pro() => MiraBoxModels.N4Pro.Describe(N4ProId, N4ProName, null, isConnected: false, detail: null);

    [Test]
    public void Identity_N4Pro_IsAMiraBoxStreamDockOverUsb()
    {
        // Arrange
        var n4 = N4Pro();

        // Act
        var identity = n4.Identity;

        // Assert
        Assert.That(identity, Is.EqualTo(new DeviceIdentity("MiraBox", "StreamDock N4 Pro", Connection: "USB")));
    }

    [Test]
    public void Displays_N4Pro_AreTheMainScreenWithTenKeysAndTheStripWithFourZones()
    {
        // Arrange
        var n4 = N4Pro();

        // Act
        var displays = n4.Displays.Select(static display => (display.Id, display.ControlIds.Count));

        // Assert
        Assert.That(displays, Is.EqualTo(new[] { ("main", 10), ("strip", 4) }));
    }

    [Test]
    public void Shape_N4ProKnob_IsRound()
    {
        // Arrange
        var n4 = N4Pro();

        // Act
        var shape = n4.FindControl("encoder.0")!.Shape;

        // Assert
        Assert.That(shape, Is.EqualTo(ControlShape.Round));
    }

    [Test]
    public void Has_N4Pro_RgbLightsTouchAndBackdrop()
    {
        // Arrange
        var n4 = N4Pro();

        // Act
        var has = n4.Has(DeviceCapabilities.RgbLights | DeviceCapabilities.Touch | DeviceCapabilities.Backdrop);

        // Assert
        Assert.That(has, Is.True);
    }

    [Test]
    public void Has_N4Pro_NoAnimatedImages()
    {
        // Arrange
        var n4 = N4Pro();

        // Act
        var has = n4.Has(DeviceCapabilities.AnimatedImages);

        // Assert
        Assert.That(has, Is.False);
    }

    private static DeviceDefinition StreamDeckPedal() => StreamDeckModel.All.Single(static model => model.Name == Pedal).Describe("p", false, null, null, null);

    [Test]
    public void Displays_Pedal_AreNone()
    {
        // Arrange
        var pedal = StreamDeckPedal();

        // Act
        var displays = pedal.Displays;

        // Assert
        Assert.That(displays, Is.Empty);
    }

    [Test]
    public void Has_Pedal_NoImages()
    {
        // Arrange
        var pedal = StreamDeckPedal();

        // Act
        var has = pedal.Has(DeviceCapabilities.Images);

        // Assert
        Assert.That(has, Is.False);
    }

    [Test]
    public void FindDescriptorProblems_InconsistentDevice_DescribesEveryProblem()
    {
        // Arrange
        var device = new DeviceDefinition(
            "d",
            "D",
            [
                new PhysicalControl("key.0", "K", ControlType.Key, new Rect(0, 0, 1, 1), InputCapabilities.Press, OutputCapabilities.ColorImage, 72, 72),
                new PhysicalControl("key.0", "K", ControlType.Key, new Rect(0, 0, 1, 1), InputCapabilities.Press, OutputCapabilities.None, DisplayControlId: "touch.9") { DisplayId = "nowhere" },
            ])
        {
            Displays = [new DeviceDisplay("main", 0, 480) { ControlIds = ["key.7"] }],
        };

        // Act
        var problems = device.FindDescriptorProblems();

        // Assert
        Assert.That(problems, Is.EquivalentTo(new[]
        {
            "Control id key.0 is used twice.",
            "key.0 shows images but names no display.",
            "key.0 links to touch.9, which isn't a control of this device.",
            "key.0 is on display nowhere, which isn't listed.",
            "Display main has no size.",
            "Display main lists key.7, which isn't a control of this device.",
        }));
    }

    [Test]
    public void IdentityText_N4ProCard_IsTheMakerModelAndConnection()
    {
        // Arrange
        var card = new DeviceInfoViewModel(N4Pro(), null);

        // Act
        var text = card.IdentityText;

        // Assert
        Assert.That(text, Is.EqualTo("MiraBox StreamDock N4 Pro · USB"));
    }

    [Test]
    public void CapabilitiesText_N4ProCard_StartsWithScreensTouchBrightnessAndRgbLights()
    {
        // Arrange
        var card = new DeviceInfoViewModel(N4Pro(), null);

        // Act
        var text = card.CapabilitiesText;

        // Assert
        Assert.That(text, Does.StartWith("Screens · Touch · Brightness · RGB lights"));
    }
}
