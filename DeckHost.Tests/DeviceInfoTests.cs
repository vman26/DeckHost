using DeckHost.App.ViewModels;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>The device cards on the Plugins page say only what the device's own plugin reports.</summary>
public sealed class DeviceInfoTests
{
    private static readonly DeviceDefinition WebDeck = new("deckhost.web", "Web Deck", [], IsConnected: true);

    [Test]
    public void A_device_without_firmware_shows_no_firmware_text()
    {
        var card = new DeviceInfoViewModel(WebDeck, status: null);

        Assert.That(card.HasFirmwareText, Is.False);
        Assert.That(card.Detail, Does.Not.Contain("firmware").IgnoreCase);
    }

    [Test]
    public void A_device_shows_the_firmware_note_of_its_own_plugin()
    {
        var note = new DeviceInfoViewModel(WebDeck, null, hasFirmware: true, firmware: new DeviceFirmwareInfo("1.2.3", CanUpdate: false, "Update it with the maker's software."));
        var updatable = new DeviceInfoViewModel(WebDeck, null, hasFirmware: true, firmware: new DeviceFirmwareInfo("2.0", CanUpdate: true));

        Assert.That(note.FirmwareText, Is.EqualTo("Firmware 1.2.3. Update it with the maker's software."));
        Assert.That(updatable.FirmwareText, Does.Contain("Settings… → Firmware"));
    }
}
