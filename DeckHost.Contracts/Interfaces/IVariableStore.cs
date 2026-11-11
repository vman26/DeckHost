using System;

namespace DeckHost.Contracts.Interfaces
{
    public interface IVariableStore
    {
        IObservable<T> Observe<T>(string key);

        void Publish<T>(string key, T value);

        bool TryGetValue<T>(string key, out T value);
    }
}
