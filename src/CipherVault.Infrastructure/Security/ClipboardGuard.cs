using CipherVault.Core.Interfaces;

namespace CipherVault.Infrastructure.Security;

using System.Security.Cryptography;
using System.Text;

public sealed class ClipboardGuard : IClipboardGuard, IDisposable
{
    private readonly Action<string> _setClipboard;
    private readonly Action _clearClipboard;
    private readonly Func<string?>? _getClipboard;
    private CancellationTokenSource? _clearCts;
    private byte[]? _ownedContentHash;
    private readonly object _lock = new();

    public int AutoClearSeconds { get; set; } = 15;
    public event Action? ClipboardCleared;

    public ClipboardGuard(Action<string> setClipboard, Action clearClipboard, Func<string?>? getClipboard = null)
    {
        _setClipboard = setClipboard;
        _clearClipboard = clearClipboard;
        _getClipboard = getClipboard;
    }

    public void CopyToClipboard(string text, bool autoClear = true)
    {
        _setClipboard(text);

        lock (_lock)
        {
            ZeroOwnedContentHash();
            _ownedContentHash = HashText(text);
        }

        if (autoClear && AutoClearSeconds > 0)
        {
            ScheduleAutoClear();
        }
    }

    public void ClearClipboard()
    {
        _clearClipboard();
        lock (_lock)
            ZeroOwnedContentHash();
        ClipboardCleared?.Invoke();
    }

    private void ScheduleAutoClear()
    {
        lock (_lock)
        {
            _clearCts?.Cancel();
            _clearCts?.Dispose();
            _clearCts = new CancellationTokenSource();
            var token = _clearCts.Token;

            Task.Delay(TimeSpan.FromSeconds(AutoClearSeconds), token)
                .ContinueWith(_ =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        ClearClipboardIfStillOwned();
                    }
                }, TaskScheduler.Default);
        }
    }

    private void ClearClipboardIfStillOwned()
    {
        byte[]? expected;
        lock (_lock)
            expected = _ownedContentHash is null ? null : (byte[])_ownedContentHash.Clone();

        if (expected == null)
            return;

        try
        {
            string? currentText = _getClipboard?.Invoke();
            if (_getClipboard != null && currentText == null)
                return;

            if (currentText != null)
            {
                byte[] actual = HashText(currentText);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                        return;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(actual);
                }
            }

            ClearClipboard();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    private static byte[] HashText(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            return SHA256.HashData(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private void ZeroOwnedContentHash()
    {
        if (_ownedContentHash != null)
            CryptographicOperations.ZeroMemory(_ownedContentHash);
        _ownedContentHash = null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _clearCts?.Cancel();
            _clearCts?.Dispose();
            _clearCts = null;
            ZeroOwnedContentHash();
        }
    }
}
