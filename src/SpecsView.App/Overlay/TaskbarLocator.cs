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

    public static bool IsFullScreenAppRunning() =>
        Shell32.SHQueryUserNotificationState(out var state) == 0 &&
        state is Shell32.QunsBusy or Shell32.QunsRunningD3DFullScreen or Shell32.QunsPresentationMode;
}
