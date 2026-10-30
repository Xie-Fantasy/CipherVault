namespace CipherVault.Core.Interfaces;

public interface IClipboardGuard
{
    void CopyToClipboard(string text, bool autoClear = true);
    void ClearClipboard();
    event Action? ClipboardCleared;
    int AutoClearSeconds { get; set; }
}
