namespace OptionsTrader.WinForms;

// The header strip drawn above each panel in the combined chart images (live Charts tab, Live Chart
// window, Simulator). The real header ("Alcista - Alcista", "AAPL — 1h") is a set of WinForms labels
// above the WebView, and the screenshots only capture the WebView's content, so without this strip an
// image carried neither the symbol, nor the timeframe, nor the trend words.
internal static class CombinedChartHeader
{
    public const int BaseHeight = 22;                          // same as the real header row
    private static readonly Color Background = Color.FromArgb(19, 23, 34);
    private static readonly Color DashColor  = Color.Silver;

    // Pixel scale of a captured image against the control it came from (DPI scaling): the capture is
    // device pixels, the control's Width is not.
    public static float ScaleFor(Bitmap capture, Control source) =>
        Math.Max(1f, capture.Width / (float)Math.Max(1, source.Width));

    public static int StripHeight(float scale) => (int)Math.Round(BaseHeight * scale);

    // Draws the strip at (x, 0), `width` px wide. `trend` = the colored "Largo - Corto" words (null =
    // none known for this panel); `title` is centered, like the real header.
    public static void Draw(Graphics g, int x, int width, float scale, string title,
        IReadOnlyList<(string Text, Color Color)>? trend)
    {
        var height = StripHeight(scale);
        using var back = new SolidBrush(Background);
        g.FillRectangle(back, x, 0, width, height);

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        // GenericTypographic drops trailing spaces from MeasureString — keep them, or " - " collapses.
        using var flags = (StringFormat)StringFormat.GenericTypographic.Clone();
        flags.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        using var regular = new Font("Segoe UI", 9f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bold    = new Font("Segoe UI", 9f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var textTop = (height - bold.Height) / 2f;

        // Title, centered across the strip.
        using (var white = new SolidBrush(Color.White))
        {
            var size = g.MeasureString(title, regular, int.MaxValue, flags);
            g.DrawString(title, regular, white, x + (width - size.Width) / 2f, textTop, flags);
        }

        // Trend words, left-aligned with the real header's 6 px padding.
        if (trend != null)
        {
            var cursor = x + 6f * scale;
            foreach (var (text, color) in trend)
            {
                using var brush = new SolidBrush(color);
                g.DrawString(text, bold, brush, cursor, textTop, flags);
                cursor += g.MeasureString(text, bold, int.MaxValue, flags).Width;
            }
        }
    }

    // The three pieces of "Largo - Corto" with the colors the live header uses.
    public static IReadOnlyList<(string Text, Color Color)>? TrendSegments(string? shortDir, string? longDir)
    {
        var segs = ChartPanel.TrendLogSegments(shortDir, longDir);
        if (segs == null) return null;
        return new List<(string, Color)>
        {
            (segs[0].Text, segs[0].Color ?? Color.White),
            (" - ", DashColor),
            (segs[2].Text, segs[2].Color ?? Color.White)
        };
    }
}
