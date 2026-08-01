namespace OptionsTrader.WinForms;

// Modal trade-detail view opened from the History tab's Trade Log grid — header (symbol badge,
// option type, strike/exp/level, PnL), stat tiles (entry/exit/contracts/target/premium/duration/
// status), and the trade's 3 screenshots (Entry, Close, TradeLog) stacked in a scrollable panel,
// loaded from the S3 URLs TradeHistoryStore now records (see UploadScreenshotAsync).
public class TradeDetailForm : Form
{
    public TradeDetailForm(TradeRecord trade)
    {
        Text          = $"{trade.Symbol} — Trade Detail";
        Width         = 980;
        Height        = 1000;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox   = false;
        MaximizeBox   = true;

        var isCall  = trade.OptionType == "CALL";
        var isOpen  = !trade.Pnl.HasValue;
        var pnlColor = isOpen ? Color.Gray : (trade.Pnl!.Value >= 0 ? Color.DarkGreen : Color.DarkRed);

        // ---- Header ----
        var headerPanel = new Panel { Dock = DockStyle.Top, Height = 74, Padding = new Padding(12) };
        var badge = new Label
        {
            Text      = trade.Symbol,
            Size      = new Size(48, 48),
            Location  = new Point(12, 12),
            BackColor = Color.FromArgb(25, 25, 55),
            ForeColor = Color.White,
            Font      = new Font(Font.FontFamily, 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var lblTitle = new Label
        {
            Text     = $"{trade.Symbol} — {(isCall ? "Call" : "Put")} option",
            Location = new Point(70, 10),
            AutoSize = true,
            Font     = new Font(Font.FontFamily, 13, FontStyle.Bold)
        };
        var lblSubtitle = new Label
        {
            Text      = $"Strike {trade.StrikePrice:F0} · Exp {trade.ExpirationDate:yyyy-MM-dd} · Level {trade.Level}",
            Location  = new Point(70, 34),
            AutoSize  = true,
            ForeColor = Color.DimGray
        };
        var lblPnl = new Label
        {
            Text      = isOpen ? "Open" : $"{(trade.Pnl!.Value >= 0 ? "+" : "")}${trade.Pnl.Value:F2}",
            Font      = new Font(Font.FontFamily, 14, FontStyle.Bold),
            ForeColor = pnlColor,
            AutoSize  = true,
            Anchor    = AnchorStyles.Top | AnchorStyles.Right
        };
        var lblPnlPct = new Label
        {
            Text      = trade.PnlPercent.HasValue ? $"{(trade.PnlPercent.Value >= 0 ? "+" : "")}{trade.PnlPercent.Value:F2}%" : string.Empty,
            ForeColor = pnlColor,
            AutoSize  = true,
            Anchor    = AnchorStyles.Top | AnchorStyles.Right
        };
        headerPanel.Resize += (s, e) =>
        {
            lblPnl.Location    = new Point(headerPanel.Width - lblPnl.Width - 16, 8);
            lblPnlPct.Location = new Point(headerPanel.Width - lblPnlPct.Width - 16, 32);
        };
        headerPanel.Controls.Add(badge);
        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(lblSubtitle);
        headerPanel.Controls.Add(lblPnl);
        headerPanel.Controls.Add(lblPnlPct);

        // ---- Stat tiles (2 rows) ----
        var premium = trade.EntryPrice * trade.Contracts * 100;
        var statsRow1 = MakeTileRow(4,
            ("Entry price", $"${trade.EntryPrice:F2}"),
            ("Exit price", trade.ExitPrice.HasValue ? $"${trade.ExitPrice.Value:F2}" : "-"),
            ("Contracts", trade.Contracts.ToString()),
            ("Target", $"{trade.TargetPercent:F0}%"));
        var statsRow2 = MakeTileRow(3,
            ("Premium", $"${premium:F2}"),
            ("Duration", trade.Duration?.ToString(@"hh\:mm\:ss") ?? "-"),
            ("Status", isOpen ? "Open" : "Closed"));

        // ---- Screenshots (scrollable) ----
        var imagesPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
        var imagesLayout = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            ColumnCount = 1,
            AutoSize    = true,
            Padding     = new Padding(8)
        };
        imagesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddImageBlock(imagesLayout, "Entry", trade.EntryImageUrl);
        AddImageBlock(imagesLayout, "Close", trade.CloseImageUrl);
        AddImageBlock(imagesLayout, "Trade Log", trade.TradeLogImageUrl);
        imagesPanel.Controls.Add(imagesLayout);

        Controls.Add(imagesPanel);
        Controls.Add(statsRow2);
        Controls.Add(statsRow1);
        Controls.Add(headerPanel);
    }

    private TableLayoutPanel MakeTileRow(int columns, params (string Title, string Value)[] tiles)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 60, ColumnCount = columns, RowCount = 1 };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        for (int i = 0; i < columns; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));

        for (int i = 0; i < tiles.Length; i++)
        {
            var (title, value) = tiles[i];
            var tile = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2), BackColor = Color.WhiteSmoke };
            var lblTitle = new Label { Text = title, Location = new Point(8, 6), AutoSize = true, ForeColor = Color.DimGray };
            var lblValue = new Label { Text = value, Location = new Point(8, 24), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            tile.Controls.Add(lblTitle);
            tile.Controls.Add(lblValue);
            row.Controls.Add(tile, i, 0);
        }
        return row;
    }

    // One labeled, scaled-to-fit image block. Missing/failed images show a placeholder message
    // instead of a blank gap — this trade might predate the fields being tracked (backfilled
    // trades) or the S3 fetch might simply fail.
    private static void AddImageBlock(TableLayoutPanel host, string label, string? url)
    {
        var block = new Panel { Width = 930, Height = 480, Margin = new Padding(0, 0, 0, 10), BorderStyle = BorderStyle.FixedSingle };
        var lblCaption = new Label { Text = label, Dock = DockStyle.Top, Height = 22, Font = new Font(block.Font, FontStyle.Bold), Padding = new Padding(6, 4, 0, 0), BackColor = Color.WhiteSmoke };
        var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };

        if (string.IsNullOrWhiteSpace(url))
        {
            picture.BackColor = Color.Gainsboro;
            var lblMissing = new Label { Text = "Sin imagen guardada", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gray };
            picture.Controls.Add(lblMissing);
        }
        else
        {
            picture.LoadCompleted += (s, e) =>
            {
                if (e.Error != null)
                {
                    var lblFailed = new Label { Text = "No se pudo cargar la imagen", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Red };
                    picture.Controls.Add(lblFailed);
                }
            };
            picture.LoadAsync(url);
        }

        block.Controls.Add(picture);
        block.Controls.Add(lblCaption);
        host.Controls.Add(block);
    }
}
