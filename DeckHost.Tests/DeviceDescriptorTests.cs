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

    [Test]
    public void Every_built_in_device_has_a_consistent_versioned_descriptor_with_an_identity()
    {
        foreach (var device in EveryBuiltInDevice())
        {
            Assert.That(device.FindDescriptorProblems(), Is.Empty, device.Name);
            Assert.That(device.DescriptorVersion, Is.EqualTo(DeviceDefinition.CurrentDescriptorVersion));
            Assert.That(device.Identity?.Model, Is.Not.Null.And.Not.Empty, device.Name);
        }
    }

    [Test]
    public void Every_device_with_screens_says_so_and_puts_each_picture_on_a_display()
    {
        foreach (var device in EveryBuiltInDevice().Where(static device => device.Controls.Any(static control => control.HasImageOutput)))
        {
            Assert.That(device.Has(DeviceCapabilities.Images), Is.True, device.Name);
            Assert.That(device.Displays, Is.Not.Empty, device.Name);
            Assert.That(device.Controls.Where(static control => control.HasImageOutput).All(control => device.FindDisplay(control.DisplayId) is not null), Is.True, device.Name);
        }
    }

    [Test]
    public void The_N4_Pro_describes_its_two_screens_round_knobs_and_capabilities()
    {
        var n4 = MiraBoxModels.N4Pro.Describe("mirabox.n4pro", "MiraBox N4 Pro", null, isConnected: false, detail: null);

        Assert.That(n4.Identity, Is.EqualTo(new DeviceIdentity("MiraBox", "StreamDock N4 Pro", Connection: "USB")));
        Assert.That(n4.Displays.Select(static display => (display.Id, display.ControlIds.Count)), Is.EqualTo(new[] { ("main", 10), ("strip", 4) }));
        Assert.That(n4.FindControl("encoder.0")!.Shape, Is.EqualTo(ControlShape.Round));
        Assert.That(n4.Has(DeviceCapabilities.RgbLights | DeviceCapabilities.Touch | DeviceCapabilities.Backdrop), Is.True);
        Assert.That(n4.Has(DeviceCapabilities.AnimatedImages), Is.False);
    }

    [Test]
    public void The_pedal_has_no_screens()
    {
        var pedal = StreamDeckModel.All.Single(static model => model.Name == "Stream Deck Pedal").Describe("p", false, null, null, null);

        Assert.That(pedal.Displays, Is.Empty);
        Assert.That(pedal.Has(DeviceCapabilities.Images), Is.False);
    }

    [Test]
    public void Problems_are_found_and_described()
    {
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

        Assert.That(device.FindDescriptorProblems(), Is.EquivalentTo(new[]
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
    public void The_plugins_page_shows_identity_and_capabilities()
    {
        var card = new DeviceInfoViewModel(MiraBoxModels.N4Pro.Describe("mirabox.n4pro", "MiraBox N4 Pro", null, false, null), null);

        Assert.That(card.IdentityText, Is.EqualTo("MiraBox StreamDock N4 Pro · USB"));
        Assert.That(card.CapabilitiesText, Does.StartWith("Screens · Touch · Brightness · RGB lights"));
    }
}
