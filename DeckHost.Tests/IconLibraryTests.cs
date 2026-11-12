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

    [Fact]
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

            Assert.Equal("com.example.lights", pack.Id);
            Assert.Equal(("Lights", "1.0.1", "Example"), (pack.Name, pack.Version, pack.Author));
            Assert.Equal("MIT License", pack.LicenseText);
            Assert.NotNull(pack.PreviewPath);
            Assert.Equal(["Red Light", "Blue Light", "Unlisted"], pack.Icons.Select(icon => icon.Name));
            Assert.All(pack.Icons, icon => Assert.True(File.Exists(icon.FilePath)));
            Assert.DoesNotContain(pack.Icons, icon => icon.Name == "Escapes");
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void A_pack_folder_installs_and_search_uses_names_and_tags()
    {
        var library = new IconLibrary(Path.Combine(_root, "library"));
        library.Install(MakeStreamDeckPack(_root));

        Assert.Equal(["Red Light"], library.Search("red").Select(icon => icon.Name));
        Assert.Equal(["Blue Light", "Red Light"], library.Search("colour light").Select(icon => icon.Name).Order());
        Assert.Equal(3, library.Search("").Count);
        Assert.Empty(library.Search("green"));
        Assert.Equal("Blue Light", library.Search("blue light").First().Name);
    }

    [Fact]
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

            Assert.Equal(("my-icons", "My Icons"), (pack.Id, pack.Name));
            Assert.Equal(["play", "stop"], pack.Icons.Select(icon => icon.Name).Order());
        }
        finally
        {
            Directory.Delete(images, recursive: true);
        }
    }

    [Fact]
    public void Installing_again_replaces_and_uninstall_removes()
    {
        var library = new IconLibrary(Path.Combine(_root, "library"));
        var source = MakeStreamDeckPack(_root);
        library.Install(source);
        File.Delete(Path.Combine(source, "icons", "Unlisted.svg"));
        library.Install(source);

        var pack = Assert.Single(library.Packs);
        Assert.Equal(2, pack.Icons.Count);
        Assert.True(library.Uninstall(pack.Id));
        Assert.Empty(library.Packs);
        Assert.False(library.Uninstall("../escape"));
    }

    [Fact]
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
        Assert.Empty(library.Packs);
        Assert.DoesNotContain(Directory.GetDirectories(Path.Combine(_root, "library")), directory => Path.GetFileName(directory).StartsWith(".staging", StringComparison.Ordinal));
    }

    [Fact]
    public void Downloaded_icons_are_kept_with_their_license()
    {
        var library = new IconLibrary(Path.Combine(_root, "library"));
        var play = new IconifyIcon("mdi", "play", "Material Design Icons", "Apache 2.0", "https://example.com/license", "Pictogrammers", true);

        var path = library.SaveDownloaded(play, Svg);
        library.SaveDownloaded(play, Svg);
        library.SaveDownloaded(play with { Name = "pause" }, Svg);

        Assert.True(File.Exists(path));
        var pack = Assert.Single(library.Packs);
        Assert.True(pack.IsDownloads);
        Assert.Equal(["pause", "play"], pack.Icons.Select(icon => icon.Name).Order());
        Assert.Contains("Material Design Icons (mdi): Apache 2.0", pack.LicenseText);
        Assert.Single(pack.LicenseText!.Split('\n'), line => line.Contains("(mdi)", StringComparison.Ordinal));
        Assert.Equal("play", library.Search("material play").First().Name);
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

    [Fact]
    public async Task Search_returns_icons_with_their_set_license_and_colors()
    {
        var client = new IconifyClient(new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath == "/search" ? SearchResponse : null)));

        var icons = await client.SearchAsync("play");

        Assert.Equal(["mdi:play", "lucide:play", "logos:spotify-icon"], icons.Select(icon => icon.Id));
        Assert.Equal(("Material Design Icons", "Apache 2.0", "Pictogrammers", true), (icons[0].SetName, icons[0].License, icons[0].Author, icons[0].IsMonochrome));
        Assert.False(icons[2].IsMonochrome);
        Assert.Empty(await client.SearchAsync("  "));
    }

    [Fact]
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

        Assert.Single(requests);
        Assert.Contains("icons=play%2Cplay-alias%2Cmissing", requests[0]);
        Assert.Equal(2, svgs.Count);
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><path", svgs["lucide:play"]);
        Assert.Equal(svgs["lucide:play"], svgs["lucide:play-alias"]);
        Assert.NotNull(DeckHost.Core.Rendering.SvgRasterizer.ToImage(Encoding.UTF8.GetBytes(svgs["lucide:play"])));
    }

    [Fact]
    public void An_icon_uses_its_own_size_over_the_sets()
    {
        var set = (JsonObject)JsonNode.Parse("""{"width":24,"height":24,"icons":{"wide":{"body":"<g/>","width":32,"left":-4}}}""")!;

        Assert.Equal("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"24\" viewBox=\"-4 0 32 24\"><g/></svg>", IconifyClient.ToSvg(set, "wide"));
        Assert.Null(IconifyClient.ToSvg(set, "nothing"));
    }

    [Fact]
    public async Task An_unreachable_server_is_reported_plainly()
    {
        var client = new IconifyClient(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("No such host"))));

        var error = await Assert.ThrowsAsync<IconPackException>(() => client.SearchAsync("play"));
        Assert.Contains("Couldn't reach Iconify", error.Message);
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
