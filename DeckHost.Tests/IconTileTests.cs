using DeckHost.Contracts;
using DeckHost.Core.Rendering;
using DeckHost.Core.State;
using SkiaSharp;
using System.Reflection;

namespace DeckHost.Tests;

/// <summary>Icons, key tiles and readouts (the Stream Deck–like look).</summary>
public class IconTileTests
{
    public static IEnumerable<string> AllIcons() =>
        typeof(Icons).GetFields(BindingFlags.Public | BindingFlags.Static).Select(static field => field.Name);

    [TestCaseSource(nameof(AllIcons))]
    public void Every_icon_parses_and_fits_its_grid(string name)
    {
        var data = (string)typeof(Icons).GetField(name)!.GetValue(null)!;
        using var resources = new SkiaDrawingResources();
        var path = resources.GetIconPath(data);

        Assert.That(path, Is.Not.Null);
        var bounds = path!.Bounds;
        Assert.That(bounds.Width > 6 && bounds.Height > 6, Is.True, $"{name}: {bounds}");
        Assert.That(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= SkiaDrawingResources.IconGrid && bounds.Bottom <= SkiaDrawingResources.IconGrid, Is.True, $"{name}: {bounds}");
    }

    [Test]
    public void A_key_gets_a_gradient_tile_with_a_white_icon()
    {
        using var bitmap = Render(112, 112, context => context.DrawTile(Icons.Stop, 0xFFFF0000, 0xFF0000FF));

        Assert.That(bitmap.GetPixel(0, 0), Is.EqualTo(SKColors.Black)); // the tile is inset, with rounded corners
        Assert.That(bitmap.GetPixel(20, 20).Red > 150, Is.True, "red at the top left");
        Assert.That(bitmap.GetPixel(92, 92).Blue > 150, Is.True, "blue at the bottom right");
        Assert.That(bitmap.GetPixel(56, 56), Is.EqualTo(SKColors.White)); // the icon
    }

    [Test]
    public void Without_colors_there_is_no_tile()
    {
        using var bitmap = Render(112, 112, context => context.DrawTile(Icons.Stop, 0, 0));

        Assert.That(bitmap.GetPixel(20, 20), Is.EqualTo(SKColors.Black));
        Assert.That(bitmap.GetPixel(56, 56), Is.EqualTo(SKColors.White));
    }

    [Test]
    public void A_touch_zone_stays_one_surface_with_the_icon_in_the_tile_color()
    {
        using var bitmap = Render(176, 112, context => context.DrawTile(Icons.Stop, 0xFFFF0000, 0xFF0000FF, "Stop"));

        Assert.That(bitmap.GetPixel(30, 100), Is.EqualTo(SKColors.Black)); // no tile
        Assert.That(bitmap.GetPixel(88, 65), Is.EqualTo(new SKColor(0xFFFF0000))); // the icon, tinted
    }

    [Test]
    public void A_reading_on_a_touch_zone_has_the_value_on_the_right_and_a_bar()
    {
        using var bitmap = Render(176, 112, context => context.DrawReading("Volume", "42%", 0.5, 0xFF00FF00, Icons.Speaker));

        var valueLeft = Enumerable.Range(0, 176).First(x => Enumerable.Range(50, 25).Any(y => bitmap.GetPixel(x, y).Red > 200 && bitmap.GetPixel(x, y).Blue > 200 && x > 70));
        Assert.That(valueLeft > 100, Is.True, $"value starts at {valueLeft}");
        var barY = (int)(112 * 0.78);
        Assert.That(bitmap.GetPixel(70, barY), Is.EqualTo(new SKColor(0xFF00FF00))); // filled half
        Assert.That(bitmap.GetPixel(160, barY), Is.Not.EqualTo(new SKColor(0xFF00FF00)));
    }

    private static SKBitmap Render(int width, int height, Action<SkiaRenderContext> draw)
    {
        using var resources = new SkiaDrawingResources();
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        draw(new SkiaRenderContext(canvas, width, height, "dev", "key.0", new VariableStore(), resources));
        canvas.Flush();
        return bitmap;
    }
}

/// <summary>A control's own background: it replaces the action's tile, and with opacity lets the profile show.</summary>
public class ControlBackgroundTests
{
    [TestCase(null, false, "profile")] // transparent by default: the profile background
    [TestCase(null, true, "tile")] // the action's tile, when chosen
    [TestCase("#00000000", true, "profile")] // a color of its own (even transparent) replaces the tile
    [TestCase("#80000000", false, "dark profile")] // half black over the profile background
    public async Task Controls_are_transparent_unless_a_tile_or_color_is_chosen(string? background, bool showTile, string expected)
    {
        var plugins = DeckHost.App.SystemPlugins.SystemPluginSet.Create();
        var device = new KeyDevicePlugin();
        using var store = new VariableStore();
        await using var host = new DeckHost.Core.Devices.DeviceHostService([device], plugins, store);
        var frames = new System.Collections.Concurrent.ConcurrentDictionary<string, DeckHost.Core.Models.ControlFrame>();
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        var reference = new DeckHost.Contracts.Models.ActionReference(DeckHost.App.SystemPlugins.SystemPluginSet.MediaPluginId, "Media", "media.playpause", "Play",
            DeckHost.Contracts.Enums.InputCapabilities.None, DeckHost.Contracts.Enums.OutputCapabilities.None);
        host.SetProfiles([new DeckHost.Contracts.Models.DeviceProfile("p", "P", KeyDevicePlugin.DeviceId,
            [new DeckHost.Contracts.Models.ControlBinding("key.0", DeckHost.Contracts.Models.ProfileActionKind.Single, reference,
                new DeckHost.Contracts.Models.ControlAppearance(BackgroundColor: background) { ShowTile = showTile }, [])],
            Background: new DeckHost.Contracts.Models.ProfileBackground("#FF0000"))]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();

        var frame = frames["key.0"];
        var offset = ((20 * frame.Width) + 20) * 4; // inside the tile, away from the icon
        var (red, green, blue) = (frame.Rgba[offset], frame.Rgba[offset + 1], frame.Rgba[offset + 2]);
        switch (expected)
        {
            case "tile":
                Assert.That(blue > 100, Is.True, $"the media tile (pink to purple): {red},{green},{blue}");
                break;
            case "profile":
                Assert.That(red > 240 && green < 20 && blue < 20, Is.True, $"profile red: {red},{green},{blue}");
                break;
            default:
                Assert.That(red is > 100 and < 160 && green < 20 && blue < 20, Is.True, $"darkened profile red: {red},{green},{blue}");
                break;
        }
    }

    [TestCase(null, 0xFFF472B6u)] // the action's own color (media: pink)
    [TestCase("#00FF00", 0xFF00FF00u)] // the text style's color
    public async Task The_icon_follows_the_text_color(string? textColor, uint expected)
    {
        var plugins = DeckHost.App.SystemPlugins.SystemPluginSet.Create();
        using var store = new VariableStore();
        await using var host = new DeckHost.Core.Devices.DeviceHostService([new KeyDevicePlugin()], plugins, store);
        var frames = new System.Collections.Concurrent.ConcurrentDictionary<string, DeckHost.Core.Models.ControlFrame>();
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        var reference = new DeckHost.Contracts.Models.ActionReference(DeckHost.App.SystemPlugins.SystemPluginSet.MediaPluginId, "Media", "media.playpause", "Play",
            DeckHost.Contracts.Enums.InputCapabilities.None, DeckHost.Contracts.Enums.OutputCapabilities.None);
        var style = textColor is null ? null : new DeckHost.Contracts.Models.TextStyle(Color: textColor);
        host.SetProfiles([new DeckHost.Contracts.Models.DeviceProfile("p", "P", KeyDevicePlugin.DeviceId,
            [new DeckHost.Contracts.Models.ControlBinding("key.0", DeckHost.Contracts.Models.ProfileActionKind.Single, reference,
                new DeckHost.Contracts.Models.ControlAppearance(TextStyle: style), [])])]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();

        var frame = frames["key.0"];
        var offset = ((56 * frame.Width) + 43) * 4; // inside the play triangle
        Assert.That(0xFF000000u | ((uint)frame.Rgba[offset] << 16) | ((uint)frame.Rgba[offset + 1] << 8) | frame.Rgba[offset + 2], Is.EqualTo(expected));
    }

    [Test]
    public async Task The_tile_takes_the_users_color_and_corners()
    {
        var round = await RenderKey(new DeckHost.Contracts.Models.ControlAppearance { ShowTile = true, Tile = new DeckHost.Contracts.Models.TileStyle("#FF0000", null, 50) });
        var square = await RenderKey(new DeckHost.Contracts.Models.ControlAppearance { ShowTile = true });

        Assert.That(Pixel(round, 18, 56), Is.EqualTo((255, 0, 0))); // one color, no gradient
        Assert.That(Pixel(round, 12, 12), Is.Not.EqualTo((255, 0, 0))); // outside the round tile
        Assert.That(Pixel(square, 12, 12).B > 100, Is.True, "the action's own tile reaches into the corner"); // media gradient, 16 % corners
    }

    private static (int R, int G, int B) Pixel(DeckHost.Core.Models.ControlFrame frame, int x, int y)
    {
        var offset = ((y * frame.Width) + x) * 4;
        return (frame.Rgba[offset], frame.Rgba[offset + 1], frame.Rgba[offset + 2]);
    }

    private static async Task<DeckHost.Core.Models.ControlFrame> RenderKey(DeckHost.Contracts.Models.ControlAppearance appearance)
    {
        var plugins = DeckHost.App.SystemPlugins.SystemPluginSet.Create();
        using var store = new VariableStore();
        await using var host = new DeckHost.Core.Devices.DeviceHostService([new KeyDevicePlugin()], plugins, store);
        var frames = new System.Collections.Concurrent.ConcurrentDictionary<string, DeckHost.Core.Models.ControlFrame>();
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        var reference = new DeckHost.Contracts.Models.ActionReference(DeckHost.App.SystemPlugins.SystemPluginSet.MediaPluginId, "Media", "media.playpause", "Play",
            DeckHost.Contracts.Enums.InputCapabilities.None, DeckHost.Contracts.Enums.OutputCapabilities.None);
        host.SetProfiles([new DeckHost.Contracts.Models.DeviceProfile("p", "P", KeyDevicePlugin.DeviceId,
            [new DeckHost.Contracts.Models.ControlBinding("key.0", DeckHost.Contracts.Models.ProfileActionKind.Single, reference, appearance, [])])]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        return frames["key.0"];
    }

    private sealed class KeyDevicePlugin : DeckHost.Contracts.Interfaces.IDevicePlugin, DeckHost.Contracts.Interfaces.IDeviceOutput
    {
        public const string DeviceId = "key.device";

        public event EventHandler? DevicesChanged { add { } remove { } }

        public string Id => "key.plugin";

        public string Name => "Keys";

        public IReadOnlyCollection<DeckHost.Contracts.Models.DeviceDefinition> Devices { get; } =
        [
            new(DeviceId, "Key device",
            [
                new("key.0", "Key 1", DeckHost.Contracts.Models.ControlType.Key, new DeckHost.Contracts.Models.Rect(0, 0, 10, 10),
                    DeckHost.Contracts.Enums.InputCapabilities.Press, DeckHost.Contracts.Enums.OutputCapabilities.ColorImage, 112, 112),
            ],
            IsConnected: true),
        ];

        public DeckHost.Contracts.Interfaces.IDeviceOutput Output => this;

        public Task InitializeAsync(DeckHost.Contracts.Interfaces.IDevicePluginContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
        {
            error = null;
            return true;
        }

        public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error)
        {
            error = null;
            return true;
        }

        public void Clear(string deviceId)
        {
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>An icon picked from the library, on the tile in place of the action's own.</summary>
public sealed class ChosenIconTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("deckhost-chosen-icon").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void A_single_color_svg_file_is_drawn_in_the_given_color()
    {
        var path = Path.Combine(_folder, "square.svg");
        File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path fill=\"currentColor\" d=\"M2 2h20v20H2z\"/></svg>");

        using var resources = new SkiaDrawingResources();
        using var bitmap = new SKBitmap(new SKImageInfo(112, 112, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        new SkiaRenderContext(canvas, 112, 112, "dev", "key.0", new VariableStore(), resources).DrawIcon(path, 56, 56, 48, 0xFF00FF00);
        canvas.Flush();

        Assert.That(bitmap.GetPixel(56, 56), Is.EqualTo(new SKColor(0xFF00FF00)));
        Assert.That(bitmap.GetPixel(10, 10), Is.EqualTo(SKColors.Black)); // only the icon's square
    }

    [Test]
    public void A_colored_icon_keeps_its_colors()
    {
        var path = Path.Combine(_folder, "red.svg");
        File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path fill=\"#FF0000\" d=\"M2 2h20v20H2z\"/></svg>");

        using var resources = new SkiaDrawingResources();
        using var bitmap = new SKBitmap(new SKImageInfo(112, 112, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        new SkiaRenderContext(canvas, 112, 112, "dev", "key.0", new VariableStore(), resources).DrawIcon(path, 56, 56, 48, 0xFF00FF00);
        canvas.Flush();

        Assert.That(bitmap.GetPixel(56, 56), Is.EqualTo(SKColors.Red));
    }

    [Test]
    public void A_zone_with_only_an_icon_has_content_of_its_own()
    {
        var reference = new DeckHost.Contracts.Models.ActionReference("p", "P", "display", "Display", DeckHost.Contracts.Enums.InputCapabilities.None, DeckHost.Contracts.Enums.OutputCapabilities.None);
        var iconOnly = new DeckHost.Contracts.Models.ControlBinding("touch.0", DeckHost.Contracts.Models.ProfileActionKind.Single, reference,
            new DeckHost.Contracts.Models.ControlAppearance { IconPath = "/icons/mic.svg" }, []);

        Assert.That(ControlRenderer.HasOwnContent(iconOnly, new AppearanceOnlyAction("display")), Is.True);
        Assert.That(ControlRenderer.HasOwnContent(iconOnly with { Appearance = new DeckHost.Contracts.Models.ControlAppearance() }, new AppearanceOnlyAction("display")), Is.False);
    }

    [Test]
    public void Choosing_an_icon_for_an_empty_control_creates_a_display_binding_with_it()
    {
        var builtIns = DeckHost.App.SystemPlugins.SystemPluginSet.Create().First(plugin => plugin.Id == DeckHost.App.SystemPlugins.SystemPluginSet.BuiltInPluginId);
        var display = DeckHost.App.ViewModels.ActionItemViewModel.Create(builtIns.Id, builtIns.Name, builtIns.Actions.First(action => action.Id == "builtin.display-content"));
        var control = new DeckHost.App.ViewModels.ControlItemViewModel(new DeckHost.Contracts.Models.PhysicalControl("key.0", "Key 1", DeckHost.Contracts.Models.ControlType.Key,
            new DeckHost.Contracts.Models.Rect(0, 0, 10, 10), DeckHost.Contracts.Enums.InputCapabilities.Press, DeckHost.Contracts.Enums.OutputCapabilities.ColorImage));
        var customize = new DeckHost.App.ViewModels.ControlCustomizationViewModel(control, null, [display], display);

        Assert.That(customize.BuildBinding(), Is.Null);
        customize.IconPath = "/icons/mic.svg";
        var binding = customize.BuildBinding();

        Assert.That(binding, Is.Not.Null);
        Assert.That(binding!.Action.ActionId, Is.EqualTo("builtin.display-content"));
        Assert.That(binding.Appearance.IconPath, Is.EqualTo("/icons/mic.svg"));
        customize.ClearIconCommand.Execute(null);
        Assert.That(customize.BuildBinding(), Is.Null);
    }
}

/// <summary>Stream Deck plugin images: their black background becomes transparent, black inside stays.</summary>
public class EdgeBlackTests
{
    private const int Size = 12;

    /// <summary>Black background, a white square (3..8) with a black dot in its middle and a gray rim on its left side.</summary>
    private static byte[] Picture()
    {
        var rgba = new byte[Size * Size * 4];
        for (var index = 0; index < Size * Size; index++)
        {
            rgba[(index * 4) + 3] = 255;
        }

        void Set(int x, int y, byte value)
        {
            var offset = ((y * Size) + x) * 4;
            rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = value;
        }

        for (var y = 3; y <= 8; y++)
        {
            for (var x = 3; x <= 8; x++)
            {
                Set(x, y, 255);
            }

            Set(2, y, 80); // anti-aliased rim against the black
        }

        Set(5, 5, 0);
        Set(6, 5, 0);
        return rgba;
    }

    private static (byte R, byte A) At(byte[] rgba, int x, int y) => (rgba[((y * Size) + x) * 4], rgba[(((y * Size) + x) * 4) + 3]);

    [Test]
    public void The_black_around_the_picture_goes_and_the_black_inside_stays()
    {
        var rgba = Picture();

        EdgeBlack.Clear(rgba, Size, Size);

        Assert.That(At(rgba, 0, 0).A, Is.EqualTo(0));
        Assert.That(At(rgba, 11, 11).A, Is.EqualTo(0));
        Assert.That(At(rgba, 4, 4).A, Is.EqualTo((byte)255)); // white
        Assert.That(At(rgba, 5, 5), Is.EqualTo(((byte)0, (byte)255))); // the black dot inside the white square
        var rim = At(rgba, 2, 5);
        Assert.That(rim.A, Is.InRange(1, 254)); // the gray rim fades out instead of staying a dark edge
        Assert.That(rim.R > 80, Is.True, $"un-blended from black: {rim.R}");
    }

    [Test]
    public void A_picture_without_a_black_edge_is_left_alone()
    {
        var rgba = Picture();
        for (var index = 0; index < Size * Size; index++)
        {
            if (rgba[index * 4] == 0)
            {
                rgba[index * 4] = 200; // a red background instead of black
            }
        }

        var before = rgba.ToArray();
        EdgeBlack.Clear(rgba, Size, Size);

        Assert.That(rgba, Is.EqualTo(before));
    }

    [Test]
    public void An_encoded_plugin_image_decodes_with_its_background_cleared()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(Picture(), 0, bitmap.GetPixels(), Size * Size * 4);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        using var image = EdgeBlack.Clear(png.ToArray());
        using var pixels = SKBitmap.FromImage(image!);

        Assert.That(pixels.GetPixel(0, 0).Alpha, Is.EqualTo(0));
        Assert.That(pixels.GetPixel(4, 4), Is.EqualTo(SKColors.White));
    }
}
