using DeckHost.Contracts.Models;
using DeckHost.Core.Plugins;
using DeckHost.Core.Rendering;
using DeckHost.Core.State;
using DeckHost.Core.StreamDeck;
using SkiaSharp;
using System.Text;
using System.Text.Json.Nodes;

namespace DeckHost.Tests;

public sealed class RenderingAndStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "deckhost-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void Rasterizes_svg_images()
    {
        var svg = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20"><rect width="20" height="20" fill="#ff0000"/></svg>""");

        Assert.That(SvgRasterizer.IsSvg(svg), Is.True);
        Assert.That(SvgRasterizer.IsSvg([0x89, 0x50, 0x4E, 0x47]), Is.False);

        using var image = SvgRasterizer.ToImage(svg, 64);
        Assert.That(image, Is.Not.Null);
        Assert.That(image.Width, Is.EqualTo(64));

        using var bitmap = SKBitmap.FromImage(image);
        var center = bitmap.GetPixel(32, 32);
        Assert.That(center.Red, Is.EqualTo((byte)255));
        Assert.That(center.Green, Is.EqualTo((byte)0));
    }

    [TestCase(TextVerticalAlignment.Top, true)]
    [TestCase(TextVerticalAlignment.Bottom, false)]
    public void Labels_follow_the_text_style(TextVerticalAlignment vertical, bool expectTopHalf)
    {
        using var resources = new SkiaDrawingResources();
        using var bitmap = new SKBitmap(new SKImageInfo(96, 96, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        var context = new SkiaRenderContext(canvas, 96, 96, "dev", "key.0", new VariableStore(), resources)
        {
            TextStyle = new TextStyle(Color: "#00FF00", Size: 14, Bold: true, Vertical: vertical),
        };

        context.DrawLabel("Hello\nWorld");
        canvas.Flush();

        var (top, bottom) = (CountGreen(bitmap, 0, 48), CountGreen(bitmap, 48, 96));
        Assert.That(expectTopHalf ? top > bottom : bottom > top, Is.True, $"top={top} bottom={bottom}");
    }

    [Test]
    public void Italic_and_bold_always_show()
    {
        using var resources = new SkiaDrawingResources();

        // The default font, a named font and a family that isn't installed: a real italic or bold
        // face when the font has one, else the text is slanted or emboldened.
        foreach (var family in new[] { null, SKTypeface.Default.FamilyName, "No Such Font Family" })
        {
            var italic = resources.GetFont(14, family, bold: false, italic: true);
            Assert.That(italic.Typeface.IsItalic || italic.SkewX < 0, Is.True, $"italic, family '{family}'");

            var bold = resources.GetFont(14, family, bold: true, italic: false);
            Assert.That(bold.Typeface.IsBold || bold.Embolden, Is.True, $"bold, family '{family}'");
            Assert.That(bold.SkewX, Is.EqualTo(0f));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Italic_changes_the_text_an_action_draws(bool label)
    {
        byte[] Draw(bool italic)
        {
            using var resources = new SkiaDrawingResources();
            using var bitmap = new SKBitmap(new SKImageInfo(96, 96, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Black);
            var context = new SkiaRenderContext(canvas, 96, 96, "dev", "key.0", new VariableStore(), resources)
            {
                TextStyle = new TextStyle(Italic: italic),
            };

            // The control's label, and text an action places itself (a clock, a counter).
            if (label)
            {
                context.DrawLabel("Hello");
            }
            else
            {
                context.DrawCenteredText("12:34", 48, 48, 20);
            }

            canvas.Flush();
            return bitmap.Bytes;
        }

        Assert.That(Draw(italic: true), Is.Not.EqualTo(Draw(italic: false)));
    }

    [Test]
    public void A_strip_shows_one_line()
    {
        using var resources = new SkiaDrawingResources();
        using var bitmap = new SKBitmap(new SKImageInfo(176, 112, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        var context = new SkiaRenderContext(canvas, 176, 112, "dev", "touch.0", new VariableStore(), resources);

        // The bottom 36 pixels, drawn compact: two lines of large text become one line that fits.
        context.DrawRegion(0, 76, 176, 36, "encoder.0", null, compact: true, region =>
        {
            Assert.That(region.IsCompact, Is.True);
            Assert.That((region.Width, region.Height, region.ControlId), Is.EqualTo((176, 36, "encoder.0")));
            region.TextStyle = new TextStyle(Color: "#00FF00", Size: 40);
            region.DrawLabel("Master volume\n42%");
        });
        canvas.Flush();

        Assert.That(CountGreen(bitmap, 0, 76), Is.EqualTo(0));
        Assert.That(CountGreen(bitmap, 76, 112) > 40, Is.True);
        var edgeColumns = new[] { 0, 1, 174, 175 };
        Assert.That(edgeColumns.Any(x => Enumerable.Range(76, 36).Any(y => bitmap.GetPixel(x, y).Green > 160)), Is.False);
    }

    [Test]
    public void Wraps_long_text_onto_several_lines()
    {
        using var resources = new SkiaDrawingResources();
        using var bitmap = new SKBitmap(new SKImageInfo(96, 96, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        var context = new SkiaRenderContext(canvas, 96, 96, "dev", "key.0", new VariableStore(), resources)
        {
            TextStyle = new TextStyle(Color: "#00FF00", Size: 14, Wrap: true),
        };

        context.DrawLabel("one two three four five six");
        canvas.Flush();

        // Wrapped text covers more than one line band.
        var rowsWithText = Enumerable.Range(0, 96).Count(y => Enumerable.Range(0, 96).Any(x => bitmap.GetPixel(x, y).Green > 160));
        Assert.That(rowsWithText > 20, Is.True, $"rows={rowsWithText}");
    }

    [Test]
    public void Parses_a_DeckHost_catalog_with_categories()
    {
        var entries = PluginStore.ParseDeckHostCatalog("""
            {
              "deckhost.plugin.obs": {
                "name": "OBS Studio",
                "author": "DeckHost",
                "description": "Scenes and recording",
                "version": "1.0.0",
                "category": "Streaming",
                "download_url": "https://example.com/obs.zip"
              }
            }
            """);

        var entry = Expect.Single(entries);
        Assert.That((entry.Id, entry.Name, entry.Category, entry.Source), Is.EqualTo(("deckhost.plugin.obs", "OBS Studio", "Streaming", PluginStore.DeckHostSource)));
        Assert.That(entry.DownloadUrl, Is.EqualTo("https://example.com/obs.zip"));
    }

    [Test]
    public void Copies_and_moves_stream_deck_settings_with_profiles_and_controls()
    {
        var data = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(data, "settings"));
        var instances = Path.Combine(data, "settings", "instances.json");
        File.WriteAllText(instances, """{ "dev|p1|key.0|com.test.action": { "channel": "general" } }""");

        using (var runtime = new StreamDeckRuntime(data, Path.Combine(_root, "logs"), (_, _) => { }))
        {
            IPluginStateTransfer transfer = runtime;
            transfer.CopyProfileSettings("p1", "p2");
            transfer.MoveControlSettings("dev", "p2", "key.0", "key.5", copy: false);
        }

        var saved = JsonNode.Parse(File.ReadAllText(instances))!.AsObject();
        Assert.That(saved["dev|p1|key.0|com.test.action"]?["channel"]?.GetValue<string>(), Is.EqualTo("general"));
        Assert.That(saved["dev|p2|key.5|com.test.action"]?["channel"]?.GetValue<string>(), Is.EqualTo("general"));
        Assert.That(saved.ContainsKey("dev|p2|key.0|com.test.action"), Is.False);
    }

    [Test]
    public void Shrinks_unwrapped_text_to_fit_the_width()
    {
        using var resources = new SkiaDrawingResources();
        using var bitmap = new SKBitmap(new SKImageInfo(96, 96, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        var context = new SkiaRenderContext(canvas, 96, 96, "dev", "key.0", new VariableStore(), resources)
        {
            TextStyle = new TextStyle(Color: "#00FF00", Wrap: false),
        };

        context.DrawLabel("Microphone muted");
        canvas.Flush();

        // Nothing is cut off at the edges: the line was made smaller instead.
        var edgeColumns = new[] { 0, 1, 94, 95 };
        Assert.That(edgeColumns.Any(x => Enumerable.Range(0, 96).Any(y => bitmap.GetPixel(x, y).Green > 160)), Is.False);
        Assert.That(CountGreen(bitmap, 0, 96) > 50, Is.True);
    }

    [TestCase("Discord", null, "Communication")]
    [TestCase("OBS Studio", "Control scenes", "Streaming")]
    [TestCase("Philips Hue", null, "Lighting & smart home")]
    [TestCase("Zzz", "nothing known", PluginCategories.Other)]
    public void Derives_a_category_when_the_catalog_has_none(string name, string? description, string expected) =>
        Assert.That(PluginCategories.Resolve(null, name, description, "com.example.plugin"), Is.EqualTo(expected));

    [Test]
    public void A_given_category_wins() => Assert.That(PluginCategories.Resolve("Tools", "Discord", null, "x"), Is.EqualTo("Tools"));

    [Test]
    public async Task Finds_plugins_in_a_local_folder_and_installs_from_it()
    {
        // A plugin project's build output (plugin.json next to its assembly) and a package.
        var build = Path.Combine(_root, "plugins", "My.Plugin", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, "plugin.json"), """{ "id": "my.plugin", "name": "My Plugin", "version": "1.0.0", "assembly": "My.Plugin.dll" }""");
        File.WriteAllBytes(Path.Combine(build, "My.Plugin.dll"), [0]);

        // The project folder's own plugin.json has no assembly next to it, so it isn't listed.
        File.WriteAllText(Path.Combine(_root, "plugins", "My.Plugin", "plugin.json"), """{ "id": "my.plugin", "name": "Source", "version": "1.0.0", "assembly": "My.Plugin.dll" }""");

        var packageSource = Path.Combine(_root, "package");
        Directory.CreateDirectory(packageSource);
        File.WriteAllText(Path.Combine(packageSource, "plugin.json"), """{ "id": "other.plugin", "name": "Other", "version": "2.0.0", "assembly": "Other.dll", "description": "Volume control" }""");
        System.IO.Compression.ZipFile.CreateFromDirectory(packageSource, Path.Combine(_root, "plugins", "other.zip"));

        var entries = PluginStore.LoadLocalSource(Path.Combine(_root, "plugins")).OrderBy(static entry => entry.Id).ToList();

        Assert.That(entries.Select(static entry => entry.Id), Is.EqualTo(new[] { "my.plugin", "other.plugin" }));
        Assert.That(entries[0].Name, Is.EqualTo("My Plugin"));
        Assert.That(entries[1].Category, Is.EqualTo("Audio"));

        using var http = new HttpClient();
        var store = new PluginStore(http);
        var downloaded = await store.DownloadAsync(entries[0], Path.Combine(_root, "downloads"), null, CancellationToken.None);
        using var archive = System.IO.Compression.ZipFile.OpenRead(downloaded);
        Assert.That(archive.Entries.Any(static entry => entry.FullName == "plugin.json"), Is.True);
    }

    [Test]
    public async Task The_DeckHost_catalog_carries_checksums_permissions_and_paid_plugins()
    {
        Directory.CreateDirectory(_root);
        var package = Path.Combine(_root, "good.zip");
        File.WriteAllText(package, "package bytes");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package)));
        var entries = PluginStore.ParseDeckHostCatalog($$"""
            [
              { "id": "a.plugin", "name": "A", "download_url": "{{package.Replace("\\", "\\\\")}}", "sha256": "{{sha.ToLowerInvariant()}}", "permissions": ["network", "processes"] },
              { "id": "b.plugin", "name": "B", "purchase_url": "https://example.test/buy", "price": "€5" }
            ]
            """);

        var free = entries.Single(entry => entry.Id == "a.plugin");
        Assert.That(free.Permissions, Is.EqualTo(new[] { "network", "processes" }));
        var paid = entries.Single(entry => entry.Id == "b.plugin");
        Assert.That((paid.PurchaseUrl, paid.Price), Is.EqualTo(("https://example.test/buy", "€5")));
        Assert.That(paid.StorePageUrl, Is.EqualTo("https://example.test/buy"));

        using var http = new HttpClient();
        var store = new PluginStore(http) { IncludeOfficialCatalog = false };
        Assert.ThrowsAsync<PluginException>(() => store.DownloadAsync(paid, Path.Combine(_root, "downloads"), null, CancellationToken.None));
    }

    [Test]
    public async Task A_download_that_does_not_match_its_checksum_is_refused()
    {
        var bytes = "the real package"u8.ToArray();
        using var http = new HttpClient(new FixedResponse(bytes));
        var store = new PluginStore(http) { IncludeOfficialCatalog = false };
        var good = new StoreEntry("a", "A", null, null, "DeckHost", "1.0", null, null, "https://example.test/a.zip") { Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) };
        var bad = good with { Sha256 = new string('0', 64) };

        var path = await store.DownloadAsync(good, Path.Combine(_root, "downloads"), null, CancellationToken.None);
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        var error = Assert.ThrowsAsync<PluginException>(() => store.DownloadAsync(bad, Path.Combine(_root, "downloads"), null, CancellationToken.None));
        Assert.That(error.Message, Does.Contain("checksum"));
        Assert.That(Directory.GetFiles(Path.Combine(_root, "downloads")), Has.Exactly(1).Items);
    }

    [Test]
    public async Task The_official_catalog_is_read_first_and_a_missing_one_is_simply_empty()
    {
        using var missing = new HttpClient(new FixedResponse([], System.Net.HttpStatusCode.NotFound));
        var store = new PluginStore(missing);
        var (entries, errors) = await store.LoadAsync(CancellationToken.None);
        Assert.That(errors.Any(error => error.StartsWith(PluginStore.DeckHostSource, StringComparison.Ordinal)), Is.False);
        Assert.That(entries.Any(entry => entry.Source == PluginStore.DeckHostSource), Is.False);

        using var present = new HttpClient(new FixedResponse("""{ "deckhost.plugin.obs": { "name": "OBS Studio", "download_url": "https://example.test/obs.zip" } }"""u8.ToArray()));
        var (official, _) = await new PluginStore(present).LoadAsync(CancellationToken.None);
        Assert.That(official.Any(entry => entry.Id == "deckhost.plugin.obs" && entry.Source == PluginStore.DeckHostSource), Is.True);
    }

    private const string MirrorCatalog = """
        [
          { "id": "com.a.one", "name": "One", "author": "A", "version": "1.0", "icon": "https://plugins.amankhanna.me/icons/com.a.one.png", "marketplace_url": "https://marketplace.elgato.com/search?q=One",
            "license": "MIT", "source_repository": "https://github.com/o/one", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "download_url": "https://github.com/vman26/deckhost-plugins/releases/download/elgato-mirror/com.a.one-1.0.zip", "mirrored": true },
          { "id": "com.a.two", "name": "Two", "version": "1.0", "license": "CC-BY-NC-4.0", "sha256": null, "download_url": null, "mirrored": false },
          { "id": "com.a.three", "name": "Three", "version": "1.0", "sha256": "short", "download_url": "https://example.test/three.zip", "mirrored": true },
          { "id": "com.a.four", "name": "Four", "version": "1.0", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "download_url": "http://example.test/four.zip", "mirrored": true },
          { "id": "com.a.five", "name": "Five", "version": "1.0", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "download_url": "https://example.test/five.zip", "mirrored": false },
          { "id": "com.a.only-mirror", "name": "Only here", "version": "1.0", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "download_url": "https://example.test/only.zip", "mirrored": true }
        ]
        """;

    private const string ArchiveCatalog = """
        [ { "id": "com.a.one", "name": "One", "author": "A", "version": "1.1", "link": "https://github.com/o/one" },
          { "id": "com.a.two", "name": "Two", "author": "B", "version": "1.0", "link": "https://example.test" } ]
        """;

    [Test]
    public void The_mirror_catalog_gives_checksummed_copies_and_nothing_less()
    {
        var copies = PluginStore.ParseElgatoMirrorCatalog(MirrorCatalog);

        // Only a mirrored entry with an https download and a full SHA-256 counts.
        Assert.That(copies.Select(copy => copy.Id), Is.EqualTo(new[] { "com.a.one", "com.a.only-mirror" }));
        var one = copies[0];
        Assert.That(one.Sha256, Has.Length.EqualTo(64));
        Assert.That(one.DownloadUrl, Does.StartWith("https://github.com/vman26/deckhost-plugins/releases/download/elgato-mirror/"));
        Assert.That(one.Source, Is.EqualTo(PluginStore.ElgatoArchiveSource));
        Assert.That(one.Description, Does.Contain("MIT"));
        Assert.That(one.RepositoryUrl, Is.EqualTo("https://github.com/o/one"));
        Assert.That(PluginStore.ParseElgatoMirrorCatalog("{ \"not\": \"an array\" }"), Is.Empty);
    }

    [Test]
    public void A_mirrored_copy_replaces_the_archives_download_and_only_for_what_the_archive_lists()
    {
        var archive = PluginStore.ParseElgatoArchiveCatalog(ArchiveCatalog);
        var merged = PluginStore.MergeElgatoMirror(archive, PluginStore.ParseElgatoMirrorCatalog(MirrorCatalog));

        Assert.That(merged.Select(entry => entry.Id), Is.EqualTo(new[] { "com.a.one", "com.a.two" }));
        Assert.That(merged[0].Sha256, Is.Not.Null);
        Assert.That(merged[0].DownloadUrl, Does.Contain("elgato-mirror"));
        // Not copied (license): the archive's own download, as before.
        Assert.That(merged[1].DownloadUrl, Is.EqualTo("https://plugins.amankhanna.me/rezipped/com.a.two.zip"));
        Assert.That(merged[1].Sha256, Is.Null);
        Assert.That(PluginStore.MergeElgatoMirror(archive, []), Is.SameAs(archive));
    }

    [Test]
    public async Task The_store_reads_the_mirror_with_the_archive_and_copes_without_either()
    {
        static HttpClient Client(Func<string, (System.Net.HttpStatusCode Status, string Body)> route) => new(new RoutedResponse(route));
        static (System.Net.HttpStatusCode, string) NotFound() => (System.Net.HttpStatusCode.NotFound, string.Empty);
        static IEnumerable<string> ElgatoErrors(IEnumerable<string> errors) => errors.Where(error => error.StartsWith(PluginStore.ElgatoArchiveSource, StringComparison.Ordinal));

        // Both: the copy with its checksum wins for the plugin it covers.
        using var both = Client(url => url switch
        {
            PluginStore.ElgatoMirrorCatalogUrl => (System.Net.HttpStatusCode.OK, MirrorCatalog),
            var u when u.Contains("amankhanna.me/catalogue.json", StringComparison.Ordinal) => (System.Net.HttpStatusCode.OK, ArchiveCatalog),
            _ => NotFound(),
        });
        var (entries, errors) = await new PluginStore(both).LoadAsync(CancellationToken.None);
        var elgato = entries.Where(entry => entry.Source == PluginStore.ElgatoArchiveSource).ToList();
        Assert.That(elgato.Select(entry => entry.Id), Is.EqualTo(new[] { "com.a.one", "com.a.two" }));
        Assert.That(elgato[0].Sha256, Is.Not.Null);
        Assert.That(ElgatoErrors(errors), Is.Empty);

        // No mirror yet (404): exactly the archive, no error.
        using var noMirror = Client(url => url.Contains("amankhanna.me/catalogue.json", StringComparison.Ordinal) ? (System.Net.HttpStatusCode.OK, ArchiveCatalog) : NotFound());
        (entries, errors) = await new PluginStore(noMirror).LoadAsync(CancellationToken.None);
        Assert.That(entries.Where(entry => entry.Source == PluginStore.ElgatoArchiveSource).Select(entry => entry.Sha256), Is.EqualTo(new string?[] { null, null }));
        Assert.That(ElgatoErrors(errors), Is.Empty);

        // A broken mirror file is ignored too.
        using var broken = Client(url => url == PluginStore.ElgatoMirrorCatalogUrl ? (System.Net.HttpStatusCode.OK, "{ not json") : url.Contains("amankhanna.me/catalogue.json", StringComparison.Ordinal) ? (System.Net.HttpStatusCode.OK, ArchiveCatalog) : NotFound());
        (entries, errors) = await new PluginStore(broken).LoadAsync(CancellationToken.None);
        Assert.That(entries.Count(entry => entry.Source == PluginStore.ElgatoArchiveSource), Is.EqualTo(2));
        Assert.That(ElgatoErrors(errors), Is.Empty);

        // The archive is down: the mirrored copies still install; with neither, the error shows.
        using var archiveDown = Client(url => url == PluginStore.ElgatoMirrorCatalogUrl ? (System.Net.HttpStatusCode.OK, MirrorCatalog) : NotFound());
        (entries, errors) = await new PluginStore(archiveDown).LoadAsync(CancellationToken.None);
        Assert.That(entries.Where(entry => entry.Source == PluginStore.ElgatoArchiveSource).Select(entry => entry.Id), Is.EqualTo(new[] { "com.a.one", "com.a.only-mirror" }));
        Assert.That(ElgatoErrors(errors), Is.Empty);
        using var neither = Client(_ => NotFound());
        (_, errors) = await new PluginStore(neither).LoadAsync(CancellationToken.None);
        Assert.That(ElgatoErrors(errors), Is.Not.Empty);

        // Tests and tools turn the official sources off: then the mirror isn't asked for either.
        var asked = new List<string>();
        using var recording = Client(url => { asked.Add(url); return url.Contains("amankhanna.me/catalogue.json", StringComparison.Ordinal) ? (System.Net.HttpStatusCode.OK, ArchiveCatalog) : NotFound(); });
        await new PluginStore(recording) { IncludeOfficialCatalog = false }.LoadAsync(CancellationToken.None);
        Assert.That(asked, Does.Not.Contain(PluginStore.ElgatoMirrorCatalogUrl));
    }

    private sealed class RoutedResponse(Func<string, (System.Net.HttpStatusCode Status, string Body)> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = route(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class FixedResponse(byte[] body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }

    private static int CountGreen(SKBitmap bitmap, int fromY, int toY)
    {
        var count = 0;
        for (var y = fromY; y < toY; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Green > 160 && pixel.Red < 100)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
