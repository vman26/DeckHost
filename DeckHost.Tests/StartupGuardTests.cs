using DeckHost.App.Platform;

namespace DeckHost.Tests;

/// <summary>Safe mode after crashes at start: what the guard remembers between runs.</summary>
public sealed class StartupGuardTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan SoonAfterStart = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongAfterStart = TimeSpan.FromMinutes(10);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckhost-guard-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = Start;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private StartupGuard Guard() => new(_folder, () => _now);

    private StartupDecision RunThatNeverBecameHealthy()
    {
        var decision = Guard().BeginRun();
        _now += LongAfterStart;
        return decision;
    }

    [Test]
    public void BeginRun_FirstRun_IsNotSafeMode()
    {
        // Arrange
        var guard = Guard();

        // Act
        var decision = guard.BeginRun();

        // Assert
        Assert.That(decision.SafeMode, Is.False);
    }

    [Test]
    public void BeginRun_SafeModeArgument_IsSafeModeWithAReason()
    {
        // Arrange
        var guard = Guard();

        // Act
        var decision = guard.BeginRun(forceSafeMode: true);

        // Assert
        Assert.That(decision.SafeMode, Is.True);
        Assert.That(decision.Reason, Is.Not.Empty);
    }

    [Test]
    public void BeginRun_PreviousRunNeverBecameHealthyOnce_IsNotSafeModeYet()
    {
        // Arrange
        RunThatNeverBecameHealthy();

        // Act
        var decision = Guard().BeginRun();

        // Assert
        Assert.That(decision.SafeMode, Is.False);
        Assert.That(decision.UnhealthyRuns, Is.EqualTo(1));
    }

    [Test]
    public void BeginRun_PreviousRunsNeverBecameHealthyTwice_IsSafeMode()
    {
        // Arrange
        RunThatNeverBecameHealthy();
        RunThatNeverBecameHealthy();

        // Act
        var decision = Guard().BeginRun();

        // Assert
        Assert.That(decision.SafeMode, Is.True);
        Assert.That(decision.UnhealthyRuns, Is.EqualTo(StartupGuard.UnhealthyRunsForSafeMode));
    }

    [Test]
    public void BeginRun_PreviousRunCrashedSoonAfterStarting_IsSafeModeAtOnce()
    {
        // Arrange
        var crashed = Guard();
        crashed.BeginRun();
        _now += SoonAfterStart;
        crashed.MarkCrashed();

        // Act
        var decision = Guard().BeginRun();

        // Assert
        Assert.That(decision.SafeMode, Is.True);
    }

    [Test]
    public void BeginRun_PreviousRunCrashedLongAfterStarting_IsNotSafeModeYet()
    {
        // Arrange
        var crashed = Guard();
        crashed.BeginRun();
        _now += LongAfterStart;
        crashed.MarkCrashed();

        // Act
        var decision = Guard().BeginRun();

        // Assert
        Assert.That(decision.SafeMode, Is.False);
    }

    [Test]
    public void BeginRun_AfterAHealthyRun_ForgetsTheEarlierFailures()
    {
        // Arrange
        RunThatNeverBecameHealthy();
        var healthy = Guard();
        healthy.BeginRun();
        healthy.MarkHealthy();

        // Act
        var decision = Guard().BeginRun();

        // Assert
        Assert.That(decision.UnhealthyRuns, Is.Zero);
    }

    [Test]
    public void BeginRun_AfterACleanExit_DoesNotCountThatRunAsAFailure()
    {
        // Arrange
        var closed = Guard();
        closed.BeginRun();
        closed.MarkCleanExit();

        // Act
        var decision = Guard().BeginRun();

        // Assert
        Assert.That(decision.UnhealthyRuns, Is.Zero);
    }

    [Test]
    public void BeginRun_AfterClearFailures_IsNotSafeMode()
    {
        // Arrange
        RunThatNeverBecameHealthy();
        RunThatNeverBecameHealthy();
        Guard().ClearFailures();

        // Act
        var decision = Guard().BeginRun();

        // Assert
        Assert.That(decision.SafeMode, Is.False);
    }

    [Test]
    public void BeginRun_StateFileThatCannotBeRead_IsNotSafeMode()
    {
        // Arrange
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, StartupGuard.FileName), "{ not json");

        // Act
        var decision = Guard().BeginRun();

        // Assert
        Assert.That(decision.SafeMode, Is.False);
    }
}
