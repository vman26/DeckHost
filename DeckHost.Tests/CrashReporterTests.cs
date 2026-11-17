using DeckHost.App.Platform;

namespace DeckHost.Tests;

/// <summary>The safety net: an unexpected exception is recovered from with a report, until it happens in a storm.</summary>
public sealed class CrashReporterTests : IDisposable
{
    private const int Limit = 2;

    private readonly string _original = CrashReporter.CrashDirectory;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckhost-crashes-" + Guid.NewGuid().ToString("N"));
    private readonly List<Exception> _recovered = [];

    public CrashReporterTests()
    {
        CrashReporter.CrashDirectory = _folder;
        CrashReporter.Limiter = new RecoveryLimiter(Limit);
        CrashReporter.Recovered += _recovered.Add;
    }

    public void Dispose()
    {
        CrashReporter.Recovered -= _recovered.Add;
        CrashReporter.CrashDirectory = _original;
        CrashReporter.Limiter = new RecoveryLimiter();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static Exception Failure() => new InvalidOperationException("something nobody caught");

    [Test]
    public void Recover_UnexpectedException_ReturnsTrueSoDeckHostCarriesOn()
    {
        // Arrange
        var exception = Failure();

        // Act
        var recovered = CrashReporter.Recover(exception);

        // Assert
        Assert.That(recovered, Is.True);
    }

    [Test]
    public void Recover_UnexpectedException_WritesOneReportThatSaysItCarriedOn()
    {
        // Arrange
        var exception = Failure();

        // Act
        CrashReporter.Recover(exception);

        // Assert
        var report = File.ReadAllText(Expect.Single(Directory.GetFiles(_folder)));
        Assert.That(report, Does.Contain("carried on").And.Contain("something nobody caught"));
    }

    [Test]
    public void Recover_UnexpectedException_RaisesRecoveredOnce()
    {
        // Arrange
        var exception = Failure();

        // Act
        CrashReporter.Recover(exception);

        // Assert
        Assert.That(_recovered, Is.EqualTo(new[] { exception }));
    }

    [Test]
    public void Recover_MoreExceptionsThanTheLimitInARow_ReturnsFalse()
    {
        // Arrange
        CrashReporter.Recover(Failure());
        CrashReporter.Recover(Failure());

        // Act
        var recovered = CrashReporter.Recover(Failure());

        // Assert
        Assert.That(recovered, Is.False);
    }
}
