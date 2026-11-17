using DeckHost.App.ViewModels;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>The device cards on the Plugins page say only what the device's own plugin reports.</summary>
public sealed class DeviceInfoTests
{
    private const string FirmwareVersion = "1.2.3";
    private const string MakersNote = "Update it with the maker's software.";
    private static readonly DeviceDefinition WebDeck = new("deckhost.web", "Web Deck", [], IsConnected: true);

    [Test]
    public void HasFirmwareText_DeviceWithoutFirmware_IsFalse()
    {
        // Arrange
        var card = new DeviceInfoViewModel(WebDeck, status: null);

        // Act
        var hasText = card.HasFirmwareText;

        // Assert
        Assert.That(hasText, Is.False);
    }

    [Test]
    public void Detail_DeviceWithoutFirmware_DoesNotMentionFirmware()
    {
        // Arrange
        var card = new DeviceInfoViewModel(WebDeck, status: null);

        // Act
        var detail = card.Detail;

        // Assert
        Assert.That(detail, Does.Not.Contain("firmware").IgnoreCase);
    }

    [Test]
    public void FirmwareText_DeviceThatCannotUpdate_ShowsTheNoteOfItsOwnPlugin()
    {
        // Arrange
        var card = new DeviceInfoViewModel(WebDeck, null, hasFirmware: true, firmware: new DeviceFirmwareInfo(FirmwareVersion, CanUpdate: false, MakersNote));

        // Act
        var text = card.FirmwareText;

        // Assert
        Assert.That(text, Is.EqualTo($"Firmware {FirmwareVersion}. {MakersNote}"));
    }

    [Test]
    public void FirmwareText_DeviceThatCanUpdate_PointsToTheFirmwareSettings()
    {
        // Arrange
        var card = new DeviceInfoViewModel(WebDeck, null, hasFirmware: true, firmware: new DeviceFirmwareInfo("2.0", CanUpdate: true));

        // Act
        var text = card.FirmwareText;

        // Assert
        Assert.That(text, Does.Contain("Settings… → Firmware"));
    }
}
