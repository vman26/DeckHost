using DeckHost.Core.Icons;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace DeckHost.Tests;

public sealed class IconLibraryTests : IDisposable
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M4 4h16v16H4z\"/></svg>";
    private const string PackId = "com.example.lights";
    private const string RedLight = "Red Light";
    private const string BlueLight = "Blue Light";
    private const string Unlisted = "Unlisted";
    private const string Escapes = "Escapes";
    private const string LicenseText = "MIT License";
    private const string LibraryFolder = "library";

    private readonly string _root = Directory.CreateTempSubdirectory("deckhost-icons").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private IconLibrary NewLibrary() => new(Path.Combine(_root, LibraryFolder));

    /// <summary>A pack laid out like Elgato's: com.author.name.sdIconPack/ with manifest.json, icons.json, icons/, license.txt.</summary>
    private string MakeStreamDeckPack(string parent)
    {
        var pack = Path.Combine(parent, PackId + ".sdIconPack");
        Directory.CreateDirectory(Path.Combine(pack, "icons"));
        File.WriteAllText(Path.Combine(pack, "manifest.json"), """
            { "Name": "Lights", "Version": "1.0.1", "Description": "Light icons", "Author": "Example", "URL": "https://example.com", "Icon": "pack.svg", "License": "license.txt" }
            """);
        File.WriteAllText(Path.Combine(pack, "icons.json"), """
            [
              { "path": "Red Light.svg", "name": "Red Light", "tags": ["colour", "red", "light"] },
              { "path": "Blue Light.svg", "name": "Blue Light", "tags": ["colour", "blue"] },
              { "path": "../outside.svg", "name": "Escapes" }
            ]
            """);
        File.WriteAllText(Path.Combine(pack, "icons", "Red Light.svg"), Svg);
        File.WriteAllText(Path.Combine(pack, "icons", "Blue Light.svg"), Svg);
        File.WriteAllText(Path.Combine(pack, "icons", "Unlisted.svg"), Svg);
        File.WriteAllText(Path.Combine(pack, "pack.svg"), Svg);
        File.WriteAllText(Path.Combine(pack, "license.txt"), LicenseText);
        return pack;
    }

    /// <summary>Zips a Stream Deck icon pack made in a scratch folder and installs it; the scratch folder and the archive are removed.</summary>
    private IconPack InstallZippedStreamDeckPack(IconLibrary library)
    {
        var source = Directory.CreateTempSubdirectory("deckhost-pack-src").FullName;
        var archive = Path.Combine(_root, "..", Path.GetFileName(_root) + "-lights.streamDeckIconPack");
        try
        {
            MakeStreamDeckPack(source);
            ZipFile.CreateFromDirectory(source, archive);
            return library.Install(archive);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            File.Delete(archive);
        }
    }

    // ---------------------------------------------------------------- installing

    [Test]
    public void Install_AStreamDeckIconPackArchive_ReadsItsManifest()
    {
        // Arrange
        var library = NewLibrary();

        // Act
        var pack = InstallZippedStreamDeckPack(library);

        // Assert
        Assert.That(pack.Id, Is.EqualTo(PackId));
        Assert.That((pack.Name, pack.Version, pack.Author), Is.EqualTo(("Lights", "1.0.1", "Example")));
        Assert.That(pack.LicenseText, Is.EqualTo(LicenseText));
        Assert.That(pack.PreviewPath, Is.Not.Null);
    }

    [Test]
    public void Install_AStreamDeckIconPackArchive_ListsItsIconsAndSkipsOnesThatEscapeTheFolder()
    {
        // Arrange
        var library = NewLibrary();

        // Act
        var pack = InstallZippedStreamDeckPack(library);

        // Assert
        Assert.That(pack.Icons.Select(icon => icon.Name), Is.EqualTo(new[] { RedLight, BlueLight, Unlisted }));
        Expect.All(pack.Icons, icon => Assert.That(File.Exists(icon.FilePath), Is.True));
        Assert.That(pack.Icons.Any(icon => icon.Name == Escapes), Is.False);
    }

    [Test]
    public void Install_AZipOfImages_MakesAPackNamedAfterTheFile()
    {
        // Arrange
        var images = Directory.CreateTempSubdirectory("deckhost-images").FullName;
        var archive = Path.Combine(_root, "My Icons.zip");
        try
        {
            Directory.CreateDirectory(Path.Combine(images, "media"));
            File.WriteAllText(Path.Combine(images, "media", "play.svg"), Svg);
            File.WriteAllBytes(Path.Combine(images, "stop.png"), [0x89, 0x50, 0x4E, 0x47]);
            File.WriteAllText(Path.Combine(images, "readme.txt"), "not an icon");
            ZipFile.CreateFromDirectory(images, archive);

            // Act
            var pack = NewLibrary().Install(archive);

            // Assert
            Assert.That((pack.Id, pack.Name), Is.EqualTo(("my-icons", "My Icons")));
            Assert.That(pack.Icons.Select(icon => icon.Name).Order(), Is.EqualTo(new[] { "play", "stop" }));
        }
        finally
        {
            Directory.Delete(images, recursive: true);
        }
    }

    [Test]
    public void Install_ThePackAgain_ReplacesItsIcons()
    {
        // Arrange
        var library = NewLibrary();
        var source = MakeStreamDeckPack(_root);
        library.Install(source);
        File.Delete(Path.Combine(source, "icons", "Unlisted.svg"));

        // Act
        library.Install(source);

        // Assert
        Assert.That(Expect.Single(library.Packs).Icons.Count, Is.EqualTo(2));
    }

    [Test]
    public void Uninstall_AnInstalledPack_RemovesIt()
    {
        // Arrange
        var library = NewLibrary();
        var pack = library.Install(MakeStreamDeckPack(_root));

        // Act
        var removed = library.Uninstall(pack.Id);

        // Assert
        Assert.That(removed, Is.True);
        Assert.That(library.Packs, Is.Empty);
    }

    [Test]
    public void Uninstall_AnIdThatEscapesTheLibrary_IsRefused()
    {
        // Arrange
        var library = NewLibrary();

        // Act
        var removed = library.Uninstall("../escape");

        // Assert
        Assert.That(removed, Is.False);
    }

    [Test]
    public void Install_ArchiveWithoutImages_IsRefused()
    {
        // Arrange
        var empty = Path.Combine(_root, "empty.zip");
        using (var zip = ZipFile.Open(empty, ZipArchiveMode.Create))
        {
            zip.CreateEntry("notes.txt");
        }

        var library = NewLibrary();

        // Act
        TestDelegate install = () => library.Install(empty);

        // Assert
        Assert.Throws<IconPackException>(install);
        Assert.That(library.Packs, Is.Empty);
    }

    [Test]
    public void Install_FileThatIsNoZip_IsRefusedAndLeavesNoStagingFolder()
    {
        // Arrange
        var broken = Path.Combine(_root, "broken.zip");
        File.WriteAllText(broken, "not a zip");
        var library = NewLibrary();

        // Act
        TestDelegate install = () => library.Install(broken);

        // Assert
        Assert.Throws<IconPackException>(install);
        Assert.That(Directory.GetDirectories(Path.Combine(_root, LibraryFolder)).Any(directory => Path.GetFileName(directory).StartsWith(".staging", StringComparison.Ordinal)), Is.False);
    }

    // ---------------------------------------------------------------- searching

    [Test]
    public void Search_ByATag_FindsTheIcon()
    {
        // Arrange
        var library = NewLibrary();
        library.Install(MakeStreamDeckPack(_root));

        // Act
        var found = library.Search("red");

        // Assert
        Assert.That(found.Select(icon => icon.Name), Is.EqualTo(new[] { RedLight }));
    }

    [Test]
    public void Search_SeveralWords_FindsIconsThatMatchAllOfThem()
    {
        // Arrange
        var library = NewLibrary();
        library.Install(MakeStreamDeckPack(_root));

        // Act
        var found = library.Search("colour light");

        // Assert
        Assert.That(found.Select(icon => icon.Name).Order(), Is.EqualTo(new[] { BlueLight, RedLight }));
    }

    [Test]
    public void Search_NothingTyped_ListsEveryIcon()
    {
        // Arrange
        var library = NewLibrary();
        library.Install(MakeStreamDeckPack(_root));

        // Act
        var found = library.Search(string.Empty);

        // Assert
        Assert.That(found.Count, Is.EqualTo(3));
    }

    [Test]
    public void Search_AWordNoIconHas_FindsNothing()
    {
        // Arrange
        var library = NewLibrary();
        library.Install(MakeStreamDeckPack(_root));

        // Act
        var found = library.Search("green");

        // Assert
        Assert.That(found, Is.Empty);
    }

    [Test]
    public void Search_AnExactName_ListsItFirst()
    {
        // Arrange
        var library = NewLibrary();
        library.Install(MakeStreamDeckPack(_root));

        // Act
        var found = library.Search("blue light");

        // Assert
        Assert.That(found.First().Name, Is.EqualTo(BlueLight));
    }

    // ---------------------------------------------------------------- downloaded icons

    private static readonly IconifyIcon Play = new("mdi", "play", "Material Design Icons", "Apache 2.0", "https://example.com/license", "Pictogrammers", true);

    [Test]
    public void SaveDownloaded_AnIcon_WritesTheFileIntoTheDownloadsPack()
    {
        // Arrange
        var library = NewLibrary();

        // Act
        var path = library.SaveDownloaded(Play, Svg);

        // Assert
        Assert.That(File.Exists(path), Is.True);
        Assert.That(Expect.Single(library.Packs).IsDownloads, Is.True);
    }

    [Test]
    public void SaveDownloaded_TheSameIconTwiceAndAnotherOne_ListsEachOnce()
    {
        // Arrange
        var library = NewLibrary();

        // Act
        library.SaveDownloaded(Play, Svg);
        library.SaveDownloaded(Play, Svg);
        library.SaveDownloaded(Play with { Name = "pause" }, Svg);

        // Assert
        Assert.That(Expect.Single(library.Packs).Icons.Select(icon => icon.Name).Order(), Is.EqualTo(new[] { "pause", "play" }));
    }

    [Test]
    public void SaveDownloaded_Icons_KeepTheirSetsLicenseOnce()
    {
        // Arrange
        var library = NewLibrary();

        // Act
        library.SaveDownloaded(Play, Svg);
        library.SaveDownloaded(Play with { Name = "pause" }, Svg);

        // Assert
        var license = Expect.Single(library.Packs).LicenseText!;
        Assert.That(license, Does.Contain("Material Design Icons (mdi): Apache 2.0"));
        Assert.That(license.Split('\n').Count(line => line.Contains("(mdi)", StringComparison.Ordinal)), Is.EqualTo(1));
    }

    [Test]
    public void Search_ADownloadedIconByItsSetName_FindsIt()
    {
        // Arrange
        var library = NewLibrary();
        library.SaveDownloaded(Play, Svg);
        library.SaveDownloaded(Play with { Name = "pause" }, Svg);

        // Act
        var found = library.Search("material play");

        // Assert
        Assert.That(found.First().Name, Is.EqualTo("play"));
    }
}

public class IconifyClientTests
{
    // Trimmed from real api.iconify.design responses.
    private const string SearchResponse = """
        {"icons":["mdi:play","lucide:play","logos:spotify-icon"],"total":3,"limit":32,"start":0,
         "collections":{
           "mdi":{"name":"Material Design Icons","author":{"name":"Pictogrammers"},"license":{"title":"Apache 2.0","spdx":"Apache-2.0","url":"https://github.com/Templarian/MaterialDesign/blob/master/LICENSE"},"palette":false},
           "lucide":{"name":"Lucide","author":{"name":"Lucide Contributors"},"license":{"title":"ISC","spdx":"ISC"},"palette":false},
           "logos":{"name":"SVG Logos","license":{"title":"CC0"},"palette":true}}}
        """;

    private const string LucideResponse = """
        {"prefix":"lucide","width":24,"height":24,
         "icons":{"play":{"body":"<path fill=\"none\" stroke=\"currentColor\" d=\"M5 5v14l14-7z\"/>"}},
         "aliases":{"play-alias":{"parent":"play"}}}
        """;

    private const string SearchPath = "/search";
    private const string LucideSetPath = "/lucide.json";
    private const string BlankSearch = "  ";

    private static readonly IconifyIcon LucidePlay = new("lucide", "play", "Lucide", "ISC", null, null, true);

    private static IconifyClient ClientAnswering(Func<HttpRequestMessage, string?> respond) => new(new HttpClient(new FakeHandler(respond)));

    [Test]
    public async Task SearchAsync_AQuery_ReturnsTheIconsInTheOrderTheServerGaveThem()
    {
        // Arrange
        var client = ClientAnswering(request => request.RequestUri!.AbsolutePath == SearchPath ? SearchResponse : null);

        // Act
        var icons = await client.SearchAsync("play");

        // Assert
        Assert.That(icons.Select(icon => icon.Id), Is.EqualTo(new[] { "mdi:play", "lucide:play", "logos:spotify-icon" }));
    }

    [Test]
    public async Task SearchAsync_AQuery_AttachesTheSetsLicenseAuthorAndColors()
    {
        // Arrange
        var client = ClientAnswering(request => request.RequestUri!.AbsolutePath == SearchPath ? SearchResponse : null);

        // Act
        var icons = await client.SearchAsync("play");

        // Assert
        Assert.That((icons[0].SetName, icons[0].License, icons[0].Author, icons[0].IsMonochrome), Is.EqualTo(("Material Design Icons", "Apache 2.0", "Pictogrammers", true)));
        Assert.That(icons[2].IsMonochrome, Is.False);
    }

    [Test]
    public async Task SearchAsync_ABlankQuery_FindsNothing()
    {
        // Arrange
        var client = ClientAnswering(request => request.RequestUri!.AbsolutePath == SearchPath ? SearchResponse : null);

        // Act
        var icons = await client.SearchAsync(BlankSearch);

        // Assert
        Assert.That(icons, Is.Empty);
    }

    [Test]
    public async Task GetSvgsAsync_IconsOfOneSet_AreFetchedWithASingleRequest()
    {
        // Arrange
        var requests = new List<string>();
        var client = ClientAnswering(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return request.RequestUri.AbsolutePath == LucideSetPath ? LucideResponse : null;
        });

        // Act
        await client.GetSvgsAsync([LucidePlay, LucidePlay with { Name = "play-alias" }, LucidePlay with { Name = "missing" }]);

        // Assert
        Assert.That(requests, Has.Exactly(1).Items);
        Assert.That(requests[0], Does.Contain("icons=play%2Cplay-alias%2Cmissing"));
    }

    [Test]
    public async Task GetSvgsAsync_IconsAndAnAlias_ComeBackAsSvgDocumentsWithoutTheMissingOne()
    {
        // Arrange
        var client = ClientAnswering(request => request.RequestUri!.AbsolutePath == LucideSetPath ? LucideResponse : null);

        // Act
        var svgs = await client.GetSvgsAsync([LucidePlay, LucidePlay with { Name = "play-alias" }, LucidePlay with { Name = "missing" }]);

        // Assert
        Assert.That(svgs.Count, Is.EqualTo(2));
        Assert.That(svgs["lucide:play"], Does.StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><path"));
        Assert.That(svgs["lucide:play-alias"], Is.EqualTo(svgs["lucide:play"]));
    }

    [Test]
    public async Task GetSvgsAsync_AnIcon_ProducesADocumentTheRasterizerCanDraw()
    {
        // Arrange
        var client = ClientAnswering(request => request.RequestUri!.AbsolutePath == LucideSetPath ? LucideResponse : null);
        var svgs = await client.GetSvgsAsync([LucidePlay]);

        // Act
        var image = DeckHost.Core.Rendering.SvgRasterizer.ToImage(Encoding.UTF8.GetBytes(svgs["lucide:play"]));

        // Assert
        Assert.That(image, Is.Not.Null);
    }

    [Test]
    public void ToSvg_AnIconWithItsOwnSize_UsesItOverTheSets()
    {
        // Arrange
        var set = (JsonObject)JsonNode.Parse("""{"width":24,"height":24,"icons":{"wide":{"body":"<g/>","width":32,"left":-4}}}""")!;

        // Act
        var svg = IconifyClient.ToSvg(set, "wide");

        // Assert
        Assert.That(svg, Is.EqualTo("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"24\" viewBox=\"-4 0 32 24\"><g/></svg>"));
    }

    [Test]
    public void ToSvg_AnUnknownIcon_IsNull()
    {
        // Arrange
        var set = (JsonObject)JsonNode.Parse("""{"width":24,"height":24,"icons":{}}""")!;

        // Act
        var svg = IconifyClient.ToSvg(set, "nothing");

        // Assert
        Assert.That(svg, Is.Null);
    }

    [Test]
    public void SearchAsync_AnUnreachableServer_ReportsItPlainly()
    {
        // Arrange
        var client = ClientAnswering(_ => throw new HttpRequestException("No such host"));

        // Act
        var error = Assert.ThrowsAsync<IconPackException>(() => client.SearchAsync("play"));

        // Assert
        Assert.That(error.Message, Does.Contain("Couldn't reach Iconify"));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, string?> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = respond(request);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
