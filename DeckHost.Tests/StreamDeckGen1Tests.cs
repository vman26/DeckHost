using DeckHost.Contracts.Enums;
using DeckHost.Device.Elgato;

namespace DeckHost.Tests;

public class StreamDeckGen1Tests
{
    private static readonly StreamDeckModel Original = StreamDeckModel.Find(0x0060)!;
    private static readonly StreamDeckModel Mini = StreamDeckModel.Find(0x0063)!;

    private static byte[] Solid(int size, byte r, byte g, byte b)
    {
        var rgba = new byte[size * size * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (r, g, b, 255);
        }

        return rgba;
    }

    [Fact]
    public void The_first_edition_and_the_Mini_are_driven_now()
    {
        Assert.Equal(1, Original.Generation);
        Assert.Equal(15, Original.KeyCount);
        Assert.Equal(6, Mini.KeyCount);
        Assert.Equal(80, Mini.KeyImageSize);
        Assert.Empty(StreamDeckModel.NotYetSupported);
    }

    [Fact]
    public void The_first_edition_numbers_keys_from_the_right_in_each_row()
    {
        Assert.Equal([4, 3, 2, 1, 0, 9, 8, 7, 6, 5, 14, 13, 12, 11, 10], Enumerable.Range(0, 15).Select(Original.HardwareKey));
        Assert.Equal([0, 1, 2, 3, 4, 5], Enumerable.Range(0, 6).Select(Mini.HardwareKey));
    }

    [Fact]
    public void A_key_image_is_a_bottom_up_BGR_bitmap_of_the_model_size()
    {
        var bmp = StreamDeckProtocol.EncodeBmp(Solid(8, 255, 0, 0), 8, 8, Mini);

        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(54 + (80 * 3 * 80), bmp.Length);
        Assert.Equal(80, BitConverter.ToInt32(bmp, 18));
        Assert.Equal(24, bmp[28]);
        Assert.Equal([0, 0, 255], bmp[54..57]); // blue, green, red
    }

    [Fact]
    public void The_first_edition_turns_images_and_the_Mini_mirrors_them_on_the_diagonal()
    {
        // Left half white, right half black.
        static byte[] Half(int size)
        {
            var rgba = new byte[size * size * 4];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size / 2; x++)
                {
                    rgba.AsSpan(((y * size) + x) * 4, 4).Fill(255);
                }
            }

            return rgba;
        }

        static byte Pixel(byte[] bmp, int size, int x, int y) => bmp[54 + ((size - 1 - y) * size * 3) + (x * 3)];

        var original = StreamDeckProtocol.EncodeBmp(Half(72), 72, 72, Original);
        Assert.True(Pixel(original, 72, 2, 36) < 20 && Pixel(original, 72, 69, 36) > 235);       // turned: white on the right

        var mini = StreamDeckProtocol.EncodeBmp(Half(80), 80, 80, Mini);
        Assert.True(Pixel(mini, 80, 40, 2) > 235 && Pixel(mini, 80, 40, 77) < 20);            // transposed: white on top
    }

    [Fact]
    public void Image_reports_are_padded_paged_and_name_the_hardware_key()
    {
        var bmp = new byte[15606];
        var reports = StreamDeckProtocol.Gen1KeyImageReports(Original, 0, bmp).ToList();

        Assert.Equal(2, reports.Count);
        Assert.All(reports, static report => Assert.Equal(8191, report.Length));
        Assert.Equal([0x02, 0x01, 1, 0, 0, 5], reports[0][..6]);   // page 1, key 0 is hardware key 4 (+1)
        Assert.Equal([0x02, 0x01, 2, 0, 1, 5], reports[1][..6]);   // last page

        var mini = StreamDeckProtocol.Gen1KeyImageReports(Mini, 2, new byte[19254]).ToList();
        Assert.Equal(20, mini.Count);
        Assert.Equal([0x02, 0x01, 0, 0, 0, 3], mini[0][..6]);      // the Mini counts pages from 0
        Assert.Equal(1, mini[^1][4]);
    }

    [Fact]
    public void Input_reports_list_every_key_by_hardware_number()
    {
        var keys = new bool[15];
        var dials = new bool[1];
        var report = new byte[16];
        report[0] = 0x01;
        report[1 + 4] = 1;   // hardware key 4 = DeckHost key 0

        var pressed = StreamDeckProtocol.Parse(report, Original, keys, dials);
        report[1 + 4] = 0;
        var released = StreamDeckProtocol.Parse(report, Original, keys, dials);

        Assert.Equal(new StreamDeckInput("key.0", InputCapabilities.Press, 1), Assert.Single(pressed));
        Assert.Equal(new StreamDeckInput("key.0", InputCapabilities.Release, 0), Assert.Single(released));
    }

    [Fact]
    public void First_generation_feature_reports()
    {
        Assert.Equal([0x05, 0x55, 0xAA, 0xD1, 0x01, 40], StreamDeckProtocol.BrightnessReport(Mini, 40)[..6]);
        Assert.Equal(17, StreamDeckProtocol.BrightnessReport(Mini, 40).Length);
        Assert.Equal([0x0B, 0x63], StreamDeckProtocol.ResetReport(Original)[..2]);
        Assert.Equal(0x03, StreamDeckProtocol.BrightnessReport(StreamDeckModel.Find(0x0080)!, 40)[0]);
    }
}
