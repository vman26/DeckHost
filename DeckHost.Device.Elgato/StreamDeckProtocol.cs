using DeckHost.Contracts.Enums;
using SkiaSharp;

namespace DeckHost.Device.Elgato;

/// <summary>An input the device reported, as DeckHost sees it.</summary>
public readonly record struct StreamDeckInput(string ControlId, InputCapabilities Event, int Value);

/// <summary>
/// The Stream Deck USB HID protocol (both generations), as pure functions: the reports that
/// carry a key image or a part of the Stream Deck +'s touch strip, brightness and reset, and how
/// an input report turns into key, dial and touch events.
/// </summary>
public static class StreamDeckProtocol
{
    /// <summary>Output reports are this long (report id included).</summary>
    public const int ReportLength = 1024;

    private const int KeyHeaderLength = 8;
    private const int StripHeaderLength = 16;

    /// <summary>The reports that set key <paramref name="key"/> to a JPEG: [0x02, 0x07, key, last, length, page].</summary>
    public static IEnumerable<byte[]> KeyImageReports(int key, byte[] jpeg)
    {
        var payload = ReportLength - KeyHeaderLength;
        var page = 0;
        for (var offset = 0; offset < jpeg.Length || page == 0; offset += payload, page++)
        {
            var length = Math.Min(payload, jpeg.Length - offset);
            var report = new byte[ReportLength];
            report[0] = 0x02;
            report[1] = 0x07;
            report[2] = (byte)key;
            report[3] = (byte)(offset + length >= jpeg.Length ? 1 : 0);
            report[4] = (byte)(length & 0xFF);
            report[5] = (byte)(length >> 8);
            report[6] = (byte)(page & 0xFF);
            report[7] = (byte)(page >> 8);
            jpeg.AsSpan(offset, length).CopyTo(report.AsSpan(KeyHeaderLength));
            yield return report;
        }
    }

    /// <summary>The reports that draw a JPEG at (x, y) on the Stream Deck +'s touch strip: [0x02, 0x0C, x, y, w, h, last, page, length].</summary>
    public static IEnumerable<byte[]> TouchStripReports(int x, int y, int width, int height, byte[] jpeg)
    {
        var payload = ReportLength - StripHeaderLength;
        var page = 0;
        for (var offset = 0; offset < jpeg.Length || page == 0; offset += payload, page++)
        {
            var length = Math.Min(payload, jpeg.Length - offset);
            var report = new byte[ReportLength];
            report[0] = 0x02;
            report[1] = 0x0C;
            WriteUInt16(report, 2, x);
            WriteUInt16(report, 4, y);
            WriteUInt16(report, 6, width);
            WriteUInt16(report, 8, height);
            report[10] = (byte)(offset + length >= jpeg.Length ? 1 : 0);
            WriteUInt16(report, 11, page);
            WriteUInt16(report, 13, length);
            jpeg.AsSpan(offset, length).CopyTo(report.AsSpan(StripHeaderLength));
            yield return report;
        }
    }

    // ---------------------------------------------------------------- first generation

    private const int Gen1HeaderLength = 16;

    /// <summary>
    /// First generation (first edition, Mini): the reports that set key <paramref name="key"/> (reading
    /// order) to a BMP: [0x02, 0x01, page, 0, last, hardware key + 1, 0…], each padded to the model's
    /// <see cref="StreamDeckModel.ImageReportLength"/>. The first edition counts pages from 1.
    /// </summary>
    public static IEnumerable<byte[]> Gen1KeyImageReports(StreamDeckModel model, int key, byte[] bmp)
    {
        var payload = model.ImageReportLength - Gen1HeaderLength;
        var firstPage = model.KeysRightToLeft ? 1 : 0;
        var page = 0;
        for (var offset = 0; offset < bmp.Length || page == 0; offset += payload, page++)
        {
            var length = Math.Min(payload, bmp.Length - offset);
            var report = new byte[model.ImageReportLength];
            report[0] = 0x02;
            report[1] = 0x01;
            report[2] = (byte)(page + firstPage);
            report[4] = (byte)(offset + length >= bmp.Length ? 1 : 0);
            report[5] = (byte)(model.HardwareKey(key) + 1);
            bmp.AsSpan(offset, length).CopyTo(report.AsSpan(Gen1HeaderLength));
            yield return report;
        }
    }

    /// <summary>First generation: an empty image report that resets the device's image stream.</summary>
    public static byte[] Gen1ResetStreamReport(StreamDeckModel model)
    {
        var report = new byte[model.ImageReportLength];
        report[0] = 0x02;
        return report;
    }

    /// <summary>
    /// A rendered control (RGBA) as the first generation's key image: a 24-bit BMP (bottom-up rows,
    /// BGR) at the model's key size, turned 180° (first edition) or mirrored on the diagonal (Mini).
    /// </summary>
    public static byte[] EncodeBmp(ReadOnlySpan<byte> rgba, int sourceWidth, int sourceHeight, StreamDeckModel model)
    {
        var size = model.KeyImageSize;
        var scaled = Scale(rgba, sourceWidth, sourceHeight, size, size);
        const int HeaderLength = 54;
        var rowLength = ((size * 3) + 3) & ~3;
        var bmp = new byte[HeaderLength + (rowLength * size)];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32(bmp, 2, bmp.Length);
        WriteInt32(bmp, 10, HeaderLength);
        WriteInt32(bmp, 14, 40);
        WriteInt32(bmp, 18, size);
        WriteInt32(bmp, 22, size);
        bmp[26] = 1;
        bmp[28] = 24;
        WriteInt32(bmp, 34, rowLength * size);
        WriteInt32(bmp, 38, 2835);
        WriteInt32(bmp, 42, 2835);
        for (var y = 0; y < size; y++)
        {
            // BMP rows go from the bottom up.
            var row = HeaderLength + ((size - 1 - y) * rowLength);
            for (var x = 0; x < size; x++)
            {
                var (sx, sy) = model.TransposeKeyImages ? (y, x) : model.FlipKeyImages ? (size - 1 - x, size - 1 - y) : (x, y);
                var source = ((sy * size) + sx) * 4;
                var target = row + (x * 3);
                bmp[target] = scaled[source + 2];
                bmp[target + 1] = scaled[source + 1];
                bmp[target + 2] = scaled[source];
            }
        }

        return bmp;
    }

    /// <summary>Feature report: key brightness in percent (first generation: 17 bytes, 0x05 0x55 0xAA 0xD1 0x01).</summary>
    public static byte[] BrightnessReport(StreamDeckModel model, int percent)
    {
        if (model.Generation != 1)
        {
            return BrightnessReport(percent);
        }

        var report = new byte[17];
        report[0] = 0x05;
        report[1] = 0x55;
        report[2] = 0xAA;
        report[3] = 0xD1;
        report[4] = 0x01;
        report[5] = (byte)Math.Clamp(percent, 0, 100);
        return report;
    }

    /// <summary>Feature report: back to the device's own logo screen.</summary>
    public static byte[] ResetReport(StreamDeckModel model)
    {
        if (model.Generation != 1)
        {
            return ResetReport();
        }

        var report = new byte[17];
        report[0] = 0x0B;
        report[1] = 0x63;
        return report;
    }

    /// <summary>The feature report to read the firmware version with (0x05, 32 bytes; first generation 0x04, 17 bytes).</summary>
    public static byte[] FirmwareRequest(StreamDeckModel model)
    {
        var report = new byte[model.Generation == 1 ? 17 : 32];
        report[0] = (byte)(model.Generation == 1 ? 0x04 : 0x05);
        return report;
    }

    /// <summary>The version in a firmware feature report: text from byte 6 (first generation 5) up to the first NUL.</summary>
    public static string? FirmwareVersion(StreamDeckModel model, ReadOnlySpan<byte> report)
    {
        var start = model.Generation == 1 ? 5 : 6;
        if (report.Length <= start)
        {
            return null;
        }

        var text = report[start..];
        var end = text.IndexOf((byte)0);
        var version = System.Text.Encoding.ASCII.GetString(end >= 0 ? text[..end] : text).Trim();
        return version.Length > 0 && version.All(static c => c >= ' ' && c < 127) ? version : null;
    }

    /// <summary>Feature report: key brightness in percent.</summary>
    public static byte[] BrightnessReport(int percent)
    {
        var report = new byte[32];
        report[0] = 0x03;
        report[1] = 0x08;
        report[2] = (byte)Math.Clamp(percent, 0, 100);
        return report;
    }

    /// <summary>Feature report: back to the device's own logo screen.</summary>
    public static byte[] ResetReport()
    {
        var report = new byte[32];
        report[0] = 0x03;
        report[1] = 0x02;
        return report;
    }

    /// <summary>
    /// The events in an input report, given the keys held before (updated). Keys report all states
    /// at once: changes become Press/Release. The Stream Deck + adds dials (press states or turn
    /// steps) and the touch strip (a tap on a zone, a long press, or a drag as a swipe).
    /// </summary>
    public static IReadOnlyList<StreamDeckInput> Parse(ReadOnlySpan<byte> report, StreamDeckModel model, bool[] keysDown, bool[] dialsDown)
    {
        var events = new List<StreamDeckInput>();
        if (model.Generation == 1)
        {
            // First generation: [0x01, state of hardware key 0, 1, …].
            for (var key = 0; key < model.KeyCount && report.Length > 0 && report[0] == 0x01 && 1 + model.HardwareKey(key) < report.Length; key++)
            {
                var down = report[1 + model.HardwareKey(key)] != 0;
                if (down != keysDown[key])
                {
                    keysDown[key] = down;
                    events.Add(new StreamDeckInput($"key.{key}", down ? InputCapabilities.Press : InputCapabilities.Release, down ? 1 : 0));
                }
            }

            return events;
        }

        if (report.Length < 5 || report[0] != 0x01)
        {
            return events;
        }

        switch (report[1])
        {
            case 0x00:
                for (var key = 0; key < model.KeyCount && 4 + key < report.Length; key++)
                {
                    var down = report[4 + key] != 0;
                    if (down != keysDown[key])
                    {
                        keysDown[key] = down;
                        events.Add(new StreamDeckInput($"key.{key}", down ? InputCapabilities.Press : InputCapabilities.Release, down ? 1 : 0));
                    }
                }

                break;

            case 0x03 when model.Dials > 0 && report.Length >= 5 + model.Dials:
                for (var dial = 0; dial < model.Dials; dial++)
                {
                    var value = report[5 + dial];
                    if (report[4] == 0x00)
                    {
                        var down = value != 0;
                        if (down != dialsDown[dial])
                        {
                            dialsDown[dial] = down;
                            events.Add(new StreamDeckInput($"encoder.{dial}", down ? InputCapabilities.Press : InputCapabilities.Release, down ? 1 : 0));
                        }
                    }
                    else if (value != 0)
                    {
                        events.Add(new StreamDeckInput($"encoder.{dial}", InputCapabilities.Rotate, (sbyte)value));
                    }
                }

                break;

            case 0x02 when model.TouchStripWidth > 0 && report.Length >= 12:
            {
                var x = report[6] | (report[7] << 8);
                var zone = Math.Clamp(x * model.Dials / model.TouchStripWidth, 0, model.Dials - 1);
                switch (report[4])
                {
                    case 0x01: // short tap
                        events.Add(new StreamDeckInput($"touch.{zone}", InputCapabilities.Press, 1));
                        events.Add(new StreamDeckInput($"touch.{zone}", InputCapabilities.Release, 0));
                        break;
                    case 0x02: // long press
                        events.Add(new StreamDeckInput($"touch.{zone}", InputCapabilities.LongPress, 1));
                        break;
                    case 0x03: // drag: from x to the end x
                        var endX = report[10] | (report[11] << 8);
                        if (Math.Abs(endX - x) > 20)
                        {
                            events.Add(new StreamDeckInput("touch.swipe", InputCapabilities.Swipe, endX < x ? -1 : 1));
                        }

                        break;
                }

                break;
            }
        }

        return events;
    }

    /// <summary>
    /// A rendered control (RGBA) as the JPEG the device wants: scaled to <paramref name="width"/>×
    /// <paramref name="height"/> and, for keys of most models, turned 180°.
    /// </summary>
    /// <summary>The image (RGBA) scaled to <paramref name="width"/>×<paramref name="height"/> on black, as RGBA.</summary>
    internal static byte[] Scale(ReadOnlySpan<byte> rgba, int sourceWidth, int sourceHeight, int width, int height)
    {
        var info = new SKImageInfo(sourceWidth, sourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var source = new SKBitmap(info);
        rgba[..Math.Min(rgba.Length, info.BytesSize)].CopyTo(source.GetPixelSpan());
        using var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Black);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        return target.GetPixelSpan().ToArray();
    }

    public static byte[] EncodeJpeg(ReadOnlySpan<byte> rgba, int sourceWidth, int sourceHeight, int width, int height, bool flip, int quality = 92)
    {
        var info = new SKImageInfo(sourceWidth, sourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var source = new SKBitmap(info);
        rgba[..Math.Min(rgba.Length, info.BytesSize)].CopyTo(source.GetPixelSpan());

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        if (flip)
        {
            canvas.Translate(width, height);
            canvas.Scale(-1, -1);
        }

        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteUInt16(byte[] report, int offset, int value)
    {
        report[offset] = (byte)(value & 0xFF);
        report[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
