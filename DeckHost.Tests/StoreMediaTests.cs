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

    [TestCase("https://github.com/OpenActionPlugins/counter", "OpenActionPlugins", "counter", null, "")]
    [TestCase("https://github.com/owner/repo.git", "owner", "repo", null, "")]
    [TestCase("https://www.github.com/owner/repo/", "owner", "repo", null, "")]
    [TestCase("https://github.com/owner/mono/tree/main/plugins/clock", "owner", "mono", "main", "plugins/clock")]
    public void Reads_GitHub_repository_urls(string url, string owner, string repository, string? reference, string folder) =>
        Assert.That(GitHubLocation.Parse(url), Is.EqualTo(new GitHubLocation(owner, repository, reference, folder)));

    [TestCase("https://barraider.github.io/")]
    [TestCase("https://github.com/owner")]
    [TestCase("https://gitlab.com/owner/repo")]
    [TestCase("not a url")]
    [TestCase(null)]
    public void Other_urls_are_not_repositories(string? url) => Assert.That(GitHubLocation.Parse(url), Is.Null);

    [Test]
    public void Repository_bases_point_at_raw_files_and_web_pages()
    {
        var plain = GitHubLocation.Parse("https://github.com/o/r")!;
        Assert.That(plain.RawBase.AbsoluteUri, Is.EqualTo("https://raw.githubusercontent.com/o/r/HEAD/"));
        Assert.That(plain.WebBase.AbsoluteUri, Is.EqualTo("https://github.com/o/r/blob/HEAD/"));

        var folder = GitHubLocation.Parse("https://github.com/o/r/tree/v2/src/my plugin")!;
        Assert.That(folder.RawBase.AbsoluteUri, Is.EqualTo("https://raw.githubusercontent.com/o/r/v2/src/my%20plugin/"));
    }

    [Test]
    public void The_repository_comes_from_the_repository_or_the_page()
    {
        var openAction = Entry(repository: "https://github.com/o/r");
        var elgato = Entry(repository: "https://barraider.github.io/", page: "https://github.com/o/page-repo");

        Assert.That(StoreMedia.FindRepository(openAction)!.Repository, Is.EqualTo("r"));
        Assert.That(StoreMedia.FindRepository(elgato)!.Repository, Is.EqualTo("page-repo"));
        Assert.That(StoreMedia.FindRepository(Entry(repository: "https://example.com")), Is.Null);
    }

    [Test]
    public void GitHub_blob_links_are_read_from_the_raw_file()
    {
        Assert.That(StoreMedia.ToRawGitHubUrl("https://github.com/o/r/blob/main/docs/shot.png"), Is.EqualTo("https://raw.githubusercontent.com/o/r/main/docs/shot.png"));
        Assert.That(StoreMedia.ToRawGitHubUrl("https://example.com/a.png"), Is.EqualTo("https://example.com/a.png"));
    }

    [Test]
    public void Elgato_archive_icons_come_from_the_archive()
    {
        var entry = Expect.Single(PluginStore.ParseElgatoArchiveCatalog("""
            [ { "id": "com.barraider.soundpad", "name": "Soundpad", "icon": "https://appstore.elgato.com/streamDeckPlugin/x.png" } ]
            """));
        Assert.That(entry.IconUrl, Is.EqualTo("https://plugins.amankhanna.me/icons/com.barraider.soundpad.png"));
    }

    [Test]
    public async Task Finds_the_readme_under_its_usual_names()
    {
        var requested = new List<string>();
        var media = new StoreMedia(new HttpClient(new FakeHandler(request =>
        {
            requested.Add(request.RequestUri!.AbsoluteUri);
            return request.RequestUri!.AbsolutePath.EndsWith("/readme.md", StringComparison.Ordinal) ? "# Hello"u8.ToArray() : null;
        })), _cache);

        var readme = await media.GetReadmeAsync(Entry(repository: "https://github.com/o/r"));

        Assert.That(readme, Is.Not.Null);
        Assert.That(readme.Markdown, Is.EqualTo("# Hello"));
        Assert.That(readme.ImageBase.AbsoluteUri, Is.EqualTo("https://raw.githubusercontent.com/o/r/HEAD/"));
        Assert.That(readme.LinkBase.AbsoluteUri, Is.EqualTo("https://github.com/o/r/blob/HEAD/"));
        Assert.That(requested, Is.EqualTo(new[] { "https://raw.githubusercontent.com/o/r/HEAD/README.md", "https://raw.githubusercontent.com/o/r/HEAD/readme.md" }));

        // Asked again: from memory.
        await media.GetReadmeAsync(Entry(repository: "https://github.com/o/r"));
        Assert.That(requested.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task No_repository_or_no_readme_gives_null()
    {
        var media = new StoreMedia(new HttpClient(new FakeHandler(_ => null)), _cache);

        Assert.That(await media.GetReadmeAsync(Entry(repository: "https://example.com")), Is.Null);
        Assert.That(await media.GetReadmeAsync(Entry(repository: "https://github.com/o/empty")), Is.Null);
    }

    [Test]
    public async Task Images_are_cached_on_disk()
    {
        var downloads = 0;
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3 };
        var handler = new FakeHandler(_ =>
        {
            downloads++;
            return png;
        });

        Assert.That(await new StoreMedia(new HttpClient(handler), _cache).GetImageAsync("https://example.com/icon.png"), Is.EqualTo(png));

        // A new session (a new StoreMedia) reads the file instead of downloading again.
        Assert.That(await new StoreMedia(new HttpClient(handler), _cache).GetImageAsync("https://example.com/icon.png"), Is.EqualTo(png));
        Assert.That(downloads, Is.EqualTo(1));
    }

    [Test]
    public async Task Missing_images_and_local_files_are_handled()
    {
        var media = new StoreMedia(new HttpClient(new FakeHandler(_ => null)), _cache);
        Assert.That(await media.GetImageAsync("https://example.com/missing.png"), Is.Null);
        Assert.That(await media.GetImageAsync("javascript:alert(1)"), Is.Null);
        Assert.That(await media.GetImageAsync(null), Is.Null);

        var local = Path.Combine(_cache, "local.png");
        await File.WriteAllBytesAsync(local, [1, 2, 3]);
        Assert.That(await media.GetImageAsync(local), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Installed_plugins_report_their_icon()
    {
        var root = Directory.CreateTempSubdirectory("deckhost-icon-catalog").FullName;
        try
        {
            var plugin = Path.Combine(root, "functional", "test.plugin");
            Directory.CreateDirectory(plugin);
            File.WriteAllText(Path.Combine(plugin, PluginManifest.FileName),
                """{ "id": "test.plugin", "name": "Test", "version": "1.0.0", "assembly": "Missing.dll", "icon": "icon.png" }""");
            File.WriteAllBytes(Path.Combine(plugin, "icon.png"), [1]);

            var catalog = new PluginCatalog(root, [], [], []);
            catalog.Load();
            var entry = Expect.Single(catalog.Entries, entry => entry.Id == "test.plugin");
            Assert.That(entry.IconPath, Is.EqualTo(Path.Combine(plugin, "icon.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("docs/shot.png", "https://raw.githubusercontent.com/o/r/HEAD/docs/shot.png")]
    [TestCase("./img/a.svg", "https://raw.githubusercontent.com/o/r/HEAD/img/a.svg")]
    [TestCase("/assets/logo.png", "https://raw.githubusercontent.com/o/r/HEAD/assets/logo.png")]
    [TestCase("https://img.shields.io/badge/x-y-blue", "https://img.shields.io/badge/x-y-blue")]
    [TestCase("#installation", null)]
    [TestCase("javascript:alert(1)", null)]
    [TestCase("", null)]
    public void Readme_urls_resolve_against_the_repository(string url, string? expected) =>
        Assert.That(MarkdownView.Resolve(url, new Uri("https://raw.githubusercontent.com/o/r/HEAD/")), Is.EqualTo(expected));

    [Test]
    public void Root_relative_links_stay_in_the_repository()
    {
        Assert.That(MarkdownView.Resolve("/LICENSE", new Uri("https://github.com/o/r/blob/HEAD/plugins/x/")), Is.EqualTo("https://github.com/o/r/blob/HEAD/LICENSE"));
        Assert.That(MarkdownView.Resolve("docs/setup.md", new Uri("https://github.com/o/r/blob/HEAD/plugins/x/")), Is.EqualTo("https://github.com/o/r/blob/HEAD/plugins/x/docs/setup.md"));
    }

    [Test]
    public void Reads_images_and_text_from_readme_html()
    {
        const string html = """
            <p align="center">
              <img src="docs/logo.png" width="120" alt="Logo">
              <img src='https://example.com/b.svg'>
            </p>
            <!-- hidden -->
            <h3>Hello &amp; welcome</h3><br/>to the plugin
            """;

        Assert.That(MarkdownView.FindHtmlImages(html), Is.EqualTo(new[] { ("docs/logo.png", 120d), ("https://example.com/b.svg", (double?)null) }));
        Assert.That(MarkdownView.HtmlToText(html), Is.EqualTo("Hello & welcome\nto the plugin"));
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
