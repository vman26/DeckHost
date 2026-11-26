using DeckHost.App.Platform;

namespace DeckHost.Tests;

/// <summary>The welcome tour's help when no device shows up: the udev rule on Linux, a vendor app holding the device elsewhere.</summary>
public sealed class DeviceSetupHelpTests
{
    private const string AppImageFile = "/home/someone/Apps/DeckHost-1.0.0-linux-x86_64.AppImage";

    [Test]
    public void Advise_AppImageWithDevicePluggedInAndNoRule_SaysTheRuleIsMissing()
    {
        // Arrange
        var facts = new DeviceSetupFacts(HelpSystem.Linux, AppImageFile, RuleInstalled: false, SupportedDevicePlugged: true);

        // Act
        var advice = DeviceSetupHelp.Advise(facts);

        // Assert
        Assert.That(advice.Text, Does.Contain("the device rule is missing"));
        Assert.That(advice.IsProblem, Is.True);
    }

    [Test]
    public void Advise_AppImage_DoesNotPointToInstallScript()
    {
        // Arrange
        var facts = new DeviceSetupFacts(HelpSystem.Linux, AppImageFile, RuleInstalled: false);

        // Act
        var advice = DeviceSetupHelp.Advise(facts);

        // Assert
        Assert.That(advice.Text, Does.Not.Contain("install.sh"));
        Assert.That(advice.DocsPath, Is.EqualTo(DeviceSetupHelp.AppImageDocsPath));
    }

    [Test]
    public void Advise_AppImage_GivesCommandsThatExtractTheRuleFromThatFile()
    {
        // Arrange
        var facts = new DeviceSetupFacts(HelpSystem.Linux, AppImageFile, RuleInstalled: false);

        // Act
        var advice = DeviceSetupHelp.Advise(facts);

        // Assert
        Assert.That(advice.Commands, Does.Contain($"'{AppImageFile}' --appimage-extract usr/bin/{DeviceSetupHelp.RuleFileName}"));
        Assert.That(advice.Commands, Does.Contain("/etc/udev/rules.d/"));
    }

    [Test]
    public void Advise_LinuxDownloadWithoutRule_PointsToInstallScript()
    {
        // Arrange
        var facts = new DeviceSetupFacts(HelpSystem.Linux, RuleInstalled: false);

        // Act
        var advice = DeviceSetupHelp.Advise(facts);

        // Assert
        Assert.That(advice.Text, Does.Contain("./install.sh"));
    }

    [Test]
    public void Advise_RuleInstalled_HasNoCommands()
    {
        // Arrange
        var facts = new DeviceSetupFacts(HelpSystem.Linux, AppImageFile, RuleInstalled: true);

        // Act
        var advice = DeviceSetupHelp.Advise(facts);

        // Assert
        Assert.That(advice.Commands, Is.Null);
        Assert.That(advice.IsProblem, Is.False);
    }

    [Test]
    public void Advise_StreamDeckAppRunningOnWindows_SaysItHoldsTheDevice()
    {
        // Arrange
        var facts = new DeviceSetupFacts(HelpSystem.Windows, HoldingApps: DeviceSetupHelp.HoldingApps(["explorer", "StreamDeck"]));

        // Act
        var advice = DeviceSetupHelp.Advise(facts);

        // Assert
        Assert.That(advice.Text, Does.StartWith("The Elgato Stream Deck app is running and holds the device"));
        Assert.That(advice.IsProblem, Is.True);
    }

    [Test]
    public void Advise_NothingFoundOnMacOS_GivesTheGeneralHelp()
    {
        // Arrange
        var facts = new DeviceSetupFacts(HelpSystem.MacOS, HoldingApps: []);

        // Act
        var advice = DeviceSetupHelp.Advise(facts);

        // Assert
        Assert.That(advice.IsProblem, Is.False);
        Assert.That(advice.Text, Does.Contain("Plug the device in directly"));
    }

    [TestCase("Stream Deck")]
    [TestCase("StreamDeck")]
    [TestCase("StreamDock")]
    [TestCase("MiraBox")]
    public void HoldingApps_VendorProcess_IsFound(string processName)
    {
        // Act
        var apps = DeviceSetupHelp.HoldingApps([processName]);

        // Assert
        Assert.That(apps, Has.Count.EqualTo(1));
    }

    [Test]
    public void HoldingApps_DeckHostItself_IsNotFound()
    {
        // Act
        var apps = DeviceSetupHelp.HoldingApps(["DeckHost", "DeckHost.App"]);

        // Assert
        Assert.That(apps, Is.Empty);
    }

    [TestCase("5548\n", true)]
    [TestCase("0FD9", true)]
    [TestCase("6603", true)]
    [TestCase("046d", false)]
    [TestCase(null, false)]
    public void IsSupportedVendor_UsbVendorId_MatchesTheRule(string? vendorId, bool supported)
    {
        // Act
        var result = DeviceSetupHelp.IsSupportedVendor(vendorId);

        // Assert
        Assert.That(result, Is.EqualTo(supported));
    }

    [Test]
    public void ShellQuote_PathWithAQuote_StaysOneArgument()
    {
        // Arrange
        const string Path = "/home/o'brien/DeckHost.AppImage";

        // Act
        var quoted = DeviceSetupHelp.ShellQuote(Path);

        // Assert
        Assert.That(quoted, Is.EqualTo("'/home/o'\\''brien/DeckHost.AppImage'"));
    }

    [Test]
    public void SupportedVendorIds_EveryVendorInThePackagedRule_IsKnown()
    {
        // Arrange
        var rule = File.ReadAllText(Path.Combine(RepositoryRoot(), "packaging", "linux", DeviceSetupHelp.RuleFileName));
        var vendors = System.Text.RegularExpressions.Regex.Matches(rule, "idVendor}?==\"([0-9a-fA-F]{4})\"").Select(static match => match.Groups[1].Value.ToLowerInvariant()).Distinct();

        // Act
        var unknown = vendors.Where(static vendor => !DeviceSetupHelp.IsSupportedVendor(vendor));

        // Assert
        Assert.That(unknown, Is.Empty);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeckHost.slnx")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }
}
