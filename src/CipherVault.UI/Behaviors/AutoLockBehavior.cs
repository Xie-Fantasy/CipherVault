namespace CipherVault.UI.Behaviors;

using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

public sealed class AutoLockBehavior : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;
    private int _timeoutMinutes = 5;
    private bool _disposed;

    public event Action? IdleTimeoutReached;

    public int TimeoutMinutes
    {
        get => _timeoutMinutes;
        set => _timeoutMinutes = Math.Clamp(value, 1, 60);
    }

    public bool LockOnSystemLock { get; set; } = true;

    public AutoLockBehavior()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _timer.Tick += OnTimerTick;

        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var idleTime = GetSystemIdleTime();
        if (idleTime >= TimeSpan.FromMinutes(_timeoutMinutes))
        {
            RaiseIdleTimeout();
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (LockOnSystemLock && e.Reason == SessionSwitchReason.SessionLock)
        {
            RaiseIdleTimeout();
        }
    }

    private static TimeSpan GetSystemIdleTime()
    {
        var lastInputInfo = new LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
        };

        if (GetLastInputInfo(ref lastInputInfo))
        {
            uint idleMs = unchecked((uint)Environment.TickCount - lastInputInfo.dwTime);
            return TimeSpan.FromMilliseconds(idleMs);
        }

        return TimeSpan.Zero;
    }

    private void RaiseIdleTimeout()
    {
        if (_dispatcher.CheckAccess())
        {
            IdleTimeoutReached?.Invoke();
        }
        else
        {
            _dispatcher.BeginInvoke(() => IdleTimeoutReached?.Invoke());
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
}
