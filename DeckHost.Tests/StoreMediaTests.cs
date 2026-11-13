using DeckHost.App.Views;
using DeckHost.Core.Plugins;
using System.Net;
using Xunit;

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

    [Theory]
    [InlineData("https://github.com/OpenActionPlugins/counter", "OpenActionPlugins", "counter", null, "")]
    [InlineData("https://github.com/owner/repo.git", "owner", "repo", null, "")]
    [InlineData("https://www.github.com/owner/repo/", "owner", "repo", null, "")]
    [InlineData("https://github.com/owner/mono/tree/main/plugins/clock", "owner", "mono", "main", "plugins/clock")]
    public void Reads_GitHub_repository_urls(string url, string owner, string repository, string? reference, string folder) =>
        Assert.Equal(new GitHubLocation(owner, repository, reference, folder), GitHubLocation.Parse(url));

    [Theory]
    [InlineData("https://barraider.github.io/")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void Other_urls_are_not_repositories(string? url) => Assert.Null(GitHubLocation.Parse(url));

    [Fact]
    public void Repository_bases_point_at_raw_files_and_web_pages()
    {
        var plain = GitHubLocation.Parse("https://github.com/o/r")!;
        Assert.Equal("https://raw.githubusercontent.com/o/r/HEAD/", plain.RawBase.AbsoluteUri);
        Assert.Equal("https://github.com/o/r/blob/HEAD/", plain.WebBase.AbsoluteUri);

        var folder = GitHubLocation.Parse("https://github.com/o/r/tree/v2/src/my plugin")!;
        Assert.Equal("https://raw.githubusercontent.com/o/r/v2/src/my%20plugin/", folder.RawBase.AbsoluteUri);
    }

    [Fact]
    public void The_repository_comes_from_the_repository_or_the_page()
    {
        var openAction = Entry(repository: "https://github.com/o/r");
        var elgato = Entry(repository: "https://barraider.github.io/", page: "https://github.com/o/page-repo");

        Assert.Equal("r", StoreMedia.FindRepository(openAction)!.Repository);
        Assert.Equal("page-repo", StoreMedia.FindRepository(elgato)!.Repository);
        Assert.Null(StoreMedia.FindRepository(Entry(repository: "https://example.com")));
    }

    [Fact]
    public void GitHub_blob_links_are_read_from_the_raw_file()
    {
        Assert.Equal("https://raw.githubusercontent.com/o/r/main/docs/shot.png", StoreMedia.ToRawGitHubUrl("https://github.com/o/r/blob/main/docs/shot.png"));
        Assert.Equal("https://example.com/a.png", StoreMedia.ToRawGitHubUrl("https://example.com/a.png"));
    }

    [Fact]
    public void Elgato_archive_icons_come_from_the_archive()
    {
        var entry = Assert.Single(PluginStore.ParseElgatoArchiveCatalog("""
            [ { "id": "com.barraider.soundpad", "name": "Soundpad", "icon": "https://appstore.elgato.com/streamDeckPlugin/x.png" } ]
            """));
        Assert.Equal("https://plugins.amankhanna.me/icons/com.barraider.soundpad.png", entry.IconUrl);
    }

    [Fact]
    public async Task Finds_the_readme_under_its_usual_names()
    {
        var requested = new List<string>();
        var media = new StoreMedia(new HttpClient(new FakeHandler(request =>
        {
            requested.Add(request.RequestUri!.AbsoluteUri);
            return request.RequestUri!.AbsolutePath.EndsWith("/readme.md", StringComparison.Ordinal) ? "# Hello"u8.ToArray() : null;
        })), _cache);

        var readme = await media.GetReadmeAsync(Entry(repository: "https://github.com/o/r"));

        Assert.NotNull(readme);
        Assert.Equal("# Hello", readme.Markdown);
        Assert.Equal("https://raw.githubusercontent.com/o/r/HEAD/", readme.ImageBase.AbsoluteUri);
        Assert.Equal("https://github.com/o/r/blob/HEAD/", readme.LinkBase.AbsoluteUri);
        Assert.Equal(["https://raw.githubusercontent.com/o/r/HEAD/README.md", "https://raw.githubusercontent.com/o/r/HEAD/readme.md"], requested);

        // Asked again: from memory.
        await media.GetReadmeAsync(Entry(repository: "https://github.com/o/r"));
        Assert.Equal(2, requested.Count);
    }

    [Fact]
    public async Task No_repository_or_no_readme_gives_null()
    {
        var media = new StoreMedia(new HttpClient(new FakeHandler(_ => null)), _cache);

        Assert.Null(await media.GetReadmeAsync(Entry(repository: "https://example.com")));
        Assert.Null(await media.GetReadmeAsync(Entry(repository: "https://github.com/o/empty")));
    }

    [Fact]
    public async Task Images_are_cached_on_disk()
    {
        var downloads = 0;
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3 };
        var handler = new FakeHandler(_ =>
        {
            downloads++;
            return png;
        });

        Assert.Equal(png, await new StoreMedia(new HttpClient(handler), _cache).GetImageAsync("https://example.com/icon.png"));

        // A new session (a new StoreMedia) reads the file instead of downloading again.
        Assert.Equal(png, await new StoreMedia(new HttpClient(handler), _cache).GetImageAsync("https://example.com/icon.png"));
        Assert.Equal(1, downloads);
    }

    [Fact]
    public async Task Missing_images_and_local_files_are_handled()
    {
        var media = new StoreMedia(new HttpClient(new FakeHandler(_ => null)), _cache);
        Assert.Null(await media.GetImageAsync("https://example.com/missing.png"));
        Assert.Null(await media.GetImageAsync("javascript:alert(1)"));
        Assert.Null(await media.GetImageAsync(null));

        var local = Path.Combine(_cache, "local.png");
        await File.WriteAllBytesAsync(local, [1, 2, 3]);
        Assert.Equal([1, 2, 3], await media.GetImageAsync(local));
    }

    [Fact]
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
            var entry = Assert.Single(catalog.Entries, entry => entry.Id == "test.plugin");
            Assert.Equal(Path.Combine(plugin, "icon.png"), entry.IconPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("docs/shot.png", "https://raw.githubusercontent.com/o/r/HEAD/docs/shot.png")]
    [InlineData("./img/a.svg", "https://raw.githubusercontent.com/o/r/HEAD/img/a.svg")]
    [InlineData("/assets/logo.png", "https://raw.githubusercontent.com/o/r/HEAD/assets/logo.png")]
    [InlineData("https://img.shields.io/badge/x-y-blue", "https://img.shields.io/badge/x-y-blue")]
    [InlineData("#installation", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    public void Readme_urls_resolve_against_the_repository(string url, string? expected) =>
        Assert.Equal(expected, MarkdownView.Resolve(url, new Uri("https://raw.githubusercontent.com/o/r/HEAD/")));

    [Fact]
    public void Root_relative_links_stay_in_the_repository()
    {
        Assert.Equal("https://github.com/o/r/blob/HEAD/LICENSE", MarkdownView.Resolve("/LICENSE", new Uri("https://github.com/o/r/blob/HEAD/plugins/x/")));
        Assert.Equal("https://github.com/o/r/blob/HEAD/plugins/x/docs/setup.md", MarkdownView.Resolve("docs/setup.md", new Uri("https://github.com/o/r/blob/HEAD/plugins/x/")));
    }

    [Fact]
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

        Assert.Equal([("docs/logo.png", 120d), ("https://example.com/b.svg", (double?)null)], MarkdownView.FindHtmlImages(html));
        Assert.Equal("Hello & welcome\nto the plugin", MarkdownView.HtmlToText(html));
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
