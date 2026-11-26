using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.Elgato;

namespace DeckHost.Tests;

public sealed class StreamDeckNeoTests
{
    private const int NeoProductId = 0x009a;
    private const int NeoKeys = 8;
    private const int InfoWidth = 248;
    private const int InfoHeight = 58;

    private static StreamDeckModel Neo => StreamDeckModel.Find(NeoProductId)!;

    [Test]
    public void Controls_Neo_HasTheInfoScreenAndTwoTouchPointsWithLights()
    {
        // Arrange
        var model = Neo;

        // Act
        var controls = model.Controls();

        // Assert
        var screen = Expect.Single(controls, static control => control.Id == StreamDeckModel.InfoScreenId);
        Assert.That((screen.RenderWidth, screen.RenderHeight, screen.Type), Is.EqualTo((InfoWidth, InfoHeight, ControlType.Display)));
        var points = controls.Where(static control => control.Id.StartsWith("button.", StringComparison.Ordinal)).ToList();
        Assert.That(points.Select(static point => point.LedIndex), Is.EqualTo(new int?[] { 0, 1 }));
    }

    [Test]
    public void Parse_TouchPointDown_ReportsAPressOnTheButton()
    {
        // Arrange
        var model = Neo;
        var keysDown = new bool[NeoKeys + model.TouchPoints];
        var report = new byte[64];
        report[0] = 0x01;
        report[4 + NeoKeys + 1] = 1;

        // Act
        var events = StreamDeckProtocol.Parse(report, model, keysDown, new bool[1]);

        // Assert
        Assert.That(events, Is.EqualTo(new[] { new StreamDeckInput("button.1", InputCapabilities.Press, 1) }));
    }

    [Test]
    public void InfoScreenReports_AnImage_UseTheInfoScreenCommand()
    {
        // Arrange
        var jpeg = new byte[StreamDeckProtocol.ReportLength + 10];

        // Act
        var reports = StreamDeckProtocol.InfoScreenReports(jpeg).ToList();

        // Assert
        Assert.That(reports.Select(static report => (report[0], report[1], report[3])), Is.EqualTo(new[] { ((byte)0x02, (byte)0x0B, (byte)0), ((byte)0x02, (byte)0x0B, (byte)1) }));
    }

    [Test]
    public void TouchPointColorReport_SecondPoint_AddressesItAfterTheKeys()
    {
        // Arrange
        const byte Red = 10;
        const byte Green = 20;
        const byte Blue = 30;

        // Act
        var report = StreamDeckProtocol.TouchPointColorReport(Neo, 1, Red, Green, Blue);

        // Assert
        Assert.That(report[..6], Is.EqualTo(new byte[] { 0x03, 0x06, NeoKeys + 1, Red, Green, Blue }));
    }

    [Test]
    public void Describe_AModelNotCheckedOnHardware_SaysItIsAPreview()
    {
        // Arrange
        var model = Neo;

        // Act
        var device = model.Describe("elgato.test", isConnected: false, serial: null, firmware: null, detail: null);

        // Assert
        Assert.That(device.Limits!.Notes, Has.Some.StartsWith("Preview"));
    }

    [Test]
    public void NotYetSupported_StreamDeckStudio_IsRecognized()
    {
        // Arrange
        const int StudioProductId = 0x00aa;

        // Act
        var known = StreamDeckModel.NotYetSupported.ContainsKey(StudioProductId);

        // Assert
        Assert.That(known, Is.True);
    }
}
