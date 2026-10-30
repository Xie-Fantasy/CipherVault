using CipherVault.Core.Interfaces;

namespace CipherVault.Infrastructure.Security;

public sealed class AntiCaptureService : IAntiCaptureService
{
    public bool IsEnabled { get; private set; }

    public void EnableAntiCapture(IntPtr windowHandle)
    {
        IsEnabled = NativeMethods.SetWindowDisplayAffinity(windowHandle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        if (!IsEnabled)
        {
            System.Diagnostics.Trace.TraceWarning(
                "SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) failed with Win32 error {0}.",
                System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
    }

    public void DisableAntiCapture(IntPtr windowHandle)
    {
        bool disabled = NativeMethods.SetWindowDisplayAffinity(windowHandle, NativeMethods.WDA_NONE);
        IsEnabled = !disabled;
    }
}
