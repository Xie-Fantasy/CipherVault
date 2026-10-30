namespace CipherVault.Core.Interfaces;

public interface IAntiCaptureService
{
    void EnableAntiCapture(IntPtr windowHandle);
    void DisableAntiCapture(IntPtr windowHandle);
    bool IsEnabled { get; }
}
