namespace OptionsTrader.WinForms;

// Where a companion window goes when it should sit against the RIGHT edge of its parent window. Pure
// geometry (no controls), so it can be checked against real window positions.
internal static class WindowDocking
{
    // parent / workingArea: screen coordinates of the parent's outer bounds and of the usable area of the
    // monitor it is on. Normal case: the child starts at the parent's right edge (+ gap), at topAlignY.
    // No room there -> the child goes to the parent's LEFT side; no room either -> it is pushed against the
    // right edge of the working area, still on the same monitor. Vertically it never leaves the working area.
    public static Point ComputeDockedLocation(Rectangle parent, Size child, Rectangle workingArea, int topAlignY, int gap = 0)
    {
        var left = parent.Right + gap;
        if (left + child.Width > workingArea.Right)
        {
            var leftSide = parent.Left - gap - child.Width;
            left = leftSide >= workingArea.Left ? leftSide : workingArea.Right - child.Width;
        }
        left = Math.Max(workingArea.Left, left);

        var top = Math.Min(topAlignY, workingArea.Bottom - child.Height);
        top = Math.Max(workingArea.Top, top);
        return new Point(left, top);
    }
}
