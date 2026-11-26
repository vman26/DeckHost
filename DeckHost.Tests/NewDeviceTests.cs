using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Device.Elgato;
using DeckHost.Device.Web;
using SkiaSharp;

namespace DeckHost.Tests;

public sealed class ElgatoProtocolTests
{
    private const int JpegLength = 2500;
    private const int FullChunk = 2032;
    private const int PictureSize = 10;
    private const int KeyPixels = 72;
    private const int RedThreshold = 200;
    private const int DarkThreshold = 50;
    private const int PlusTouchZoneWidth = 200;
    private const string MiniSerial = "AL12K1A02345";
    private static readonly StreamDeckModel Mk2 = StreamDeckModel.Find(0x0080)!;
    private static readonly StreamDeckModel Plus = StreamDeckModel.Find(0x0084)!;

    private static byte[] Jpeg() => [.. Enumerable.Range(0, JpegLength).Select(static index => (byte)index)];

    private static byte[] LeftHalfRedPicture()
    {
        var rgba = new byte[PictureSize * PictureSize * 4];
        for (var y = 0; y < PictureSize; y++)
        {
            for (var x = 0; x < PictureSize; x++)
            {
                var offset = ((y * PictureSize) + x) * 4;
                rgba[offset] = (byte)(x < PictureSize / 2 ? 255 : 0);
                rgba[offset + 3] = 255;
            }
        }

        return rgba;
    }

    private static IReadOnlyList<byte[]> KeyReportsOfTheJpeg() => [.. StreamDeckProtocol.KeyImageReports(7, Jpeg())];

    [Test]
    public void KeyImageReports_Jpeg2500Bytes_IsSplitIntoThreeReports()
    {
        // Arrange
        var jpeg = Jpeg();

        // Act
        var reports = StreamDeckProtocol.KeyImageReports(7, jpeg).ToList();

        // Assert
        Assert.That(reports.Count, Is.EqualTo(3));
    }

    [Test]
    public void KeyImageReports_EveryReport_HasTheFullReportLength()
    {
        // Arrange
        var reports = KeyReportsOfTheJpeg();

        // Act
        var lengths = reports.Select(static report => report.Length);

        // Assert
        Assert.That(lengths, Is.All.EqualTo(StreamDeckProtocol.ReportLength));
    }

    [Test]
    public void KeyImageReports_FirstReport_CarriesTheKeyAndAFullChunk()
    {
        // Arrange
        var reports = KeyReportsOfTheJpeg();

        // Act
        var header = reports[0][..8];

        // Assert
        Assert.That(header, Is.EqualTo(new byte[] { 0x02, 0x07, 7, 0, 0xF8, 0x03, 0, 0 }));
    }

    [Test]
    public void KeyImageReports_LastReport_CarriesTheRestAndTheFinalFlag()
    {
        // Arrange
        var reports = KeyReportsOfTheJpeg();
        var rest = JpegLength - FullChunk;

        // Act
        var header = reports[2][..8];

        // Assert
        Assert.That(header, Is.EqualTo(new byte[] { 0x02, 0x07, 7, 1, (byte)(rest & 0xFF), (byte)(rest >> 8), 2, 0 }));
    }

    [Test]
    public void KeyImageReports_JoinedPayloads_AreTheOriginalJpeg()
    {
        // Arrange
        var reports = KeyReportsOfTheJpeg();

        // Act
        var joined = reports.SelectMany(static report => report.Skip(8).Take(report[4] | (report[5] << 8))).ToArray();

        // Assert
        Assert.That(joined, Is.EqualTo(Jpeg()));
    }

    [Test]
    public void TouchStripReports_Region_IsInTheHeaderFollowedByThePayload()
    {
        // Arrange
        byte[] payload = [1, 2, 3];

        // Act
        var report = StreamDeckProtocol.TouchStripReports(200, 0, 200, 100, payload).Single();

        // Assert
        Assert.That(report[..15], Is.EqualTo(new byte[] { 0x02, 0x0C, 200, 0, 0, 0, 200, 0, 100, 0, 1, 0, 0, 3, 0 }));
        Assert.That(report[16..19], Is.EqualTo(payload));
    }

    private static byte[] ReportWithKeyDown(bool down)
    {
        var report = new byte[512];
        report[0] = 0x01;
        report[4 + 3] = (byte)(down ? 1 : 0);
        return report;
    }

    [Test]
    public void Parse_KeyGoingDown_ReportsAPress()
    {
        // Arrange
        var keys = new bool[Mk2.KeyCount];
        var dials = new bool[1];

        // Act
        var pressed = StreamDeckProtocol.Parse(ReportWithKeyDown(true), Mk2, keys, dials);

        // Assert
        Assert.That(pressed, Is.EqualTo(new[] { new StreamDeckInput("key.3", InputCapabilities.Press, 1) }));
    }

    [Test]
    public void Parse_KeyStillDown_ReportsNothing()
    {
        // Arrange
        var keys = new bool[Mk2.KeyCount];
        var dials = new bool[1];
        StreamDeckProtocol.Parse(ReportWithKeyDown(true), Mk2, keys, dials);

        // Act
        var again = StreamDeckProtocol.Parse(ReportWithKeyDown(true), Mk2, keys, dials);

        // Assert
        Assert.That(again, Is.Empty);
    }

    [Test]
    public void Parse_KeyGoingUp_ReportsARelease()
    {
        // Arrange
        var keys = new bool[Mk2.KeyCount];
        var dials = new bool[1];
        StreamDeckProtocol.Parse(ReportWithKeyDown(true), Mk2, keys, dials);

        // Act
        var released = StreamDeckProtocol.Parse(ReportWithKeyDown(false), Mk2, keys, dials);

        // Assert
        Assert.That(released, Is.EqualTo(new[] { new StreamDeckInput("key.3", InputCapabilities.Release, 0) }));
    }

    private static IReadOnlyList<StreamDeckInput> ParseOnAPlus(params byte[] report) =>
        StreamDeckProtocol.Parse(report, Plus, new bool[Plus.KeyCount], new bool[Plus.Dials]);

    [Test]
    public void Parse_PlusDialsTurned_ReportsRotationsWithTheirDirection()
    {
        // Arrange
        byte[] report = [0x01, 0x03, 0, 0, 0x01, 0, 0xFE, 0, 3];

        // Act
        var turn = ParseOnAPlus(report);

        // Assert
        Assert.That(turn, Is.EqualTo(new[] { new StreamDeckInput("encoder.1", InputCapabilities.Rotate, -2), new StreamDeckInput("encoder.3", InputCapabilities.Rotate, 3) }));
    }

    [Test]
    public void Parse_PlusDialPushed_ReportsAPress()
    {
        // Arrange
        byte[] report = [0x01, 0x03, 0, 0, 0x00, 1, 0, 0, 0];

        // Act
        var push = ParseOnAPlus(report);

        // Assert
        Assert.That(push, Is.EqualTo(new[] { new StreamDeckInput("encoder.0", InputCapabilities.Press, 1) }));
    }

    [Test]
    public void Parse_PlusTouchAt600Of800_IsATapOnTheFourthZone()
    {
        // Arrange
        byte[] report = [0x01, 0x02, 0, 0, 0x01, 0, 0x58, 0x02, 50, 0, 0, 0, 0, 0];

        // Act
        var tap = ParseOnAPlus(report);

        // Assert
        Assert.That(tap[0].ControlId, Is.EqualTo("touch.3"));
    }

    [Test]
    public void Parse_PlusSwipeToTheLeft_ReportsASwipeLeft()
    {
        // Arrange
        byte[] report = [0x01, 0x02, 0, 0, 0x03, 0, 0x20, 0x03, 50, 0, 0x64, 0, 50, 0];

        // Act
        var swipe = ParseOnAPlus(report);

        // Assert
        Assert.That(swipe, Is.EqualTo(new[] { new StreamDeckInput("touch.swipe", InputCapabilities.Swipe, -1) }));
    }

    [Test]
    public void Controls_Plus_HasEightKeys()
    {
        // Arrange
        var model = Plus;

        // Act
        var keys = model.Controls().Count(static control => control.Type == ControlType.Key);

        // Assert
        Assert.That(keys, Is.EqualTo(8));
    }

    [Test]
    public void Controls_Plus_LinksTheThirdDialToItsTouchZone()
    {
        // Arrange
        var controls = Plus.Controls();

        // Act
        var dial = controls.Single(static control => control.Id == "encoder.2");

        // Assert
        Assert.That(dial.DisplayControlId, Is.EqualTo("touch.2"));
    }

    [Test]
    public void Controls_Plus_GivesATouchZoneTwoHundredPixels()
    {
        // Arrange
        var controls = Plus.Controls();

        // Act
        var zone = controls.Single(static control => control.Id == "touch.0");

        // Assert
        Assert.That(zone.RenderWidth, Is.EqualTo(PlusTouchZoneWidth));
    }

    [Test]
    public void Controls_StreamDeckXl_HasThirtyTwoControls()
    {
        // Arrange
        var model = StreamDeckModel.Find(0x008f)!;

        // Act
        var controls = model.Controls();

        // Assert
        Assert.That(controls.Count, Is.EqualTo(32));
    }

    [Test]
    public void Controls_Pedal_HaveNoImages()
    {
        // Arrange
        var model = StreamDeckModel.Find(0x0086)!;

        // Act
        var widths = model.Controls().Select(static control => control.RenderWidth);

        // Assert
        Assert.That(widths, Is.All.EqualTo(0));
    }

    [Test]
    public void EncodeJpeg_Picture_IsAJpegFile()
    {
        // Arrange
        var rgba = LeftHalfRedPicture();

        // Act
        var jpeg = StreamDeckProtocol.EncodeJpeg(rgba, PictureSize, PictureSize, KeyPixels, KeyPixels, flip: true);

        // Assert
        Assert.That(jpeg[..2], Is.EqualTo(new byte[] { 0xFF, 0xD8 }));
    }

    [Test]
    public void EncodeJpeg_Picture_IsScaledToTheModelSize()
    {
        // Arrange
        var rgba = LeftHalfRedPicture();

        // Act
        using var bitmap = SKBitmap.Decode(StreamDeckProtocol.EncodeJpeg(rgba, PictureSize, PictureSize, KeyPixels, KeyPixels, flip: true));

        // Assert
        Assert.That(bitmap.Width, Is.EqualTo(KeyPixels));
    }

    [Test]
    public void EncodeJpeg_RedOnTheLeftHalf_IsOnTheRightAfterTurning180Degrees()
    {
        // Arrange
        var rgba = LeftHalfRedPicture();

        // Act
        using var bitmap = SKBitmap.Decode(StreamDeckProtocol.EncodeJpeg(rgba, PictureSize, PictureSize, KeyPixels, KeyPixels, flip: true));

        // Assert
        Assert.That(bitmap.GetPixel(60, 36).Red, Is.GreaterThan(RedThreshold));
        Assert.That(bitmap.GetPixel(10, 36).Red, Is.LessThan(DarkThreshold));
    }

    [Test]
    public void DeviceIdFor_ModelAndSerial_IsLowerCaseAndStable()
    {
        // Arrange
        var model = Mk2;

        // Act
        var id = ElgatoDevicePlugin.DeviceIdFor(model, MiniSerial);

        // Assert
        Assert.That(id, Is.EqualTo("elgato.0080.al12k1a02345"));
    }

    [Test]
    public void DeviceIdFor_UnusableSerial_IsUnknown()
    {
        // Arrange
        const string pathLikeSerial = "\\\\?\\";

        // Act
        var id = ElgatoDevicePlugin.DeviceIdFor(Plus, pathLikeSerial);

        // Assert
        Assert.That(id, Is.EqualTo("elgato.0084.unknown"));
    }

    [Test]
    public void Devices_PluginWithoutAnyDeck_IsEmpty()
    {
        // Arrange
        using var plugin = new ElgatoDevicePlugin(Path.Combine(Path.GetTempPath(), "deckhost-elgato-" + Guid.NewGuid().ToString("N")));

        // Act
        var devices = plugin.Devices;

        // Assert
        Assert.That(devices, Is.Empty);
    }

    [Test]
    public void TryPushImage_DisconnectedDeck_FailsQuietlyWithAnError()
    {
        // Arrange
        using var plugin = new ElgatoDevicePlugin(Path.Combine(Path.GetTempPath(), "deckhost-elgato-" + Guid.NewGuid().ToString("N")));

        // Act
        var pushed = plugin.TryPushImage("elgato.0080.x", "key.0", new byte[4], 1, 1, out var error);

        // Assert
        Assert.That(pushed, Is.False);
        Assert.That(error, Is.Not.Null);
    }
}

public sealed class WebDeckTests : IDisposable
{
    private const string Loopback = "127.0.0.1";
    private const string PairingText = "Pairing code";
    private const string Pin = "4321";
    private const int GridColumns = 3;
    private const int GridRows = 2;
    private const int ImagePixels = 144;
    private const string FingerprintPattern = "^([0-9A-F]{2}:){31}[0-9A-F]{2}$";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "deckhost-web-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static string InvariantPort(int port) => port.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<(WebDeckPlugin Plugin, int Port, string Code, RecordingContext Context)> StartedAsync(bool https = false, int? columns = null, int? rows = null)
    {
        var port = FreePort();
        var plugin = new WebDeckPlugin(_directory);
        var context = new RecordingContext();
        await plugin.InitializeAsync(context, CancellationToken.None);
        var settings = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId)
            .With(WebDeckPlugin.Sharing, WebDeckPlugin.SharingLocal)
            .With(WebDeckPlugin.Port, InvariantPort(port));
        settings = https ? settings.With(WebDeckPlugin.Https, "true") : settings;
        settings = columns is { } c ? settings.With(WebDeckPlugin.Columns, c.ToString(System.Globalization.CultureInfo.InvariantCulture)) : settings;
        settings = rows is { } r ? settings.With(WebDeckPlugin.Rows, r.ToString(System.Globalization.CultureInfo.InvariantCulture)) : settings;
        Assert.That(plugin.ApplyDeviceSettings(WebDeckPlugin.DeviceId, settings), Is.Null);
        return (plugin, port, settings.Get(WebDeckPlugin.Code), context);
    }

    private static HttpClient InsecureClient(out HttpClientHandler handler, Action<string?>? onFingerprint = null)
    {
        handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                onFingerprint?.Invoke(certificate is null ? null : WebDeckCertificate.Fingerprint(new System.Security.Cryptography.X509Certificates.X509Certificate2(certificate)));
                return true;
            },
        };
        return new HttpClient(handler);
    }

    [Test]
    public void Controls_FourByTwoGrid_HasEightKeys()
    {
        // Arrange
        const int columns = 4;

        // Act
        var controls = WebDeckLayout.Controls(columns, GridRows);

        // Assert
        Assert.That(controls.Count, Is.EqualTo(8));
    }

    [Test]
    public void Controls_FourByTwoGrid_EndsWithKeySeven()
    {
        // Arrange
        const int columns = 4;

        // Act
        var controls = WebDeckLayout.Controls(columns, GridRows);

        // Assert
        Assert.That(controls[^1].Id, Is.EqualTo("key.7"));
    }

    [Test]
    public void Controls_Key_HasTheKeyPixelSize()
    {
        // Arrange
        const int columns = 4;

        // Act
        var controls = WebDeckLayout.Controls(columns, GridRows);

        // Assert
        Assert.That(controls[0].RenderWidth, Is.EqualTo(WebDeckLayout.KeyPixels));
    }

    [Test]
    public void Controls_AbsurdGridSize_IsLimitedToTheMaximum()
    {
        // Arrange
        const int absurd = 99;

        // Act
        var controls = WebDeckLayout.Controls(absurd, absurd);

        // Assert
        Assert.That(controls.Count, Is.EqualTo(WebDeckLayout.MaxColumns * WebDeckLayout.MaxRows));
    }

    [TestCase("{\"type\":\"down\",\"key\":\"key.2\"}", true, "key.2", InputCapabilities.Press)]
    [TestCase("{\"type\":\"up\",\"key\":\"key.0\"}", true, "key.0", InputCapabilities.Release)]
    [TestCase("{\"type\":\"down\",\"key\":\"key.15\"}", false, "", InputCapabilities.None)]
    [TestCase("{\"type\":\"down\",\"key\":\"encoder.0\"}", false, "", InputCapabilities.None)]
    [TestCase("not json", false, "", InputCapabilities.None)]
    public void TryParseInput_PageMessage_IsCheckedAgainstTheGrid(string json, bool ok, string controlId, InputCapabilities input)
    {
        // Arrange
        const int keysOnTheGrid = 15;

        // Act
        var parsed = WebDeckLayout.TryParseInput(json, keysOnTheGrid, out var parsedId, out var parsedInput);

        // Assert
        Assert.That(parsed, Is.EqualTo(ok));
        Assert.That(parsedId, Is.EqualTo(controlId));
        Assert.That(parsedInput, Is.EqualTo(input));
    }

    [Test]
    public void Devices_NewPlugin_IsOffByDefault()
    {
        // Arrange
        using var plugin = new WebDeckPlugin(_directory);

        // Act
        var connected = plugin.Devices.Single().IsConnected;

        // Assert
        Assert.That(connected, Is.False);
    }

    [Test]
    public void Link_NewPlugin_IsNull()
    {
        // Arrange
        using var plugin = new WebDeckPlugin(_directory);

        // Act
        var link = plugin.Link;

        // Assert
        Assert.That(link, Is.Null);
    }

    [Test]
    public void GetDeviceSettings_NewPlugin_HasAPairingCodeOfTwoGroupsOfFour()
    {
        // Arrange
        using var plugin = new WebDeckPlugin(_directory);

        // Act
        var code = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code);

        // Assert
        Assert.That(code, Does.Match("^[A-Z2-9]{4}-[A-Z2-9]{4}$"));
    }

    [TestCase("k7qm-3xrd", "K7QM3XRD")]
    [TestCase(" K7QM 3XRD ", "K7QM3XRD")]
    public void NormalizeCode_TypedCode_IgnoresCaseDashesAndSpaces(string typed, string normalized)
    {
        // Arrange (the typed code)

        // Act
        var result = WebDeckPlugin.NormalizeCode(typed);

        // Assert
        Assert.That(result, Is.EqualTo(normalized));
    }

    private (WebDeckPlugin Plugin, string Code) PluginWithAPin()
    {
        var plugin = new WebDeckPlugin(_directory);
        var code = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code);
        Assert.That(plugin.ApplyDeviceSettings(WebDeckPlugin.DeviceId, plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).With(WebDeckPlugin.Pin, Pin)), Is.Null);
        return (plugin, code);
    }

    private static void GuessWrongUntilTheLimit(WebDeckPlugin plugin, IPAddress address)
    {
        for (var attempt = 0; attempt < WebDeckPlugin.MaxFailures; attempt++)
        {
            plugin.CheckAccess($"/check?code=WRONG-CODE&pin={Pin}", address);
        }
    }

    [Test]
    public void CheckAccess_PinSetButNotGiven_AsksForThePin()
    {
        // Arrange
        var (plugin, code) = PluginWithAPin();
        using var _ = plugin;

        // Act
        var answer = plugin.CheckAccess($"/check?code={code.ToLowerInvariant()}", IPAddress.Parse("192.168.1.20"));

        // Assert
        Assert.That(answer, Is.EqualTo("pin"));
    }

    [Test]
    public void CheckAccess_RightCodeWithoutTheDashAndTheRightPin_IsAllowed()
    {
        // Arrange
        var (plugin, code) = PluginWithAPin();
        using var _ = plugin;

        // Act
        var answer = plugin.CheckAccess($"/check?code={code.Replace("-", "", StringComparison.Ordinal)}&pin={Pin}", IPAddress.Parse("192.168.1.20"));

        // Assert
        Assert.That(answer, Is.Null);
    }

    [Test]
    public void CheckAccess_WrongCode_AnswersCode()
    {
        // Arrange
        var (plugin, _) = PluginWithAPin();
        using var _ = plugin;

        // Act
        var answer = plugin.CheckAccess($"/check?code=WRONG-CODE&pin={Pin}", IPAddress.Parse("192.168.1.20"));

        // Assert
        Assert.That(answer, Is.EqualTo("code"));
    }

    [Test]
    public void CheckAccess_TooManyWrongGuesses_LocksTheAddressOutEvenWithTheRightCode()
    {
        // Arrange
        var (plugin, code) = PluginWithAPin();
        using var _ = plugin;
        var phone = IPAddress.Parse("192.168.1.20");
        GuessWrongUntilTheLimit(plugin, phone);

        // Act
        var answer = plugin.CheckAccess($"/check?code={code}&pin={Pin}", phone);

        // Assert
        Assert.That(answer, Is.EqualTo("wait"));
    }

    [Test]
    public void CheckAccess_AnotherAddressWhileOneIsLockedOut_IsAllowed()
    {
        // Arrange
        var (plugin, code) = PluginWithAPin();
        using var _ = plugin;
        GuessWrongUntilTheLimit(plugin, IPAddress.Parse("192.168.1.20"));

        // Act
        var answer = plugin.CheckAccess($"/ws?code={code}&pin={Pin}", IPAddress.Parse("192.168.1.21"));

        // Assert
        Assert.That(answer, Is.Null);
    }

    private static async Task<string> PageOverHttpsAsync(int port, Action<string?> onFingerprint)
    {
        using var http = InsecureClient(out var handler, onFingerprint);
        using var _ = handler;
        return await http.GetStringAsync(new Uri($"https://{Loopback}:{port}/"));
    }

    [Test]
    public async Task Https_PageOverTls_IsTheDeckWithItsPairingForm()
    {
        // Arrange
        var (plugin, port, _, _) = await StartedAsync(https: true);
        using var _ = plugin;

        // Act
        var page = await PageOverHttpsAsync(port, _ => { });

        // Assert
        Assert.That(page, Does.Contain(PairingText));
    }

    [Test]
    public async Task Https_Enabled_ReportsASecureServerWithAnHttpsAddress()
    {
        // Arrange
        var (plugin, _, _, _) = await StartedAsync(https: true);
        using var _ = plugin;

        // Act
        var (secure, address) = (plugin.Secure, plugin.Address);

        // Assert
        Assert.That(secure, Is.True);
        Assert.That(address, Does.StartWith("https://"));
    }

    [Test]
    public async Task Https_Enabled_ShowsAColonSeparatedFingerprint()
    {
        // Arrange
        var (plugin, _, _, _) = await StartedAsync(https: true);
        using var _ = plugin;

        // Act
        var fingerprint = plugin.CertificateFingerprint;

        // Assert
        Assert.That(fingerprint, Is.Not.Null.And.Match(FingerprintPattern));
    }

    [Test]
    public async Task Https_CertificateOnTheWire_IsTheOneWhoseFingerprintDeviceSettingsShows()
    {
        // Arrange
        var (plugin, port, _, _) = await StartedAsync(https: true);
        using var _ = plugin;
        string? seen = null;

        // Act
        await PageOverHttpsAsync(port, fingerprint => seen = fingerprint);

        // Assert
        Assert.That(seen, Is.EqualTo(plugin.CertificateFingerprint));
    }

    [Test]
    public async Task GetDeviceSettingDefinitions_HttpsOn_DescribesTheFingerprintInTheHttpsSetting()
    {
        // Arrange
        var (plugin, _, _, _) = await StartedAsync(https: true);
        using var _ = plugin;

        // Act
        var https = plugin.GetDeviceSettingDefinitions(WebDeckPlugin.DeviceId).Single(static setting => setting.Key == WebDeckPlugin.Https);

        // Assert
        Assert.That(https.Description, Does.Contain(plugin.CertificateFingerprint!));
    }

    [Test]
    public async Task Https_WrongPairingCode_IsForbiddenJustLikeOverHttp()
    {
        // Arrange
        var (plugin, port, _, _) = await StartedAsync(https: true);
        using var _ = plugin;
        using var http = InsecureClient(out var handler);
        using var __ = handler;

        // Act
        var response = await http.GetAsync(new Uri($"https://{Loopback}:{port}/check?code=nope"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Https_WebSocketWithTheRightCode_ReceivesTheLayout()
    {
        // Arrange
        var (plugin, port, code, _) = await StartedAsync(https: true);
        using var _ = plugin;
        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        await socket.ConnectAsync(new Uri($"wss://{Loopback}:{port}/ws?code={code}"), CancellationToken.None);

        // Act
        var message = JsonDocument.Parse(await ReceiveAsync(socket)).RootElement;

        // Assert
        Assert.That(message.GetProperty("type").GetString(), Is.EqualTo("layout"));
    }

    [Test]
    public async Task Https_PlainHttpToTheHttpsPort_IsRefused()
    {
        // Arrange
        var (plugin, port, _, _) = await StartedAsync(https: true);
        using var _ = plugin;
        using var plain = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // Act
        var request = () => plain.GetStringAsync(new Uri($"http://{Loopback}:{port}/"));

        // Assert
        Assert.CatchAsync<Exception>(() => request());
    }

    [Test]
    public async Task Https_AfterARefusedPlainRequest_TheServerKeepsWorking()
    {
        // Arrange
        var (plugin, port, _, _) = await StartedAsync(https: true);
        using var _ = plugin;
        using var plain = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        Assert.CatchAsync<Exception>(() => plain.GetStringAsync(new Uri($"http://{Loopback}:{port}/")));

        // Act
        var page = await PageOverHttpsAsync(port, _ => { });

        // Assert
        Assert.That(page, Does.Contain(PairingText));
    }

    [Test]
    public async Task Secure_HttpsOff_IsFalse()
    {
        // Arrange
        var (plugin, _, _, _) = await StartedAsync();
        using var _ = plugin;

        // Act
        var secure = plugin.Secure;

        // Assert
        Assert.That(secure, Is.False);
    }

    [Test]
    public async Task CertificateFingerprint_HttpsOff_IsNull()
    {
        // Arrange
        var (plugin, _, _, _) = await StartedAsync();
        using var _ = plugin;

        // Act
        var fingerprint = plugin.CertificateFingerprint;

        // Assert
        Assert.That(fingerprint, Is.Null);
    }

    [Test]
    public async Task Address_HttpsOff_IsPlainHttp()
    {
        // Arrange
        var (plugin, _, _, _) = await StartedAsync();
        using var _ = plugin;

        // Act
        var address = plugin.Address;

        // Assert
        Assert.That(address, Does.StartWith("http://"));
    }

    [Test]
    public async Task ApplyDeviceSettings_HttpsOff_MakesNoCertificate()
    {
        // Arrange
        var (plugin, _, _, _) = await StartedAsync();
        using var _ = plugin;

        // Act
        var made = File.Exists(Path.Combine(_directory, "web.deck.pfx"));

        // Assert
        Assert.That(made, Is.False);
    }

    private static readonly DateTimeOffset CertificateStart = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] CertificateNames = ["127.0.0.1", "localhost", "192.168.1.20", "deck-pc"];

    private System.Security.Cryptography.X509Certificates.X509Certificate2 FirstCertificate(out bool made) =>
        WebDeckCertificate.LoadOrCreate(_directory, CertificateNames, CertificateStart, out made);

    [Test]
    public void LoadOrCreate_FirstTime_MakesACertificate()
    {
        // Arrange (an empty folder)

        // Act
        using var first = FirstCertificate(out var made);

        // Assert
        Assert.That(made, Is.True);
    }

    [Test]
    public void LoadOrCreate_SameNamesInAnyOrder_ReusesTheCertificate()
    {
        // Arrange
        using var first = FirstCertificate(out _);

        // Act
        using var again = WebDeckCertificate.LoadOrCreate(_directory, CertificateNames.Reverse().ToArray(), CertificateStart.AddDays(10), out var made);

        // Assert
        Assert.That(made, Is.False);
        Assert.That(WebDeckCertificate.Fingerprint(again), Is.EqualTo(WebDeckCertificate.Fingerprint(first)));
    }

    [Test]
    public void LoadOrCreate_NewNetworkAddress_MakesANewCertificate()
    {
        // Arrange
        using var first = FirstCertificate(out _);

        // Act
        using var moved = WebDeckCertificate.LoadOrCreate(_directory, [.. CertificateNames, "192.168.1.99"], CertificateStart.AddDays(10), out var made);

        // Assert
        Assert.That(made, Is.True);
        Assert.That(WebDeckCertificate.Fingerprint(moved), Is.Not.EqualTo(WebDeckCertificate.Fingerprint(first)));
    }

    [Test]
    public void LoadOrCreate_CertificateAboutToExpire_IsReplaced()
    {
        // Arrange
        using var first = FirstCertificate(out _);
        var nearTheEnd = CertificateStart.AddDays(WebDeckCertificate.Lifetime.TotalDays - 1);

        // Act
        using var replaced = WebDeckCertificate.LoadOrCreate(_directory, CertificateNames, nearTheEnd, out var made);

        // Assert
        Assert.That(made, Is.True);
    }

    [Test]
    public void LoadOrCreate_Certificate_ListsEveryNameAsAnAlternativeName()
    {
        // Arrange
        using var certificate = FirstCertificate(out _);

        // Act
        var alternative = certificate.Extensions.OfType<System.Security.Cryptography.X509Certificates.X509Extension>().Single(static extension => extension.Oid?.Value == "2.5.29.17").Format(false);

        // Assert
        Assert.That(alternative, Does.Contain("192.168.1.20").And.Contain("deck-pc").And.Contain("127.0.0.1"));
    }

    [Test]
    public void LoadOrCreate_Certificate_UsesAnEllipticCurveKey()
    {
        // Arrange
        using var certificate = FirstCertificate(out _);

        // Act
        var key = System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions.GetECDsaPublicKey(certificate);

        // Assert
        Assert.That(key, Is.Not.Null);
    }

    [Test]
    public void LoadOrCreate_Certificate_HasItsPrivateKey()
    {
        // Arrange
        using var certificate = FirstCertificate(out _);

        // Act
        var hasKey = certificate.HasPrivateKey;

        // Assert
        Assert.That(hasKey, Is.True);
    }

    private async Task<(WebDeckPlugin Plugin, string Origin)> StartedOnPlainHttpAsync()
    {
        var (plugin, port, _, _) = await StartedAsync();
        return (plugin, $"http://{Loopback}:{port}");
    }

    private static async Task<JsonElement> ManifestAsync(string origin)
    {
        using var http = new HttpClient();
        return JsonDocument.Parse(await http.GetStringAsync(new Uri(origin + "/manifest.webmanifest"))).RootElement;
    }

    [Test]
    public async Task Manifest_Deck_StartsAtTheRoot()
    {
        // Arrange
        var (plugin, origin) = await StartedOnPlainHttpAsync();
        using var _ = plugin;

        // Act
        var manifest = await ManifestAsync(origin);

        // Assert
        Assert.That(manifest.GetProperty("start_url").GetString(), Is.EqualTo("/"));
    }

    [Test]
    public async Task Manifest_Deck_OpensFullscreen()
    {
        // Arrange
        var (plugin, origin) = await StartedOnPlainHttpAsync();
        using var _ = plugin;

        // Act
        var manifest = await ManifestAsync(origin);

        // Assert
        Assert.That(manifest.GetProperty("display").GetString(), Is.EqualTo("fullscreen"));
    }

    [Test]
    public async Task Manifest_Deck_NamesAnIconOfEachSize()
    {
        // Arrange
        var (plugin, origin) = await StartedOnPlainHttpAsync();
        using var _ = plugin;

        // Act
        var manifest = await ManifestAsync(origin);

        // Assert
        Assert.That(manifest.GetProperty("icons").EnumerateArray().Select(static icon => icon.GetProperty("sizes").GetString()), Is.EqualTo(new[] { "192x192", "512x512" }));
    }

    [TestCase("/icon-192.png", 192)]
    [TestCase("/icon-512.png", 512)]
    public async Task Icon_ManifestIcon_IsAPngOfTheSizeTheManifestNames(string path, int size)
    {
        // Arrange
        var (plugin, origin) = await StartedOnPlainHttpAsync();
        using var _ = plugin;
        using var http = new HttpClient();

        // Act
        var png = await http.GetByteArrayAsync(new Uri(origin + path));

        // Assert
        Assert.That(png[..8], Is.EqualTo(PngSignature));
        Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), Is.EqualTo(size));
    }

    [Test]
    public async Task Worker_Deck_OnlyEverHandlesThePage()
    {
        // Arrange
        var (plugin, origin) = await StartedOnPlainHttpAsync();
        using var _ = plugin;
        using var http = new HttpClient();

        // Act
        var worker = await http.GetStringAsync(new Uri(origin + "/sw.js"));

        // Assert
        Assert.That(worker, Does.Contain("pathname !== '/'").And.Not.Contain("/ws").And.Not.Contain("/check").And.Not.Contain("/info"));
    }

    [Test]
    public async Task Page_Deck_LinksTheManifestAndRegistersTheWorkerOnlyInASecureContext()
    {
        // Arrange
        var (plugin, origin) = await StartedOnPlainHttpAsync();
        using var _ = plugin;
        using var http = new HttpClient();

        // Act
        var page = await http.GetStringAsync(new Uri(origin + "/"));

        // Assert
        Assert.That(page, Does.Contain("rel=\"manifest\"").And.Contain("isSecureContext"));
    }

    [Test]
    public async Task Worker_Deck_IsNeverCachedByTheBrowser()
    {
        // Arrange
        var (plugin, origin) = await StartedOnPlainHttpAsync();
        using var _ = plugin;
        using var http = new HttpClient();

        // Act
        var response = await http.GetAsync(new Uri(origin + "/sw.js"));

        // Assert
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    private static string CodeOf(WebDeckPlugin plugin) => plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code);

    private static string RunNewCode(WebDeckPlugin plugin)
    {
        var action = Expect.Single(plugin.GetSettingActions(WebDeckPlugin.DeviceId), static action => action.Id == WebDeckPlugin.NewCodeAction);
        return plugin.RunSettingAction(WebDeckPlugin.DeviceId, action.Id);
    }

    [Test]
    public void RunSettingAction_NewPairingCode_ReplacesTheOldOne()
    {
        // Arrange
        using var plugin = new WebDeckPlugin(_directory);
        var old = CodeOf(plugin);

        // Act
        RunNewCode(plugin);

        // Assert
        Assert.That(CodeOf(plugin), Is.Not.EqualTo(old));
    }

    [Test]
    public void RunSettingAction_NewPairingCode_ShowsTheNewCodeInItsMessage()
    {
        // Arrange
        using var plugin = new WebDeckPlugin(_directory);

        // Act
        var message = RunNewCode(plugin);

        // Assert
        Assert.That(message, Does.Contain(CodeOf(plugin)));
    }

    [Test]
    public void CheckAccess_AfterANewPairingCode_RefusesTheOldOne()
    {
        // Arrange
        using var plugin = new WebDeckPlugin(_directory);
        var old = CodeOf(plugin);
        RunNewCode(plugin);

        // Act
        var answer = plugin.CheckAccess($"/ws?code={old}", IPAddress.Loopback);

        // Assert
        Assert.That(answer, Is.EqualTo("code"));
    }

    [Test]
    public void GetDeviceSettings_AfterANewPairingCodeAndARestart_KeepsTheNewCode()
    {
        // Arrange
        string code;
        using (var plugin = new WebDeckPlugin(_directory))
        {
            RunNewCode(plugin);
            code = CodeOf(plugin);
        }

        // Act
        using var restarted = new WebDeckPlugin(_directory);

        // Assert
        Assert.That(CodeOf(restarted), Is.EqualTo(code));
    }

    private async Task<(WebDeckPlugin Plugin, int Port, string Code, RecordingContext Context)> StartedWithAGridAndAnImageAsync()
    {
        var started = await StartedAsync(columns: GridColumns, rows: GridRows);
        started.Plugin.TryPushImage(WebDeckPlugin.DeviceId, "key.1", new byte[ImagePixels * ImagePixels * 4], ImagePixels, ImagePixels, out _);
        return started;
    }

    [Test]
    public async Task Devices_GridOfThreeByTwo_IsConnectedWithSixControls()
    {
        // Arrange
        var (plugin, _, _, _) = await StartedWithAGridAndAnImageAsync();
        using var _ = plugin;

        // Act
        var device = plugin.Devices.Single();

        // Assert
        Assert.That(device.IsConnected, Is.True);
        Assert.That(device.Controls.Count, Is.EqualTo(GridColumns * GridRows));
    }

    [Test]
    public async Task Page_WithoutACode_OpensAndAsksForIt()
    {
        // Arrange
        var (plugin, port, _, _) = await StartedWithAGridAndAnImageAsync();
        using var _ = plugin;
        using var http = new HttpClient();

        // Act
        var page = await http.GetStringAsync(new Uri($"http://{Loopback}:{port}/"));

        // Assert
        Assert.That(page, Does.Contain(PairingText));
    }

    [Test]
    public async Task Info_WithoutAPin_SaysNoPinIsNeeded()
    {
        // Arrange
        var (plugin, port, _, _) = await StartedWithAGridAndAnImageAsync();
        using var _ = plugin;
        using var http = new HttpClient();

        // Act
        var info = await http.GetStringAsync(new Uri($"http://{Loopback}:{port}/info"));

        // Assert
        Assert.That(info, Does.Contain("\"pin\":false"));
    }

    [Test]
    public async Task Check_WrongCode_IsForbidden()
    {
        // Arrange
        var (plugin, port, _, _) = await StartedWithAGridAndAnImageAsync();
        using var _ = plugin;
        using var http = new HttpClient();

        // Act
        var response = await http.GetAsync(new Uri($"http://{Loopback}:{port}/check?code=nope"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Check_RightCode_IsOk()
    {
        // Arrange
        var (plugin, port, code, _) = await StartedWithAGridAndAnImageAsync();
        using var _ = plugin;
        using var http = new HttpClient();

        // Act
        var response = await http.GetAsync(new Uri($"http://{Loopback}:{port}/check?code={code}"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task WebSocket_WrongCode_IsRefused()
    {
        // Arrange
        var (plugin, port, _, _) = await StartedWithAGridAndAnImageAsync();
        using var _ = plugin;
        using var refused = new ClientWebSocket();

        // Act
        var connect = () => refused.ConnectAsync(new Uri($"ws://{Loopback}:{port}/ws?code=nope"), CancellationToken.None);

        // Assert
        Assert.CatchAsync<WebSocketException>(() => connect());
    }

    private static async Task<ClientWebSocket> PairedSocketAsync(int port, string code)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://{Loopback}:{port}/ws?code={code}"), CancellationToken.None);
        return socket;
    }

    [Test]
    public async Task WebSocket_RightCode_FirstSendsTheLayoutWithTheGridColumns()
    {
        // Arrange
        var (plugin, port, code, _) = await StartedWithAGridAndAnImageAsync();
        using var _ = plugin;
        using var socket = await PairedSocketAsync(port, code);

        // Act
        var layout = JsonDocument.Parse(await ReceiveAsync(socket)).RootElement;

        // Assert
        Assert.That(layout.GetProperty("columns").GetInt32(), Is.EqualTo(GridColumns));
    }

    [Test]
    public async Task WebSocket_RightCode_ThenSendsTheImageOfTheKeyThatWasPushed()
    {
        // Arrange
        var (plugin, port, code, _) = await StartedWithAGridAndAnImageAsync();
        using var _ = plugin;
        using var socket = await PairedSocketAsync(port, code);
        await ReceiveAsync(socket);

        // Act
        var image = JsonDocument.Parse(await ReceiveAsync(socket)).RootElement;

        // Assert
        Assert.That(image.GetProperty("key").GetString(), Is.EqualTo("key.1"));
    }

    [Test]
    public async Task WebSocket_PressAndReleaseOfAKey_AreReportedAsInput()
    {
        // Arrange
        var (plugin, port, code, context) = await StartedWithAGridAndAnImageAsync();
        using var _ = plugin;
        using var socket = await PairedSocketAsync(port, code);

        // Act
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"down\",\"key\":\"key.4\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"up\",\"key\":\"key.4\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitForInputsAsync(context, 2);

        // Assert
        Assert.That(
            context.Inputs.Select(static input => (input.ControlId, input.Input)).ToArray(),
            Is.EqualTo(new[] { ("key.4", InputCapabilities.Press), ("key.4", InputCapabilities.Release) }));
    }

    private static async Task WaitForInputsAsync(RecordingContext context, int count)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (context.Inputs.Count < count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket socket)
    {
        var buffer = new byte[256 * 1024];
        var total = 0;
        using var timeout = new CancellationTokenSource(Patience);
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(total), timeout.Token);
            total += result.Count;
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(buffer, 0, total);
            }
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class RecordingContext : IDevicePluginContext
    {
        public ConcurrentQueue<(string ControlId, InputCapabilities Input)> Inputs { get; } = new();

        public void ReportInputEvent(string deviceId, string controlId, InputCapabilities eventType, int value)
        {
            if (eventType != InputCapabilities.LongPress)
            {
                Inputs.Enqueue((controlId, eventType));
            }
        }

        public void ReportConnectionStatus(string deviceId, string state, string message)
        {
        }

        public void Log(string level, string message)
        {
        }
    }
}
