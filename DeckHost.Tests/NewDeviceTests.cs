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

    [Fact]
    public void KeyImageIsSplitIntoNumberedReports()
    {
        var jpeg = Enumerable.Range(0, 2500).Select(static index => (byte)index).ToArray();

        var reports = StreamDeckProtocol.KeyImageReports(7, jpeg).ToList();

        Assert.Equal(3, reports.Count);
        Assert.All(reports, static report => Assert.Equal(StreamDeckProtocol.ReportLength, report.Length));
        Assert.Equal(new byte[] { 0x02, 0x07, 7, 0, 0xF8, 0x03, 0, 0 }, reports[0][..8]);
        Assert.Equal(new byte[] { 0x02, 0x07, 7, 1, 2500 - 2032 & 0xFF, (2500 - 2032) >> 8, 2, 0 }, reports[2][..8]);
        var joined = reports.SelectMany(static report => report.Skip(8).Take(report[4] | (report[5] << 8))).ToArray();
        Assert.Equal(jpeg, joined);
    }

    [Fact]
    public void TouchStripReportCarriesItsRegion()
    {
        var report = StreamDeckProtocol.TouchStripReports(200, 0, 200, 100, [1, 2, 3]).Single();

        Assert.Equal(new byte[] { 0x02, 0x0C, 200, 0, 0, 0, 200, 0, 100, 0, 1, 0, 0, 3, 0 }, report[..15]);
        Assert.Equal(new byte[] { 1, 2, 3 }, report[16..19]);
    }

    [Fact]
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

        Assert.Equal([new StreamDeckInput("key.3", InputCapabilities.Press, 1)], pressed);
        Assert.Empty(again);
        Assert.Equal([new StreamDeckInput("key.3", InputCapabilities.Release, 0)], released);
    }

    [Fact]
    public void PlusDialsAndTouchStripAreParsed()
    {
        var keys = new bool[Plus.KeyCount];
        var dials = new bool[Plus.Dials];

        var turn = StreamDeckProtocol.Parse(new byte[] { 0x01, 0x03, 0, 0, 0x01, 0, 0xFE, 0, 3 }, Plus, keys, dials);
        var push = StreamDeckProtocol.Parse(new byte[] { 0x01, 0x03, 0, 0, 0x00, 1, 0, 0, 0 }, Plus, keys, dials);
        var tap = StreamDeckProtocol.Parse(new byte[] { 0x01, 0x02, 0, 0, 0x01, 0, 0x58, 0x02, 50, 0, 0, 0, 0, 0 }, Plus, keys, dials);
        var swipe = StreamDeckProtocol.Parse(new byte[] { 0x01, 0x02, 0, 0, 0x03, 0, 0x20, 0x03, 50, 0, 0x64, 0, 50, 0 }, Plus, keys, dials);

        Assert.Equal(
            [new StreamDeckInput("encoder.1", InputCapabilities.Rotate, -2), new StreamDeckInput("encoder.3", InputCapabilities.Rotate, 3)],
            turn);
        Assert.Equal([new StreamDeckInput("encoder.0", InputCapabilities.Press, 1)], push);
        Assert.Equal("touch.3", tap[0].ControlId); // x = 600 of 800
        Assert.Equal([new StreamDeckInput("touch.swipe", InputCapabilities.Swipe, -1)], swipe);
    }

    [Fact]
    public void PlusLinksEachDialToItsTouchZone()
    {
        var controls = Plus.Controls();

        Assert.Equal(8, controls.Count(static control => control.Type == ControlType.Key));
        Assert.Equal("touch.2", controls.Single(static control => control.Id == "encoder.2").DisplayControlId);
        Assert.Equal(200, controls.Single(static control => control.Id == "touch.0").RenderWidth);
        Assert.Equal(32, StreamDeckModel.Find(0x008f)!.Controls().Count);
        Assert.All(StreamDeckModel.Find(0x0086)!.Controls(), static control => Assert.Equal(0, control.RenderWidth));
    }

    [Fact]
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

        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        Assert.Equal(72, bitmap.Width);
        Assert.True(bitmap.GetPixel(60, 36).Red > 200);
        Assert.True(bitmap.GetPixel(10, 36).Red < 50);
    }

    [Fact]
    public void DeviceIdsAreStableAndSafe()
    {
        Assert.Equal("elgato.0080.al12k1a02345", ElgatoDevicePlugin.DeviceIdFor(Mk2, "AL12K1A02345"));
        Assert.Equal("elgato.0084.unknown", ElgatoDevicePlugin.DeviceIdFor(Plus, "\\\\?\\"));
    }

    [Fact]
    public void ImagesForADisconnectedDeckFailQuietly()
    {
        using var plugin = new ElgatoDevicePlugin(Path.Combine(Path.GetTempPath(), "deckhost-elgato-" + Guid.NewGuid().ToString("N")));

        Assert.Empty(plugin.Devices);
        Assert.False(plugin.TryPushImage("elgato.0080.x", "key.0", new byte[4], 1, 1, out var error));
        Assert.NotNull(error);
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

    [Fact]
    public void GridHasTheChosenSize()
    {
        var controls = WebDeckLayout.Controls(4, 2);

        Assert.Equal(8, controls.Count);
        Assert.Equal("key.7", controls[^1].Id);
        Assert.Equal(WebDeckLayout.KeyPixels, controls[0].RenderWidth);
        Assert.Equal(WebDeckLayout.MaxColumns * WebDeckLayout.MaxRows, WebDeckLayout.Controls(99, 99).Count);
    }

    [Theory]
    [InlineData("{\"type\":\"down\",\"key\":\"key.2\"}", true, "key.2", InputCapabilities.Press)]
    [InlineData("{\"type\":\"up\",\"key\":\"key.0\"}", true, "key.0", InputCapabilities.Release)]
    [InlineData("{\"type\":\"down\",\"key\":\"key.15\"}", false, "", InputCapabilities.None)]
    [InlineData("{\"type\":\"down\",\"key\":\"encoder.0\"}", false, "", InputCapabilities.None)]
    [InlineData("not json", false, "", InputCapabilities.None)]
    public void PageMessagesAreChecked(string json, bool ok, string controlId, InputCapabilities input)
    {
        Assert.Equal(ok, WebDeckLayout.TryParseInput(json, 15, out var parsedId, out var parsedInput));
        Assert.Equal(controlId, parsedId);
        Assert.Equal(input, parsedInput);
    }

    [Fact]
    public void OffByDefaultWithAPairingCode()
    {
        using var plugin = new WebDeckPlugin(_directory);

        Assert.False(plugin.Devices.Single().IsConnected);
        Assert.Null(plugin.Link);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code));
    }

    [Theory]
    [InlineData("k7qm-3xrd", "K7QM3XRD")]
    [InlineData(" K7QM 3XRD ", "K7QM3XRD")]
    public void CodesAreComparedWithoutCaseDashesOrSpaces(string typed, string normalized) =>
        Assert.Equal(normalized, WebDeckPlugin.NormalizeCode(typed));

    [Fact]
    public void ThePinIsNeededWhenSetAndGuessingIsLimited()
    {
        using var plugin = new WebDeckPlugin(_directory);
        var code = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code);
        Assert.Null(plugin.ApplyDeviceSettings(WebDeckPlugin.DeviceId, plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).With(WebDeckPlugin.Pin, "4321")));
        var phone = IPAddress.Parse("192.168.1.20");
        var other = IPAddress.Parse("192.168.1.21");

        Assert.Equal("pin", plugin.CheckAccess($"/check?code={code.ToLowerInvariant()}", phone));
        Assert.Null(plugin.CheckAccess($"/check?code={code.Replace("-", "", StringComparison.Ordinal)}&pin=4321", phone));
        for (var attempt = 0; attempt < WebDeckPlugin.MaxFailures; attempt++)
        {
            Assert.Equal("code", plugin.CheckAccess("/check?code=WRONG-CODE&pin=4321", phone));
        }

        // Locked out for a while, even with the right code; other addresses aren't.
        Assert.Equal("wait", plugin.CheckAccess($"/check?code={code}&pin=4321", phone));
        Assert.Null(plugin.CheckAccess($"/ws?code={code}&pin=4321", other));
    }

    [Fact]
    public void NewPairingCodeReplacesTheOldOne()
    {
        using var plugin = new WebDeckPlugin(_directory);
        var old = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code);

        var action = Assert.Single(plugin.GetSettingActions(WebDeckPlugin.DeviceId));
        var message = plugin.RunSettingAction(WebDeckPlugin.DeviceId, action.Id);
        var code = plugin.GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code);

        Assert.NotEqual(old, code);
        Assert.Contains(code, message, StringComparison.Ordinal);
        Assert.Equal("code", plugin.CheckAccess($"/ws?code={old}", IPAddress.Loopback));
        Assert.Equal(code, new WebDeckPlugin(_directory).GetDeviceSettings(WebDeckPlugin.DeviceId).Get(WebDeckPlugin.Code));
    }

    [Fact]
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
        Assert.Null(plugin.ApplyDeviceSettings(WebDeckPlugin.DeviceId, settings));
        Assert.True(plugin.Devices.Single().IsConnected);
        Assert.Equal(6, plugin.Devices.Single().Controls.Count);
        plugin.TryPushImage(WebDeckPlugin.DeviceId, "key.1", new byte[144 * 144 * 4], 144, 144, out _);

        var code = settings.Get(WebDeckPlugin.Code);
        using var http = new HttpClient();
        // The page opens without a code (it asks for it); the deck itself needs it.
        var page = await http.GetStringAsync(new Uri($"http://127.0.0.1:{port}/"));
        Assert.Contains("Pairing code", page, StringComparison.Ordinal);
        Assert.Contains("\"pin\":false", await http.GetStringAsync(new Uri($"http://127.0.0.1:{port}/info")), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(new Uri($"http://127.0.0.1:{port}/check?code=nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(new Uri($"http://127.0.0.1:{port}/check?code={code}"))).StatusCode);
        using (var refused = new ClientWebSocket())
        {
            await Assert.ThrowsAnyAsync<WebSocketException>(() => refused.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?code=nope"), CancellationToken.None));
        }

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?code={code}"), CancellationToken.None);
        var layout = JsonDocument.Parse(await ReceiveAsync(socket)).RootElement;
        var image = JsonDocument.Parse(await ReceiveAsync(socket)).RootElement;
        Assert.Equal(3, layout.GetProperty("columns").GetInt32());
        Assert.Equal("key.1", image.GetProperty("key").GetString());

        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"down\",\"key\":\"key.4\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"up\",\"key\":\"key.4\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (context.Inputs.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(
            [("key.4", InputCapabilities.Press), ("key.4", InputCapabilities.Release)],
            context.Inputs.Select(static input => (input.ControlId, input.Input)).ToArray());
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
