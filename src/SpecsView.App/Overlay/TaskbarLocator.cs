using SpecsView.App.Native;

namespace SpecsView.App.Overlay;

internal sealed record TaskbarInfo(Rectangle Bounds, Rectangle? Tray, uint Dpi)
{
    public bool IsHorizontal => Bounds.Width >= Bounds.Height;

    public float Scale => Dpi / 96f;
}

/// <summary>Finds the primary taskbar and its notification area, in physical pixels.</summary>
internal static class TaskbarLocator
{
    public static TaskbarInfo? Locate()
    {
        var taskbar = User32.FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0 || !User32.GetWindowRect(taskbar, out var bounds))
        {
            return null; // Explorer is not running (or restarting).
        }

        Rectangle? tray = null;
        var notify = User32.FindWindowEx(taskbar, 0, "TrayNotifyWnd", null);
        if (notify != 0 && User32.GetWindowRect(notify, out var trayRect))
        {
            tray = trayRect.ToRectangle();
        }

        var dpi = User32.GetDpiForWindow(taskbar);
        return new TaskbarInfo(bounds.ToRectangle(), tray, dpi == 0 ? 96 : dpi);
    }

    // The desktop and the taskbar itself cover the whole monitor but are never "full-screen apps".
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>
    /// True when a game/video/presentation covers the entire monitor. QUNS_BUSY is deliberately not
    /// trusted: Windows reports it for ordinary windows too (e.g. File Explorer gaining focus after
    /// another window is minimized), which made the widget disappear.
    /// </summary>
    public static bool IsFullScreenAppRunning()
    {
        if (Shell32.SHQueryUserNotificationState(out var state) == 0 &&
            state is Shell32.QunsRunningD3DFullScreen or Shell32.QunsPresentationMode)
        {
            return true;
        }

        var foreground = User32.GetForegroundWindow();
        if (foreground == 0 || User32.IsIconic(foreground) || IsShellWindow(foreground) ||
            !User32.GetWindowRect(foreground, out var window))
        {
            return false;
        }

        var monitor = new User32.MonitorInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<User32.MonitorInfo>() };
        if (!User32.GetMonitorInfo(User32.MonitorFromWindow(foreground, User32.MonitorDefaultToNearest), ref monitor))
        {
            return false;
        }

        // Maximized windows stop at the work area; only true full-screen windows cover the taskbar too.
        return window.ToRectangle().Contains(monitor.Monitor.ToRectangle());
    }

    private static unsafe bool IsShellWindow(nint hwnd)
    {
        var buffer = stackalloc char[64];
        var length = User32.GetClassName(hwnd, buffer, 64);
        var className = new ReadOnlySpan<char>(buffer, Math.Max(0, length));
        foreach (var shellClass in ShellClasses)
        {
            if (className.SequenceEqual(shellClass))
            {
                return true;
            }
        }

        return false;
    }
}
