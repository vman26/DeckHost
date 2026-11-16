using DeckHost.Device.Elgato;
using DeckHost.Device.MiraBox;
using System.Text.Json;

namespace DeckHost.Tests;

/// <summary>Remembered, unplugged devices can be forgotten from Device settings.</summary>
public sealed class ForgetDeviceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("deckhost-forget").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void The_N4_Pro_listed_before_any_device_was_plugged_in_can_be_forgotten_for_good()
    {
        using (var plugin = new MiraBoxDevicePlugin(_directory))
        {
            var forget = Expect.Single(plugin.GetSettingActions(MiraBoxDevicePlugin.PrimaryDeviceId));
            Assert.That((forget.Id, forget.Confirm), Is.EqualTo((MiraBoxDevicePlugin.ForgetActionId, "Forget MiraBox N4 Pro?")));
            var changed = 0;
            plugin.DevicesChanged += (_, _) => changed++;

            Assert.That(plugin.RunSettingAction(MiraBoxDevicePlugin.PrimaryDeviceId, MiraBoxDevicePlugin.ForgetActionId), Does.StartWith("Forgotten"));

            Assert.That(plugin.Devices, Is.Empty);
            Assert.That(changed, Is.EqualTo(1));
        }

        using var restarted = new MiraBoxDevicePlugin(_directory);
        Assert.That(restarted.Devices, Is.Empty);
    }

    [Test]
    public void A_remembered_MiraBox_model_can_be_forgotten()
    {
        File.WriteAllText(Path.Combine(_directory, "mirabox-known.json"), JsonSerializer.Serialize(new[] { new { Id = "mirabox.xl", OriginType = MiraBoxModels.Origin.XL, Serial = "X1" } }));
        using var plugin = new MiraBoxDevicePlugin(_directory);
        Assert.That(plugin.Devices.Select(static device => device.Id), Is.EquivalentTo(new[] { "mirabox.n4pro", "mirabox.xl" }));

        plugin.RunSettingAction("mirabox.xl", MiraBoxDevicePlugin.ForgetActionId);

        Assert.That(plugin.Devices.Select(static device => device.Id), Is.EqualTo(new[] { "mirabox.n4pro" }));
        using var restarted = new MiraBoxDevicePlugin(_directory);
        Assert.That(restarted.Devices.Select(static device => device.Id), Is.EqualTo(new[] { "mirabox.n4pro" }));
    }

    [Test]
    public void A_remembered_Stream_Deck_can_be_forgotten()
    {
        var model = StreamDeckModel.All[0];
        var id = $"elgato.{model.ProductId:x4}.abc";
        File.WriteAllText(Path.Combine(_directory, "elgato-known.json"), JsonSerializer.Serialize(new[] { new { Id = id, model.ProductId, Serial = "ABC" } }));
        using var plugin = new ElgatoDevicePlugin(_directory);
        Assert.That(Expect.Single(plugin.Devices).Id, Is.EqualTo(id));
        Assert.That(Expect.Single(plugin.GetSettingActions(id)).Label, Is.EqualTo("Forget this device"));

        plugin.RunSettingAction(id, ElgatoDevicePlugin.ForgetActionId);

        Assert.That(plugin.Devices, Is.Empty);
        using var restarted = new ElgatoDevicePlugin(_directory);
        Assert.That(restarted.Devices, Is.Empty);
    }

    [Test]
    public void Unknown_devices_offer_nothing()
    {
        using var mirabox = new MiraBoxDevicePlugin(_directory);
        using var elgato = new ElgatoDevicePlugin(_directory);

        Assert.That(mirabox.GetSettingActions("mirabox.nothing"), Is.Empty);
        Assert.That(elgato.GetSettingActions("elgato.nothing"), Is.Empty);
        Assert.That(mirabox.RunSettingAction("mirabox.nothing", MiraBoxDevicePlugin.ForgetActionId), Does.StartWith("Only"));
    }
}
