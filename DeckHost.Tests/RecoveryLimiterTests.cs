using DeckHost.App.Platform;

namespace DeckHost.Tests;

/// <summary>How many unexpected exceptions in a row DeckHost carries on after.</summary>
public sealed class RecoveryLimiterTests
{
    private const int Limit = 3;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static RecoveryLimiter LimiterThatAlreadyAllowedTheLimit()
    {
        var limiter = new RecoveryLimiter(Limit, Window);
        for (var index = 0; index < Limit; index++)
        {
            limiter.Allow(Start);
        }

        return limiter;
    }

    [Test]
    public void Allow_FirstException_IsAllowed()
    {
        // Arrange
        var limiter = new RecoveryLimiter(Limit, Window);

        // Act
        var allowed = limiter.Allow(Start);

        // Assert
        Assert.That(allowed, Is.True);
    }

    [Test]
    public void Allow_OneMoreThanTheLimitWithinTheWindow_IsRefused()
    {
        // Arrange
        var limiter = LimiterThatAlreadyAllowedTheLimit();

        // Act
        var allowed = limiter.Allow(Start);

        // Assert
        Assert.That(allowed, Is.False);
    }

    [Test]
    public void Allow_AfterTheWindowPassed_IsAllowedAgain()
    {
        // Arrange
        var limiter = LimiterThatAlreadyAllowedTheLimit();

        // Act
        var allowed = limiter.Allow(Start + Window + TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(allowed, Is.True);
    }
}
