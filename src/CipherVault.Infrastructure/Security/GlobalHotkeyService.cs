using CipherVault.Core.Interfaces;

namespace CipherVault.Infrastructure.Security;

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private IntPtr _windowHandle;
    private const int SearchHotkeyId = 9000;
    private bool _isRegistered;

    private static readonly Dictionary<string, uint> NamedKeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Letters map to their VK codes (A=0x41 .. Z=0x5A)
        // Digits map to their VK codes (0=0x30 .. 9=0x39)
        // Function keys
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        // Navigation
        ["SPACE"] = 0x20, ["ENTER"] = 0x0D, ["RETURN"] = 0x0D,
        ["TAB"] = 0x09, ["ESCAPE"] = 0x1B, ["ESC"] = 0x1B,
        ["BACK"] = 0x08, ["BACKSPACE"] = 0x08,
        ["DELETE"] = 0x2E, ["DEL"] = 0x2E,
        ["INSERT"] = 0x2D, ["INS"] = 0x2D,
        ["HOME"] = 0x24, ["END"] = 0x23,
        ["PAGEUP"] = 0x21, ["PRIOR"] = 0x21,
        ["PAGEDOWN"] = 0x22, ["NEXT"] = 0x22,
        // Arrow keys
        ["LEFT"] = 0x25, ["UP"] = 0x26, ["RIGHT"] = 0x27, ["DOWN"] = 0x28,
        // Numpad
        ["NUMPAD0"] = 0x60, ["NUMPAD1"] = 0x61, ["NUMPAD2"] = 0x62,
        ["NUMPAD3"] = 0x63, ["NUMPAD4"] = 0x64, ["NUMPAD5"] = 0x65,
        ["NUMPAD6"] = 0x66, ["NUMPAD7"] = 0x67, ["NUMPAD8"] = 0x68,
        ["NUMPAD9"] = 0x69,
        ["MULTIPLY"] = 0x6A, ["ADD"] = 0x6B, ["SUBTRACT"] = 0x6D,
        ["DECIMAL"] = 0x6E, ["DIVIDE"] = 0x6F,
        // Media keys
        ["VOLUME_MUTE"] = 0xAD, ["VOLUME_DOWN"] = 0xAE, ["VOLUME_UP"] = 0xAF,
        ["MEDIA_NEXT"] = 0xB0, ["MEDIA_PREV"] = 0xB1,
        ["MEDIA_STOP"] = 0xB2, ["MEDIA_PLAY_PAUSE"] = 0xB3,
        // Misc
        ["PRINTSCREEN"] = 0x2C, ["PAUSE"] = 0x13,
        ["NUMLOCK"] = 0x90, ["SCROLLLOCK"] = 0x91,
        ["CAPSLOCK"] = 0x14,
    };

    public event Action? HotkeyPressed;
    public bool IsRegistered => _isRegistered;

    public void SetWindowHandle(IntPtr handle) => _windowHandle = handle;

    public void Register(string hotkey)
    {
        UnregisterSearch();
        var (modifiers, vk) = ParseHotkey(hotkey);
        _isRegistered = vk != 0 && NativeMethods.RegisterHotKey(_windowHandle, SearchHotkeyId, modifiers, vk);
    }

    public void Unregister()
    {
        UnregisterSearch();
    }

    /// <summary>
    /// Called by the WPF window's WndProc when a WM_HOTKEY message is received.
    /// </summary>
    public void HandleHotkeyMessage(int wParam)
    {
        if (wParam == SearchHotkeyId)
            HotkeyPressed?.Invoke();
    }

    public void Dispose() => Unregister();

    public static bool IsValidHotkey(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
            return false;
        var (modifiers, vk) = ParseHotkey(hotkey);
        return modifiers != 0 && vk != 0;
    }

    private void UnregisterSearch()
    {
        if (!_isRegistered)
            return;
        NativeMethods.UnregisterHotKey(_windowHandle, SearchHotkeyId);
        _isRegistered = false;
    }

    private static (uint modifiers, uint vk) ParseHotkey(string hotkey)
    {
        uint mods = 0;
        uint key = 0;
        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    mods |= NativeMethods.MOD_CONTROL;
                    break;
                case "SHIFT":
                    mods |= NativeMethods.MOD_SHIFT;
                    break;
                case "ALT":
                    mods |= NativeMethods.MOD_ALT;
                    break;
                case "WIN":
                    mods |= NativeMethods.MOD_WIN;
                    break;
                default:
                    key = ResolveKeyCode(part);
                    break;
            }
        }
        return (mods, key);
    }

    private static uint ResolveKeyCode(string keyName)
    {
        // Single character: letters A-Z or digits 0-9
        if (keyName.Length == 1)
        {
            var c = char.ToUpperInvariant(keyName[0]);
            // VK codes for 'A'-'Z' are 0x41-0x5A, for '0'-'9' are 0x30-0x39
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return (uint)c;
        }

        // Named key lookup
        if (NamedKeyMap.TryGetValue(keyName, out var vk))
            return vk;

        // Fallback: try parsing as a hex VK code (e.g. "0x50")
        if (keyName.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(keyName.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hexVk))
        {
            return hexVk;
        }

        return 0;
    }
}
