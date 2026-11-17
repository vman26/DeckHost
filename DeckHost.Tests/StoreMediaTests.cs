using DeckHost.App.Views;
using DeckHost.Core.Plugins;
using System.Net;

namespace DeckHost.Tests;

public sealed class StoreMediaTests : IDisposable
{
    private readonly string _cache = Directory.CreateTempSubdirectory("deckhost-store-media").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_cache, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private const string RawBase = "https://raw.githubusercontent.com/o/r/HEAD/";
    private const string WebBase = "https://github.com/o/r/blob/HEAD/";
    private const string ExampleRepository = "https://github.com/o/r";
    private const string NotARepository = "https://example.com";

    [TestCase("https://github.com/OpenActionPlugins/counter", "OpenActionPlugins", "counter", null, "")]
    [TestCase("https://github.com/owner/repo.git", "owner", "repo", null, "")]
    [TestCase("https://www.github.com/owner/repo/", "owner", "repo", null, "")]
    [TestCase("https://github.com/owner/mono/tree/main/plugins/clock", "owner", "mono", "main", "plugins/clock")]
    public void Parse_AGitHubRepositoryUrl_ReadsOwnerRepositoryRefAndFolder(string url, string owner, string repository, string? reference, string folder)
    {
        // Act
        var location = GitHubLocation.Parse(url);

        // Assert
        Assert.That(location, Is.EqualTo(new GitHubLocation(owner, repository, reference, folder)));
    }

    [TestCase("https://barraider.github.io/")]
    [TestCase("https://github.com/owner")]
    [TestCase("https://gitlab.com/owner/repo")]
    [TestCase("not a url")]
    [TestCase(null)]
    public void Parse_AnUrlThatIsNotARepository_IsNull(string? url)
    {
        // Act
        var location = GitHubLocation.Parse(url);

        // Assert
        Assert.That(location, Is.Null);
    }

    [Test]
    public void RawBase_AWholeRepository_PointsAtItsRawFiles()
    {
        // Arrange
        var plain = GitHubLocation.Parse(ExampleRepository)!;

        // Act
        var raw = plain.RawBase.AbsoluteUri;

        // Assert
        Assert.That(raw, Is.EqualTo(RawBase));
    }

    [Test]
    public void WebBase_AWholeRepository_PointsAtItsWebPages()
    {
        // Arrange
        var plain = GitHubLocation.Parse(ExampleRepository)!;

        // Act
        var web = plain.WebBase.AbsoluteUri;

        // Assert
        Assert.That(web, Is.EqualTo(WebBase));
    }

    [Test]
    public void RawBase_AFolderOfARef_PointsAtThatFolder()
    {
        // Arrange
        var folder = GitHubLocation.Parse("https://github.com/o/r/tree/v2/src/my plugin")!;

        // Act
        var raw = folder.RawBase.AbsoluteUri;

        // Assert
        Assert.That(raw, Is.EqualTo("https://raw.githubusercontent.com/o/r/v2/src/my%20plugin/"));
    }

    [Test]
    public void FindRepository_AnEntryWithARepositoryUrl_UsesIt()
    {
        // Arrange
        var openAction = Entry(repository: ExampleRepository);

        // Act
        var location = StoreMedia.FindRepository(openAction);

        // Assert
        Assert.That(location!.Repository, Is.EqualTo("r"));
    }

    [Test]
    public void FindRepository_AnEntryWhoseRepositoryIsAWebsite_UsesItsGitHubPage()
    {
        // Arrange
        var elgato = Entry(repository: "https://barraider.github.io/", page: "https://github.com/o/page-repo");

        // Act
        var location = StoreMedia.FindRepository(elgato);

        // Assert
        Assert.That(location!.Repository, Is.EqualTo("page-repo"));
    }

    [Test]
    public void FindRepository_AnEntryWithoutAGitHubRepository_IsNull()
    {
        // Act
        var location = StoreMedia.FindRepository(Entry(repository: NotARepository));

        // Assert
        Assert.That(location, Is.Null);
    }

    [Test]
    public void ToRawGitHubUrl_AGitHubBlobLink_IsReadFromTheRawFile()
    {
        // Act
        var url = StoreMedia.ToRawGitHubUrl("https://github.com/o/r/blob/main/docs/shot.png");

        // Assert
        Assert.That(url, Is.EqualTo("https://raw.githubusercontent.com/o/r/main/docs/shot.png"));
    }

    [Test]
    public void ToRawGitHubUrl_AnyOtherLink_IsUnchanged()
    {
        // Act
        var url = StoreMedia.ToRawGitHubUrl("https://example.com/a.png");

        // Assert
        Assert.That(url, Is.EqualTo("https://example.com/a.png"));
    }

    [Test]
    public void ParseElgatoArchiveCatalog_AnEntry_TakesItsIconFromTheArchive()
    {
        // Arrange
        const string catalog = """
            [ { "id": "com.barraider.soundpad", "name": "Soundpad", "icon": "https://appstore.elgato.com/streamDeckPlugin/x.png" } ]
            """;

        // Act
        var entry = Expect.Single(PluginStore.ParseElgatoArchiveCatalog(catalog));

        // Assert
        Assert.That(entry.IconUrl, Is.EqualTo("https://plugins.amankhanna.me/icons/com.barraider.soundpad.png"));
    }

    private StoreMedia MediaAnsweringReadmes(List<string> requested) => new(new HttpClient(new FakeHandler(request =>
    {
        requested.Add(request.RequestUri!.AbsoluteUri);
        return request.RequestUri!.AbsolutePath.EndsWith("/readme.md", StringComparison.Ordinal) ? "# Hello"u8.ToArray() : null;
    })), _cache);

    [Test]
    public async Task GetReadmeAsync_ARepositoryWhoseReadmeIsLowerCase_FindsItUnderItsUsualNames()
    {
        // Arrange
        var requested = new List<string>();
        var media = MediaAnsweringReadmes(requested);

        // Act
        var readme = await media.GetReadmeAsync(Entry(repository: ExampleRepository));

        // Assert
        Assert.That(readme, Is.Not.Null);
        Assert.That(readme.Markdown, Is.EqualTo("# Hello"));
        Assert.That(requested, Is.EqualTo(new[] { RawBase + "README.md", RawBase + "readme.md" }));
    }

    [Test]
    public async Task GetReadmeAsync_AFoundReadme_ResolvesImagesAndLinksAgainstTheRepository()
    {
        // Arrange
        var media = MediaAnsweringReadmes([]);

        // Act
        var readme = await media.GetReadmeAsync(Entry(repository: ExampleRepository));

        // Assert
        Assert.That(readme!.ImageBase.AbsoluteUri, Is.EqualTo(RawBase));
        Assert.That(readme.LinkBase.AbsoluteUri, Is.EqualTo(WebBase));
    }

    [Test]
    public async Task GetReadmeAsync_AskedAgain_ComesFromMemory()
    {
        // Arrange
        var requested = new List<string>();
        var media = MediaAnsweringReadmes(requested);
        await media.GetReadmeAsync(Entry(repository: ExampleRepository));

        // Act
        await media.GetReadmeAsync(Entry(repository: ExampleRepository));

        // Assert
        Assert.That(requested.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task GetReadmeAsync_NoGitHubRepository_IsNull()
    {
        // Arrange
        var media = new StoreMedia(new HttpClient(new FakeHandler(_ => null)), _cache);

        // Act
        var readme = await media.GetReadmeAsync(Entry(repository: NotARepository));

        // Assert
        Assert.That(readme, Is.Null);
    }

    [Test]
    public async Task GetReadmeAsync_ARepositoryWithoutAReadme_IsNull()
    {
        // Arrange
        var media = new StoreMedia(new HttpClient(new FakeHandler(_ => null)), _cache);

        // Act
        var readme = await media.GetReadmeAsync(Entry(repository: "https://github.com/o/empty"));

        // Assert
        Assert.That(readme, Is.Null);
    }

    [Test]
    public async Task GetImageAsync_ADownloadedImage_IsReturned()
    {
        // Arrange
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3 };
        var media = new StoreMedia(new HttpClient(new FakeHandler(_ => png)), _cache);

        // Act
        var image = await media.GetImageAsync("https://example.com/icon.png");

        // Assert
        Assert.That(image, Is.EqualTo(png));
    }

    [Test]
    public async Task GetImageAsync_ANewSessionAskingForTheSameImage_ReadsTheDiskCacheInsteadOfDownloadingAgain()
    {
        // Arrange
        var downloads = 0;
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3 };
        var handler = new FakeHandler(_ =>
        {
            downloads++;
            return png;
        });
        await new StoreMedia(new HttpClient(handler), _cache).GetImageAsync("https://example.com/icon.png");

        // Act
        var image = await new StoreMedia(new HttpClient(handler), _cache).GetImageAsync("https://example.com/icon.png");

        // Assert
        Assert.That(image, Is.EqualTo(png));
        Assert.That(downloads, Is.EqualTo(1));
    }

    [TestCase("https://example.com/missing.png")]
    [TestCase("javascript:alert(1)")]
    [TestCase(null)]
    public async Task GetImageAsync_AMissingOrUnsafeAddress_IsNull(string? address)
    {
        // Arrange
        var media = new StoreMedia(new HttpClient(new FakeHandler(_ => null)), _cache);

        // Act
        var image = await media.GetImageAsync(address);

        // Assert
        Assert.That(image, Is.Null);
    }

    [Test]
    public async Task GetImageAsync_ALocalFile_IsReadFromDisk()
    {
        // Arrange
        var media = new StoreMedia(new HttpClient(new FakeHandler(_ => null)), _cache);
        var local = Path.Combine(_cache, "local.png");
        await File.WriteAllBytesAsync(local, [1, 2, 3]);

        // Act
        var image = await media.GetImageAsync(local);

        // Assert
        Assert.That(image, Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void Load_AnInstalledPluginWithAnIcon_ReportsItsIconPath()
    {
        // Arrange
        var root = Directory.CreateTempSubdirectory("deckhost-icon-catalog").FullName;
        try
        {
            var plugin = Path.Combine(root, "functional", "test.plugin");
            Directory.CreateDirectory(plugin);
            File.WriteAllText(Path.Combine(plugin, PluginManifest.FileName),
                """{ "id": "test.plugin", "name": "Test", "version": "1.0.0", "assembly": "Missing.dll", "icon": "icon.png" }""");
            File.WriteAllBytes(Path.Combine(plugin, "icon.png"), [1]);
            var catalog = new PluginCatalog(root, [], [], []);

            // Act
            catalog.Load();

            // Assert
            var entry = Expect.Single(catalog.Entries, entry => entry.Id == "test.plugin");
            Assert.That(entry.IconPath, Is.EqualTo(Path.Combine(plugin, "icon.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("docs/shot.png", RawBase + "docs/shot.png")]
    [TestCase("./img/a.svg", RawBase + "img/a.svg")]
    [TestCase("/assets/logo.png", RawBase + "assets/logo.png")]
    [TestCase("https://img.shields.io/badge/x-y-blue", "https://img.shields.io/badge/x-y-blue")]
    [TestCase("#installation", null)]
    [TestCase("javascript:alert(1)", null)]
    [TestCase("", null)]
    public void Resolve_AReadmeUrl_IsResolvedAgainstTheRepository(string url, string? expected)
    {
        // Act
        var resolved = MarkdownView.Resolve(url, new Uri(RawBase));

        // Assert
        Assert.That(resolved, Is.EqualTo(expected));
    }

    [TestCase("/LICENSE", "https://github.com/o/r/blob/HEAD/LICENSE")]
    [TestCase("docs/setup.md", "https://github.com/o/r/blob/HEAD/plugins/x/docs/setup.md")]
    public void Resolve_ALinkInAFolderOfTheRepository_StaysInTheRepository(string url, string expected)
    {
        // Act
        var resolved = MarkdownView.Resolve(url, new Uri("https://github.com/o/r/blob/HEAD/plugins/x/"));

        // Assert
        Assert.That(resolved, Is.EqualTo(expected));
    }

    private const string ReadmeHtml = """
        <p align="center">
          <img src="docs/logo.png" width="120" alt="Logo">
          <img src='https://example.com/b.svg'>
        </p>
        <!-- hidden -->
        <h3>Hello &amp; welcome</h3><br/>to the plugin
        """;

    [Test]
    public void FindHtmlImages_ReadmeHtml_ListsTheImagesWithTheirWidths()
    {
        // Act
        var images = MarkdownView.FindHtmlImages(ReadmeHtml);

        // Assert
        Assert.That(images, Is.EqualTo(new[] { ("docs/logo.png", 120d), ("https://example.com/b.svg", (double?)null) }));
    }

    [Test]
    public void HtmlToText_ReadmeHtml_KeepsTheTextWithoutCommentsOrTags()
    {
        // Act
        var text = MarkdownView.HtmlToText(ReadmeHtml);

        // Assert
        Assert.That(text, Is.EqualTo("Hello & welcome\nto the plugin"));
    }

    private static StoreEntry Entry(string? repository = null, string? page = null) =>
        new("test.plugin", "Test", null, null, PluginStore.OpenActionSource, null, null, repository, null, PageUrl: page);

    private sealed class FakeHandler(Func<HttpRequestMessage, byte[]?> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = respond(request);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }
}
