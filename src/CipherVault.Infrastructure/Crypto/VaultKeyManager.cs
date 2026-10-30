namespace CipherVault.Infrastructure.Crypto;

using System.Runtime.InteropServices;
using CipherVault.Core.Interfaces;

/// <summary>
/// Manages the lifecycle of a vault encryption key in memory.
/// Stores the key securely using <see cref="ISecureMemoryManager"/> and provides
/// controlled access. Implements <see cref="IDisposable"/> to ensure the key is
/// securely destroyed when no longer needed.
/// </summary>
public sealed class VaultKeyManager : IDisposable
{
    private readonly ISecureMemoryManager _secureMemory;
    private IntPtr _securePointer;
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether the vault key is currently available (unlocked).
    /// </summary>
    public bool IsUnlocked => _securePointer != IntPtr.Zero;

    /// <summary>
    /// Initializes a new instance of <see cref="VaultKeyManager"/>.
    /// </summary>
    /// <param name="secureMemory">The secure memory manager used to lock/unlock memory pages.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secureMemory"/> is null.</exception>
    public VaultKeyManager(ISecureMemoryManager secureMemory)
    {
        ArgumentNullException.ThrowIfNull(secureMemory);
        _secureMemory = secureMemory;
    }

    /// <summary>
    /// Stores a vault key in memory. The key is copied into a new array and the
    /// original array is zeroed. Memory is locked via <see cref="ISecureMemoryManager"/>
    /// to prevent swapping to disk.
    /// </summary>
    /// <param name="key">The 256-bit key to store. Must be 32 bytes.</param>
    /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not 32 bytes.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a key is already stored (vault is already unlocked).</exception>
    public void StoreKey(byte[] key)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != 32)
            throw new ArgumentException("Vault key must be exactly 32 bytes (256 bits).", nameof(key));

        if (_securePointer != IntPtr.Zero)
            throw new InvalidOperationException("A vault key is already stored. Destroy it before storing a new one.");

        IntPtr pointer = IntPtr.Zero;
        try
        {
            // Keep the only long-lived copy in locked unmanaged memory.
            pointer = _secureMemory.AllocateSecureMemory(32);
            Marshal.Copy(key, 0, pointer, 32);
            _securePointer = pointer;
        }
        catch
        {
            if (pointer != IntPtr.Zero)
                _secureMemory.FreeSecureMemory(pointer);
            throw;
        }
        finally
        {
            // Zero the caller's copy even when allocation fails.
            SecureStringHelper.SecureZeroArray(key);
        }
    }

    /// <summary>
    /// Gets the currently stored vault key.
    /// </summary>
    /// <returns>A temporary copy of the 256-bit vault key. The caller must clear it after use.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown if no key is stored (vault is locked).</exception>
    public byte[] GetKey()
    {
        ThrowIfDisposed();

        if (_securePointer == IntPtr.Zero)
            throw new InvalidOperationException("Vault is locked. No key is available.");

        var key = new byte[32];
        Marshal.Copy(_securePointer, key, 0, key.Length);
        return key;
    }

    /// <summary>Executes an operation with a temporary key copy and clears that copy afterwards.</summary>
    public TResult UseKey<TResult>(Func<byte[], TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        byte[] key = GetKey();
        try
        {
            return operation(key);
        }
        finally
        {
            SecureStringHelper.SecureZeroArray(key);
        }
    }

    /// <summary>Executes an asynchronous operation with a temporary key copy and clears it afterwards.</summary>
    public async Task UseKeyAsync(Func<byte[], Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        byte[] key = GetKey();
        try
        {
            await operation(key).ConfigureAwait(false);
        }
        finally
        {
            SecureStringHelper.SecureZeroArray(key);
        }
    }

    /// <summary>
    /// Destroys the stored vault key by securely zeroing the memory and releasing
    /// the locked memory page. After this call, <see cref="IsUnlocked"/> returns false.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
    public void DestroyKey()
    {
        ThrowIfDisposed();

        if (_securePointer != IntPtr.Zero)
        {
            _secureMemory.SecureZeroMemory(_securePointer, 32);
            _secureMemory.FreeSecureMemory(_securePointer);
            _securePointer = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Disposes this instance, destroying the vault key if one is stored.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        DestroyKey();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
