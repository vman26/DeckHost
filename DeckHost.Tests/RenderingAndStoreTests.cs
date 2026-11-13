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

    [Fact]
    public void Rasterizes_svg_images()
    {
        var svg = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20"><rect width="20" height="20" fill="#ff0000"/></svg>""");

        Assert.True(SvgRasterizer.IsSvg(svg));
        Assert.False(SvgRasterizer.IsSvg([0x89, 0x50, 0x4E, 0x47]));

        using var image = SvgRasterizer.ToImage(svg, 64);
        Assert.NotNull(image);
        Assert.Equal(64, image.Width);

        using var bitmap = SKBitmap.FromImage(image);
        var center = bitmap.GetPixel(32, 32);
        Assert.Equal((byte)255, center.Red);
        Assert.Equal((byte)0, center.Green);
    }

    [Theory]
    [InlineData(TextVerticalAlignment.Top, true)]
    [InlineData(TextVerticalAlignment.Bottom, false)]
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
        Assert.True(expectTopHalf ? top > bottom : bottom > top, $"top={top} bottom={bottom}");
    }

    [Fact]
    public void Italic_and_bold_always_show()
    {
        using var resources = new SkiaDrawingResources();

        // The default font, a named font and a family that isn't installed: a real italic or bold
        // face when the font has one, else the text is slanted or emboldened.
        foreach (var family in new[] { null, SKTypeface.Default.FamilyName, "No Such Font Family" })
        {
            var italic = resources.GetFont(14, family, bold: false, italic: true);
            Assert.True(italic.Typeface.IsItalic || italic.SkewX < 0, $"italic, family '{family}'");

            var bold = resources.GetFont(14, family, bold: true, italic: false);
            Assert.True(bold.Typeface.IsBold || bold.Embolden, $"bold, family '{family}'");
            Assert.Equal(0f, bold.SkewX);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
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

        Assert.NotEqual(Draw(italic: false), Draw(italic: true));
    }

    [Fact]
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
            Assert.True(region.IsCompact);
            Assert.Equal((176, 36, "encoder.0"), (region.Width, region.Height, region.ControlId));
            region.TextStyle = new TextStyle(Color: "#00FF00", Size: 40);
            region.DrawLabel("Master volume\n42%");
        });
        canvas.Flush();

        Assert.Equal(0, CountGreen(bitmap, 0, 76));
        Assert.True(CountGreen(bitmap, 76, 112) > 40);
        var edgeColumns = new[] { 0, 1, 174, 175 };
        Assert.DoesNotContain(edgeColumns, x => Enumerable.Range(76, 36).Any(y => bitmap.GetPixel(x, y).Green > 160));
    }

    [Fact]
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
        Assert.True(rowsWithText > 20, $"rows={rowsWithText}");
    }

    [Fact]
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

        var entry = Assert.Single(entries);
        Assert.Equal(("deckhost.plugin.obs", "OBS Studio", "Streaming", PluginStore.DeckHostSource), (entry.Id, entry.Name, entry.Category, entry.Source));
        Assert.Equal("https://example.com/obs.zip", entry.DownloadUrl);
    }

    [Fact]
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
        Assert.Equal("general", saved["dev|p1|key.0|com.test.action"]?["channel"]?.GetValue<string>());
        Assert.Equal("general", saved["dev|p2|key.5|com.test.action"]?["channel"]?.GetValue<string>());
        Assert.False(saved.ContainsKey("dev|p2|key.0|com.test.action"));
    }

    [Fact]
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
        Assert.DoesNotContain(edgeColumns, x => Enumerable.Range(0, 96).Any(y => bitmap.GetPixel(x, y).Green > 160));
        Assert.True(CountGreen(bitmap, 0, 96) > 50);
    }

    [Theory]
    [InlineData("Discord", null, "Communication")]
    [InlineData("OBS Studio", "Control scenes", "Streaming")]
    [InlineData("Philips Hue", null, "Lighting & smart home")]
    [InlineData("Zzz", "nothing known", PluginCategories.Other)]
    public void Derives_a_category_when_the_catalog_has_none(string name, string? description, string expected) =>
        Assert.Equal(expected, PluginCategories.Resolve(null, name, description, "com.example.plugin"));

    [Fact]
    public void A_given_category_wins() => Assert.Equal("Tools", PluginCategories.Resolve("Tools", "Discord", null, "x"));

    [Fact]
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

        Assert.Equal(["my.plugin", "other.plugin"], entries.Select(static entry => entry.Id));
        Assert.Equal("My Plugin", entries[0].Name);
        Assert.Equal("Audio", entries[1].Category);

        using var http = new HttpClient();
        var store = new PluginStore(http);
        var downloaded = await store.DownloadAsync(entries[0], Path.Combine(_root, "downloads"), null, CancellationToken.None);
        using var archive = System.IO.Compression.ZipFile.OpenRead(downloaded);
        Assert.Contains(archive.Entries, static entry => entry.FullName == "plugin.json");
    }

    [Fact]
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
        Assert.Equal(["network", "processes"], free.Permissions);
        var paid = entries.Single(entry => entry.Id == "b.plugin");
        Assert.Equal(("https://example.test/buy", "€5"), (paid.PurchaseUrl, paid.Price));
        Assert.Equal("https://example.test/buy", paid.StorePageUrl);

        using var http = new HttpClient();
        var store = new PluginStore(http) { IncludeOfficialCatalog = false };
        await Assert.ThrowsAsync<PluginException>(() => store.DownloadAsync(paid, Path.Combine(_root, "downloads"), null, CancellationToken.None));
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_checksum_is_refused()
    {
        var bytes = "the real package"u8.ToArray();
        using var http = new HttpClient(new FixedResponse(bytes));
        var store = new PluginStore(http) { IncludeOfficialCatalog = false };
        var good = new StoreEntry("a", "A", null, null, "DeckHost", "1.0", null, null, "https://example.test/a.zip") { Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) };
        var bad = good with { Sha256 = new string('0', 64) };

        var path = await store.DownloadAsync(good, Path.Combine(_root, "downloads"), null, CancellationToken.None);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        var error = await Assert.ThrowsAsync<PluginException>(() => store.DownloadAsync(bad, Path.Combine(_root, "downloads"), null, CancellationToken.None));
        Assert.Contains("checksum", error.Message);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "downloads")));
    }

    [Fact]
    public async Task The_official_catalog_is_read_first_and_a_missing_one_is_simply_empty()
    {
        using var missing = new HttpClient(new FixedResponse([], System.Net.HttpStatusCode.NotFound));
        var store = new PluginStore(missing);
        var (entries, errors) = await store.LoadAsync(CancellationToken.None);
        Assert.DoesNotContain(errors, error => error.StartsWith(PluginStore.DeckHostSource, StringComparison.Ordinal));
        Assert.DoesNotContain(entries, entry => entry.Source == PluginStore.DeckHostSource);

        using var present = new HttpClient(new FixedResponse("""{ "deckhost.plugin.obs": { "name": "OBS Studio", "download_url": "https://example.test/obs.zip" } }"""u8.ToArray()));
        var (official, _) = await new PluginStore(present).LoadAsync(CancellationToken.None);
        Assert.Contains(official, entry => entry.Id == "deckhost.plugin.obs" && entry.Source == PluginStore.DeckHostSource);
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
