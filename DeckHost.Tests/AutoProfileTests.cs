using DeckHost.Contracts.Models;
using DeckHost.Core.Profiles;

namespace DeckHost.Tests;

public class AutoProfileTests
{
    private const string Device = "dev";
    private const string StreamingId = "streaming";
    private const string DefaultId = "default";
    private const string BrowserId = "browser";

    private static readonly DeviceProfile[] Profiles =
    [
        new(DefaultId, "Default", Device, []),
        new(StreamingId, "Streaming", Device, [], ["obs64", "Streamlabs OBS.exe"]),
        new(BrowserId, "Browser", Device, [], ["chrome", "msedge"]),
        new("other-device", "Other", "dev2", [], ["code"]),
    ];

    [TestCase("obs64", StreamingId)]
    [TestCase("OBS64", StreamingId)]
    [TestCase("obs64.exe", StreamingId)]
    [TestCase("Streamlabs OBS", StreamingId)]
    [TestCase("msedge", BrowserId)]
    public void FindProfileForApp_AFocusedAppAProfileLists_FindsThatProfile(string app, string expectedProfileId)
    {
        // Act
        var profile = AutoProfileSelector.FindProfileForApp(Profiles, Device, app);

        // Assert
        Assert.That(profile?.Id, Is.EqualTo(expectedProfileId));
    }

    [TestCase("notepad")]
    [TestCase("")]
    [TestCase(null)]
    [TestCase("code")]
    public void FindProfileForApp_NoProfileOfTheDeviceListsTheApp_ReturnsNull(string? app)
    {
        // Act
        var profile = AutoProfileSelector.FindProfileForApp(Profiles, Device, app);

        // Assert
        Assert.That(profile, Is.Null);
    }

    private static string TempProfileFile() => Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}.json");

    [Test]
    public void Load_AfterSavingAppListsAndAutoSwitchOff_ReadsThemBack()
    {
        // Arrange
        var path = TempProfileFile();
        try
        {
            var store = new ProfileStore(path);
            store.Save(new ProfileDocument(1, DefaultId, Profiles, AutoSwitchEnabled: false));

            // Act
            var loaded = store.Load()!;

            // Assert
            Assert.That(loaded.AutoSwitchEnabled, Is.False);
            Assert.That(loaded.Profiles.Single(profile => profile.Id == StreamingId).AutoSwitchApps, Is.EqualTo(new[] { "obs64", "Streamlabs OBS.exe" }));
            Assert.That(loaded.Profiles.Single(profile => profile.Id == DefaultId).AutoSwitchApps, Is.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Load_AnOlderFileWithoutTheAutoSwitchSetting_DefaultsToAutoSwitchingOn()
    {
        // Arrange
        var path = TempProfileFile();
        try
        {
            File.WriteAllText(path, """{"version":1,"selectedProfileId":"a","profiles":[{"id":"a","name":"A","deviceId":"dev","bindings":[]}]}""");

            // Act
            var loaded = new ProfileStore(path).Load()!;

            // Assert
            Assert.That(loaded.AutoSwitchEnabled, Is.True);
            Assert.That(Expect.Single(loaded.Profiles).AutoSwitchApps, Is.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
