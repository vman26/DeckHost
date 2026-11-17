using DeckHost.Core.Plugins;
using System.Net;
using System.Text;

namespace DeckHost.Tests;

/// <summary>Installing a plugin from a GitHub repository's releases.</summary>
public sealed class GitHubInstallTests
{
    private const string RepositoryLink = "https://github.com/you/clock-plugin";
    private const string StableUrl = "https://example.com/stable";
    private const string Checksum = "ABC123";

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

    private const string OnlyAPreRelease = """[ { "tag_name": "v0.1-rc", "prerelease": true, "assets": [ { "name": "a.dhplugin", "browser_download_url": "https://example.com/a" } ] } ]""";
    private const string ReleaseWithoutAPluginFile = """[ { "tag_name": "v1", "assets": [ { "name": "notes.txt", "browser_download_url": "https://example.com/n" } ] } ]""";

    [Test]
    public void PickReleaseAsset_SeveralReleases_PicksTheNewestStableOneAndItsPluginFile()
    {
        // Arrange
        var releases = Releases;

        // Act
        var picked = PluginStore.PickReleaseAsset(releases);

        // Assert
        Assert.That(picked, Is.EqualTo(("2.0.0", StableUrl, Checksum)));
    }

    [Test]
    public void PickReleaseAsset_OnlyAPreRelease_UsesIt()
    {
        // Arrange
        var releases = OnlyAPreRelease;

        // Act
        var picked = PluginStore.PickReleaseAsset(releases);

        // Assert
        Assert.That(picked, Is.EqualTo(("0.1-rc", "https://example.com/a", (string?)null)));
    }

    [Test]
    public void PickReleaseAsset_ReleaseWithoutAPluginFile_IsNull()
    {
        // Arrange
        var releases = ReleaseWithoutAPluginFile;

        // Act
        var picked = PluginStore.PickReleaseAsset(releases);

        // Assert
        Assert.That(picked, Is.Null);
    }

    private static PluginStore StoreAnsweringWithTheReleases(Action<string> onRequest) =>
        new(new HttpClient(new UpdateCheckerTests.FakeHandler(request =>
        {
            onRequest(request.RequestUri!.AbsoluteUri);
            return Encoding.UTF8.GetBytes(Releases);
        })));

    [Test]
    public async Task FromGitHubAsync_RepositoryLink_AsksTheReleasesOfThatRepository()
    {
        // Arrange
        string? asked = null;
        var store = StoreAnsweringWithTheReleases(uri => asked = uri);

        // Act
        await store.FromGitHubAsync(RepositoryLink);

        // Assert
        Assert.That(asked, Does.StartWith("https://api.github.com/repos/you/clock-plugin/releases"));
    }

    [Test]
    public async Task FromGitHubAsync_RepositoryLink_BecomesADownloadWithGitHubsChecksum()
    {
        // Arrange
        var store = StoreAnsweringWithTheReleases(_ => { });

        // Act
        var entry = await store.FromGitHubAsync(RepositoryLink);

        // Assert
        Assert.That((entry.Id, entry.Version, entry.DownloadUrl, entry.Sha256, entry.Source), Is.EqualTo(("clock-plugin", "2.0.0", StableUrl, Checksum, PluginStore.GitHubSource)));
    }

    private static PluginStore StoreThatFindsNothing() => new(new HttpClient(new UpdateCheckerTests.FakeHandler(_ => null, HttpStatusCode.NotFound)));

    [Test]
    public void FromGitHubAsync_AddressThatIsNotGitHub_SaysSo()
    {
        // Arrange
        var store = StoreThatFindsNothing();

        // Act
        var install = () => store.FromGitHubAsync("https://example.com/x");

        // Assert
        Assert.That(Assert.ThrowsAsync<PluginException>(() => install()).Message, Does.Contain("isn't a GitHub repository"));
    }

    [Test]
    public void FromGitHubAsync_MissingRepository_NamesTheRepository()
    {
        // Arrange
        var store = StoreThatFindsNothing();

        // Act
        var install = () => store.FromGitHubAsync("https://github.com/you/missing");

        // Assert
        Assert.That(Assert.ThrowsAsync<PluginException>(() => install()).Message, Does.Contain("no public repository you/missing"));
    }
}
