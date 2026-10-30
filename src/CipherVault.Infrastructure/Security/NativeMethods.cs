using System.Runtime.InteropServices;

namespace CipherVault.Infrastructure.Security;

internal static class NativeMethods
{
    // kernel32.dll
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualLock(IntPtr lpAddress, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualUnlock(IntPtr lpAddress, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LocalUnlock(IntPtr hMem);

    // user32.dll
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    public static extern uint GetTickCount();

    // Constants
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    public const uint WDA_NONE = 0x00;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
}
