using DeckHost.Contracts.Interfaces;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace DeckHost.Core.State
{
    public sealed class VariableStore : IVariableStore, IDisposable
    {
        private readonly ConcurrentDictionary<string, BehaviorSubject<object>> _variables = new();

        public IObservable<T> Observe<T>(string key)
        {
            var subject = _variables.GetOrAdd(key, _ => new BehaviorSubject<object>(default!));
            return subject.AsObservable().OfType<T>();
        }

        public void Publish<T>(string key, T value)
        {
            if (_variables.TryGetValue(key, out var subject))
            {
                subject.OnNext(value!);
            }
        }

        public void Dispose()
        {
            foreach (var variable in _variables.Values)
            {
                if (variable is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _variables.Clear();
        }
    }
}