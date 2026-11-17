using DeckHost.App.Platform;
using DeckHost.Core.Devices;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>CPU and memory are published only while something wants them: a background that follows them, or the Variables page.</summary>
public sealed class SystemDataPublisherTests : IAsyncDisposable
{
    private const int WaitSeconds = 5;
    private const int PollMilliseconds = 25;

    private readonly VariableStore _store = new();
    private readonly DeviceHostService _host;
    private readonly SystemDataPublisher _publisher;

    public SystemDataPublisherTests()
    {
        _host = new DeviceHostService([new FakeDevicePlugin()], [], _store);
        _publisher = new SystemDataPublisher(_host);
    }

    public async ValueTask DisposeAsync()
    {
        _publisher.Dispose();
        await _host.DisposeAsync();
        _store.Dispose();
    }

    private async Task<bool> WaitFor(string key)
    {
        var end = DateTime.UtcNow.AddSeconds(WaitSeconds);
        while (DateTime.UtcNow < end)
        {
            await _host.WhenIdleAsync();
            if (_store.TryGetValue<string>(key, out _))
            {
                return true;
            }

            await Task.Delay(PollMilliseconds);
        }

        return false;
    }

    [Test]
    public async Task WantWhileShown_ThePageIsShown_PublishesMemory()
    {
        // Arrange
        Assume.That(SystemMetrics.Memory(), Is.Not.Null, "this system can't read its memory");

        // Act
        _publisher.WantWhileShown(true);

        // Assert
        Assert.That(await WaitFor(SystemDataPublisher.MemoryKey), Is.True);
    }

    [Test]
    public void IsRunning_NothingWantsTheValues_IsFalse()
    {
        // Act
        var running = _publisher.IsRunning;

        // Assert
        Assert.That(running, Is.False);
    }

    [Test]
    public void IsRunning_ThePageIsShown_IsTrueUntilItIsClosed()
    {
        // Arrange
        _publisher.WantWhileShown(true);
        var whileShown = _publisher.IsRunning;

        // Act
        _publisher.WantWhileShown(false);

        // Assert
        Assert.That(whileShown, Is.True);
        Assert.That(_publisher.IsRunning, Is.False);
    }

    [Test]
    public void IsRunning_ThePageIsClosedButABackgroundFollowsTheCpu_StaysTrue()
    {
        // Arrange
        _publisher.Want([SystemDataPublisher.CpuKey]);
        _publisher.WantWhileShown(true);

        // Act
        _publisher.WantWhileShown(false);

        // Assert
        Assert.That(_publisher.IsRunning, Is.True);
    }
}
