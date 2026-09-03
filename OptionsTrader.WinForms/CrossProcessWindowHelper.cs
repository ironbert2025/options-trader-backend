using System.Runtime.InteropServices;
using System.Text;

namespace OptionsTrader.WinForms;

// Brings every top-level window whose title starts with a given prefix to the foreground, across
// ALL processes on this machine — not just this one. Needed because each ticker runs as a
// SEPARATE OS process (one Form1 + its own Live Charts windows per symbol), so a normal
// Form.BringToFront()/Activate() only ever reaches windows owned by the SAME process; reaching
// the others requires the raw Win32 window-enumeration API instead.
internal static class CrossProcessWindowHelper
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    // Brings every visible top-level window (in any process) whose title starts with
    // titlePrefix to the front, restoring it first if minimized. Windows only allows ONE true
    // foreground window at a time, so in a loop like this only the LAST one actually ends up
    // focused — the others still get un-minimized and raised in the taskbar/z-order, which is
    // what "bring them all forward" means in practice for a group of sibling windows.
    public static int BringAllToFront(string titlePrefix)
    {
        var matched = 0;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var length = GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (!title.StartsWith(titlePrefix, StringComparison.Ordinal)) return true;

            if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
            matched++;

            return true; // keep enumerating — there can be several (one per ticker instance)
        }, IntPtr.Zero);

        return matched;
    }
}
