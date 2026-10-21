using System.Drawing.Imaging;

namespace OptionsTrader.WinForms;

// Screenshot of the per-symbol screen region saved in Settings > Screen Coordinates (two corners picked with
// two clicks, see ScreenCoordsStore) — typically a chart window of another program (the ThinkOrSwim charts) that
// is not part of this app. A symbol without coordinates simply gets nothing (the feature is omitted on a PC that
// does not use it). CopyFromScreen takes whatever is on the screen at that moment, so it works whether or not
// this app's own charts are visible.
internal static class ExternalScreenCapture
{
    // The saved rectangle of a symbol, or null (no coordinates, or not a valid area).
    public static Rectangle? RectFor(string symbol)
    {
        var coords = ScreenCoordsStore.Load();
        if (!coords.TryGetValue(symbol.ToUpperInvariant(), out var tc) || tc is null) return null;
        if (string.IsNullOrWhiteSpace(tc.Coords1) || string.IsNullOrWhiteSpace(tc.Coords2)) return null;

        var a = tc.Coords1.Split(',');
        var b = tc.Coords2.Split(',');
        if (a.Length != 2 || b.Length != 2) return null;
        if (!int.TryParse(a[0].Trim(), out var x1) || !int.TryParse(a[1].Trim(), out var y1) ||
            !int.TryParse(b[0].Trim(), out var x2) || !int.TryParse(b[1].Trim(), out var y2)) return null;

        var width = Math.Abs(x2 - x1);
        var height = Math.Abs(y2 - y1);
        return width > 0 && height > 0 ? new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2), width, height) : null;
    }

    // Captures the region and saves it as a PNG at path. Null when the symbol has no region or the capture fails.
    public static string? CaptureToFile(string symbol, string path)
    {
        try
        {
            if (RectFor(symbol) is not { } rect) return null;
            using var bmp = new Bitmap(rect.Width, rect.Height);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bmp.Save(path, ImageFormat.Png);
            return path;
        }
        catch
        {
            return null; // best-effort — a failed screenshot never disturbs a trade or the charts
        }
    }

    // Same, in the trade-snapshot folder with the usual {Symbol}_{OptionType}_{timestamp}_{Tag}.png name.
    public static string? CaptureForTrade(string symbol, string optionType, string tag)
    {
        var folder = Path.Combine(@"C:\OptionsData\ChartSnapshots", symbol);
        return CaptureToFile(symbol, Path.Combine(folder, $"{symbol}_{optionType}_{DateTime.Now:yyyyMMdd_HHmmss}_{tag}.png"));
    }
}
