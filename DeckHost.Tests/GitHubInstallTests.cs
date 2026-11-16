using DeckHost.Core.Plugins;
using System.Net;
using System.Text;

namespace DeckHost.Tests;

/// <summary>Installing a plugin from a GitHub repository's releases.</summary>
public sealed class GitHubInstallTests
{
    private const string Releases = """
        [
          { "tag_name": "v3.0.0", "draft": true, "prerelease": false,
            "assets": [ { "name": "x.streamDeckPlugin", "browser_download_url": "https://example.com/draft" } ] },
          { "tag_name": "v2.1.0-beta", "draft": false, "prerelease": true,
            "assets": [ { "name": "x.streamDeckPlugin", "browser_download_url": "https://example.com/beta" } ] },
          { "tag_name": "v2.0.0", "draft": false, "prerelease": false,
            "assets": [
              { "name": "source.tar.gz", "browser_download_url": "https://example.com/src" },
              { "name": "x.zip", "browser_download_url": "https://example.com/zip" },
              { "name": "x.streamDeckPlugin", "browser_download_url": "https://example.com/stable", "digest": "sha256:ABC123" }
            ] }
        ]
        """;

    [Test]
    public void The_newest_stable_release_and_its_plugin_file_are_picked()
    {
        var picked = PluginStore.PickReleaseAsset(Releases);

        Assert.That(picked, Is.EqualTo(("2.0.0", "https://example.com/stable", "ABC123")));
    }

    [Test]
    public void A_pre_release_is_used_when_there_is_nothing_else()
    {
        var picked = PluginStore.PickReleaseAsset("""[ { "tag_name": "v0.1-rc", "prerelease": true, "assets": [ { "name": "a.dhplugin", "browser_download_url": "https://example.com/a" } ] } ]""");

        Assert.That(picked, Is.EqualTo(("0.1-rc", "https://example.com/a", (string?)null)));
        Assert.That(PluginStore.PickReleaseAsset("""[ { "tag_name": "v1", "assets": [ { "name": "notes.txt", "browser_download_url": "https://example.com/n" } ] } ]"""), Is.Null);
    }

    [Test]
    public async Task A_repository_link_becomes_a_download_with_githubs_checksum()
    {
        string? asked = null;
        var store = new PluginStore(new HttpClient(new UpdateCheckerTests.FakeHandler(request =>
        {
            asked = request.RequestUri!.AbsoluteUri;
            return Encoding.UTF8.GetBytes(Releases);
        })));

        var entry = await store.FromGitHubAsync("https://github.com/you/clock-plugin");

        Assert.That(asked, Does.StartWith("https://api.github.com/repos/you/clock-plugin/releases"));
        Assert.That((entry.Id, entry.Version, entry.DownloadUrl, entry.Sha256, entry.Source), Is.EqualTo(("clock-plugin", "2.0.0", "https://example.com/stable", "ABC123", PluginStore.GitHubSource)));
    }

    [Test]
    public async Task Clear_messages_for_wrong_addresses_and_missing_repositories()
    {
        var store = new PluginStore(new HttpClient(new UpdateCheckerTests.FakeHandler(_ => null, HttpStatusCode.NotFound)));

        Assert.That((Assert.ThrowsAsync<PluginException>(() => store.FromGitHubAsync("https://example.com/x"))).Message, Does.Contain("isn't a GitHub repository"));
        Assert.That((Assert.ThrowsAsync<PluginException>(() => store.FromGitHubAsync("https://github.com/you/missing"))).Message, Does.Contain("no public repository you/missing"));
    }
}
