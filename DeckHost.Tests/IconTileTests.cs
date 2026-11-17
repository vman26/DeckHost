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

    private const int KeySize = 112;
    private const int ZoneWidth = 176;
    private const int ReadingWidth = 176;
    private const uint RedArgb = 0xFFFF0000;
    private const uint BlueArgb = 0xFF0000FF;
    private const uint GreenArgb = 0xFF00FF00;
    private const int StrongChannel = 150;
    private const int MinIconExtent = 6;

    [TestCaseSource(nameof(AllIcons))]
    public void GetIconPath_EveryIcon_ParsesToAPathLargeEnoughToSee(string name)
    {
        // Arrange
        var data = (string)typeof(Icons).GetField(name)!.GetValue(null)!;
        using var resources = new SkiaDrawingResources();

        // Act
        var path = resources.GetIconPath(data);

        // Assert
        Assert.That(path, Is.Not.Null);
        Assert.That(path!.Bounds.Width > MinIconExtent && path.Bounds.Height > MinIconExtent, Is.True, $"{name}: {path.Bounds}");
    }

    [TestCaseSource(nameof(AllIcons))]
    public void GetIconPath_EveryIcon_FitsItsGrid(string name)
    {
        // Arrange
        var data = (string)typeof(Icons).GetField(name)!.GetValue(null)!;
        using var resources = new SkiaDrawingResources();

        // Act
        var bounds = resources.GetIconPath(data)!.Bounds;

        // Assert
        Assert.That(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= SkiaDrawingResources.IconGrid && bounds.Bottom <= SkiaDrawingResources.IconGrid, Is.True, $"{name}: {bounds}");
    }

    [Test]
    public void DrawTile_ColorsOnAKey_FillsAnInsetRoundedGradientTile()
    {
        // Act
        using var bitmap = Render(KeySize, KeySize, context => context.DrawTile(Icons.Stop, RedArgb, BlueArgb));

        // Assert
        Assert.That(bitmap.GetPixel(0, 0), Is.EqualTo(SKColors.Black), "the tile is inset, with rounded corners");
        Assert.That(bitmap.GetPixel(20, 20).Red > StrongChannel, Is.True, "red at the top left");
        Assert.That(bitmap.GetPixel(92, 92).Blue > StrongChannel, Is.True, "blue at the bottom right");
    }

    [Test]
    public void DrawTile_ColorsOnAKey_DrawsTheIconInWhite()
    {
        // Act
        using var bitmap = Render(KeySize, KeySize, context => context.DrawTile(Icons.Stop, RedArgb, BlueArgb));

        // Assert
        Assert.That(bitmap.GetPixel(56, 56), Is.EqualTo(SKColors.White));
    }

    [Test]
    public void DrawTile_WithoutColors_DrawsNoTileButStillTheIcon()
    {
        // Act
        using var bitmap = Render(KeySize, KeySize, context => context.DrawTile(Icons.Stop, 0, 0));

        // Assert
        Assert.That(bitmap.GetPixel(20, 20), Is.EqualTo(SKColors.Black));
        Assert.That(bitmap.GetPixel(56, 56), Is.EqualTo(SKColors.White));
    }

    [Test]
    public void DrawTile_OnATouchZone_KeepsOneSurfaceAndTintsTheIcon()
    {
        // Act
        using var bitmap = Render(ZoneWidth, KeySize, context => context.DrawTile(Icons.Stop, RedArgb, BlueArgb, "Stop"));

        // Assert
        Assert.That(bitmap.GetPixel(30, 100), Is.EqualTo(SKColors.Black), "no tile");
        Assert.That(bitmap.GetPixel(88, 65), Is.EqualTo(new SKColor(RedArgb)), "the icon, tinted");
    }

    [Test]
    public void DrawReading_OnATouchZone_PutsTheValueOnTheRight()
    {
        // Act
        using var bitmap = Render(ReadingWidth, KeySize, context => context.DrawReading("Volume", "42%", 0.5, GreenArgb, Icons.Speaker));

        // Assert
        var valueLeft = Enumerable.Range(0, ReadingWidth).First(x => Enumerable.Range(50, 25).Any(y => bitmap.GetPixel(x, y).Red > 200 && bitmap.GetPixel(x, y).Blue > 200 && x > 70));
        Assert.That(valueLeft > 100, Is.True, $"value starts at {valueLeft}");
    }

    [Test]
    public void DrawReading_OnATouchZoneAtHalf_FillsHalfOfTheBar()
    {
        // Act
        using var bitmap = Render(ReadingWidth, KeySize, context => context.DrawReading("Volume", "42%", 0.5, GreenArgb, Icons.Speaker));

        // Assert
        var barY = (int)(KeySize * 0.78);
        Assert.That(bitmap.GetPixel(70, barY), Is.EqualTo(new SKColor(GreenArgb)), "filled half");
        Assert.That(bitmap.GetPixel(160, barY), Is.Not.EqualTo(new SKColor(GreenArgb)));
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
    private const string Profile = "profile";
    private const string Tile = "tile";
    private const string DarkProfile = "dark profile";
    private const string ProfileRed = "#FF0000";
    private const uint MediaPink = 0xFFF472B6u;
    private const uint ChosenGreen = 0xFF00FF00u;
    private const int InsideTheTile = 20;
    private const int InsidePlayTriangle = 43;

    private static DeckHost.Contracts.Models.ActionReference PlayPause() => new(DeckHost.App.SystemPlugins.SystemPluginSet.MediaPluginId, "Media", "media.playpause", "Play",
        DeckHost.Contracts.Enums.InputCapabilities.None, DeckHost.Contracts.Enums.OutputCapabilities.None);

    [TestCase(null, false, Profile, TestName = "Render_NoBackgroundAndNoTile_ShowsTheProfileBackground")]
    [TestCase(null, true, Tile, TestName = "Render_NoBackgroundButTheTileChosen_ShowsTheActionsTile")]
    [TestCase("#00000000", true, Profile, TestName = "Render_ATransparentBackgroundOfItsOwn_ReplacesTheTile")]
    [TestCase("#80000000", false, DarkProfile, TestName = "Render_HalfBlackBackground_DarkensTheProfileBackground")]
    public async Task Render_BackgroundAndTileChoices_DecideWhatShowsBehindTheIcon(string? background, bool showTile, string expected)
    {
        // Arrange
        var appearance = new DeckHost.Contracts.Models.ControlAppearance(BackgroundColor: background) { ShowTile = showTile };

        // Act
        var frame = await RenderKey(appearance, new DeckHost.Contracts.Models.ProfileBackground(ProfileRed));

        // Assert
        var (red, green, blue) = Pixel(frame, InsideTheTile, InsideTheTile);
        switch (expected)
        {
            case Tile:
                Assert.That(blue > 100, Is.True, $"the media tile (pink to purple): {red},{green},{blue}");
                break;
            case Profile:
                Assert.That(red > 240 && green < 20 && blue < 20, Is.True, $"profile red: {red},{green},{blue}");
                break;
            default:
                Assert.That(red is > 100 and < 160 && green < 20 && blue < 20, Is.True, $"darkened profile red: {red},{green},{blue}");
                break;
        }
    }

    [TestCase(null, MediaPink, TestName = "Render_WithoutATextColor_DrawsTheIconInTheActionsColor")]
    [TestCase("#00FF00", ChosenGreen, TestName = "Render_WithATextStyleColor_DrawsTheIconInThatColor")]
    public async Task Render_TheIcon_FollowsTheTextColor(string? textColor, uint expected)
    {
        // Arrange
        var style = textColor is null ? null : new DeckHost.Contracts.Models.TextStyle(Color: textColor);

        // Act
        var frame = await RenderKey(new DeckHost.Contracts.Models.ControlAppearance(TextStyle: style));

        // Assert
        var (red, green, blue) = Pixel(frame, InsidePlayTriangle, 56);
        Assert.That(0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | (uint)blue, Is.EqualTo(expected));
    }

    [Test]
    public async Task Render_ATileWithAColorAndRoundCorners_FillsItWithThatOneColor()
    {
        // Act
        var round = await RenderKey(new DeckHost.Contracts.Models.ControlAppearance { ShowTile = true, Tile = new DeckHost.Contracts.Models.TileStyle("#FF0000", null, 50) });

        // Assert
        Assert.That(Pixel(round, 18, 56), Is.EqualTo((255, 0, 0)), "one color, no gradient");
    }

    [Test]
    public async Task Render_ATileWithRoundCorners_LeavesTheCornerOutsideTheTile()
    {
        // Act
        var round = await RenderKey(new DeckHost.Contracts.Models.ControlAppearance { ShowTile = true, Tile = new DeckHost.Contracts.Models.TileStyle("#FF0000", null, 50) });

        // Assert
        Assert.That(Pixel(round, 12, 12), Is.Not.EqualTo((255, 0, 0)), "outside the round tile");
    }

    [Test]
    public async Task Render_ATileWithoutAStyle_ReachesIntoTheCornerWithTheActionsOwnGradient()
    {
        // Act
        var square = await RenderKey(new DeckHost.Contracts.Models.ControlAppearance { ShowTile = true });

        // Assert
        Assert.That(Pixel(square, 12, 12).B > 100, Is.True, "the action's own tile reaches into the corner");
    }

    private static (int R, int G, int B) Pixel(DeckHost.Core.Models.ControlFrame frame, int x, int y)
    {
        var offset = ((y * frame.Width) + x) * 4;
        return (frame.Rgba[offset], frame.Rgba[offset + 1], frame.Rgba[offset + 2]);
    }

    private static async Task<DeckHost.Core.Models.ControlFrame> RenderKey(DeckHost.Contracts.Models.ControlAppearance appearance, DeckHost.Contracts.Models.ProfileBackground? background = null)
    {
        var plugins = DeckHost.App.SystemPlugins.SystemPluginSet.Create();
        using var store = new VariableStore();
        await using var host = new DeckHost.Core.Devices.DeviceHostService([new KeyDevicePlugin()], plugins, store);
        var frames = new System.Collections.Concurrent.ConcurrentDictionary<string, DeckHost.Core.Models.ControlFrame>();
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        host.SetProfiles([new DeckHost.Contracts.Models.DeviceProfile("p", "P", KeyDevicePlugin.DeviceId,
            [new DeckHost.Contracts.Models.ControlBinding("key.0", DeckHost.Contracts.Models.ProfileActionKind.Single, PlayPause(), appearance, [])],
            Background: background)]);
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

    private const string SquareSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path fill=\"currentColor\" d=\"M2 2h20v20H2z\"/></svg>";
    private const string RedSquareSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path fill=\"#FF0000\" d=\"M2 2h20v20H2z\"/></svg>";
    private const string MicIcon = "/icons/mic.svg";
    private const string DisplayContentId = "builtin.display-content";
    private const uint TintArgb = 0xFF00FF00;

    private SKBitmap DrawIconFile(string name, string svg)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, svg);
        using var resources = new SkiaDrawingResources();
        var bitmap = new SKBitmap(new SKImageInfo(112, 112, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        new SkiaRenderContext(canvas, 112, 112, "dev", "key.0", new VariableStore(), resources).DrawIcon(path, 56, 56, 48, TintArgb);
        canvas.Flush();
        return bitmap;
    }

    private static DeckHost.Contracts.Models.ControlBinding DisplayBinding(DeckHost.Contracts.Models.ControlAppearance appearance)
    {
        var reference = new DeckHost.Contracts.Models.ActionReference("p", "P", "display", "Display", DeckHost.Contracts.Enums.InputCapabilities.None, DeckHost.Contracts.Enums.OutputCapabilities.None);
        return new DeckHost.Contracts.Models.ControlBinding("touch.0", DeckHost.Contracts.Models.ProfileActionKind.Single, reference, appearance, []);
    }

    [Test]
    public void DrawIcon_ASingleColorSvgFile_IsDrawnInTheGivenColor()
    {
        // Act
        using var bitmap = DrawIconFile("square.svg", SquareSvg);

        // Assert
        Assert.That(bitmap.GetPixel(56, 56), Is.EqualTo(new SKColor(TintArgb)));
    }

    [Test]
    public void DrawIcon_ASingleColorSvgFile_CoversOnlyTheIconsSquare()
    {
        // Act
        using var bitmap = DrawIconFile("square.svg", SquareSvg);

        // Assert
        Assert.That(bitmap.GetPixel(10, 10), Is.EqualTo(SKColors.Black));
    }

    [Test]
    public void DrawIcon_AColoredIcon_KeepsItsColors()
    {
        // Act
        using var bitmap = DrawIconFile("red.svg", RedSquareSvg);

        // Assert
        Assert.That(bitmap.GetPixel(56, 56), Is.EqualTo(SKColors.Red));
    }

    [Test]
    public void HasOwnContent_AZoneWithOnlyAnIcon_IsTrue()
    {
        // Arrange
        var iconOnly = DisplayBinding(new DeckHost.Contracts.Models.ControlAppearance { IconPath = MicIcon });

        // Act
        var hasContent = ControlRenderer.HasOwnContent(iconOnly, new AppearanceOnlyAction("display"));

        // Assert
        Assert.That(hasContent, Is.True);
    }

    [Test]
    public void HasOwnContent_AZoneWithNothingSet_IsFalse()
    {
        // Arrange
        var empty = DisplayBinding(new DeckHost.Contracts.Models.ControlAppearance());

        // Act
        var hasContent = ControlRenderer.HasOwnContent(empty, new AppearanceOnlyAction("display"));

        // Assert
        Assert.That(hasContent, Is.False);
    }

    private static DeckHost.App.ViewModels.ControlCustomizationViewModel EditorForAnEmptyKey()
    {
        var builtIns = DeckHost.App.SystemPlugins.SystemPluginSet.Create().First(plugin => plugin.Id == DeckHost.App.SystemPlugins.SystemPluginSet.BuiltInPluginId);
        var display = DeckHost.App.ViewModels.ActionItemViewModel.Create(builtIns.Id, builtIns.Name, builtIns.Actions.First(action => action.Id == DisplayContentId));
        var control = new DeckHost.App.ViewModels.ControlItemViewModel(new DeckHost.Contracts.Models.PhysicalControl("key.0", "Key 1", DeckHost.Contracts.Models.ControlType.Key,
            new DeckHost.Contracts.Models.Rect(0, 0, 10, 10), DeckHost.Contracts.Enums.InputCapabilities.Press, DeckHost.Contracts.Enums.OutputCapabilities.ColorImage));
        return new DeckHost.App.ViewModels.ControlCustomizationViewModel(control, null, [display], display);
    }

    [Test]
    public void BuildBinding_AnEmptyControlWithNoIcon_HasNoBinding()
    {
        // Arrange
        var customize = EditorForAnEmptyKey();

        // Act
        var binding = customize.BuildBinding();

        // Assert
        Assert.That(binding, Is.Null);
    }

    [Test]
    public void BuildBinding_AnIconChosenForAnEmptyControl_CreatesADisplayBindingWithIt()
    {
        // Arrange
        var customize = EditorForAnEmptyKey();
        customize.IconPath = MicIcon;

        // Act
        var binding = customize.BuildBinding();

        // Assert
        Assert.That(binding, Is.Not.Null);
        Assert.That(binding!.Action.ActionId, Is.EqualTo(DisplayContentId));
        Assert.That(binding.Appearance.IconPath, Is.EqualTo(MicIcon));
    }

    [Test]
    public void ClearIcon_AfterChoosingOne_LeavesTheControlEmptyAgain()
    {
        // Arrange
        var customize = EditorForAnEmptyKey();
        customize.IconPath = MicIcon;

        // Act
        customize.ClearIconCommand.Execute(null);

        // Assert
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

    private const byte Opaque = 255;
    private const int RimStartsAbove = 80;

    [Test]
    public void Clear_ABlackBackground_BecomesTransparent()
    {
        // Arrange
        var rgba = Picture();

        // Act
        EdgeBlack.Clear(rgba, Size, Size);

        // Assert
        Assert.That(At(rgba, 0, 0).A, Is.EqualTo(0));
        Assert.That(At(rgba, 11, 11).A, Is.EqualTo(0));
    }

    [Test]
    public void Clear_BlackInsideThePicture_Stays()
    {
        // Arrange
        var rgba = Picture();

        // Act
        EdgeBlack.Clear(rgba, Size, Size);

        // Assert
        Assert.That(At(rgba, 4, 4).A, Is.EqualTo(Opaque), "white");
        Assert.That(At(rgba, 5, 5), Is.EqualTo(((byte)0, Opaque)), "the black dot inside the white square");
    }

    [Test]
    public void Clear_AnAntiAliasedRim_FadesOutInsteadOfStayingADarkEdge()
    {
        // Arrange
        var rgba = Picture();

        // Act
        EdgeBlack.Clear(rgba, Size, Size);

        // Assert
        var rim = At(rgba, 2, 5);
        Assert.That(rim.A, Is.InRange(1, Opaque - 1));
        Assert.That(rim.R > RimStartsAbove, Is.True, $"un-blended from black: {rim.R}");
    }

    [Test]
    public void Clear_APictureWithoutABlackEdge_IsLeftAlone()
    {
        // Arrange
        var rgba = Picture();
        for (var index = 0; index < Size * Size; index++)
        {
            if (rgba[index * 4] == 0)
            {
                rgba[index * 4] = 200; // a red background instead of black
            }
        }

        var before = rgba.ToArray();

        // Act
        EdgeBlack.Clear(rgba, Size, Size);

        // Assert
        Assert.That(rgba, Is.EqualTo(before));
    }

    [Test]
    public void Clear_AnEncodedPluginImage_DecodesWithItsBackgroundCleared()
    {
        // Arrange
        using var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(Picture(), 0, bitmap.GetPixels(), Size * Size * 4);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        // Act
        using var image = EdgeBlack.Clear(png.ToArray());
        using var pixels = SKBitmap.FromImage(image!);

        // Assert
        Assert.That(pixels.GetPixel(0, 0).Alpha, Is.EqualTo(0));
        Assert.That(pixels.GetPixel(4, 4), Is.EqualTo(SKColors.White));
    }
}
