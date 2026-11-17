using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ThixFred.Core.Platform.Windows;

/// <summary>
/// Whether a main window is one the user sees: visible and not cloaked by the shell (UWP hosts and the touch keyboard keep
/// cloaked windows with titles that are no program of the user).
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class VisibleWindows
{
    private const int DwmWindowAttributeCloaked = 14;

    public static bool IsShown(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindowVisible(window))
        {
            return false;
        }

        return DwmGetWindowAttribute(window, DwmWindowAttributeCloaked, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr window);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
