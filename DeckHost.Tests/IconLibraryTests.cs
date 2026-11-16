using DeckHost.Core.Icons;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace DeckHost.Tests;

public sealed class IconLibraryTests : IDisposable
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M4 4h16v16H4z\"/></svg>";

    private readonly string _root = Directory.CreateTempSubdirectory("deckhost-icons").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A pack laid out like Elgato's: com.author.name.sdIconPack/ with manifest.json, icons.json, icons/, license.txt.</summary>
    private string MakeStreamDeckPack(string parent)
    {
        var pack = Path.Combine(parent, "com.example.lights.sdIconPack");
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
        File.WriteAllText(Path.Combine(pack, "license.txt"), "MIT License");
        return pack;
    }

    [Test]
    public void A_Stream_Deck_icon_pack_installs_from_its_archive()
    {
        var source = Directory.CreateTempSubdirectory("deckhost-pack-src").FullName;
        try
        {
            MakeStreamDeckPack(source);
            var archive = Path.Combine(_root, "..", Path.GetFileName(_root) + "-lights.streamDeckIconPack");
            ZipFile.CreateFromDirectory(source, archive);
            var library = new IconLibrary(Path.Combine(_root, "library"));

            var pack = library.Install(archive);
            File.Delete(archive);

            Assert.That(pack.Id, Is.EqualTo("com.example.lights"));
            Assert.That((pack.Name, pack.Version, pack.Author), Is.EqualTo(("Lights", "1.0.1", "Example")));
            Assert.That(pack.LicenseText, Is.EqualTo("MIT License"));
            Assert.That(pack.PreviewPath, Is.Not.Null);
            Assert.That(pack.Icons.Select(icon => icon.Name), Is.EqualTo(new[] { "Red Light", "Blue Light", "Unlisted" }));
            Expect.All(pack.Icons, icon => Assert.That(File.Exists(icon.FilePath), Is.True));
            Assert.That(pack.Icons.Any(icon => icon.Name == "Escapes"), Is.False);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Test]
    public void A_pack_folder_installs_and_search_uses_names_and_tags()
    {
        var library = new IconLibrary(Path.Combine(_root, "library"));
        library.Install(MakeStreamDeckPack(_root));

        Assert.That(library.Search("red").Select(icon => icon.Name), Is.EqualTo(new[] { "Red Light" }));
        Assert.That(library.Search("colour light").Select(icon => icon.Name).Order(), Is.EqualTo(new[] { "Blue Light", "Red Light" }));
        Assert.That(library.Search("").Count, Is.EqualTo(3));
        Assert.That(library.Search("green"), Is.Empty);
        Assert.That(library.Search("blue light").First().Name, Is.EqualTo("Blue Light"));
    }

    [Test]
    public void A_zip_of_images_becomes_a_pack_named_after_the_file()
    {
        var images = Directory.CreateTempSubdirectory("deckhost-images").FullName;
        var archive = Path.Combine(_root, "My Icons.zip");
        try
        {
            Directory.CreateDirectory(Path.Combine(images, "media"));
            File.WriteAllText(Path.Combine(images, "media", "play.svg"), Svg);
            File.WriteAllBytes(Path.Combine(images, "stop.png"), [0x89, 0x50, 0x4E, 0x47]);
            File.WriteAllText(Path.Combine(images, "readme.txt"), "not an icon");
            ZipFile.CreateFromDirectory(images, archive);
            var library = new IconLibrary(Path.Combine(_root, "library"));

            var pack = library.Install(archive);

            Assert.That((pack.Id, pack.Name), Is.EqualTo(("my-icons", "My Icons")));
            Assert.That(pack.Icons.Select(icon => icon.Name).Order(), Is.EqualTo(new[] { "play", "stop" }));
        }
        finally
        {
            Directory.Delete(images, recursive: true);
        }
    }

    [Test]
    public void Installing_again_replaces_and_uninstall_removes()
    {
        var library = new IconLibrary(Path.Combine(_root, "library"));
        var source = MakeStreamDeckPack(_root);
        library.Install(source);
        File.Delete(Path.Combine(source, "icons", "Unlisted.svg"));
        library.Install(source);

        var pack = Expect.Single(library.Packs);
        Assert.That(pack.Icons.Count, Is.EqualTo(2));
        Assert.That(library.Uninstall(pack.Id), Is.True);
        Assert.That(library.Packs, Is.Empty);
        Assert.That(library.Uninstall("../escape"), Is.False);
    }

    [Test]
    public void Something_that_is_no_icon_pack_is_refused()
    {
        var empty = Path.Combine(_root, "empty.zip");
        using (var zip = ZipFile.Open(empty, ZipArchiveMode.Create))
        {
            zip.CreateEntry("notes.txt");
        }

        var library = new IconLibrary(Path.Combine(_root, "library"));
        Assert.Throws<IconPackException>(() => library.Install(empty));
        File.WriteAllText(Path.Combine(_root, "broken.zip"), "not a zip");
        Assert.Throws<IconPackException>(() => library.Install(Path.Combine(_root, "broken.zip")));
        Assert.That(library.Packs, Is.Empty);
        Assert.That(Directory.GetDirectories(Path.Combine(_root, "library")).Any(directory => Path.GetFileName(directory).StartsWith(".staging", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void Downloaded_icons_are_kept_with_their_license()
    {
        var library = new IconLibrary(Path.Combine(_root, "library"));
        var play = new IconifyIcon("mdi", "play", "Material Design Icons", "Apache 2.0", "https://example.com/license", "Pictogrammers", true);

        var path = library.SaveDownloaded(play, Svg);
        library.SaveDownloaded(play, Svg);
        library.SaveDownloaded(play with { Name = "pause" }, Svg);

        Assert.That(File.Exists(path), Is.True);
        var pack = Expect.Single(library.Packs);
        Assert.That(pack.IsDownloads, Is.True);
        Assert.That(pack.Icons.Select(icon => icon.Name).Order(), Is.EqualTo(new[] { "pause", "play" }));
        Assert.That(pack.LicenseText, Does.Contain("Material Design Icons (mdi): Apache 2.0"));
        Assert.That(pack.LicenseText!.Split('\n').Count(line => line.Contains("(mdi)", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(library.Search("material play").First().Name, Is.EqualTo("play"));
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

    [Test]
    public async Task Search_returns_icons_with_their_set_license_and_colors()
    {
        var client = new IconifyClient(new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath == "/search" ? SearchResponse : null)));

        var icons = await client.SearchAsync("play");

        Assert.That(icons.Select(icon => icon.Id), Is.EqualTo(new[] { "mdi:play", "lucide:play", "logos:spotify-icon" }));
        Assert.That((icons[0].SetName, icons[0].License, icons[0].Author, icons[0].IsMonochrome), Is.EqualTo(("Material Design Icons", "Apache 2.0", "Pictogrammers", true)));
        Assert.That(icons[2].IsMonochrome, Is.False);
        Assert.That(await client.SearchAsync("  "), Is.Empty);
    }

    [Test]
    public async Task Icons_come_back_as_svg_documents_one_request_per_set()
    {
        var requests = new List<string>();
        var client = new IconifyClient(new HttpClient(new FakeHandler(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return request.RequestUri.AbsolutePath == "/lucide.json" ? LucideResponse : null;
        })));
        var play = new IconifyIcon("lucide", "play", "Lucide", "ISC", null, null, true);

        var svgs = await client.GetSvgsAsync([play, play with { Name = "play-alias" }, play with { Name = "missing" }]);

        Assert.That(requests, Has.Exactly(1).Items);
        Assert.That(requests[0], Does.Contain("icons=play%2Cplay-alias%2Cmissing"));
        Assert.That(svgs.Count, Is.EqualTo(2));
        Assert.That(svgs["lucide:play"], Does.StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><path"));
        Assert.That(svgs["lucide:play-alias"], Is.EqualTo(svgs["lucide:play"]));
        Assert.That(DeckHost.Core.Rendering.SvgRasterizer.ToImage(Encoding.UTF8.GetBytes(svgs["lucide:play"])), Is.Not.Null);
    }

    [Test]
    public void An_icon_uses_its_own_size_over_the_sets()
    {
        var set = (JsonObject)JsonNode.Parse("""{"width":24,"height":24,"icons":{"wide":{"body":"<g/>","width":32,"left":-4}}}""")!;

        Assert.That(IconifyClient.ToSvg(set, "wide"), Is.EqualTo("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"24\" viewBox=\"-4 0 32 24\"><g/></svg>"));
        Assert.That(IconifyClient.ToSvg(set, "nothing"), Is.Null);
    }

    [Test]
    public async Task An_unreachable_server_is_reported_plainly()
    {
        var client = new IconifyClient(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("No such host"))));

        var error = Assert.ThrowsAsync<IconPackException>(() => client.SearchAsync("play"));
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
