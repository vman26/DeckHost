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

    [Theory]
    [InlineData("obs64", "streaming")]
    [InlineData("OBS64", "streaming")]
    [InlineData("obs64.exe", "streaming")]
    [InlineData("Streamlabs OBS", "streaming")]
    [InlineData("msedge", "browser")]
    public void Finds_the_profile_for_the_focused_app(string app, string expectedProfileId)
    {
        Assert.Equal(expectedProfileId, AutoProfileSelector.FindProfileForApp(Profiles, "dev", app)?.Id);
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("code")]
    public void Returns_null_when_no_profile_of_the_device_lists_the_app(string? app)
    {
        Assert.Null(AutoProfileSelector.FindProfileForApp(Profiles, "dev", app));
    }

    [Fact]
    public void App_lists_and_the_auto_switch_setting_are_saved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ProfileStore(path);
            store.Save(new ProfileDocument(1, "default", Profiles, AutoSwitchEnabled: false));

            var loaded = store.Load()!;
            Assert.False(loaded.AutoSwitchEnabled);
            Assert.Equal(["obs64", "Streamlabs OBS.exe"], loaded.Profiles.Single(profile => profile.Id == "streaming").AutoSwitchApps);
            Assert.Null(loaded.Profiles.Single(profile => profile.Id == "default").AutoSwitchApps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Older_profile_files_default_to_auto_switching_on()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"version":1,"selectedProfileId":"a","profiles":[{"id":"a","name":"A","deviceId":"dev","bindings":[]}]}""");
            var loaded = new ProfileStore(path).Load()!;
            Assert.True(loaded.AutoSwitchEnabled);
            Assert.Null(Assert.Single(loaded.Profiles).AutoSwitchApps);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
