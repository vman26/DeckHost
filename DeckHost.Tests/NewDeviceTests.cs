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
    private static readonly StreamDeckModel Mk2 = StreamDeckModel.Find(0x0080)!;
    private static readonly StreamDeckModel Plus = StreamDeckModel.Find(0x0084)!;

    [Test]
    public void KeyImageIsSplitIntoNumberedReports()
    {
        var jpeg = Enumerable.Range(0, 2500).Select(static index => (byte)index).ToArray();

        var reports = StreamDeckProtocol.KeyImageReports(7, jpeg).ToList();

        Assert.That(reports.Count, Is.EqualTo(3));
        Expect.All(reports, static report => Assert.That(report.Length, Is.EqualTo(StreamDeckProtocol.ReportLength)));
        Assert.That(reports[0][..8], Is.EqualTo(new byte[] { 0x02, 0x07, 7, 0, 0xF8, 0x03, 0, 0 }));
        Assert.That(reports[2][..8], Is.EqualTo(new byte[] { 0x02, 0x07, 7, 1, 2500 - 2032 & 0xFF, (2500 - 2032) >> 8, 2, 0 }));
        var joined = reports.SelectMany(static report => report.Skip(8).Take(report[4] | (report[5] << 8))).ToArray();
        Assert.That(joined, Is.EqualTo(jpeg));
    }

    [Test]
    public void TouchStripReportCarriesItsRegion()
    {
        var report = StreamDeckProtocol.TouchStripReports(200, 0, 200, 100, [1, 2, 3]).Single();

        Assert.That(report[..15], Is.EqualTo(new byte[] { 0x02, 0x0C, 200, 0, 0, 0, 200, 0, 100, 0, 1, 0, 0, 3, 0 }));
        Assert.That(report[16..19], Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void KeyStatesBecomePressAndRelease()
    {
        var keys = new bool[Mk2.KeyCount];
        var dials = new bool[1];
        var report = new byte[512];
        report[0] = 0x01;
        report[4 + 3] = 1;

        var pressed = StreamDeckProtocol.Parse(report, Mk2, keys, dials);
        var again = StreamDeckProtocol.Parse(report, Mk2, keys, dials);
        report[4 + 3] = 0;
        var released = StreamDeckProtocol.Parse(report, Mk2, keys, dials);

        Assert.That(pressed, Is.EqualTo(new[] { new StreamDeckInput("key.3", InputCapabilities.Press, 1) }));
        Assert.That(again, Is.Empty);
        Assert.That(released, Is.EqualTo(new[] { new StreamDeckInput("key.3", InputCapabilities.Release, 0) }));
    }

    [Test]
    public void PlusDialsAndTouchStripAreParsed()
    {
        var keys = new bool[Plus.KeyCount];
        var dials = new bool[Plus.Dials];

        var turn = StreamDeckProtocol.Parse(new byte[] { 0x01, 0x03, 0, 0, 0x01, 0, 0xFE, 0, 3 }, Plus, keys, dials);
        var push = StreamDeckProtocol.Parse(new byte[] { 0x01, 0x03, 0, 0, 0x00, 1, 0, 0, 0 }, Plus, keys, dials);
        var tap = StreamDeckProtocol.Parse(new byte[] { 0x01, 0x02, 0, 0, 0x01, 0, 0x58, 0x02, 50, 0, 0, 0, 0, 0 }, Plus, keys, dials);
        var swipe = StreamDeckProtocol.Parse(new byte[] { 0x01, 0x02, 0, 0, 0x03, 0, 0x20, 0x03, 50, 0, 0x64, 0, 50, 0 }, Plus, keys, dials);

        Assert.That(
            turn,
            Is.EqualTo(new[] { new StreamDeckInput("encoder.1", InputCapabilities.Rotate, -2), new StreamDeckInput("encoder.3", InputCapabilities.Rotate, 3) }));
        Assert.That(push, Is.EqualTo(new[] { new StreamDeckInput("encoder.0", InputCapabilities.Press, 1) }));
        Assert.That(tap[0].ControlId, Is.EqualTo("touch.3")); // x = 600 of 800
        Assert.That(swipe, Is.EqualTo(new[] { new StreamDeckInput("touch.swipe", InputCapabilities.Swipe, -1) }));
    }

    [Test]
    public void PlusLinksEachDialToItsTouchZone()
    {
        var controls = Plus.Controls();

        Assert.That(controls.Count(static control => control.Type == ControlType.Key), Is.EqualTo(8));
        Assert.That(controls.Single(static control => control.Id == "encoder.2").DisplayControlId, Is.EqualTo("touch.2"));
        Assert.That(controls.Single(static control => control.Id == "touch.0").RenderWidth, Is.EqualTo(200));
        Assert.That(StreamDeckModel.Find(0x008f)!.Controls().Count, Is.EqualTo(32));
        Expect.All(StreamDeckModel.Find(0x0086)!.Controls(), static control => Assert.That(control.RenderWidth, Is.EqualTo(0)));
    }

    [Test]
    public void KeyImagesAreJpegAtTheModelSizeAndTurned()
    {
        // Red on the left half: after turning 180° it is on the right.
        var rgba = new byte[10 * 10 * 4];
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                var offset = ((y * 10) + x) * 4;
                rgba[offset] = (byte)(x < 5 ? 255 : 0);
                rgba[offset + 3] = 255;
            }
        }

        var jpeg = StreamDeckProtocol.EncodeJpeg(rgba, 10, 10, 72, 72, flip: true);
        using var bitmap = SKBitmap.Decode(jpeg);

        Assert.That(jpeg[0], Is.EqualTo(0xFF));
        Assert.That(jpeg[1], Is.EqualTo(0xD8));
        Assert.That(bitmap.Width, Is.EqualTo(72));
        Assert.That(bitmap.GetPixel(60, 36).Red > 200, Is.True);
        Assert.That(bitmap.GetPixel(10, 36).Red < 50, Is.True);
    }

    [Test]
    public void DeviceIdsAreStableAndSafe()
    {
        Assert.That(ElgatoDevicePlugin.DeviceIdFor(Mk2, "AL12K1A02345"), Is.EqualTo("elgato.0080.al12k1a02345"));
        Assert.That(ElgatoDevicePlugin.DeviceIdFor(Plus, "\\\\?\\"), Is.EqualTo("elgato.0084.unknown"));
    }

    [Test]
    public void ImagesForADisconnectedDeckFailQuietly()
    {
        using var plugin = new ElgatoDevicePlugin(Path.Combine(Path.GetTempPath(), "deckhost-elgato-" + Guid.NewGuid().ToString("N")));

        Assert.That(plugin.Devices, Is.Empty);
        Assert.That(plugin.TryPushImage("elgato.0080.x", "key.0", new byte[4], 1, 1, out var error), Is.False);
        Assert.That(error, Is.Not.Null);
    }
}

public sealed class WebDeckTests : IDisposable
{
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

    [Test]
    public void GridHasTheChosenSize()
    {
        var controls = WebDeckLayout.Controls(4, 2);

        Assert.That(controls.Count, Is.EqualTo(8));
        Assert.That(controls[^1].Id, Is.EqualTo("key.7"));
        Assert.That(controls[0].RenderWidth, Is.EqualTo(WebDeckLayout.KeyPixels));
        Assert.That(WebDeckLayout.Controls(99, 99).Count, Is.EqualTo(WebDeckLayout.MaxColumns * WebDeckLayout.MaxRows));
    }

    [TestCase("{\"type\":\"down\",\"key\":\"key.2\"}", true, "key.2", InputCapabilities.Press)]
    [TestCase("{\"type\":\"up\",\"key\":\"key.0\"}", true, "key.0", InputCapabilities.Release)]
    [TestCase("{\"type\":\"down\",\"key\":\"key.15\"}", false, "", InputCapabilities.None)]
    [TestCase("{\"type\":\"down\",\"key\":\"encoder.0\"}", false, "", InputCapabilities.None)]
    [TestCase("not json", false, "", InputCapabilities.None)]
    public void PageMessagesAreChecked(string json, bool ok, string controlId, InputCapabilities input)
    {
        Assert.That(WebDeckLayout.TryParseInput(json, 15, out var parsedId, out var parsedInput), Is.EqualTo(ok));
        Assert.That(parsedId, Is.EqualTo(controlId));
        Assert.That(parsedInput, Is.EqualTo(input));
    }

    [Test]
    public void OffByDefaultWithAPairingCode()
    {
        using var plugin = new WebDeckPlugin(_directory);

        Assert.That(plugin.Devices.Single().IsConnected, Is.False);
        Assert.That(plugin.Link, Is.Null);
        Assert.That(plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code), Does.Match("^[A-Z2-9]{4}-[A-Z2-9]{4}$"));
    }

    [TestCase("k7qm-3xrd", "K7QM3XRD")]
    [TestCase(" K7QM 3XRD ", "K7QM3XRD")]
    public void CodesAreComparedWithoutCaseDashesOrSpaces(string typed, string normalized) =>
        Assert.That(WebDeckPlugin.NormalizeCode(typed), Is.EqualTo(normalized));

    [Test]
    public void ThePinIsNeededWhenSetAndGuessingIsLimited()
    {
        using var plugin = new WebDeckPlugin(_directory);
        var code = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code);
        Assert.That(plugin.ApplyDeviceSettings(WebDeckPlugin.DeviceId, plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).With(WebDeckPlugin.Pin, "4321")), Is.Null);
        var phone = IPAddress.Parse("192.168.1.20");
        var other = IPAddress.Parse("192.168.1.21");

        Assert.That(plugin.CheckAccess($"/check?code={code.ToLowerInvariant()}", phone), Is.EqualTo("pin"));
        Assert.That(plugin.CheckAccess($"/check?code={code.Replace("-", "", StringComparison.Ordinal)}&pin=4321", phone), Is.Null);
        for (var attempt = 0; attempt < WebDeckPlugin.MaxFailures; attempt++)
        {
            Assert.That(plugin.CheckAccess("/check?code=WRONG-CODE&pin=4321", phone), Is.EqualTo("code"));
        }

        // Locked out for a while, even with the right code; other addresses aren't.
        Assert.That(plugin.CheckAccess($"/check?code={code}&pin=4321", phone), Is.EqualTo("wait"));
        Assert.That(plugin.CheckAccess($"/ws?code={code}&pin=4321", other), Is.Null);
    }

    private async Task<(WebDeckPlugin Plugin, int Port, string Code)> StartSecure()
    {
        var port = FreePort();
        var plugin = new WebDeckPlugin(_directory);
        await plugin.InitializeAsync(new RecordingContext(), CancellationToken.None);
        var settings = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId)
            .With(WebDeckPlugin.Sharing, WebDeckPlugin.SharingLocal)
            .With(WebDeckPlugin.Port, port.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .With(WebDeckPlugin.Https, "true");
        Assert.That(plugin.ApplyDeviceSettings(WebDeckPlugin.DeviceId, settings), Is.Null);
        return (plugin, port, settings.Get(WebDeckPlugin.Code));
    }

    [Test]
    public async Task OverHttpsThePageAndTheDeckAreEncryptedWithACertificateWhoseFingerprintIsShown()
    {
        var (plugin, port, code) = await StartSecure();
        using var _ = plugin;
        string? seen = null;
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                seen = certificate is null ? null : WebDeckCertificate.Fingerprint(new System.Security.Cryptography.X509Certificates.X509Certificate2(certificate));
                return true;
            },
        };
        using var http = new HttpClient(handler);

        var page = await http.GetStringAsync(new Uri($"https://127.0.0.1:{port}/"));

        Assert.That(page, Does.Contain("Pairing code"));
        Assert.That(plugin.Secure, Is.True);
        Assert.That(plugin.Address, Does.StartWith("https://"));
        Assert.That(plugin.CertificateFingerprint, Is.Not.Null.And.Match("^([0-9A-F]{2}:){31}[0-9A-F]{2}$"));
        Assert.That(seen, Is.EqualTo(plugin.CertificateFingerprint), "the certificate on the wire is the one whose fingerprint Device Settings shows");
        var https = plugin.GetDeviceSettingDefinitions(WebDeckPlugin.DeviceId).Single(static setting => setting.Key == WebDeckPlugin.Https);
        Assert.That(https.Description, Does.Contain(plugin.CertificateFingerprint!));

        Assert.That((await http.GetAsync(new Uri($"https://127.0.0.1:{port}/check?code=nope"))).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "pairing works the same over TLS");
        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        await socket.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/ws?code={code}"), CancellationToken.None);
        Assert.That(JsonDocument.Parse(await ReceiveAsync(socket)).RootElement.GetProperty("type").GetString(), Is.EqualTo("layout"));
    }

    [Test]
    public async Task PlainHttpToTheHttpsPortIsRefusedAndTheServerKeepsWorking()
    {
        var (plugin, port, _) = await StartSecure();
        using var _ = plugin;
        using var plain = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        Assert.CatchAsync<Exception>(() => plain.GetStringAsync(new Uri($"http://127.0.0.1:{port}/")));

        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = static (_, _, _, _) => true };
        using var secure = new HttpClient(handler);
        Assert.That(await secure.GetStringAsync(new Uri($"https://127.0.0.1:{port}/")), Does.Contain("Pairing code"));
    }

    [Test]
    public async Task WithoutHttpsThereIsNoCertificateAndTheAddressIsPlain()
    {
        var port = FreePort();
        using var plugin = new WebDeckPlugin(_directory);
        await plugin.InitializeAsync(new RecordingContext(), CancellationToken.None);
        var settings = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).With(WebDeckPlugin.Sharing, WebDeckPlugin.SharingLocal).With(WebDeckPlugin.Port, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.That(plugin.ApplyDeviceSettings(WebDeckPlugin.DeviceId, settings), Is.Null);

        Assert.That(plugin.Secure, Is.False);
        Assert.That(plugin.CertificateFingerprint, Is.Null);
        Assert.That(plugin.Address, Does.StartWith("http://"));
        Assert.That(File.Exists(Path.Combine(_directory, "web.deck.pfx")), Is.False, "no certificate is made unless HTTPS is on");
    }

    [Test]
    public void TheCertificateIsKeptAcrossRestartsAndMadeAgainForNewNamesOrWhenItIsAboutToExpire()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        string[] names = ["127.0.0.1", "localhost", "192.168.1.20", "deck-pc"];

        using var first = WebDeckCertificate.LoadOrCreate(_directory, names, now, out var madeFirst);
        using var again = WebDeckCertificate.LoadOrCreate(_directory, names.Reverse().ToArray(), now.AddDays(10), out var madeAgain);
        using var moved = WebDeckCertificate.LoadOrCreate(_directory, [.. names, "192.168.1.99"], now.AddDays(10), out var madeMoved);
        using var old = WebDeckCertificate.LoadOrCreate(_directory, [.. names, "192.168.1.99"], now.AddDays(10 + (int)WebDeckCertificate.Lifetime.TotalDays - 10), out var madeOld);

        Assert.That(madeFirst, Is.True);
        Assert.That(madeAgain, Is.False, "the same names, in any order, reuse the certificate");
        Assert.That(WebDeckCertificate.Fingerprint(again), Is.EqualTo(WebDeckCertificate.Fingerprint(first)));
        Assert.That(madeMoved, Is.True, "a new network address needs a new certificate");
        Assert.That(WebDeckCertificate.Fingerprint(moved), Is.Not.EqualTo(WebDeckCertificate.Fingerprint(first)));
        Assert.That(madeOld, Is.True, "one that is about to expire is replaced");

        var alternative = first.Extensions.OfType<System.Security.Cryptography.X509Certificates.X509Extension>().Single(static extension => extension.Oid?.Value == "2.5.29.17").Format(false);
        Assert.That(alternative, Does.Contain("192.168.1.20").And.Contain("deck-pc").And.Contain("127.0.0.1"));
        Assert.That(System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions.GetECDsaPublicKey(first), Is.Not.Null, "an elliptic curve key");
        Assert.That(first.HasPrivateKey, Is.True);
    }

    [Test]
    public async Task TheDeckIsInstallableItServesAManifestIconsAndAWorkerThatNeverTouchesThePairing()
    {
        var port = FreePort();
        using var plugin = new WebDeckPlugin(_directory);
        await plugin.InitializeAsync(new RecordingContext(), CancellationToken.None);
        Assert.That(plugin.ApplyDeviceSettings(WebDeckPlugin.DeviceId, plugin.GetDeviceSettings(WebDeckPlugin.DeviceId)
            .With(WebDeckPlugin.Sharing, WebDeckPlugin.SharingLocal).With(WebDeckPlugin.Port, port.ToString(System.Globalization.CultureInfo.InvariantCulture))), Is.Null);
        using var http = new HttpClient();
        var origin = $"http://127.0.0.1:{port}";

        var manifest = JsonDocument.Parse(await http.GetStringAsync(new Uri(origin + "/manifest.webmanifest"))).RootElement;
        Assert.That(manifest.GetProperty("start_url").GetString(), Is.EqualTo("/"));
        Assert.That(manifest.GetProperty("display").GetString(), Is.EqualTo("fullscreen"));
        Assert.That(manifest.GetProperty("icons").EnumerateArray().Select(static icon => icon.GetProperty("sizes").GetString()), Is.EqualTo(new[] { "192x192", "512x512" }));

        foreach (var (path, size) in new[] { ("/icon-192.png", 192), ("/icon-512.png", 512) })
        {
            var png = await http.GetByteArrayAsync(new Uri(origin + path));
            Assert.That(png[..8], Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "a PNG");
            Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), Is.EqualTo(size), "of the size the manifest names");
        }

        var worker = await http.GetStringAsync(new Uri(origin + "/sw.js"));
        Assert.That(worker, Does.Contain("pathname !== '/'").And.Not.Contain("/ws").And.Not.Contain("/check").And.Not.Contain("/info"), "it only ever handles the page");
        var page = await http.GetStringAsync(new Uri(origin + "/"));
        Assert.That(page, Does.Contain("rel=\"manifest\"").And.Contain("isSecureContext"), "the page links the manifest and registers the worker only in a secure context");
        var response = await http.GetAsync(new Uri(origin + "/sw.js"));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True, "the worker itself is never cached by the browser");
    }

    [Test]
    public void NewPairingCodeReplacesTheOldOne()
    {
        using var plugin = new WebDeckPlugin(_directory);
        var old = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code);

        var action = Expect.Single(plugin.GetSettingActions(WebDeckPlugin.DeviceId));
        var message = plugin.RunSettingAction(WebDeckPlugin.DeviceId, action.Id);
        var code = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code);

        Assert.That(code, Is.Not.EqualTo(old));
        Assert.That(message, Does.Contain(code));
        Assert.That(plugin.CheckAccess($"/ws?code={old}", IPAddress.Loopback), Is.EqualTo("code"));
        Assert.That(new WebDeckPlugin(_directory).GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code), Is.EqualTo(code));
    }

    [Test]
    public async Task APairedPageGetsTheDeckAndSendsPresses()
    {
        var port = FreePort();
        using var plugin = new WebDeckPlugin(_directory);
        var context = new RecordingContext();
        await plugin.InitializeAsync(context, CancellationToken.None);
        var settings = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId)
            .With(WebDeckPlugin.Sharing, WebDeckPlugin.SharingLocal)
            .With(WebDeckPlugin.Port, port.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .With(WebDeckPlugin.Columns, "3")
            .With(WebDeckPlugin.Rows, "2");
        Assert.That(plugin.ApplyDeviceSettings(WebDeckPlugin.DeviceId, settings), Is.Null);
        Assert.That(plugin.Devices.Single().IsConnected, Is.True);
        Assert.That(plugin.Devices.Single().Controls.Count, Is.EqualTo(6));
        plugin.TryPushImage(WebDeckPlugin.DeviceId, "key.1", new byte[144 * 144 * 4], 144, 144, out _);

        var code = settings.Get(WebDeckPlugin.Code);
        using var http = new HttpClient();
        // The page opens without a code (it asks for it); the deck itself needs it.
        var page = await http.GetStringAsync(new Uri($"http://127.0.0.1:{port}/"));
        Assert.That(page, Does.Contain("Pairing code"));
        Assert.That(await http.GetStringAsync(new Uri($"http://127.0.0.1:{port}/info")), Does.Contain("\"pin\":false"));
        Assert.That((await http.GetAsync(new Uri($"http://127.0.0.1:{port}/check?code=nope"))).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await http.GetAsync(new Uri($"http://127.0.0.1:{port}/check?code={code}"))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using (var refused = new ClientWebSocket())
        {
            Assert.CatchAsync<WebSocketException>(() => refused.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?code=nope"), CancellationToken.None));
        }

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?code={code}"), CancellationToken.None);
        var layout = JsonDocument.Parse(await ReceiveAsync(socket)).RootElement;
        var image = JsonDocument.Parse(await ReceiveAsync(socket)).RootElement;
        Assert.That(layout.GetProperty("columns").GetInt32(), Is.EqualTo(3));
        Assert.That(image.GetProperty("key").GetString(), Is.EqualTo("key.1"));

        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"down\",\"key\":\"key.4\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"up\",\"key\":\"key.4\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (context.Inputs.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.That(
            context.Inputs.Select(static input => (input.ControlId, input.Input)).ToArray(),
            Is.EqualTo(new[] { ("key.4", InputCapabilities.Press), ("key.4", InputCapabilities.Release) }));
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket socket)
    {
        var buffer = new byte[256 * 1024];
        var total = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
