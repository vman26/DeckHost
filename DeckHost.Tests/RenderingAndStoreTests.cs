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

    private const int KeySize = 96;
    private const string GreenText = "#00FF00";
    private const string Elgato = "com.a.one";
    private const string ElgatoTwo = "com.a.two";
    private const string OnlyMirror = "com.a.only-mirror";
    private const string ArchiveCatalogPath = "amankhanna.me/catalogue.json";
    private const string ObsId = "deckhost.plugin.obs";
    private const string DownloadsFolder = "downloads";
    private const string PluginJson = "plugin.json";

    private static SKBitmap NewBlackBitmap(int width = KeySize, int height = KeySize)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        return bitmap;
    }

    private static SkiaRenderContext ContextOn(SKCanvas canvas, SkiaDrawingResources resources, int width = KeySize, int height = KeySize, string controlId = "key.0") =>
        new(canvas, width, height, "dev", controlId, new VariableStore(), resources);

    private static bool TouchesAnEdge(SKBitmap bitmap, int[] columns, int fromY, int toY) =>
        columns.Any(x => Enumerable.Range(fromY, toY - fromY).Any(y => bitmap.GetPixel(x, y).Green > 160));

    // ---------------------------------------------------------------- SVG

    private static byte[] RedSquareSvg() =>
        Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20"><rect width="20" height="20" fill="#ff0000"/></svg>""");

    [Test]
    public void IsSvg_AnSvgDocument_IsTrue()
    {
        // Act
        var isSvg = SvgRasterizer.IsSvg(RedSquareSvg());

        // Assert
        Assert.That(isSvg, Is.True);
    }

    [Test]
    public void IsSvg_APngHeader_IsFalse()
    {
        // Act
        var isSvg = SvgRasterizer.IsSvg([0x89, 0x50, 0x4E, 0x47]);

        // Assert
        Assert.That(isSvg, Is.False);
    }

    [Test]
    public void ToImage_AnSvg_IsDrawnAtTheRequestedWidthInItsOwnColors()
    {
        // Act
        using var image = SvgRasterizer.ToImage(RedSquareSvg(), 64);

        // Assert
        Assert.That(image, Is.Not.Null);
        Assert.That(image.Width, Is.EqualTo(64));
        using var bitmap = SKBitmap.FromImage(image);
        var center = bitmap.GetPixel(32, 32);
        Assert.That((center.Red, center.Green), Is.EqualTo(((byte)255, (byte)0)));
    }

    // ---------------------------------------------------------------- text

    [TestCase(TextVerticalAlignment.Top, true)]
    [TestCase(TextVerticalAlignment.Bottom, false)]
    public void DrawLabel_WithAVerticalAlignment_PutsTheTextInThatHalf(TextVerticalAlignment vertical, bool expectTopHalf)
    {
        // Arrange
        using var resources = new SkiaDrawingResources();
        using var bitmap = NewBlackBitmap();
        using var canvas = new SKCanvas(bitmap);
        var context = ContextOn(canvas, resources);
        context.TextStyle = new TextStyle(Color: GreenText, Size: 14, Bold: true, Vertical: vertical);

        // Act
        context.DrawLabel("Hello\nWorld");
        canvas.Flush();

        // Assert
        var (top, bottom) = (CountGreen(bitmap, 0, KeySize / 2), CountGreen(bitmap, KeySize / 2, KeySize));
        Assert.That(expectTopHalf ? top > bottom : bottom > top, Is.True, $"top={top} bottom={bottom}");
    }

    public static IEnumerable<string?> FontFamilies() => [null, SKTypeface.Default.FamilyName, "No Such Font Family"];

    [TestCaseSource(nameof(FontFamilies))]
    public void GetFont_Italic_AlwaysShowsAsItalic(string? family)
    {
        // Arrange
        using var resources = new SkiaDrawingResources();

        // Act (a real italic face when the font has one, else the text is slanted)
        var italic = resources.GetFont(14, family, bold: false, italic: true);

        // Assert
        Assert.That(italic.Typeface.IsItalic || italic.SkewX < 0, Is.True, $"italic, family '{family}'");
    }

    [TestCaseSource(nameof(FontFamilies))]
    public void GetFont_Bold_AlwaysShowsAsBoldAndUpright(string? family)
    {
        // Arrange
        using var resources = new SkiaDrawingResources();

        // Act (a real bold face when the font has one, else the text is emboldened)
        var bold = resources.GetFont(14, family, bold: true, italic: false);

        // Assert
        Assert.That(bold.Typeface.IsBold || bold.Embolden, Is.True, $"bold, family '{family}'");
        Assert.That(bold.SkewX, Is.EqualTo(0f));
    }

    private static byte[] DrawWithItalic(bool italic, bool label)
    {
        using var resources = new SkiaDrawingResources();
        using var bitmap = NewBlackBitmap();
        using var canvas = new SKCanvas(bitmap);
        var context = ContextOn(canvas, resources);
        context.TextStyle = new TextStyle(Italic: italic);

        // The control's label, or text an action places itself (a clock, a counter).
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

    [TestCase(true, TestName = "TextStyle_ItalicOnTheLabel_ChangesWhatIsDrawn")]
    [TestCase(false, TestName = "TextStyle_ItalicOnTextAnActionPlaces_ChangesWhatIsDrawn")]
    public void TextStyle_Italic_ChangesTheTextAnActionDraws(bool label)
    {
        // Act
        var italic = DrawWithItalic(italic: true, label);
        var upright = DrawWithItalic(italic: false, label);

        // Assert
        Assert.That(italic, Is.Not.EqualTo(upright));
    }

    private static SKBitmap DrawCompactStrip(out SKCanvas canvas)
    {
        var resources = new SkiaDrawingResources();
        var bitmap = NewBlackBitmap(176, 112);
        canvas = new SKCanvas(bitmap);
        var context = ContextOn(canvas, resources, 176, 112, "touch.0");

        // The bottom 36 pixels, drawn compact: two lines of large text become one line that fits.
        context.DrawRegion(0, 76, 176, 36, "encoder.0", null, compact: true, region =>
        {
            region.TextStyle = new TextStyle(Color: GreenText, Size: 40);
            region.DrawLabel("Master volume\n42%");
        });
        canvas.Flush();
        resources.Dispose();
        return bitmap;
    }

    [Test]
    public void DrawRegion_Compact_GivesTheActionAStripOfItsOwnSize()
    {
        // Arrange
        using var resources = new SkiaDrawingResources();
        using var bitmap = NewBlackBitmap(176, 112);
        using var canvas = new SKCanvas(bitmap);
        var context = ContextOn(canvas, resources, 176, 112, "touch.0");
        (bool IsCompact, int Width, int Height, string ControlId) seen = default;

        // Act
        context.DrawRegion(0, 76, 176, 36, "encoder.0", null, compact: true, region => seen = (region.IsCompact, region.Width, region.Height, region.ControlId));

        // Assert
        Assert.That(seen, Is.EqualTo((true, 176, 36, "encoder.0")));
    }

    [Test]
    public void DrawRegion_Compact_KeepsTheTextInsideTheStrip()
    {
        // Act
        using var bitmap = DrawCompactStrip(out var canvas);
        canvas.Dispose();

        // Assert
        Assert.That(CountGreen(bitmap, 0, 76), Is.EqualTo(0));
        Assert.That(CountGreen(bitmap, 76, 112) > 40, Is.True);
    }

    [Test]
    public void DrawRegion_Compact_ShrinksTextSoNothingIsCutAtTheEdges()
    {
        // Act
        using var bitmap = DrawCompactStrip(out var canvas);
        canvas.Dispose();

        // Assert
        Assert.That(TouchesAnEdge(bitmap, [0, 1, 174, 175], 76, 112), Is.False);
    }

    [Test]
    public void DrawLabel_LongTextWithWrapOn_CoversSeveralLines()
    {
        // Arrange
        using var resources = new SkiaDrawingResources();
        using var bitmap = NewBlackBitmap();
        using var canvas = new SKCanvas(bitmap);
        var context = ContextOn(canvas, resources);
        context.TextStyle = new TextStyle(Color: GreenText, Size: 14, Wrap: true);

        // Act
        context.DrawLabel("one two three four five six");
        canvas.Flush();

        // Assert
        var rowsWithText = Enumerable.Range(0, KeySize).Count(y => Enumerable.Range(0, KeySize).Any(x => bitmap.GetPixel(x, y).Green > 160));
        Assert.That(rowsWithText > 20, Is.True, $"rows={rowsWithText}");
    }

    private SKBitmap DrawUnwrappedMicrophoneText()
    {
        using var resources = new SkiaDrawingResources();
        var bitmap = NewBlackBitmap();
        using var canvas = new SKCanvas(bitmap);
        var context = ContextOn(canvas, resources);
        context.TextStyle = new TextStyle(Color: GreenText, Wrap: false);
        context.DrawLabel("Microphone muted");
        canvas.Flush();
        return bitmap;
    }

    [Test]
    public void DrawLabel_LongTextWithWrapOff_IsMadeSmallerSoNothingIsCutOff()
    {
        // Act
        using var bitmap = DrawUnwrappedMicrophoneText();

        // Assert
        Assert.That(TouchesAnEdge(bitmap, [0, 1, 94, 95], 0, KeySize), Is.False);
        Assert.That(CountGreen(bitmap, 0, KeySize) > 50, Is.True);
    }

    // ---------------------------------------------------------------- stream deck settings

    private (string Instances, string Data) WriteStreamDeckInstances()
    {
        var data = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(data, "settings"));
        var instances = Path.Combine(data, "settings", "instances.json");
        File.WriteAllText(instances, """{ "dev|p1|key.0|com.test.action": { "channel": "general" } }""");
        return (instances, data);
    }

    [Test]
    public void CopyProfileSettings_AProfile_CopiesItsStreamDeckSettings()
    {
        // Arrange
        var (instances, data) = WriteStreamDeckInstances();

        // Act
        using (var runtime = new StreamDeckRuntime(data, Path.Combine(_root, "logs"), (_, _) => { }))
        {
            ((IPluginStateTransfer)runtime).CopyProfileSettings("p1", "p2");
        }

        // Assert
        var saved = JsonNode.Parse(File.ReadAllText(instances))!.AsObject();
        Assert.That(saved["dev|p1|key.0|com.test.action"]?["channel"]?.GetValue<string>(), Is.EqualTo("general"));
        Assert.That(saved["dev|p2|key.0|com.test.action"]?["channel"]?.GetValue<string>(), Is.EqualTo("general"));
    }

    [Test]
    public void MoveControlSettings_AControl_MovesItsStreamDeckSettingsToTheNewControl()
    {
        // Arrange
        var (instances, data) = WriteStreamDeckInstances();

        // Act
        using (var runtime = new StreamDeckRuntime(data, Path.Combine(_root, "logs"), (_, _) => { }))
        {
            IPluginStateTransfer transfer = runtime;
            transfer.CopyProfileSettings("p1", "p2");
            transfer.MoveControlSettings("dev", "p2", "key.0", "key.5", copy: false);
        }

        // Assert
        var saved = JsonNode.Parse(File.ReadAllText(instances))!.AsObject();
        Assert.That(saved["dev|p2|key.5|com.test.action"]?["channel"]?.GetValue<string>(), Is.EqualTo("general"));
        Assert.That(saved.ContainsKey("dev|p2|key.0|com.test.action"), Is.False);
    }

    // ---------------------------------------------------------------- catalogs and categories

    [Test]
    public void ParseDeckHostCatalog_AnEntryWithACategory_ReadsItsFields()
    {
        // Arrange
        const string catalog = """
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
            """;

        // Act
        var entry = Expect.Single(PluginStore.ParseDeckHostCatalog(catalog));

        // Assert
        Assert.That((entry.Id, entry.Name, entry.Category, entry.Source), Is.EqualTo((ObsId, "OBS Studio", "Streaming", PluginStore.DeckHostSource)));
        Assert.That(entry.DownloadUrl, Is.EqualTo("https://example.com/obs.zip"));
    }

    [TestCase("Discord", null, "Communication")]
    [TestCase("OBS Studio", "Control scenes", "Streaming")]
    [TestCase("Philips Hue", null, "Lighting & smart home")]
    [TestCase("Zzz", "nothing known", PluginCategories.Other)]
    public void Resolve_WithoutACatalogCategory_DerivesOneFromTheNameAndDescription(string name, string? description, string expected)
    {
        // Act
        var category = PluginCategories.Resolve(null, name, description, "com.example.plugin");

        // Assert
        Assert.That(category, Is.EqualTo(expected));
    }

    [Test]
    public void Resolve_AGivenCategory_Wins()
    {
        // Act
        var category = PluginCategories.Resolve("Tools", "Discord", null, "x");

        // Assert
        Assert.That(category, Is.EqualTo("Tools"));
    }

    // ---------------------------------------------------------------- local sources

    private string MakeLocalPluginFolder()
    {
        // A plugin project's build output (plugin.json next to its assembly) and a package.
        var plugins = Path.Combine(_root, "plugins");
        var build = Path.Combine(plugins, "My.Plugin", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, PluginJson), """{ "id": "my.plugin", "name": "My Plugin", "version": "1.0.0", "assembly": "My.Plugin.dll" }""");
        File.WriteAllBytes(Path.Combine(build, "My.Plugin.dll"), [0]);

        // The project folder's own plugin.json has no assembly next to it, so it isn't listed.
        File.WriteAllText(Path.Combine(plugins, "My.Plugin", PluginJson), """{ "id": "my.plugin", "name": "Source", "version": "1.0.0", "assembly": "My.Plugin.dll" }""");

        var packageSource = Path.Combine(_root, "package");
        Directory.CreateDirectory(packageSource);
        File.WriteAllText(Path.Combine(packageSource, PluginJson), """{ "id": "other.plugin", "name": "Other", "version": "2.0.0", "assembly": "Other.dll", "description": "Volume control" }""");
        System.IO.Compression.ZipFile.CreateFromDirectory(packageSource, Path.Combine(plugins, "other.zip"));
        return plugins;
    }

    [Test]
    public void LoadLocalSource_AFolderWithABuildOutputAndAPackage_ListsBothButNotTheProjectsOwnManifest()
    {
        // Arrange
        var folder = MakeLocalPluginFolder();

        // Act
        var entries = PluginStore.LoadLocalSource(folder).OrderBy(static entry => entry.Id).ToList();

        // Assert
        Assert.That(entries.Select(static entry => entry.Id), Is.EqualTo(new[] { "my.plugin", "other.plugin" }));
        Assert.That(entries[0].Name, Is.EqualTo("My Plugin"));
    }

    [Test]
    public void LoadLocalSource_APackageWithADescription_GetsADerivedCategory()
    {
        // Arrange
        var folder = MakeLocalPluginFolder();

        // Act
        var entries = PluginStore.LoadLocalSource(folder).OrderBy(static entry => entry.Id).ToList();

        // Assert
        Assert.That(entries[1].Category, Is.EqualTo("Audio"));
    }

    [Test]
    public async Task DownloadAsync_ABuildOutputFromALocalSource_IsPackedIntoAZip()
    {
        // Arrange
        var entries = PluginStore.LoadLocalSource(MakeLocalPluginFolder()).OrderBy(static entry => entry.Id).ToList();
        using var http = new HttpClient();
        var store = new PluginStore(http);

        // Act
        var downloaded = await store.DownloadAsync(entries[0], Path.Combine(_root, DownloadsFolder), null, CancellationToken.None);

        // Assert
        using var archive = System.IO.Compression.ZipFile.OpenRead(downloaded);
        Assert.That(archive.Entries.Any(static entry => entry.FullName == PluginJson), Is.True);
    }

    // ---------------------------------------------------------------- checksums, permissions, paid plugins

    private (StoreEntry Free, StoreEntry Paid) ParseCatalogWithAPaidPlugin()
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
        return (entries.Single(entry => entry.Id == "a.plugin"), entries.Single(entry => entry.Id == "b.plugin"));
    }

    [Test]
    public void ParseDeckHostCatalog_AFreePlugin_CarriesItsPermissions()
    {
        // Act
        var (free, _) = ParseCatalogWithAPaidPlugin();

        // Assert
        Assert.That(free.Permissions, Is.EqualTo(new[] { "network", "processes" }));
    }

    [Test]
    public void ParseDeckHostCatalog_APaidPlugin_CarriesItsPriceAndPurchasePage()
    {
        // Act
        var (_, paid) = ParseCatalogWithAPaidPlugin();

        // Assert
        Assert.That((paid.PurchaseUrl, paid.Price), Is.EqualTo(("https://example.test/buy", "€5")));
        Assert.That(paid.StorePageUrl, Is.EqualTo("https://example.test/buy"));
    }

    [Test]
    public void DownloadAsync_APaidPlugin_IsRefused()
    {
        // Arrange
        var (_, paid) = ParseCatalogWithAPaidPlugin();
        using var http = new HttpClient();
        var store = new PluginStore(http) { IncludeOfficialCatalog = false };

        // Act
        AsyncTestDelegate download = () => store.DownloadAsync(paid, Path.Combine(_root, DownloadsFolder), null, CancellationToken.None);

        // Assert
        Assert.ThrowsAsync<PluginException>(download);
    }

    private static StoreEntry EntryWithChecksum(byte[] bytes) =>
        new("a", "A", null, null, "DeckHost", "1.0", null, null, "https://example.test/a.zip") { Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) };

    [Test]
    public async Task DownloadAsync_ADownloadMatchingItsChecksum_IsSaved()
    {
        // Arrange
        var bytes = "the real package"u8.ToArray();
        using var http = new HttpClient(new FixedResponse(bytes));
        var store = new PluginStore(http) { IncludeOfficialCatalog = false };

        // Act
        var path = await store.DownloadAsync(EntryWithChecksum(bytes), Path.Combine(_root, DownloadsFolder), null, CancellationToken.None);

        // Assert
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
    }

    [Test]
    public void DownloadAsync_ADownloadThatDoesNotMatchItsChecksum_IsRefused()
    {
        // Arrange
        var bytes = "the real package"u8.ToArray();
        using var http = new HttpClient(new FixedResponse(bytes));
        var store = new PluginStore(http) { IncludeOfficialCatalog = false };
        var bad = EntryWithChecksum(bytes) with { Sha256 = new string('0', 64) };

        // Act
        var error = Assert.ThrowsAsync<PluginException>(() => store.DownloadAsync(bad, Path.Combine(_root, DownloadsFolder), null, CancellationToken.None));

        // Assert
        Assert.That(error.Message, Does.Contain("checksum"));
    }

    [Test]
    public void DownloadAsync_ADownloadThatDoesNotMatchItsChecksum_LeavesNoFileBehind()
    {
        // Arrange
        var bytes = "the real package"u8.ToArray();
        using var http = new HttpClient(new FixedResponse(bytes));
        var store = new PluginStore(http) { IncludeOfficialCatalog = false };
        var bad = EntryWithChecksum(bytes) with { Sha256 = new string('0', 64) };
        var folder = Path.Combine(_root, DownloadsFolder);

        // Act
        Assert.ThrowsAsync<PluginException>(() => store.DownloadAsync(bad, folder, null, CancellationToken.None));

        // Assert
        Assert.That(Directory.Exists(folder) ? Directory.GetFiles(folder) : [], Is.Empty);
    }

    // ---------------------------------------------------------------- the official catalog

    [Test]
    public async Task LoadAsync_AMissingOfficialCatalog_IsSimplyEmptyWithoutAnError()
    {
        // Arrange
        using var missing = new HttpClient(new FixedResponse([], System.Net.HttpStatusCode.NotFound));
        var store = new PluginStore(missing);

        // Act
        var (entries, errors) = await store.LoadAsync(CancellationToken.None);

        // Assert
        Assert.That(errors.Any(error => error.StartsWith(PluginStore.DeckHostSource, StringComparison.Ordinal)), Is.False);
        Assert.That(entries.Any(entry => entry.Source == PluginStore.DeckHostSource), Is.False);
    }

    [Test]
    public async Task LoadAsync_APresentOfficialCatalog_IsRead()
    {
        // Arrange
        using var present = new HttpClient(new FixedResponse("""{ "deckhost.plugin.obs": { "name": "OBS Studio", "download_url": "https://example.test/obs.zip" } }"""u8.ToArray()));

        // Act
        var (official, _) = await new PluginStore(present).LoadAsync(CancellationToken.None);

        // Assert
        Assert.That(official.Any(entry => entry.Id == ObsId && entry.Source == PluginStore.DeckHostSource), Is.True);
    }

    // ---------------------------------------------------------------- the Elgato mirror

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
    public void ParseElgatoMirrorCatalog_Entries_KeepsOnlyMirroredOnesWithAnHttpsDownloadAndAFullChecksum()
    {
        // Act
        var copies = PluginStore.ParseElgatoMirrorCatalog(MirrorCatalog);

        // Assert
        Assert.That(copies.Select(copy => copy.Id), Is.EqualTo(new[] { Elgato, OnlyMirror }));
    }

    [Test]
    public void ParseElgatoMirrorCatalog_AMirroredEntry_CarriesChecksumDownloadLicenseAndRepository()
    {
        // Act
        var one = PluginStore.ParseElgatoMirrorCatalog(MirrorCatalog)[0];

        // Assert
        Assert.That(one.Sha256, Has.Length.EqualTo(64));
        Assert.That(one.DownloadUrl, Does.StartWith("https://github.com/vman26/deckhost-plugins/releases/download/elgato-mirror/"));
        Assert.That(one.Source, Is.EqualTo(PluginStore.ElgatoArchiveSource));
        Assert.That(one.Description, Does.Contain("MIT"));
        Assert.That(one.RepositoryUrl, Is.EqualTo("https://github.com/o/one"));
    }

    [Test]
    public void ParseElgatoMirrorCatalog_SomethingThatIsNotAnArray_IsEmpty()
    {
        // Act
        var copies = PluginStore.ParseElgatoMirrorCatalog("{ \"not\": \"an array\" }");

        // Assert
        Assert.That(copies, Is.Empty);
    }

    [Test]
    public void MergeElgatoMirror_ACopyOfAnArchivePlugin_ReplacesTheArchivesDownloadAndOnlyForWhatTheArchiveLists()
    {
        // Arrange
        var archive = PluginStore.ParseElgatoArchiveCatalog(ArchiveCatalog);

        // Act
        var merged = PluginStore.MergeElgatoMirror(archive, PluginStore.ParseElgatoMirrorCatalog(MirrorCatalog));

        // Assert
        Assert.That(merged.Select(entry => entry.Id), Is.EqualTo(new[] { Elgato, ElgatoTwo }));
        Assert.That(merged[0].Sha256, Is.Not.Null);
        Assert.That(merged[0].DownloadUrl, Does.Contain("elgato-mirror"));
    }

    [Test]
    public void MergeElgatoMirror_APluginThatWasNotCopied_KeepsTheArchivesOwnDownload()
    {
        // Arrange
        var archive = PluginStore.ParseElgatoArchiveCatalog(ArchiveCatalog);

        // Act
        var merged = PluginStore.MergeElgatoMirror(archive, PluginStore.ParseElgatoMirrorCatalog(MirrorCatalog));

        // Assert
        Assert.That(merged[1].DownloadUrl, Is.EqualTo("https://plugins.amankhanna.me/rezipped/com.a.two.zip"));
        Assert.That(merged[1].Sha256, Is.Null);
    }

    [Test]
    public void MergeElgatoMirror_NoCopies_ReturnsTheArchiveUnchanged()
    {
        // Arrange
        var archive = PluginStore.ParseElgatoArchiveCatalog(ArchiveCatalog);

        // Act
        var merged = PluginStore.MergeElgatoMirror(archive, []);

        // Assert
        Assert.That(merged, Is.SameAs(archive));
    }

    private static HttpClient Client(Func<string, (System.Net.HttpStatusCode Status, string Body)> route) => new(new RoutedResponse(route));

    private static (System.Net.HttpStatusCode, string) NotFound() => (System.Net.HttpStatusCode.NotFound, string.Empty);

    private static (System.Net.HttpStatusCode, string) Ok(string body) => (System.Net.HttpStatusCode.OK, body);

    private static IEnumerable<string> ElgatoErrors(IEnumerable<string> errors) =>
        errors.Where(error => error.StartsWith(PluginStore.ElgatoArchiveSource, StringComparison.Ordinal));

    private static bool IsArchive(string url) => url.Contains(ArchiveCatalogPath, StringComparison.Ordinal);

    [Test]
    public async Task LoadAsync_MirrorAndArchive_GiveTheChecksummedCopyForThePluginItCovers()
    {
        // Arrange
        using var both = Client(url => url == PluginStore.ElgatoMirrorCatalogUrl ? Ok(MirrorCatalog) : IsArchive(url) ? Ok(ArchiveCatalog) : NotFound());

        // Act
        var (entries, errors) = await new PluginStore(both).LoadAsync(CancellationToken.None);

        // Assert
        var elgato = entries.Where(entry => entry.Source == PluginStore.ElgatoArchiveSource).ToList();
        Assert.That(elgato.Select(entry => entry.Id), Is.EqualTo(new[] { Elgato, ElgatoTwo }));
        Assert.That(elgato[0].Sha256, Is.Not.Null);
        Assert.That(ElgatoErrors(errors), Is.Empty);
    }

    [Test]
    public async Task LoadAsync_NoMirrorYet_IsExactlyTheArchiveWithoutAnError()
    {
        // Arrange
        using var noMirror = Client(url => IsArchive(url) ? Ok(ArchiveCatalog) : NotFound());

        // Act
        var (entries, errors) = await new PluginStore(noMirror).LoadAsync(CancellationToken.None);

        // Assert
        Assert.That(entries.Where(entry => entry.Source == PluginStore.ElgatoArchiveSource).Select(entry => entry.Sha256), Is.EqualTo(new string?[] { null, null }));
        Assert.That(ElgatoErrors(errors), Is.Empty);
    }

    [Test]
    public async Task LoadAsync_ABrokenMirrorFile_IsIgnored()
    {
        // Arrange
        using var broken = Client(url => url == PluginStore.ElgatoMirrorCatalogUrl ? Ok("{ not json") : IsArchive(url) ? Ok(ArchiveCatalog) : NotFound());

        // Act
        var (entries, errors) = await new PluginStore(broken).LoadAsync(CancellationToken.None);

        // Assert
        Assert.That(entries.Count(entry => entry.Source == PluginStore.ElgatoArchiveSource), Is.EqualTo(2));
        Assert.That(ElgatoErrors(errors), Is.Empty);
    }

    [Test]
    public async Task LoadAsync_TheArchiveIsDown_StillListsTheMirroredCopies()
    {
        // Arrange
        using var archiveDown = Client(url => url == PluginStore.ElgatoMirrorCatalogUrl ? Ok(MirrorCatalog) : NotFound());

        // Act
        var (entries, errors) = await new PluginStore(archiveDown).LoadAsync(CancellationToken.None);

        // Assert
        Assert.That(entries.Where(entry => entry.Source == PluginStore.ElgatoArchiveSource).Select(entry => entry.Id), Is.EqualTo(new[] { Elgato, OnlyMirror }));
        Assert.That(ElgatoErrors(errors), Is.Empty);
    }

    [Test]
    public async Task LoadAsync_NeitherMirrorNorArchive_ShowsTheError()
    {
        // Arrange
        using var neither = Client(_ => NotFound());

        // Act
        var (_, errors) = await new PluginStore(neither).LoadAsync(CancellationToken.None);

        // Assert
        Assert.That(ElgatoErrors(errors), Is.Not.Empty);
    }

    [Test]
    public async Task LoadAsync_OfficialSourcesTurnedOff_DoesNotAskForTheMirror()
    {
        // Arrange (tests and tools turn the official sources off)
        var asked = new List<string>();
        using var recording = Client(url =>
        {
            asked.Add(url);
            return IsArchive(url) ? Ok(ArchiveCatalog) : NotFound();
        });

        // Act
        await new PluginStore(recording) { IncludeOfficialCatalog = false }.LoadAsync(CancellationToken.None);

        // Assert
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
