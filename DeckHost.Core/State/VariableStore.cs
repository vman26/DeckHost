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

        public void Dispose()
        {
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
