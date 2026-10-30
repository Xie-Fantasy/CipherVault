namespace CipherVault.Core.Interfaces;

public interface ISecureMemoryManager : IDisposable
{
    IntPtr AllocateSecureMemory(int size);
    void FreeSecureMemory(IntPtr pointer);
    void SecureZeroMemory(IntPtr pointer, int size);
}
