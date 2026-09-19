using System.Runtime.InteropServices;

namespace Parrot.App.Interop;

/// <summary>Shell notification state, as reported by SHQueryUserNotificationState.</summary>
internal enum UserNotificationState
{
    NotPresent = 1,
    Busy = 2,
    RunningDirect3DFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    RunningWindowsStoreApp = 7,
}

internal static partial class NativeMethods
{
    public const int GwlExStyle = -20;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    public struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLastInputInfo(ref LastInputInfo info);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static partial int GetWindowLong(IntPtr hWnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    public static partial int SetWindowLong(IntPtr hWnd, int index, int newStyle);

    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out UserNotificationState state);

    /// <summary>How long the keyboard and mouse have been untouched.</summary>
    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };

        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;

        // Both values are 32-bit millisecond tick counts that wrap about every 49 days;
        // the unchecked subtraction stays correct across the wrap.
        var elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    /// <summary>
    /// True when a game, video or presentation is filling the screen. Interrupting those
    /// is the fastest way to get the app uninstalled.
    /// </summary>
    public static bool IsFullScreenOrPresenting()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) != 0)
                return false;

            return state is UserNotificationState.RunningDirect3DFullScreen
                or UserNotificationState.PresentationMode
                or UserNotificationState.Busy
                or UserNotificationState.QuietTime;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Marks a window as never-activating and hidden from Alt+Tab, so a prompt can appear
    /// without stealing the keystrokes the user is in the middle of typing.
    /// </summary>
    public static void MakeNonActivating(IntPtr handle)
    {
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }
}
