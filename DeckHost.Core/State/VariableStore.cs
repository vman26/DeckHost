using DeckHost.Contracts.Interfaces;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace DeckHost.Core.State
{
    /// <summary>
    /// Thread-safe key/value store with change notifications. Values are boxed; keep it for
    /// state and telemetry, not per-pixel data.
    /// </summary>
    public sealed class VariableStore : IVariableStore, IDisposable
    {
        private readonly ConcurrentDictionary<string, BehaviorSubject<object?>> _variables = new(StringComparer.Ordinal);
        private readonly Subject<KeyValuePair<string, object?>> _changes = new();

        /// <summary>Every value published, with its key (what the workflow event triggers listen to).</summary>
        public IObservable<KeyValuePair<string, object?>> Changes => _changes.AsObservable();

        public IObservable<T> Observe<T>(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return GetSubject(key, null).AsObservable().OfType<T>();
        }

        public void Publish<T>(string key, T value)
        {
            ArgumentNullException.ThrowIfNull(key);

            // GetOrAdd + OnNext on the same subject: a concurrent Observe can never end up
            // subscribed to a subject that a racing Publish then replaces.
            var subject = GetSubject(key, value);
            subject.OnNext(value);
            _changes.OnNext(new KeyValuePair<string, object?>(key, value));
        }

        public bool TryGetValue<T>(string key, out T value)
        {
            if (_variables.TryGetValue(key, out var subject) && subject.Value is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default!;
            return false;
        }

        /// <summary>The keys starting with <paramref name="prefix"/> and their values (for a plugin in its own process).</summary>
        public IReadOnlyList<KeyValuePair<string, object?>> Snapshot(string prefix) =>
            [.. _variables.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(static pair => new KeyValuePair<string, object?>(pair.Key, pair.Value.Value))];

        public void Dispose()
        {
            _changes.OnCompleted();
            _changes.Dispose();
            foreach (var variable in _variables.Values)
            {
                variable.OnCompleted();
                variable.Dispose();
            }

            _variables.Clear();
        }

        private BehaviorSubject<object?> GetSubject(string key, object? initialValue) =>
            _variables.GetOrAdd(key, static (_, initial) => new BehaviorSubject<object?>(initial), initialValue);
    }
}
