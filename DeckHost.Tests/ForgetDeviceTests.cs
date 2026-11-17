using DeckHost.Device.Elgato;
using DeckHost.Device.MiraBox;
using System.Text.Json;

namespace DeckHost.Tests;

/// <summary>Remembered, unplugged devices can be forgotten from Device settings.</summary>
public sealed class ForgetDeviceTests : IDisposable
{
    private const string XlId = "mirabox.xl";
    private const string N4ProId = "mirabox.n4pro";
    private const string UnknownMiraBox = "mirabox.nothing";
    private const string UnknownElgato = "elgato.nothing";
    private const string ForgetLabel = "Forget this device";
    private const string ForgetQuestion = "Forget MiraBox N4 Pro?";

    private readonly string _directory = Directory.CreateTempSubdirectory("deckhost-forget").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void RememberAnXl() =>
        File.WriteAllText(Path.Combine(_directory, "mirabox-known.json"), JsonSerializer.Serialize(new[] { new { Id = XlId, OriginType = MiraBoxModels.Origin.XL, Serial = "X1" } }));

    private string RememberAStreamDeck()
    {
        var model = StreamDeckModel.All[0];
        var id = $"elgato.{model.ProductId:x4}.abc";
        File.WriteAllText(Path.Combine(_directory, "elgato-known.json"), JsonSerializer.Serialize(new[] { new { Id = id, model.ProductId, Serial = "ABC" } }));
        return id;
    }

    [Test]
    public void GetSettingActions_AlwaysListedN4Pro_OffersToForgetItWithAConfirmation()
    {
        // Arrange
        using var plugin = new MiraBoxDevicePlugin(_directory);

        // Act
        var forget = Expect.Single(plugin.GetSettingActions(MiraBoxDevicePlugin.PrimaryDeviceId));

        // Assert
        Assert.That((forget.Id, forget.Confirm), Is.EqualTo((MiraBoxDevicePlugin.ForgetActionId, ForgetQuestion)));
    }

    [Test]
    public void RunSettingAction_ForgetTheN4Pro_RemovesItAndReportsOneChange()
    {
        // Arrange
        using var plugin = new MiraBoxDevicePlugin(_directory);
        var changed = 0;
        plugin.DevicesChanged += (_, _) => changed++;

        // Act
        var message = plugin.RunSettingAction(MiraBoxDevicePlugin.PrimaryDeviceId, MiraBoxDevicePlugin.ForgetActionId);

        // Assert
        Assert.That(message, Does.StartWith("Forgotten"));
        Assert.That(plugin.Devices, Is.Empty);
        Assert.That(changed, Is.EqualTo(1));
    }

    [Test]
    public void Devices_AfterForgettingTheN4ProAndRestarting_DoesNotListItAgain()
    {
        // Arrange
        using (var plugin = new MiraBoxDevicePlugin(_directory))
        {
            plugin.RunSettingAction(MiraBoxDevicePlugin.PrimaryDeviceId, MiraBoxDevicePlugin.ForgetActionId);
        }

        // Act
        using var restarted = new MiraBoxDevicePlugin(_directory);

        // Assert
        Assert.That(restarted.Devices, Is.Empty);
    }

    [Test]
    public void RunSettingAction_ForgetARememberedMiraBoxModel_ListsOnlyTheN4Pro()
    {
        // Arrange
        RememberAnXl();
        using var plugin = new MiraBoxDevicePlugin(_directory);

        // Act
        plugin.RunSettingAction(XlId, MiraBoxDevicePlugin.ForgetActionId);

        // Assert
        Assert.That(plugin.Devices.Select(static device => device.Id), Is.EqualTo(new[] { N4ProId }));
    }

    [Test]
    public void Devices_RememberedMiraBoxModel_IsListedNextToTheN4Pro()
    {
        // Arrange
        RememberAnXl();

        // Act
        using var plugin = new MiraBoxDevicePlugin(_directory);

        // Assert
        Assert.That(plugin.Devices.Select(static device => device.Id), Is.EquivalentTo(new[] { N4ProId, XlId }));
    }

    [Test]
    public void Devices_AfterForgettingAMiraBoxModelAndRestarting_ListsOnlyTheN4Pro()
    {
        // Arrange
        RememberAnXl();
        using (var plugin = new MiraBoxDevicePlugin(_directory))
        {
            plugin.RunSettingAction(XlId, MiraBoxDevicePlugin.ForgetActionId);
        }

        // Act
        using var restarted = new MiraBoxDevicePlugin(_directory);

        // Assert
        Assert.That(restarted.Devices.Select(static device => device.Id), Is.EqualTo(new[] { N4ProId }));
    }

    [Test]
    public void GetSettingActions_RememberedStreamDeck_OffersToForgetIt()
    {
        // Arrange
        var id = RememberAStreamDeck();
        using var plugin = new ElgatoDevicePlugin(_directory);

        // Act
        var forget = Expect.Single(plugin.GetSettingActions(id));

        // Assert
        Assert.That(forget.Label, Is.EqualTo(ForgetLabel));
    }

    [Test]
    public void RunSettingAction_ForgetARememberedStreamDeck_RemovesIt()
    {
        // Arrange
        var id = RememberAStreamDeck();
        using var plugin = new ElgatoDevicePlugin(_directory);

        // Act
        plugin.RunSettingAction(id, ElgatoDevicePlugin.ForgetActionId);

        // Assert
        Assert.That(plugin.Devices, Is.Empty);
    }

    [Test]
    public void Devices_AfterForgettingAStreamDeckAndRestarting_DoesNotListItAgain()
    {
        // Arrange
        var id = RememberAStreamDeck();
        using (var plugin = new ElgatoDevicePlugin(_directory))
        {
            plugin.RunSettingAction(id, ElgatoDevicePlugin.ForgetActionId);
        }

        // Act
        using var restarted = new ElgatoDevicePlugin(_directory);

        // Assert
        Assert.That(restarted.Devices, Is.Empty);
    }

    [Test]
    public void GetSettingActions_UnknownMiraBoxDevice_OffersNothing()
    {
        // Arrange
        using var mirabox = new MiraBoxDevicePlugin(_directory);

        // Act
        var actions = mirabox.GetSettingActions(UnknownMiraBox);

        // Assert
        Assert.That(actions, Is.Empty);
    }

    [Test]
    public void GetSettingActions_UnknownStreamDeck_OffersNothing()
    {
        // Arrange
        using var elgato = new ElgatoDevicePlugin(_directory);

        // Act
        var actions = elgato.GetSettingActions(UnknownElgato);

        // Assert
        Assert.That(actions, Is.Empty);
    }

    [Test]
    public void RunSettingAction_ForgetAnUnknownMiraBoxDevice_SaysOnlyUnpluggedDevicesCanBeForgotten()
    {
        // Arrange
        using var mirabox = new MiraBoxDevicePlugin(_directory);

        // Act
        var message = mirabox.RunSettingAction(UnknownMiraBox, MiraBoxDevicePlugin.ForgetActionId);

        // Assert
        Assert.That(message, Does.StartWith("Only"));
    }
}
