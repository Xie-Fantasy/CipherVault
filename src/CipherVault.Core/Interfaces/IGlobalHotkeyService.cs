namespace CipherVault.Core.Interfaces;

public interface IGlobalHotkeyService : IDisposable
{
    event Action? HotkeyPressed;
    void Register(string hotkey);
    void Unregister();
    bool IsRegistered { get; }
}
