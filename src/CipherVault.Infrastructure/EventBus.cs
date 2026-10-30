namespace CipherVault.Infrastructure;

using CipherVault.Core.Interfaces;
using System.Collections.Concurrent;

/// <summary>
/// Thread-safe in-memory event bus implementation.
/// Provides loose coupling between publishers and subscribers via typed messages.
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    public void Publish<T>(T message) where T : class
    {
        if (!_handlers.TryGetValue(typeof(T), out var handlers))
            return;

        // Snapshot the handler list to avoid issues if a handler unsubscribes during iteration
        List<Delegate> snapshot;
        lock (handlers)
        {
            snapshot = new List<Delegate>(handlers);
        }

        foreach (var handler in snapshot)
        {
            try
            {
                ((Action<T>)handler)(message);
            }
            catch (Exception ex)
            {
                // Log but don't propagate — one failing subscriber shouldn't break others
                System.Diagnostics.Debug.WriteLine(
                    $"EventBus: handler for {typeof(T).Name} threw: {ex.Message}");
            }
        }
    }

    public void Subscribe<T>(Action<T> handler) where T : class
    {
        var handlers = _handlers.GetOrAdd(typeof(T), _ => new List<Delegate>());
        lock (handlers)
        {
            handlers.Add(handler);
        }
    }

    public void Unsubscribe<T>(Action<T> handler) where T : class
    {
        if (!_handlers.TryGetValue(typeof(T), out var handlers))
            return;

        lock (handlers)
        {
            handlers.Remove(handler);
        }
    }
}
