using OptionsTrader.Application.DTOs.Options;
using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// Standalone "replay" window: pick a Symbol + a day that was actually recorded while the app was
// running live, then step ◀/▶ through the options-chain snapshots from that day exactly as
// SimulationDataLoader read them. Completely independent of Form1's live polling and of
// MultiChartForm's live streaming — can be open at the same time as either without any
// interaction between them (separate controls, separate data, separate stores).
public class SimulatorForm : Form
{
    private readonly ComboBox _cmbSymbol = new() { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(8, 8), Size = new Size(100, 24) };
    private readonly ComboBox _cmbDate   = new() { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(116, 8), Size = new Size(120, 24) };
    private readonly Button _btnCargar   = new() { Text = "Cargar", Location = new Point(244, 8), Size = new Size(70, 24) };
    private readonly Button _btnAtras    = new() { Text = "◀ Atrás", Location = new Point(8, 40), Size = new Size(90, 26), Enabled = false };
    private readonly Button _btnAdelante = new() { Text = "Adelante ▶", Location = new Point(102, 40), Size = new Size(90, 26), Enabled = false };
    private readonly Label _lblStep      = new() { Location = new Point(200, 46), Size = new Size(260, 20), Text = "Sin datos cargados" };

    private readonly DataGridView _dgvChain = new()
    {
        Location = new Point(8, 74), Size = new Size(900, 220),
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect
    };

    // 3-column TableLayoutPanel (same pattern as MultiChartForm's own chart row) — deterministic
    // left-to-right order, unlike stacking multiple Dock=Left panels.
    private readonly TableLayoutPanel _chartsHost = new()
    {
        Location = new Point(8, 302), Size = new Size(900, 260),
        ColumnCount = 3, RowCount = 1
    };
    private readonly SimulatedChartPanel _hourlyChart = new("1h") { Dock = DockStyle.Fill };
    private readonly SimulatedChartPanel _rthChart    = new("15m RTH") { Dock = DockStyle.Fill };
    private readonly SimulatedChartPanel _fullChart   = new("15m RTH+Overnight") { Dock = DockStyle.Fill };

    private readonly DataGridView _dgvTrades = new()
    {
        Location = new Point(8, 570), Size = new Size(900, 130),
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true,
        RowHeadersVisible = false
    };

    private List<DateOnly> _availableDates = new();
    private List<SimulationStep> _steps = new();
    private List<CandleData> _hourlyCandles = new();
    private List<CandleData> _intradayCandles = new(); // shared by the 15m RTH and RTH+Overnight panels
    private int _currentIndex = -1;
    private string _symbol = string.Empty;
    private DateOnly _simDate;
    private TickerEntry? _ticker;

    public SimulatorForm()
    {
        Text          = "Simulador";
        Width         = 940;
        Height        = 740;
        StartPosition = FormStartPosition.CenterScreen;

        BuildChainColumns();
        BuildTradesColumns();

        _chartsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        _chartsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        _chartsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        _chartsHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _chartsHost.Controls.Add(_hourlyChart, 0, 0);
        _chartsHost.Controls.Add(_rthChart, 1, 0);
        _chartsHost.Controls.Add(_fullChart, 2, 0);

        Controls.Add(_cmbSymbol);
        Controls.Add(_cmbDate);
        Controls.Add(_btnCargar);
        Controls.Add(_btnAtras);
        Controls.Add(_btnAdelante);
        Controls.Add(_lblStep);
        Controls.Add(_dgvChain);
        Controls.Add(_chartsHost);
        Controls.Add(_dgvTrades);

        _cmbSymbol.SelectedIndexChanged += (s, e) => RefreshAvailableDates();
        _btnCargar.Click    += (s, e) => LoadSelectedDay();
        _btnAtras.Click     += (s, e) => Step(-1);
        _btnAdelante.Click  += (s, e) => Step(1);
        _dgvChain.CellClick += DgvChain_CellClick;

        Load += (s, e) => LoadSymbols();
    }

    private void LoadSymbols()
    {
        var symbols = TickerSettingsStore.Load().Select(t => t.Symbol).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        _cmbSymbol.Items.Clear();
        foreach (var s in symbols) _cmbSymbol.Items.Add(s);
        if (_cmbSymbol.Items.Count > 0) _cmbSymbol.SelectedIndex = 0;
    }

    private void RefreshAvailableDates()
    {
        _cmbDate.Items.Clear();
        if (_cmbSymbol.SelectedItem is not string symbol) return;

        _availableDates = SimulationDataLoader.GetAvailableDates(symbol);
        foreach (var d in _availableDates) _cmbDate.Items.Add(d.ToString("yyyy-MM-dd"));
        if (_cmbDate.Items.Count > 0) _cmbDate.SelectedIndex = 0;
    }

    private void LoadSelectedDay()
    {
        if (_cmbSymbol.SelectedItem is not string symbol) return;
        if (_cmbDate.SelectedItem is not string dateStr || !DateOnly.TryParse(dateStr, out var date)) return;

        var tickers = TickerSettingsStore.Load();
        _ticker = tickers.FirstOrDefault(t => t.Symbol == symbol);
        if (_ticker == null)
        {
            MessageBox.Show($"No hay configuración de rango para {symbol} en la tabla de Tickers.",
                "Simulador", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _symbol  = symbol;
        _simDate = date;
        _steps   = SimulationDataLoader.LoadDay(symbol, date);
        // Same amount of surrounding context the live charts default to (7 days for 1h, 3 for the
        // two 15m panels) — see ChartPanel.LoadHistoryAsync's visibleDays.
        _hourlyCandles   = SimulationDataLoader.LoadHourlyCandlesWithContext(symbol, date);
        _intradayCandles = SimulationDataLoader.LoadUnderlyingCandlesWithContext(symbol, date, contextDays: 3);
        _currentIndex = _steps.Count > 0 ? 0 : -1;

        _dgvTrades.Rows.Clear();
        _openSimTrades.Clear();

        UpdateStepButtons();
        RenderCurrentStep();
    }

    private void Step(int direction)
    {
        if (_steps.Count == 0) return;
        var next = _currentIndex + direction;
        if (next < 0 || next >= _steps.Count) return;
        _currentIndex = next;
        RenderCurrentStep();
    }

    private void UpdateStepButtons()
    {
        _btnAtras.Enabled    = _currentIndex > 0;
        _btnAdelante.Enabled = _currentIndex >= 0 && _currentIndex < _steps.Count - 1;
    }

    private void RenderCurrentStep()
    {
        UpdateStepButtons();
        if (_currentIndex < 0 || _ticker == null)
        {
            _lblStep.Text = "Sin datos cargados";
            return;
        }

        var step = _steps[_currentIndex];
        _lblStep.Text = $"Paso {_currentIndex + 1}/{_steps.Count} — {EasternTime(step.Time):HH:mm:ss} — Spot {step.UnderlyingPrice:F2}";

        Form1.PopulateQuotesGrid(_dgvChain, step.Quotes, _ticker, applyCountsFilter: false);

        var hourlyUpToNow   = _hourlyCandles.Where(c => c.Time <= step.Time).ToList();
        var intradayUpToNow = _intradayCandles.Where(c => c.Time <= step.Time).ToList();

        _ = _hourlyChart.CargarHastaPasoAsync(
            CandleAggregation.AggregateToHourlyRthBuckets(hourlyUpToNow), visibleDays: 7);
        _ = _rthChart.CargarHastaPasoAsync(CandleAggregation.AggregateToInterval(
            CandleAggregation.FilterSession(intradayUpToNow, rthOnly: true), 15, rthOnly: true), visibleDays: 3);
        _ = _fullChart.CargarHastaPasoAsync(CandleAggregation.AggregateToInterval(
            intradayUpToNow, 15, rthOnly: false), visibleDays: 3);

        RefreshOpenSimTradesPnL(step);
    }

    // ----- Demo trades (practice only — separate from real/demo trades in Form1) -----

    private sealed record OpenSimTrade(DataGridViewRow Row, string OptionType, decimal StrikePrice, int Contracts, DateTime EntryTime, decimal EntryPrice, decimal TBid);
    private readonly List<OpenSimTrade> _openSimTrades = new();

    // step.Time / trade.EntryTime are real UTC (same convention as CandleData.Time, needed so the
    // chart's candlesUpToNow filter compares apples to apples) — converted to Eastern only at the
    // point of display/logging, same "disguise" pattern used elsewhere in this codebase.
    private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static DateTime EasternTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, EasternZone);

    private void BuildChainColumns()
    {
        _dgvChain.Columns.Add("colSymbol", "Symbol");
        _dgvChain.Columns.Add("colRange", "Range");
        _dgvChain.Columns.Add("colCallSprd", "Sprd");
        _dgvChain.Columns.Add("colCallBid", "Bid");
        _dgvChain.Columns.Add("colCallAsk", "Ask");
        _dgvChain.Columns.Add("colSpot", "Spot");
        _dgvChain.Columns.Add("colStrikePrice", "Strike");
        _dgvChain.Columns.Add("colPutBid", "Bid");
        _dgvChain.Columns.Add("colPutAsk", "Ask");
        _dgvChain.Columns.Add("colPutSprd", "Sprd");
        _dgvChain.Columns.Add("colContracts", "Conts");
        _dgvChain.Columns.Add("colLevel", "Level");

        foreach (DataGridViewColumn col in _dgvChain.Columns) col.Width = 50; // half the default (100)
    }

    // Same column set (names/order) as Form1's real dgvTrades, so the simulator's practice trades
    // read exactly like the live grid. StrikePrice is a button column (not plain text) per request
    // — purely visual here, nothing depends on clicking it (unlike dgvQuotes' strike buttons,
    // which actually open a trade).
    private void BuildTradesColumns()
    {
        _dgvTrades.Columns.Add("colSimTime", "Time");
        _dgvTrades.Columns.Add("colSimType", "Type");
        _dgvTrades.Columns.Add(new DataGridViewButtonColumn { Name = "colSimStrike", HeaderText = "StrikePrice" });
        _dgvTrades.Columns.Add("colSimBid", "Bid");
        _dgvTrades.Columns.Add("colSimAsk", "Ask");
        _dgvTrades.Columns.Add("colSimContracts", "Contracts");
        _dgvTrades.Columns.Add("colSimEntryPrice", "EntryPrice");
        _dgvTrades.Columns.Add("colSimCBid", "C_Bid");
        _dgvTrades.Columns.Add("colSimTBid", "T_Bid");
        _dgvTrades.Columns.Add("colSimPnl", "PnL");
        _dgvTrades.Columns.Add("colSimPnlPct", "PnL_Percent");
        _dgvTrades.Columns.Add("colSimPnlTarget", "PnL_Target");
        _dgvTrades.Columns.Add("colSimExitTime", "ExitTime");
        var closeCol = new DataGridViewButtonColumn { Name = "colSimClose", HeaderText = "", Text = "Close", UseColumnTextForButtonValue = true };
        _dgvTrades.Columns.Add(closeCol);
        _dgvTrades.Columns.Add("colSimPnlMin", "Min PnL%");
        _dgvTrades.Columns.Add("colSimPnlMax", "Max PnL%");
        _dgvTrades.CellContentClick += DgvTrades_CellContentClick;
    }

    // Clicking the Strike cell opens a demo trade at the current step's Bid/Ask for that row's
    // option type — same interaction the real grid uses (Form1.DgvQuotes_CellClick), just against
    // simulated data and writing to SimTradesStore instead of the real trade flow.
    private void DgvChain_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _currentIndex < 0) return;
        if (e.ColumnIndex != _dgvChain.Columns["colStrikePrice"].Index) return;

        var row      = _dgvChain.Rows[e.RowIndex];
        var rowType  = row.Tag?.ToString() ?? "CALL";
        var strikeOk = decimal.TryParse(row.Cells["colStrikePrice"].Value?.ToString(), out var strike);
        if (!strikeOk) return;

        var bidCol = rowType == "CALL" ? "colCallBid" : "colPutBid";
        var askCol = rowType == "CALL" ? "colCallAsk" : "colPutAsk";
        decimal.TryParse(row.Cells[bidCol].Value?.ToString(), out var bid);
        decimal.TryParse(row.Cells[askCol].Value?.ToString(), out var ask);
        if (ask <= 0) return; // same guard as the real grid — never open on an illiquid quote

        int.TryParse(row.Cells["colContracts"].Value?.ToString(), out var contracts);
        if (contracts <= 0) contracts = 1;

        // Same target% source and formula Form1.RecordEntryAsync uses — informational only here
        // (T_Bid/PnL_Target are shown for parity with the real grid; the simulator's trades still
        // only close via the Close button, no auto-close-at-target).
        decimal.TryParse(TargetSettingsStore.Load(), out var targetPct);
        var tBid = Math.Round(ask * (1 + targetPct / 100m), 2);

        var step = _steps[_currentIndex];
        var gridRow = _dgvTrades.Rows[_dgvTrades.Rows.Add(
            EasternTime(step.Time).ToString("HH:mm:ss"), rowType, strike.ToString("F2"),
            bid.ToString("F2"), ask.ToString("F2"), contracts, ask.ToString("F2"),
            ask.ToString("F2"), tBid.ToString("F2"), "0.00", "0.0", targetPct.ToString("F0"))];

        _openSimTrades.Add(new OpenSimTrade(gridRow, rowType, strike, contracts, step.Time, ask, tBid));
    }

    // Recomputes PnL for every open demo trade against the current step's chain — same formula
    // Form1 uses for real/demo trades (currentBid - entryPrice) * contracts * 100 — and the same
    // Min/Max PnL% tracking (UpdatePnLMinMax below, copied from Form1's private static method).
    private void RefreshOpenSimTradesPnL(SimulationStep step)
    {
        foreach (var trade in _openSimTrades)
        {
            var quote = step.Quotes.FirstOrDefault(q =>
                q.StrikePrice == trade.StrikePrice &&
                q.OptionType.ToString().ToUpperInvariant() == trade.OptionType);
            if (quote == null) continue;

            var pnl    = Math.Round((quote.Bid - trade.EntryPrice) * trade.Contracts * 100, 2);
            var pnlPct = trade.EntryPrice > 0 ? Math.Round((quote.Bid - trade.EntryPrice) / trade.EntryPrice * 100, 1) : 0m;

            trade.Row.Cells["colSimCBid"].Value    = quote.Bid.ToString("F2");
            trade.Row.Cells["colSimPnl"].Value     = pnl.ToString("F2");
            trade.Row.Cells["colSimPnlPct"].Value  = pnlPct.ToString("F1");
            trade.Row.Cells["colSimPnl"].Style.ForeColor = pnl >= 0 ? Color.LimeGreen : Color.OrangeRed;
            UpdatePnLMinMax(trade.Row, pnlPct);
        }
    }

    // Same logic as Form1's private static UpdatePnLMinMax — kept as its own copy since that one
    // isn't accessible from here (private to Form1) and this simulator is deliberately isolated
    // from the live trade flow.
    private static void UpdatePnLMinMax(DataGridViewRow row, decimal pnlPct)
    {
        var minCell = row.Cells["colSimPnlMin"];
        var maxCell = row.Cells["colSimPnlMax"];

        if (!decimal.TryParse(minCell.Value?.ToString(), out var min) || pnlPct < min)
        {
            minCell.Value           = pnlPct.ToString("F1");
            minCell.Style.ForeColor = Color.Red;
        }

        if (!decimal.TryParse(maxCell.Value?.ToString(), out var max) || pnlPct > max)
        {
            maxCell.Value           = pnlPct.ToString("F1");
            maxCell.Style.ForeColor = Color.Green;
        }
    }

    private void DgvTrades_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _currentIndex < 0) return;
        if (_dgvTrades.Columns[e.ColumnIndex].Name != "colSimClose") return;

        var row   = _dgvTrades.Rows[e.RowIndex];
        var trade = _openSimTrades.FirstOrDefault(t => t.Row == row);
        if (trade == null) return; // already closed

        var step  = _steps[_currentIndex];
        var quote = step.Quotes.FirstOrDefault(q =>
            q.StrikePrice == trade.StrikePrice &&
            q.OptionType.ToString().ToUpperInvariant() == trade.OptionType);
        var exitPrice = quote?.Bid ?? decimal.Parse(row.Cells["colSimCBid"].Value?.ToString() ?? "0");

        var pnl    = Math.Round((exitPrice - trade.EntryPrice) * trade.Contracts * 100, 2);
        var pnlPct = trade.EntryPrice > 0 ? Math.Round((exitPrice - trade.EntryPrice) / trade.EntryPrice * 100, 1) : 0m;

        SimTradesStore.Append(_symbol, _simDate, trade.OptionType, trade.StrikePrice, trade.Contracts,
            EasternTime(trade.EntryTime), trade.EntryPrice, EasternTime(step.Time), exitPrice, pnl, pnlPct);

        row.Cells["colSimExitTime"].Value = EasternTime(step.Time).ToString("HH:mm:ss");
        UpdatePnLMinMax(row, pnlPct);

        // DataGridViewButtonColumn with UseColumnTextForButtonValue=true always shows the
        // column's own Text ("Close") regardless of the cell's Value, so gray out the row instead
        // to signal visually that it's closed (row.ReadOnly already blocks re-clicking it).
        foreach (DataGridViewCell cell in row.Cells) cell.Style.BackColor = Color.Gainsboro;
        row.ReadOnly = true;
        _openSimTrades.Remove(trade);
    }
}
