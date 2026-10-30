using System.Runtime.InteropServices;
using CipherVault.Core.Interfaces;

namespace CipherVault.Infrastructure.Security;

public sealed class SecureMemoryManager : ISecureMemoryManager
{
    private readonly Dictionary<IntPtr, SecureRegion> _regions = new();

    public IntPtr AllocateSecureMemory(int size)
    {
        var ptr = Marshal.AllocHGlobal(size);
        // Zero the memory
        unsafe { new Span<byte>((void*)ptr, size).Clear(); }
        bool isLocked = NativeMethods.VirtualLock(ptr, (UIntPtr)size);
        lock (_regions)
        {
            _regions.Add(ptr, new SecureRegion(size, isLocked));
        }
        return ptr;
    }

    public void FreeSecureMemory(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
            return;

        SecureRegion region;
        lock (_regions)
        {
            if (!_regions.Remove(pointer, out region))
                throw new ArgumentException("The pointer was not allocated by this manager.", nameof(pointer));
        }

        SecureZeroMemory(pointer, region.Size);
        if (region.IsLocked)
            NativeMethods.VirtualUnlock(pointer, (UIntPtr)region.Size);
        Marshal.FreeHGlobal(pointer);
    }

    public void SecureZeroMemory(IntPtr pointer, int size)
    {
        unsafe { new Span<byte>((void*)pointer, size).Clear(); }
    }

    public void Dispose()
    {
        lock (_regions)
        {
            foreach (var (ptr, region) in _regions)
            {
                SecureZeroMemory(ptr, region.Size);
                if (region.IsLocked)
                    NativeMethods.VirtualUnlock(ptr, (UIntPtr)region.Size);
                Marshal.FreeHGlobal(ptr);
            }
            _regions.Clear();
        }
    }

    private readonly record struct SecureRegion(int Size, bool IsLocked);
}
