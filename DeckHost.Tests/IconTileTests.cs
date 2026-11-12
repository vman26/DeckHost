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
