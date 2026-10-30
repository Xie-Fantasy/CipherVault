namespace CipherVault.Core.Interfaces;

public interface IEventBus
{
    void Publish<T>(T message) where T : class;
    void Subscribe<T>(Action<T> handler) where T : class;
    void Unsubscribe<T>(Action<T> handler) where T : class;
}
