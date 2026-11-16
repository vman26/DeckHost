using DeckHost.Contracts.Models;
using DeckHost.Core.Profiles;

namespace DeckHost.Tests;

public class AutoProfileTests
{
    private static readonly DeviceProfile[] Profiles =
    [
        new("default", "Default", "dev", []),
        new("streaming", "Streaming", "dev", [], ["obs64", "Streamlabs OBS.exe"]),
        new("browser", "Browser", "dev", [], ["chrome", "msedge"]),
        new("other-device", "Other", "dev2", [], ["code"]),
    ];

    [TestCase("obs64", "streaming")]
    [TestCase("OBS64", "streaming")]
    [TestCase("obs64.exe", "streaming")]
    [TestCase("Streamlabs OBS", "streaming")]
    [TestCase("msedge", "browser")]
    public void Finds_the_profile_for_the_focused_app(string app, string expectedProfileId)
    {
        Assert.That(AutoProfileSelector.FindProfileForApp(Profiles, "dev", app)?.Id, Is.EqualTo(expectedProfileId));
    }

    [TestCase("notepad")]
    [TestCase("")]
    [TestCase(null)]
    [TestCase("code")]
    public void Returns_null_when_no_profile_of_the_device_lists_the_app(string? app)
    {
        Assert.That(AutoProfileSelector.FindProfileForApp(Profiles, "dev", app), Is.Null);
    }

    [Test]
    public void App_lists_and_the_auto_switch_setting_are_saved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ProfileStore(path);
            store.Save(new ProfileDocument(1, "default", Profiles, AutoSwitchEnabled: false));

            var loaded = store.Load()!;
            Assert.That(loaded.AutoSwitchEnabled, Is.False);
            Assert.That(loaded.Profiles.Single(profile => profile.Id == "streaming").AutoSwitchApps, Is.EqualTo(new[] { "obs64", "Streamlabs OBS.exe" }));
            Assert.That(loaded.Profiles.Single(profile => profile.Id == "default").AutoSwitchApps, Is.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Older_profile_files_default_to_auto_switching_on()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"version":1,"selectedProfileId":"a","profiles":[{"id":"a","name":"A","deviceId":"dev","bindings":[]}]}""");
            var loaded = new ProfileStore(path).Load()!;
            Assert.That(loaded.AutoSwitchEnabled, Is.True);
            Assert.That(Expect.Single(loaded.Profiles).AutoSwitchApps, Is.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
