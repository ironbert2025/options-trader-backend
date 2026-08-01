namespace OptionsTrader.WinForms;

// "History" tab content — two ways to look at TradeHistoryStore's local trade log (demo + real,
// open + closed, across every ticker instance since they all share the same file):
//   Calendar — a trading-journal-style month grid (day PnL/trade count, weekly rollup column),
//              mirroring the Angular frontend's calendar view. Click a day to drill into it.
//   Trade Log — a flat, most-recent-first grid of every trade, optionally filtered to one day.
// Built entirely in code (no designer file) — same convention as MultiChartForm/ChartPanel.
public class HistoryTabPanel : Panel
{
    private readonly RadioButton _rbCalendar;
    private readonly RadioButton _rbLog;
    private readonly Panel _calendarView;
    private readonly Panel _logView;

    private readonly Label _lblMonthPnl;
    private readonly Label _lblTradingDays;
    private readonly Label _lblWinRate;
    private readonly Label _lblTotalTrades;
    private readonly Label _lblMonthYear;
    private readonly DataGridView _dgvCalendar;

    private readonly Label _lblLogMonth;
    private readonly Label _lblLogFilter;
    private readonly Button _btnClearFilter;
    private readonly DataGridView _dgvLog;

    private List<TradeRecord> _allTrades = new();
    private DateOnly _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly _logMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly? _logFilterDate;

    private static readonly string[] DayHeaders = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT", "WEEK" };

    public HistoryTabPanel()
    {
        Dock = DockStyle.Fill;

        // ---- View toggle ----
        var togglePanel = new Panel { Dock = DockStyle.Top, Height = 32 };
        _rbCalendar = new RadioButton { Text = "Calendar", Location = new Point(0, 6), AutoSize = true };
        _rbLog      = new RadioButton { Text = "Trade Log", Location = new Point(90, 6), AutoSize = true };
        var btnRefresh = new Button { Text = "Refresh", Location = new Point(190, 2), Size = new Size(70, 24) };
        _rbCalendar.CheckedChanged += (s, e) => { _calendarView.Visible = _rbCalendar.Checked; _logView.Visible = !_rbCalendar.Checked; };
        btnRefresh.Click += (s, e) => RefreshData();
        togglePanel.Controls.Add(_rbCalendar);
        togglePanel.Controls.Add(_rbLog);
        togglePanel.Controls.Add(btnRefresh);

        // ---- Calendar view ----
        _calendarView = new Panel { Dock = DockStyle.Fill };

        var statsPanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 72, ColumnCount = 4, RowCount = 1 };
        statsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 4; i++) statsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        _lblMonthPnl    = MakeStatTile(statsPanel, 0, "Month PnL");
        _lblTradingDays = MakeStatTile(statsPanel, 1, "Trading days");
        _lblWinRate     = MakeStatTile(statsPanel, 2, "Win rate");
        _lblTotalTrades = MakeStatTile(statsPanel, 3, "Total trades");

        var navPanel = new Panel { Dock = DockStyle.Top, Height = 32 };
        var btnPrev = new Button { Text = "<", Location = new Point(0, 2), Size = new Size(28, 24) };
        var btnNext = new Button { Text = ">", Location = new Point(32, 2), Size = new Size(28, 24) };
        _lblMonthYear = new Label { Location = new Point(70, 6), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        btnPrev.Click += (s, e) => { _currentMonth = _currentMonth.AddMonths(-1); RefreshCalendar(); };
        btnNext.Click += (s, e) => { _currentMonth = _currentMonth.AddMonths(1); RefreshCalendar(); };
        navPanel.Controls.Add(btnPrev);
        navPanel.Controls.Add(btnNext);
        navPanel.Controls.Add(_lblMonthYear);

        _dgvCalendar = new DataGridView
        {
            Dock                          = DockStyle.Fill,
            ReadOnly                      = true,
            AllowUserToAddRows            = false,
            AllowUserToDeleteRows         = false,
            AllowUserToResizeRows         = false,
            RowHeadersVisible             = false,
            ColumnHeadersHeightSizeMode   = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            SelectionMode                 = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeRowsMode              = DataGridViewAutoSizeRowsMode.None,
            RowTemplate                   = { Height = 70 },
            DefaultCellStyle              = { WrapMode = DataGridViewTriState.True, Alignment = DataGridViewContentAlignment.MiddleCenter }
        };
        foreach (var header in DayHeaders)
            _dgvCalendar.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, Name = header, SortMode = DataGridViewColumnSortMode.NotSortable });
        _dgvCalendar.CellClick += DgvCalendar_CellClick;

        _calendarView.Controls.Add(_dgvCalendar);
        _calendarView.Controls.Add(navPanel);
        _calendarView.Controls.Add(statsPanel);

        // ---- Trade Log view ----
        _logView = new Panel { Dock = DockStyle.Fill, Visible = false };

        var logFilterPanel = new Panel { Dock = DockStyle.Top, Height = 30 };
        var btnLogPrev = new Button { Text = "<", Location = new Point(0, 2), Size = new Size(28, 24) };
        var btnLogNext = new Button { Text = ">", Location = new Point(32, 2), Size = new Size(28, 24) };
        _lblLogMonth = new Label { Location = new Point(70, 6), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _btnClearFilter = new Button { Text = "Ver todos", Location = new Point(200, 2), Size = new Size(80, 24), Visible = false };
        _lblLogFilter = new Label { AutoSize = true, Location = new Point(290, 6), ForeColor = Color.DimGray };
        btnLogPrev.Click += (s, e) => { _logMonth = _logMonth.AddMonths(-1); _logFilterDate = null; RefreshLog(); };
        btnLogNext.Click += (s, e) => { _logMonth = _logMonth.AddMonths(1); _logFilterDate = null; RefreshLog(); };
        _btnClearFilter.Click += (s, e) => { _logFilterDate = null; RefreshLog(); };
        logFilterPanel.Controls.Add(btnLogPrev);
        logFilterPanel.Controls.Add(btnLogNext);
        logFilterPanel.Controls.Add(_lblLogMonth);
        logFilterPanel.Controls.Add(_btnClearFilter);
        logFilterPanel.Controls.Add(_lblLogFilter);

        _dgvLog = new DataGridView
        {
            Dock                        = DockStyle.Fill,
            ReadOnly                    = true,
            AllowUserToAddRows          = false,
            AllowUserToDeleteRows       = false,
            RowHeadersVisible           = false,
            SelectionMode               = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode         = DataGridViewAutoSizeColumnsMode.AllCells
        };
        _dgvLog.Columns.Add("colDate",     "Date");
        _dgvLog.Columns.Add("colTime",     "Time");
        _dgvLog.Columns.Add("colWeek",     "Week");
        _dgvLog.Columns.Add("colSymbol",   "Symbol");
        _dgvLog.Columns.Add("colType",     "Type");
        _dgvLog.Columns.Add("colStrike",   "Strike");
        _dgvLog.Columns.Add("colEntry",    "Entry");
        _dgvLog.Columns.Add("colExit",     "Exit");
        _dgvLog.Columns.Add("colContracts","Contracts");
        _dgvLog.Columns.Add("colPnl",      "PnL");
        _dgvLog.Columns.Add("colPnlPct",   "PnL%");
        _dgvLog.Columns.Add("colDuration", "Duration");
        _dgvLog.Columns.Add("colDemo",     "Demo/Real");
        // Hidden id column — clicking a row looks the trade up fresh by this instead of relying
        // on row.Tag surviving whatever the grid does internally (header-click sorting on an
        // unbound DataGridView can reorder rows in ways that don't reliably carry Tag along).
        _dgvLog.Columns.Add(new DataGridViewTextBoxColumn { Name = "colId", Visible = false });
        foreach (DataGridViewColumn col in _dgvLog.Columns)
            col.SortMode = DataGridViewColumnSortMode.NotSortable;
        _dgvLog.CellClick += DgvLog_CellClick;

        _logView.Controls.Add(_dgvLog);
        _logView.Controls.Add(logFilterPanel);

        Controls.Add(_calendarView);
        Controls.Add(_logView);
        Controls.Add(togglePanel);

        // Default view — set now that _calendarView/_logView both exist (setting this earlier,
        // before they were constructed, meant the CheckedChanged handler above ran against null
        // fields and silently never applied the visibility swap).
        _rbLog.Checked = true;
        _calendarView.Visible = false;
        _logView.Visible = true;

        RefreshData();
    }

    // Explicit Location/Size instead of nested Dock=Top/Fill labels — the docked version left the
    // value label collapsed to zero height in practice. Both labels anchor to the tile's top-left
    // corner, which is enough since the tile itself has a fixed height (statsPanel.Height).
    private static Label MakeStatTile(TableLayoutPanel host, int col, string title)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2), BorderStyle = BorderStyle.FixedSingle };
        var lblTitle = new Label
        {
            Text      = title,
            Location  = new Point(8, 6),
            AutoSize  = true,
            ForeColor = Color.DimGray
        };
        var lblValue = new Label
        {
            Text      = "-",
            Location  = new Point(8, 26),
            AutoSize  = true,
            Font      = new Font(host.Font.FontFamily, 15, FontStyle.Bold),
            ForeColor = Color.Black
        };
        panel.Controls.Add(lblTitle);
        panel.Controls.Add(lblValue);
        host.Controls.Add(panel, col, 0);
        return lblValue;
    }

    private void RefreshData()
    {
        _allTrades = TradeHistoryStore.Load();
        RefreshCalendar();
        RefreshLog();
    }

    // ---- Calendar ----

    private void RefreshCalendar()
    {
        _lblMonthYear.Text = _currentMonth.ToString("MMMM yyyy");

        var monthTrades = _allTrades.Where(t => t.EntryTime.Year == _currentMonth.Year && t.EntryTime.Month == _currentMonth.Month).ToList();
        var closedInMonth = monthTrades.Where(t => t.Pnl.HasValue).ToList();
        var monthPnl = closedInMonth.Sum(t => t.Pnl!.Value);
        var tradingDays = monthTrades.Select(t => t.EntryTime.Date).Distinct().Count();
        var daysInMonth = DateTime.DaysInMonth(_currentMonth.Year, _currentMonth.Month);
        var wins = closedInMonth.Count(t => t.Pnl!.Value > 0);
        var winRate = closedInMonth.Count > 0 ? Math.Round(100.0 * wins / closedInMonth.Count, 0) : 0;

        _lblMonthPnl.Text    = $"{(monthPnl >= 0 ? "+" : "")}${monthPnl:F0}";
        _lblMonthPnl.ForeColor = monthPnl >= 0 ? Color.DarkGreen : Color.DarkRed;
        _lblTradingDays.Text = $"{tradingDays} / {daysInMonth}";
        _lblWinRate.Text     = $"{winRate}%";
        _lblTotalTrades.Text = monthTrades.Count.ToString();

        _dgvCalendar.Rows.Clear();
        var firstOfMonth = new DateOnly(_currentMonth.Year, _currentMonth.Month, 1);
        var startOffset = (int)firstOfMonth.DayOfWeek; // 0=Sunday
        var totalCells = startOffset + daysInMonth;
        var weekCount = (int)Math.Ceiling(totalCells / 7.0);

        var day = firstOfMonth.AddDays(-startOffset);
        for (int week = 0; week < weekCount; week++)
        {
            var rowIndex = _dgvCalendar.Rows.Add();
            var row = _dgvCalendar.Rows[rowIndex];
            decimal weekPnl = 0; int weekTrades = 0; int weekTradingDays = 0;

            for (int dow = 0; dow < 7; dow++)
            {
                var cell = row.Cells[dow];
                if (day.Month != _currentMonth.Month)
                {
                    cell.Value = string.Empty;
                    cell.Style.BackColor = Color.WhiteSmoke;
                }
                else
                {
                    var dayTrades = _allTrades.Where(t => t.EntryTime.Date == day.ToDateTime(TimeOnly.MinValue)).ToList();
                    var dayClosed = dayTrades.Where(t => t.Pnl.HasValue).ToList();
                    var dayPending = dayTrades.Count(t => !t.Pnl.HasValue);
                    var daySum = dayClosed.Sum(t => t.Pnl!.Value);

                    cell.Tag = day; // used by the click handler to drill down

                    if (dayTrades.Count == 0)
                    {
                        cell.Value = day.Day.ToString();
                        cell.Style.BackColor = Color.White;
                        cell.Style.ForeColor = Color.Gray;
                    }
                    else
                    {
                        weekPnl += daySum;
                        weekTrades += dayTrades.Count;
                        weekTradingDays++;

                        var label = dayClosed.Count == 0
                            ? "(pend.)"
                            : $"({(daySum >= 0 ? "+" : "")}${daySum:F0})";
                        cell.Value = $"{day.Day}\n{label}\n{dayTrades.Count}T";
                        cell.Style.BackColor = dayClosed.Count == 0
                            ? Color.Moccasin
                            : daySum >= 0 ? Color.LightGreen : Color.MistyRose;
                        cell.Style.ForeColor = Color.Black;
                        _ = dayPending; // included in the trade count only, no separate rendering for now
                    }
                }
                // Selection highlight would otherwise repaint the cell solid blue on click,
                // hiding the Profit/Loss/Pending color — keep the same look whether selected or not.
                cell.Style.SelectionBackColor = cell.Style.BackColor;
                cell.Style.SelectionForeColor = cell.Style.ForeColor;
                day = day.AddDays(1);
            }

            var weekCell = row.Cells[7];
            if (weekTrades == 0)
            {
                weekCell.Value = string.Empty;
                weekCell.Style.BackColor = Color.WhiteSmoke;
            }
            else
            {
                weekCell.Value = $"W{week + 1}\n{(weekPnl >= 0 ? "+" : "")}${weekPnl:F0}\n{weekTradingDays}d - {weekTrades}T";
                weekCell.Style.BackColor = weekPnl >= 0 ? Color.LightGreen : Color.MistyRose;
                weekCell.Style.ForeColor = Color.Black;
                weekCell.Style.Font = new Font(_dgvCalendar.Font, FontStyle.Bold);
            }
            weekCell.Style.SelectionBackColor = weekCell.Style.BackColor;
            weekCell.Style.SelectionForeColor = weekCell.Style.ForeColor;
        }
    }

    private void DgvCalendar_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex is < 0 or 7) return; // ignore header row / Week column
        if (_dgvCalendar.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is not DateOnly day) return;

        _logFilterDate = day;
        _logMonth = _currentMonth; // keep the log's month nav in sync with the day being drilled into
        _rbLog.Checked = true; // triggers the panel-visibility swap
        RefreshLog();
    }

    // ---- Trade Log ----

    private void DgvLog_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_dgvLog.Rows[e.RowIndex].Cells["colId"].Value is not int id) return;

        var trade = _allTrades.FirstOrDefault(t => t.Id == id);
        if (trade == null) return;

        using var detail = new TradeDetailForm(trade);
        detail.ShowDialog(FindForm());
    }

    private void RefreshLog()
    {
        _lblLogMonth.Text = _logMonth.ToString("MMMM yyyy");
        _btnClearFilter.Visible = _logFilterDate.HasValue;

        var monthTrades = _allTrades.Where(t => t.EntryTime.Year == _logMonth.Year && t.EntryTime.Month == _logMonth.Month).ToList();
        _lblLogFilter.Text = _logFilterDate.HasValue
            ? $"Filtrado: {_logFilterDate.Value:yyyy-MM-dd}"
            : $"{monthTrades.Count} trades";

        var trades = _logFilterDate.HasValue
            ? _allTrades.Where(t => DateOnly.FromDateTime(t.EntryTime) == _logFilterDate.Value)
            : monthTrades.AsEnumerable();

        _dgvLog.Rows.Clear();
        foreach (var t in trades.OrderByDescending(t => t.EntryTime))
        {
            var idx = _dgvLog.Rows.Add(
                t.EntryTime.ToString("yyyy-MM-dd"),
                t.EntryTime.ToString("HH:mm:ss"),
                $"Wk{WeekOfMonth(DateOnly.FromDateTime(t.EntryTime))}",
                t.Symbol,
                t.OptionType,
                t.StrikePrice.ToString("F2"),
                t.EntryPrice.ToString("F2"),
                t.ExitPrice?.ToString("F2") ?? "-",
                t.Contracts,
                t.Pnl.HasValue ? $"{(t.Pnl.Value >= 0 ? "+" : "")}{t.Pnl.Value:F2}" : "Open",
                t.PnlPercent.HasValue ? $"{(t.PnlPercent.Value >= 0 ? "+" : "")}{t.PnlPercent.Value:F1}%" : "-",
                t.Duration?.ToString(@"hh\:mm\:ss") ?? "-",
                t.IsDemo ? "Demo" : "Real");
            _dgvLog.Rows[idx].Cells["colId"].Value = t.Id;

            var boldFont = new Font(_dgvLog.Font, FontStyle.Bold);
            var row = _dgvLog.Rows[idx];

            row.Cells["colSymbol"].Style.ForeColor = Color.DarkBlue;

            row.Cells["colType"].Style.ForeColor = t.OptionType == "CALL" ? Color.DarkGreen : Color.DarkRed;
            row.Cells["colType"].Style.Font = boldFont;

            var pnlColor = t.Pnl.HasValue ? (t.Pnl.Value >= 0 ? Color.DarkGreen : Color.DarkRed) : Color.Black;
            row.Cells["colPnl"].Style.ForeColor = pnlColor;
            row.Cells["colPnl"].Style.Font = boldFont;
            row.Cells["colPnlPct"].Style.ForeColor = pnlColor;
            row.Cells["colPnlPct"].Style.Font = boldFont;

            row.Cells["colDemo"].Style.ForeColor = t.IsDemo ? Color.DarkOrange : Color.DarkGreen;
            row.Cells["colDemo"].Style.Font = boldFont;
        }
    }

    // Same Sunday-start week grouping the Calendar view uses (W1, W2, ...) so "Wk2" here lines
    // up with the "W2" row on the Calendar tab for the same date.
    private static int WeekOfMonth(DateOnly date)
    {
        var firstOfMonth = new DateOnly(date.Year, date.Month, 1);
        var offset = (int)firstOfMonth.DayOfWeek;
        return (date.Day + offset - 1) / 7 + 1;
    }
}
