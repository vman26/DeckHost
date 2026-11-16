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

    [Test]
    public void The_first_edition_and_the_Mini_are_driven_now()
    {
        Assert.That(Original.Generation, Is.EqualTo(1));
        Assert.That(Original.KeyCount, Is.EqualTo(15));
        Assert.That(Mini.KeyCount, Is.EqualTo(6));
        Assert.That(Mini.KeyImageSize, Is.EqualTo(80));
        Assert.That(StreamDeckModel.NotYetSupported, Is.Empty);
    }

    [Test]
    public void The_first_edition_numbers_keys_from_the_right_in_each_row()
    {
        Assert.That(Enumerable.Range(0, 15).Select(Original.HardwareKey), Is.EqualTo(new[] { 4, 3, 2, 1, 0, 9, 8, 7, 6, 5, 14, 13, 12, 11, 10 }));
        Assert.That(Enumerable.Range(0, 6).Select(Mini.HardwareKey), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void A_key_image_is_a_bottom_up_BGR_bitmap_of_the_model_size()
    {
        var bmp = StreamDeckProtocol.EncodeBmp(Solid(8, 255, 0, 0), 8, 8, Mini);

        Assert.That(bmp[0], Is.EqualTo((byte)'B'));
        Assert.That(bmp[1], Is.EqualTo((byte)'M'));
        Assert.That(bmp.Length, Is.EqualTo(54 + (80 * 3 * 80)));
        Assert.That(BitConverter.ToInt32(bmp, 18), Is.EqualTo(80));
        Assert.That(bmp[28], Is.EqualTo(24));
        Assert.That(bmp[54..57], Is.EqualTo(new[] { 0, 0, 255 })); // blue, green, red
    }

    [Test]
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
        Assert.That(Pixel(original, 72, 2, 36) < 20 && Pixel(original, 72, 69, 36) > 235, Is.True);       // turned: white on the right

        var mini = StreamDeckProtocol.EncodeBmp(Half(80), 80, 80, Mini);
        Assert.That(Pixel(mini, 80, 40, 2) > 235 && Pixel(mini, 80, 40, 77) < 20, Is.True);            // transposed: white on top
    }

    [Test]
    public void Image_reports_are_padded_paged_and_name_the_hardware_key()
    {
        var bmp = new byte[15606];
        var reports = StreamDeckProtocol.Gen1KeyImageReports(Original, 0, bmp).ToList();

        Assert.That(reports.Count, Is.EqualTo(2));
        Expect.All(reports, static report => Assert.That(report.Length, Is.EqualTo(8191)));
        Assert.That(reports[0][..6], Is.EqualTo(new[] { 0x02, 0x01, 1, 0, 0, 5 }));   // page 1, key 0 is hardware key 4 (+1)
        Assert.That(reports[1][..6], Is.EqualTo(new[] { 0x02, 0x01, 2, 0, 1, 5 }));   // last page

        var mini = StreamDeckProtocol.Gen1KeyImageReports(Mini, 2, new byte[19254]).ToList();
        Assert.That(mini.Count, Is.EqualTo(20));
        Assert.That(mini[0][..6], Is.EqualTo(new[] { 0x02, 0x01, 0, 0, 0, 3 }));      // the Mini counts pages from 0
        Assert.That(mini[^1][4], Is.EqualTo(1));
    }

    [Test]
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

        Assert.That(Expect.Single(pressed), Is.EqualTo(new StreamDeckInput("key.0", InputCapabilities.Press, 1)));
        Assert.That(Expect.Single(released), Is.EqualTo(new StreamDeckInput("key.0", InputCapabilities.Release, 0)));
    }

    [Test]
    public void First_generation_feature_reports()
    {
        Assert.That(StreamDeckProtocol.BrightnessReport(Mini, 40)[..6], Is.EqualTo(new[] { 0x05, 0x55, 0xAA, 0xD1, 0x01, 40 }));
        Assert.That(StreamDeckProtocol.BrightnessReport(Mini, 40).Length, Is.EqualTo(17));
        Assert.That(StreamDeckProtocol.ResetReport(Original)[..2], Is.EqualTo(new[] { 0x0B, 0x63 }));
        Assert.That(StreamDeckProtocol.BrightnessReport(StreamDeckModel.Find(0x0080)!, 40)[0], Is.EqualTo(0x03));
    }
}
