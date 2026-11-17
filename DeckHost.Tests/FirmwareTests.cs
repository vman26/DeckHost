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
    private const string DownloadUrl = "https://example.test/fw.bin";
    private const string InsecureUrl = "http://example.test/fw.bin";
    private const string NewVersion = "2.0";
    private const string InstalledVersion = "1.5";
    private const int ZeroChecksum = 64;
    private static readonly byte[] FirmwareBody = "firmware"u8.ToArray();

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

    private static string ChecksumOfTheBody() => Convert.ToHexString(SHA256.HashData(FirmwareBody));

    private FirmwareService DownloadingService() => new([], IdleHost(), new HttpClient(new Handler(FirmwareBody)), _folder);

    private async Task<(FlashableDevice Device, DeviceHostService Host, VariableStore Store, FirmwareService Service, int PushesBefore, string File)> HostWithAFlashableDeviceAsync()
    {
        var device = new FlashableDevice();
        var store = new VariableStore();
        var host = new DeviceHostService([device], [], store);
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
        var file = Path.Combine(_folder, "fw.bin");
        File.WriteAllText(file, InstalledVersion);
        return (device, host, store, service, device.Pushes, file);
    }

    [Test]
    public async Task InstallAsync_FirmwareFile_Succeeds()
    {
        // Arrange
        var (_, host, store, service, _, file) = await HostWithAFlashableDeviceAsync();
        await using var _ = host;
        using var __ = store;

        // Act
        var result = await service.InstallAsync(FlashableDevice.DeviceId, new FirmwareImage(file));

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task GetInfo_AfterInstallingAFile_ReportsTheNewVersion()
    {
        // Arrange
        var (_, host, store, service, _, file) = await HostWithAFlashableDeviceAsync();
        await using var _ = host;
        using var __ = store;

        // Act
        await service.InstallAsync(FlashableDevice.DeviceId, new FirmwareImage(file));

        // Assert
        Assert.That(service.GetInfo(FlashableDevice.DeviceId)!.Version, Is.EqualTo(InstalledVersion));
    }

    [Test]
    public async Task InstallAsync_WhileTheDeviceIsWritten_PausesTheDeviceInTheHost()
    {
        // Arrange
        var (device, host, store, service, _, file) = await HostWithAFlashableDeviceAsync();
        await using var _ = host;
        using var __ = store;

        // Act
        await service.InstallAsync(FlashableDevice.DeviceId, new FirmwareImage(file));
        await host.WhenIdleAsync();

        // Assert
        Assert.That(device.PushesWhileWriting, Is.EqualTo(0));
        Assert.That(host.DeviceStatistics.SingleOrDefault()?.Inputs ?? 0, Is.EqualTo(0));
    }

    [Test]
    public async Task InstallAsync_AfterTheWrite_RedrawsTheDevice()
    {
        // Arrange
        var (device, host, store, service, pushesBefore, file) = await HostWithAFlashableDeviceAsync();
        await using var _ = host;
        using var __ = store;

        // Act
        await service.InstallAsync(FlashableDevice.DeviceId, new FirmwareImage(file));
        await host.WhenIdleAsync();

        // Assert
        Assert.That(device.Pushes, Is.GreaterThan(pushesBefore));
    }

    [Test]
    public async Task DownloadAsync_HttpsWithTheRightChecksum_KeepsTheVersion()
    {
        // Arrange
        var service = DownloadingService();

        // Act
        var image = await service.DownloadAsync(new FirmwareRelease(NewVersion, new Uri(DownloadUrl), ChecksumOfTheBody().ToLowerInvariant()));

        // Assert
        Assert.That(image.Version, Is.EqualTo(NewVersion));
    }

    [Test]
    public async Task DownloadAsync_HttpsWithTheRightChecksum_SavesTheFile()
    {
        // Arrange
        var service = DownloadingService();

        // Act
        var image = await service.DownloadAsync(new FirmwareRelease(NewVersion, new Uri(DownloadUrl), ChecksumOfTheBody()));

        // Assert
        Assert.That(File.ReadAllBytes(image.Path), Is.EqualTo(FirmwareBody));
    }

    [Test]
    public void DownloadAsync_NoChecksum_IsRefused()
    {
        // Arrange
        var service = DownloadingService();

        // Act
        var download = () => service.DownloadAsync(new FirmwareRelease(NewVersion, new Uri(DownloadUrl), null));

        // Assert
        Assert.ThrowsAsync<FirmwareException>(() => download());
    }

    [Test]
    public void DownloadAsync_PlainHttp_IsRefused()
    {
        // Arrange
        var service = DownloadingService();

        // Act
        var download = () => service.DownloadAsync(new FirmwareRelease(NewVersion, new Uri(InsecureUrl), ChecksumOfTheBody()));

        // Assert
        Assert.ThrowsAsync<FirmwareException>(() => download());
    }

    [Test]
    public void DownloadAsync_WrongChecksum_IsRefusedWithAChecksumMessage()
    {
        // Arrange
        var service = DownloadingService();

        // Act
        var download = () => service.DownloadAsync(new FirmwareRelease("2.1", new Uri(DownloadUrl), new string('0', ZeroChecksum)));

        // Assert
        var mismatch = Assert.ThrowsAsync<FirmwareException>(() => download());
        Assert.That(mismatch.Message, Does.Contain("checksum"));
    }

    [Test]
    public async Task DownloadAsync_WrongChecksum_DeletesTheBadDownload()
    {
        // Arrange
        var service = DownloadingService();
        await service.DownloadAsync(new FirmwareRelease(NewVersion, new Uri(DownloadUrl), ChecksumOfTheBody()));

        // Act
        Assert.ThrowsAsync<FirmwareException>(() => service.DownloadAsync(new FirmwareRelease("2.1", new Uri(DownloadUrl), new string('0', ZeroChecksum))));

        // Assert
        Assert.That(Directory.GetFiles(_folder), Has.Exactly(1).Items);
    }

    private FirmwareService ServiceOfAFlashableAndAPlainDevice() => new([new FlashableDevice(), new FakeDevicePlugin()], IdleHost(), downloadDirectory: _folder);

    [Test]
    public void Supports_DeviceWhosePluginInstallsFirmware_IsTrue()
    {
        // Arrange
        var service = ServiceOfAFlashableAndAPlainDevice();

        // Act
        var supported = service.Supports(FlashableDevice.DeviceId);

        // Assert
        Assert.That(supported, Is.True);
    }

    [Test]
    public void Supports_DeviceWhosePluginCannotInstall_IsFalse()
    {
        // Arrange
        var service = ServiceOfAFlashableAndAPlainDevice();

        // Act
        var supported = service.Supports(FakeDevicePlugin.DeviceId);

        // Assert
        Assert.That(supported, Is.False);
    }

    [Test]
    public void InstallAsync_DeviceWhosePluginCannotInstall_IsRefused()
    {
        // Arrange
        var service = ServiceOfAFlashableAndAPlainDevice();

        // Act
        var install = () => service.InstallAsync(FakeDevicePlugin.DeviceId, new FirmwareImage("x"));

        // Assert
        Assert.ThrowsAsync<FirmwareException>(() => install());
    }

    [Test]
    public void InstallAsync_MissingFile_IsRefused()
    {
        // Arrange
        var service = ServiceOfAFlashableAndAPlainDevice();

        // Act
        var install = () => service.InstallAsync(FlashableDevice.DeviceId, new FirmwareImage(Path.Combine(_folder, "missing.bin")));

        // Assert
        Assert.ThrowsAsync<FirmwareException>(() => install());
    }

    [Test]
    public async Task CheckAsync_DeviceWhosePluginKnowsANewerRelease_ReportsIt()
    {
        // Arrange
        var service = ServiceOfAFlashableAndAPlainDevice();

        // Act
        var release = await service.CheckAsync(FlashableDevice.DeviceId);

        // Assert
        Assert.That(release!.Version, Is.EqualTo(NewVersion));
    }

    private static byte[] RequestWithVersion(StreamDeckModel model, string version, int offset)
    {
        var report = StreamDeckProtocol.FirmwareRequest(model);
        System.Text.Encoding.ASCII.GetBytes(version).CopyTo(report.AsSpan(offset));
        return report;
    }

    [Test]
    public void FirmwareRequest_SecondGenerationDeck_IsAThirtyTwoByteFeatureReportFive()
    {
        // Arrange
        var mk2 = StreamDeckModel.Find(0x0080)!;

        // Act
        var report = StreamDeckProtocol.FirmwareRequest(mk2);

        // Assert
        Assert.That((report[0], report.Length), Is.EqualTo((0x05, 32)));
    }

    [Test]
    public void FirmwareVersion_SecondGenerationReport_ReadsTheVersionText()
    {
        // Arrange
        var mk2 = StreamDeckModel.Find(0x0080)!;
        var report = RequestWithVersion(mk2, "1.01.000", 6);

        // Act
        var version = StreamDeckProtocol.FirmwareVersion(mk2, report);

        // Assert
        Assert.That(version, Is.EqualTo("1.01.000"));
    }

    [Test]
    public void FirmwareRequest_FirstGenerationDeck_IsASeventeenByteFeatureReportFour()
    {
        // Arrange
        var mini = StreamDeckModel.Find(0x0063)!;

        // Act
        var report = StreamDeckProtocol.FirmwareRequest(mini);

        // Assert
        Assert.That((report[0], report.Length), Is.EqualTo((0x04, 17)));
    }

    [Test]
    public void FirmwareVersion_FirstGenerationReport_ReadsTheVersionText()
    {
        // Arrange
        var mini = StreamDeckModel.Find(0x0063)!;
        var report = RequestWithVersion(mini, "2.03", 5);

        // Act
        var version = StreamDeckProtocol.FirmwareVersion(mini, report);

        // Assert
        Assert.That(version, Is.EqualTo("2.03"));
    }

    [Test]
    public void FirmwareVersion_EmptyReport_IsNull()
    {
        // Arrange
        var mini = StreamDeckModel.Find(0x0063)!;

        // Act
        var version = StreamDeckProtocol.FirmwareVersion(mini, new byte[17]);

        // Assert
        Assert.That(version, Is.Null);
    }

    private static DeviceHostService IdleHost() => new([], [], new VariableStore());
}
