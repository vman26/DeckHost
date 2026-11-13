using DeckHost.Contracts;
using DeckHost.Core.Rendering;
using DeckHost.Core.State;
using SkiaSharp;
using System.Reflection;

namespace DeckHost.Tests;

/// <summary>Icons, key tiles and readouts (the Stream Deck–like look).</summary>
public class IconTileTests
{
    public static TheoryData<string> AllIcons()
    {
        var data = new TheoryData<string>();
        foreach (var field in typeof(Icons).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            data.Add(field.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllIcons))]
    public void Every_icon_parses_and_fits_its_grid(string name)
    {
        var data = (string)typeof(Icons).GetField(name)!.GetValue(null)!;
        using var resources = new SkiaDrawingResources();
        var path = resources.GetIconPath(data);

        Assert.NotNull(path);
        var bounds = path!.Bounds;
        Assert.True(bounds.Width > 6 && bounds.Height > 6, $"{name}: {bounds}");
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= SkiaDrawingResources.IconGrid && bounds.Bottom <= SkiaDrawingResources.IconGrid, $"{name}: {bounds}");
    }

    [Fact]
    public void A_key_gets_a_gradient_tile_with_a_white_icon()
    {
        using var bitmap = Render(112, 112, context => context.DrawTile(Icons.Stop, 0xFFFF0000, 0xFF0000FF));

        Assert.Equal(SKColors.Black, bitmap.GetPixel(0, 0)); // the tile is inset, with rounded corners
        Assert.True(bitmap.GetPixel(20, 20).Red > 150, "red at the top left");
        Assert.True(bitmap.GetPixel(92, 92).Blue > 150, "blue at the bottom right");
        Assert.Equal(SKColors.White, bitmap.GetPixel(56, 56)); // the icon
    }

    [Fact]
    public void Without_colors_there_is_no_tile()
    {
        using var bitmap = Render(112, 112, context => context.DrawTile(Icons.Stop, 0, 0));

        Assert.Equal(SKColors.Black, bitmap.GetPixel(20, 20));
        Assert.Equal(SKColors.White, bitmap.GetPixel(56, 56));
    }

    [Fact]
    public void A_touch_zone_stays_one_surface_with_the_icon_in_the_tile_color()
    {
        using var bitmap = Render(176, 112, context => context.DrawTile(Icons.Stop, 0xFFFF0000, 0xFF0000FF, "Stop"));

        Assert.Equal(SKColors.Black, bitmap.GetPixel(30, 100)); // no tile
        Assert.Equal(new SKColor(0xFFFF0000), bitmap.GetPixel(88, 65)); // the icon, tinted
    }

    [Fact]
    public void A_reading_on_a_touch_zone_has_the_value_on_the_right_and_a_bar()
    {
        using var bitmap = Render(176, 112, context => context.DrawReading("Volume", "42%", 0.5, 0xFF00FF00, Icons.Speaker));

        var valueLeft = Enumerable.Range(0, 176).First(x => Enumerable.Range(50, 25).Any(y => bitmap.GetPixel(x, y).Red > 200 && bitmap.GetPixel(x, y).Blue > 200 && x > 70));
        Assert.True(valueLeft > 100, $"value starts at {valueLeft}");
        var barY = (int)(112 * 0.78);
        Assert.Equal(new SKColor(0xFF00FF00), bitmap.GetPixel(70, barY)); // filled half
        Assert.NotEqual(new SKColor(0xFF00FF00), bitmap.GetPixel(160, barY));
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
    [Theory]
    [InlineData(null, false, "profile")] // transparent by default: the profile background
    [InlineData(null, true, "tile")] // the action's tile, when chosen
    [InlineData("#00000000", true, "profile")] // a color of its own (even transparent) replaces the tile
    [InlineData("#80000000", false, "dark profile")] // half black over the profile background
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
                Assert.True(blue > 100, $"the media tile (pink to purple): {red},{green},{blue}");
                break;
            case "profile":
                Assert.True(red > 240 && green < 20 && blue < 20, $"profile red: {red},{green},{blue}");
                break;
            default:
                Assert.True(red is > 100 and < 160 && green < 20 && blue < 20, $"darkened profile red: {red},{green},{blue}");
                break;
        }
    }

    [Theory]
    [InlineData(null, 0xFFF472B6u)] // the action's own color (media: pink)
    [InlineData("#00FF00", 0xFF00FF00u)] // the text style's color
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
        Assert.Equal(expected, 0xFF000000u | ((uint)frame.Rgba[offset] << 16) | ((uint)frame.Rgba[offset + 1] << 8) | frame.Rgba[offset + 2]);
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

    [Fact]
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

        Assert.Equal(new SKColor(0xFF00FF00), bitmap.GetPixel(56, 56));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(10, 10)); // only the icon's square
    }

    [Fact]
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

        Assert.Equal(SKColors.Red, bitmap.GetPixel(56, 56));
    }

    [Fact]
    public void A_zone_with_only_an_icon_has_content_of_its_own()
    {
        var reference = new DeckHost.Contracts.Models.ActionReference("p", "P", "display", "Display", DeckHost.Contracts.Enums.InputCapabilities.None, DeckHost.Contracts.Enums.OutputCapabilities.None);
        var iconOnly = new DeckHost.Contracts.Models.ControlBinding("touch.0", DeckHost.Contracts.Models.ProfileActionKind.Single, reference,
            new DeckHost.Contracts.Models.ControlAppearance { IconPath = "/icons/mic.svg" }, []);

        Assert.True(ControlRenderer.HasOwnContent(iconOnly, new AppearanceOnlyAction("display")));
        Assert.False(ControlRenderer.HasOwnContent(iconOnly with { Appearance = new DeckHost.Contracts.Models.ControlAppearance() }, new AppearanceOnlyAction("display")));
    }

    [Fact]
    public void Choosing_an_icon_for_an_empty_control_creates_a_display_binding_with_it()
    {
        var builtIns = DeckHost.App.SystemPlugins.SystemPluginSet.Create().First(plugin => plugin.Id == DeckHost.App.SystemPlugins.SystemPluginSet.BuiltInPluginId);
        var display = DeckHost.App.ViewModels.ActionItemViewModel.Create(builtIns.Id, builtIns.Name, builtIns.Actions.First(action => action.Id == "builtin.display-content"));
        var control = new DeckHost.App.ViewModels.ControlItemViewModel(new DeckHost.Contracts.Models.PhysicalControl("key.0", "Key 1", DeckHost.Contracts.Models.ControlType.Key,
            new DeckHost.Contracts.Models.Rect(0, 0, 10, 10), DeckHost.Contracts.Enums.InputCapabilities.Press, DeckHost.Contracts.Enums.OutputCapabilities.ColorImage));
        var customize = new DeckHost.App.ViewModels.ControlCustomizationViewModel(control, null, [display], display);

        Assert.Null(customize.BuildBinding());
        customize.IconPath = "/icons/mic.svg";
        var binding = customize.BuildBinding();

        Assert.NotNull(binding);
        Assert.Equal("builtin.display-content", binding!.Action.ActionId);
        Assert.Equal("/icons/mic.svg", binding.Appearance.IconPath);
        customize.ClearIconCommand.Execute(null);
        Assert.Null(customize.BuildBinding());
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

    [Fact]
    public void The_black_around_the_picture_goes_and_the_black_inside_stays()
    {
        var rgba = Picture();

        EdgeBlack.Clear(rgba, Size, Size);

        Assert.Equal(0, At(rgba, 0, 0).A);
        Assert.Equal(0, At(rgba, 11, 11).A);
        Assert.Equal((byte)255, At(rgba, 4, 4).A); // white
        Assert.Equal(((byte)0, (byte)255), At(rgba, 5, 5)); // the black dot inside the white square
        var rim = At(rgba, 2, 5);
        Assert.InRange(rim.A, 1, 254); // the gray rim fades out instead of staying a dark edge
        Assert.True(rim.R > 80, $"un-blended from black: {rim.R}");
    }

    [Fact]
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

        Assert.Equal(before, rgba);
    }

    [Fact]
    public void An_encoded_plugin_image_decodes_with_its_background_cleared()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(Picture(), 0, bitmap.GetPixels(), Size * Size * 4);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        using var image = EdgeBlack.Clear(png.ToArray());
        using var pixels = SKBitmap.FromImage(image!);

        Assert.Equal(0, pixels.GetPixel(0, 0).Alpha);
        Assert.Equal(SKColors.White, pixels.GetPixel(4, 4));
    }
}
