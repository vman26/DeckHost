using System.Net;
using System.Security.Cryptography;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Firmware;
using DeckHost.Core.State;
using DeckHost.Device.Elgato;

namespace DeckHost.Tests;

public sealed class FirmwareTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckhost-firmware-" + Guid.NewGuid().ToString("N"));

    public FirmwareTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>A device whose plugin installs firmware, and checks the host left it alone meanwhile.</summary>
    private sealed class FlashableDevice : IDevicePlugin, IDeviceOutput, IDeviceFirmware
    {
        public const string DeviceId = "flash.device";

        public string Version { get; private set; } = "1.0";

        public int Pushes;

        public int PushesWhileWriting;

        public bool Writing;

        public Func<Task>? DuringWrite { get; set; }

        public event EventHandler? DevicesChanged { add { } remove { } }

        public string Id => "flash.plugin";

        public string Name => "Flashable";

        public IReadOnlyCollection<DeviceDefinition> Devices { get; } =
            [new DeviceDefinition(DeviceId, "Flashable", [new PhysicalControl("key.0", "Key 1", ControlType.Key, new Rect(0, 0, 10, 10), InputCapabilities.Press, OutputCapabilities.ColorImage, 8, 8)], IsConnected: true)];

        public IDeviceOutput Output => this;

        public IDevicePluginContext? Context { get; private set; }

        public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
        {
            Context = context;
            return Task.CompletedTask;
        }

        public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
        {
            Interlocked.Increment(ref Pushes);
            if (Writing)
            {
                Interlocked.Increment(ref PushesWhileWriting);
            }

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

        public DeviceFirmwareInfo? GetFirmwareInfo(string deviceId) => new(Version, CanUpdate: true) { FileExtensions = [".bin"] };

        public Task<FirmwareRelease?> CheckForFirmwareAsync(string deviceId, CancellationToken cancellationToken) =>
            Task.FromResult<FirmwareRelease?>(new FirmwareRelease("2.0", new Uri("https://example.test/fw-2.0.bin"), null));

        public async Task<FirmwareUpdateResult> InstallFirmwareAsync(string deviceId, FirmwareImage image, IProgress<FirmwareProgress> progress, CancellationToken cancellationToken)
        {
            Writing = true;
            progress.Report(new FirmwareProgress(0.5, "Writing"));
            if (DuringWrite is { } during)
            {
                await during();
            }

            Writing = false;
            Version = image.Version ?? File.ReadAllText(image.Path);
            return new FirmwareUpdateResult(true, $"Now running {Version}.");
        }
    }

    private sealed class Handler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    [Fact]
    public async Task Installing_pauses_the_device_in_the_host_and_redraws_it_afterwards()
    {
        var device = new FlashableDevice();
        var store = new VariableStore();
        await using var host = new DeviceHostService([device], [], store);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        var service = new FirmwareService([device], host, downloadDirectory: _folder);
        device.DuringWrite = async () =>
        {
            // The host is asked to draw and the device reports a press while it's being written.
            host.Redraw(FlashableDevice.DeviceId);
            device.Context!.ReportInputEvent(FlashableDevice.DeviceId, "key.0", InputCapabilities.Press, 1);
            await host.WhenIdleAsync();
        };
        var before = device.Pushes;
        var file = Path.Combine(_folder, "fw.bin");
        File.WriteAllText(file, "1.5");

        var result = await service.InstallAsync(FlashableDevice.DeviceId, new FirmwareImage(file));
        await host.WhenIdleAsync();

        Assert.True(result.Success);
        Assert.Equal("1.5", service.GetInfo(FlashableDevice.DeviceId)!.Version);
        Assert.Equal(0, device.PushesWhileWriting);
        Assert.Equal(0, host.DeviceStatistics.SingleOrDefault()?.Inputs ?? 0);
        Assert.True(device.Pushes > before);    // redrawn after resuming
        store.Dispose();
    }

    [Fact]
    public async Task Downloads_must_be_https_and_match_their_checksum()
    {
        var body = "firmware"u8.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(body));
        var service = new FirmwareService([], IdleHost(), new HttpClient(new Handler(body)), _folder);

        var image = await service.DownloadAsync(new FirmwareRelease("2.0", new Uri("https://example.test/fw.bin"), sha.ToLowerInvariant()));
        Assert.Equal("2.0", image.Version);
        Assert.Equal(body, File.ReadAllBytes(image.Path));

        await Assert.ThrowsAsync<FirmwareException>(() => service.DownloadAsync(new FirmwareRelease("2.0", new Uri("https://example.test/fw.bin"), null)));
        await Assert.ThrowsAsync<FirmwareException>(() => service.DownloadAsync(new FirmwareRelease("2.0", new Uri("http://example.test/fw.bin"), sha)));
        var mismatch = await Assert.ThrowsAsync<FirmwareException>(() => service.DownloadAsync(new FirmwareRelease("2.1", new Uri("https://example.test/fw.bin"), new string('0', 64))));
        Assert.Contains("checksum", mismatch.Message);
        Assert.Single(Directory.GetFiles(_folder));    // the bad download was deleted
    }

    [Fact]
    public async Task Devices_whose_plugin_cant_install_or_that_are_unplugged_are_refused()
    {
        var device = new FlashableDevice();
        var service = new FirmwareService([device, new FakeDevicePlugin()], IdleHost(), downloadDirectory: _folder);

        Assert.True(service.Supports(FlashableDevice.DeviceId));
        Assert.False(service.Supports(FakeDevicePlugin.DeviceId));
        await Assert.ThrowsAsync<FirmwareException>(() => service.InstallAsync(FakeDevicePlugin.DeviceId, new FirmwareImage("x")));
        await Assert.ThrowsAsync<FirmwareException>(() => service.InstallAsync(FlashableDevice.DeviceId, new FirmwareImage(Path.Combine(_folder, "missing.bin"))));
        Assert.Equal("2.0", (await service.CheckAsync(FlashableDevice.DeviceId))!.Version);
    }

    [Fact]
    public void Stream_Decks_report_their_firmware_version_in_a_feature_report()
    {
        var mk2 = StreamDeckModel.Find(0x0080)!;
        var report = StreamDeckProtocol.FirmwareRequest(mk2);
        Assert.Equal((0x05, 32), (report[0], report.Length));
        "1.01.000"u8.CopyTo(report.AsSpan(6));
        Assert.Equal("1.01.000", StreamDeckProtocol.FirmwareVersion(mk2, report));

        var mini = StreamDeckModel.Find(0x0063)!;
        var old = StreamDeckProtocol.FirmwareRequest(mini);
        Assert.Equal((0x04, 17), (old[0], old.Length));
        "2.03"u8.CopyTo(old.AsSpan(5));
        Assert.Equal("2.03", StreamDeckProtocol.FirmwareVersion(mini, old));
        Assert.Null(StreamDeckProtocol.FirmwareVersion(mini, new byte[17]));
    }

    private static DeviceHostService IdleHost() => new([], [], new VariableStore());
}
