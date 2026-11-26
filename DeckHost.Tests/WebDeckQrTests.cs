using DeckHost.Device.Web;

namespace DeckHost.Tests;

public sealed class WebDeckQrTests
{
    [Test]
    public void QrSvg_ALink_IsASquareSvgWithModules()
    {
        // Arrange
        const string Link = "http://192.168.1.20:8765/?code=K7QM-3XRD";

        // Act
        var svg = WebDeckPlugin.QrSvg(Link);

        // Assert
        Assert.That(svg, Does.StartWith("<svg").And.Contains("h1v1h-1z").And.EndWith("</svg>"));
    }
}

public sealed class WebDeckQrActionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"deckhost-qr-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void RunSettingAction_QrCodeWhileOnlyThisComputerCanConnect_SaysToShareFirst()
    {
        // Arrange
        using var plugin = new WebDeckPlugin(_directory);

        // Act
        var message = plugin.RunSettingAction(WebDeckPlugin.DeviceId, WebDeckPlugin.QrCodeAction);

        // Assert
        Assert.That(message, Does.Contain("Phones and tablets on this network"));
    }
}
