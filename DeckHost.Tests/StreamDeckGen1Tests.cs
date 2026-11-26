using DeckHost.Contracts.Enums;
using DeckHost.Device.Elgato;

namespace DeckHost.Tests;

public class StreamDeckGen1Tests
{
    private const int OriginalKeys = 15;
    private const int MiniKeys = 6;
    private const int OriginalImageSize = 72;
    private const int MiniImageSize = 80;
    private const int BmpHeaderLength = 54;
    private const int OriginalReportLength = 8191;
    private const int White = 235;
    private const int Black = 20;
    private const int Brightness = 40;
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

    // Left half white, right half black.
    private static byte[] LeftHalfWhite(int size)
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

    private static byte BlueChannel(byte[] bmp, int size, int x, int y) => bmp[BmpHeaderLength + ((size - 1 - y) * size * 3) + (x * 3)];

    [Test]
    public void Generation_FirstEdition_IsOne()
    {
        // Arrange
        var model = Original;

        // Act
        var generation = model.Generation;

        // Assert
        Assert.That(generation, Is.EqualTo(1));
    }

    [Test]
    public void KeyCount_FirstEditionAndMini_AreFifteenAndSix()
    {
        // Arrange
        var models = (Original, Mini);

        // Act
        var counts = (models.Original.KeyCount, models.Mini.KeyCount);

        // Assert
        Assert.That(counts, Is.EqualTo((OriginalKeys, MiniKeys)));
    }

    [Test]
    public void KeyImageSize_Mini_IsEightyPixels()
    {
        // Arrange
        var model = Mini;

        // Act
        var size = model.KeyImageSize;

        // Assert
        Assert.That(size, Is.EqualTo(MiniImageSize));
    }

    [Test]
    public void NotYetSupported_FirstEditionAndMini_AreDrivenNow()
    {
        // Arrange
        int[] gen1ProductIds = [0x0060, 0x0063, 0x0090];

        // Act
        var notYet = StreamDeckModel.NotYetSupported;

        // Assert
        Assert.That(gen1ProductIds, Has.None.AnyOf(notYet.Keys.Cast<int>().ToArray()));
    }

    [Test]
    public void HardwareKey_FirstEdition_NumbersKeysFromTheRightInEachRow()
    {
        // Arrange
        var keys = Enumerable.Range(0, OriginalKeys);

        // Act
        var hardware = keys.Select(Original.HardwareKey);

        // Assert
        Assert.That(hardware, Is.EqualTo(new[] { 4, 3, 2, 1, 0, 9, 8, 7, 6, 5, 14, 13, 12, 11, 10 }));
    }

    [Test]
    public void HardwareKey_Mini_NumbersKeysInReadingOrder()
    {
        // Arrange
        var keys = Enumerable.Range(0, MiniKeys);

        // Act
        var hardware = keys.Select(Mini.HardwareKey);

        // Assert
        Assert.That(hardware, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void EncodeBmp_Picture_IsABitmapFile()
    {
        // Arrange
        var rgba = Solid(8, 255, 0, 0);

        // Act
        var bmp = StreamDeckProtocol.EncodeBmp(rgba, 8, 8, Mini);

        // Assert
        Assert.That(bmp[..2], Is.EqualTo(new[] { (byte)'B', (byte)'M' }));
    }

    [Test]
    public void EncodeBmp_Picture_IsAnUncompressed24BitBitmapOfTheModelSize()
    {
        // Arrange
        var rgba = Solid(8, 255, 0, 0);

        // Act
        var bmp = StreamDeckProtocol.EncodeBmp(rgba, 8, 8, Mini);

        // Assert
        Assert.That(bmp.Length, Is.EqualTo(BmpHeaderLength + (MiniImageSize * 3 * MiniImageSize)));
        Assert.That(BitConverter.ToInt32(bmp, 18), Is.EqualTo(MiniImageSize));
        Assert.That(bmp[28], Is.EqualTo(24));
    }

    [Test]
    public void EncodeBmp_RedPicture_StoresBlueGreenRedOrder()
    {
        // Arrange
        var rgba = Solid(8, 255, 0, 0);

        // Act
        var bmp = StreamDeckProtocol.EncodeBmp(rgba, 8, 8, Mini);

        // Assert
        Assert.That(bmp[BmpHeaderLength..(BmpHeaderLength + 3)], Is.EqualTo(new[] { 0, 0, 255 }));
    }

    [Test]
    public void EncodeBmp_FirstEdition_TurnsTheImageSoWhiteIsOnTheRight()
    {
        // Arrange
        var rgba = LeftHalfWhite(OriginalImageSize);

        // Act
        var bmp = StreamDeckProtocol.EncodeBmp(rgba, OriginalImageSize, OriginalImageSize, Original);

        // Assert
        Assert.That(BlueChannel(bmp, OriginalImageSize, 2, 36), Is.LessThan(Black));
        Assert.That(BlueChannel(bmp, OriginalImageSize, 69, 36), Is.GreaterThan(White));
    }

    [Test]
    public void EncodeBmp_Mini_MirrorsTheImageOnTheDiagonalSoWhiteIsOnTop()
    {
        // Arrange
        var rgba = LeftHalfWhite(MiniImageSize);

        // Act
        var bmp = StreamDeckProtocol.EncodeBmp(rgba, MiniImageSize, MiniImageSize, Mini);

        // Assert
        Assert.That(BlueChannel(bmp, MiniImageSize, 40, 2), Is.GreaterThan(White));
        Assert.That(BlueChannel(bmp, MiniImageSize, 40, 77), Is.LessThan(Black));
    }

    [Test]
    public void Gen1KeyImageReports_FirstEditionImage_IsPaddedToTwoFullReports()
    {
        // Arrange
        var bmp = new byte[15606];

        // Act
        var reports = StreamDeckProtocol.Gen1KeyImageReports(Original, 0, bmp).ToList();

        // Assert
        Assert.That(reports.Count, Is.EqualTo(2));
        Assert.That(reports.Select(static report => report.Length), Is.All.EqualTo(OriginalReportLength));
    }

    [Test]
    public void Gen1KeyImageReports_FirstEditionKeyZero_NamesPageOneAndHardwareKeyFourPlusOne()
    {
        // Arrange
        var bmp = new byte[15606];

        // Act
        var reports = StreamDeckProtocol.Gen1KeyImageReports(Original, 0, bmp).ToList();

        // Assert
        Assert.That(reports[0][..6], Is.EqualTo(new[] { 0x02, 0x01, 1, 0, 0, 5 }));
    }

    [Test]
    public void Gen1KeyImageReports_FirstEditionLastPage_IsFlaggedLast()
    {
        // Arrange
        var bmp = new byte[15606];

        // Act
        var reports = StreamDeckProtocol.Gen1KeyImageReports(Original, 0, bmp).ToList();

        // Assert
        Assert.That(reports[1][..6], Is.EqualTo(new[] { 0x02, 0x01, 2, 0, 1, 5 }));
    }

    [Test]
    public void Gen1KeyImageReports_Mini_SplitsTheImageIntoTwentyReports()
    {
        // Arrange
        var bmp = new byte[19254];

        // Act
        var reports = StreamDeckProtocol.Gen1KeyImageReports(Mini, 2, bmp).ToList();

        // Assert
        Assert.That(reports.Count, Is.EqualTo(20));
    }

    [Test]
    public void Gen1KeyImageReports_Mini_CountsPagesFromZero()
    {
        // Arrange
        var bmp = new byte[19254];

        // Act
        var reports = StreamDeckProtocol.Gen1KeyImageReports(Mini, 2, bmp).ToList();

        // Assert
        Assert.That(reports[0][..6], Is.EqualTo(new[] { 0x02, 0x01, 0, 0, 0, 3 }));
    }

    [Test]
    public void Gen1KeyImageReports_Mini_FlagsTheLastReport()
    {
        // Arrange
        var bmp = new byte[19254];

        // Act
        var reports = StreamDeckProtocol.Gen1KeyImageReports(Mini, 2, bmp).ToList();

        // Assert
        Assert.That(reports[^1][4], Is.EqualTo(1));
    }

    private static byte[] FirstEditionReportWithHardwareKeyFourDown(bool down)
    {
        var report = new byte[16];
        report[0] = 0x01;
        report[1 + 4] = (byte)(down ? 1 : 0);
        return report;
    }

    [Test]
    public void Parse_FirstEditionHardwareKeyFourDown_IsDeckHostKeyZeroPressed()
    {
        // Arrange
        var keys = new bool[OriginalKeys];
        var dials = new bool[1];

        // Act
        var pressed = StreamDeckProtocol.Parse(FirstEditionReportWithHardwareKeyFourDown(true), Original, keys, dials);

        // Assert
        Assert.That(Expect.Single(pressed), Is.EqualTo(new StreamDeckInput("key.0", InputCapabilities.Press, 1)));
    }

    [Test]
    public void Parse_FirstEditionHardwareKeyFourUp_IsDeckHostKeyZeroReleased()
    {
        // Arrange
        var keys = new bool[OriginalKeys];
        var dials = new bool[1];
        StreamDeckProtocol.Parse(FirstEditionReportWithHardwareKeyFourDown(true), Original, keys, dials);

        // Act
        var released = StreamDeckProtocol.Parse(FirstEditionReportWithHardwareKeyFourDown(false), Original, keys, dials);

        // Assert
        Assert.That(Expect.Single(released), Is.EqualTo(new StreamDeckInput("key.0", InputCapabilities.Release, 0)));
    }

    [Test]
    public void BrightnessReport_Mini_IsASeventeenByteFeatureReport()
    {
        // Arrange
        var model = Mini;

        // Act
        var report = StreamDeckProtocol.BrightnessReport(model, Brightness);

        // Assert
        Assert.That(report[..6], Is.EqualTo(new[] { 0x05, 0x55, 0xAA, 0xD1, 0x01, Brightness }));
        Assert.That(report.Length, Is.EqualTo(17));
    }

    [Test]
    public void ResetReport_FirstEdition_StartsWithTheResetCommand()
    {
        // Arrange
        var model = Original;

        // Act
        var report = StreamDeckProtocol.ResetReport(model);

        // Assert
        Assert.That(report[..2], Is.EqualTo(new[] { 0x0B, 0x63 }));
    }

    [Test]
    public void BrightnessReport_SecondGeneration_UsesTheThreeCommand()
    {
        // Arrange
        var model = StreamDeckModel.Find(0x0080)!;

        // Act
        var report = StreamDeckProtocol.BrightnessReport(model, Brightness);

        // Assert
        Assert.That(report[0], Is.EqualTo(0x03));
    }
}
