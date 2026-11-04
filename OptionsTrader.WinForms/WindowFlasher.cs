using System.Runtime.InteropServices;

namespace OptionsTrader.WinForms;

// Makes a window's taskbar button flash until the user brings it to the front — for events that should pull
// the user back to the instance without stealing focus.
internal static class WindowFlasher
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FlashAll = 3;        // caption + taskbar button
    private const uint FlashUntilFront = 12; // keep flashing until the window comes to the foreground

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    public static void FlashUntilForeground(Form? form)
    {
        if (form == null || form.IsDisposed || !form.IsHandleCreated || Form.ActiveForm == form) return;
        var info = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = form.Handle, dwFlags = FlashAll | FlashUntilFront, uCount = uint.MaxValue, dwTimeout = 0 };
        FlashWindowEx(ref info);
    }
}
