using System;
using System.Collections.Generic;

namespace DeckHost.App.Platform;

/// <summary>
/// Decides whether DeckHost may keep going after yet another unexpected exception: a few in a row are recovered from,
/// a storm of them (the same failure on every redraw) is not, so the app stops instead of spinning.
/// </summary>
internal sealed class RecoveryLimiter(int limit = RecoveryLimiter.DefaultLimit, TimeSpan? window = null)
{
    public const int DefaultLimit = 5;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(10);

    private readonly Queue<DateTimeOffset> _recent = new();
    private readonly TimeSpan _window = window ?? DefaultWindow;

    /// <summary>Records an exception at <paramref name="now"/>; false when there were more than the limit within the window.</summary>
    public bool Allow(DateTimeOffset now)
    {
        lock (_recent)
        {
            while (_recent.Count > 0 && now - _recent.Peek() > _window)
            {
                _recent.Dequeue();
            }

            _recent.Enqueue(now);
            return _recent.Count <= limit;
        }
    }
}
