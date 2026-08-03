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

    // Width matches Form1's real dgvQuotes (566) — see BuildChainColumns' per-column widths.
    private readonly DataGridView _dgvChain = new()
    {
        Location = new Point(8, 74), Size = new Size(566, 220),
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect
    };

    // 3-column TableLayoutPanel (same pattern as MultiChartForm's own chart row) — deterministic
    // left-to-right order, unlike stacking multiple Dock=Left panels. Height matches the candle
    // panel area MultiChartForm actually renders at (Form Height 530, minus its 88px toolbar and
    // title bar/padding) so the simulator's charts look the same size as the real form's.
    private readonly TableLayoutPanel _chartsHost = new()
    {
        Location = new Point(8, 302), Size = new Size(900, 400),
        ColumnCount = 3, RowCount = 1
    };
    private readonly SimulatedChartPanel _hourlyChart = new("1h", ChartPanelMode.Hourly15) { Dock = DockStyle.Fill };
    private readonly SimulatedChartPanel _rthChart    = new("15m RTH", ChartPanelMode.Fifteen_RTH) { Dock = DockStyle.Fill };
    private readonly SimulatedChartPanel _fullChart   = new("15m RTH+Overnight", ChartPanelMode.Fifteen_Full) { Dock = DockStyle.Fill };

    // Cross-SMA (20/40/100/200), T-Line toggle/Clear, and a log of everything they (and the
    // once-per-day Rebote Diario check) detect — log-only, no Telegram, no persistence, per
    // request ("es un simulador"). The toggle buttons stay up top; the log itself lives below the
    // trades grid at the bottom of the form.
    private readonly Panel _pnlSmaEvents = new() { Location = new Point(590, 168), Size = new Size(440, 30) };
    private readonly TextBox _txtEventLog = new()
    {
        Location = new Point(8, 848), Size = new Size(1000, 90),
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 8.5F), BackColor = Color.Black, ForeColor = Color.LightGreen
    };

    // Same column-sizing behavior as Form1's real dgvTrades: AutoSizeColumnsMode.Fill stretches
    // the 16 columns proportionally across whatever width the grid has, instead of a fixed pixel
    // width per column (dgvTrades never sets one either).
    private readonly DataGridView _dgvTrades = new()
    {
        Location = new Point(8, 710), Size = new Size(1000, 130),
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        RowTemplate = { Height = 22 }
    };

    // Counts and Contracts, same options/behavior as Form1's grpCounts/grpContracts, but session-
    // only local fields (never written to CountsSettingsStore-equivalent or
    // ContractsSettingsStore) — this is a simulator, it must never touch the real app's settings.
    private readonly GroupBox _grpCounts    = new() { Text = "Counts", Location = new Point(590, 4), Size = new Size(185, 64) };
    private readonly GroupBox _grpContracts = new() { Text = "Contracts", Location = new Point(785, 4), Size = new Size(105, 96) };
    private string _selectedCounts    = "6"; // same default as Form1's _selectedCounts
    private string _selectedContracts = "1"; // same default as Form1's rbContracts1.Checked

    // "Go to time" — two rows of plain buttons (no typing): pick an RTH hour, then which 15-min
    // candle within it, and jump straight to the step nearest that time. Whichever is clicked
    // second (hour or minute — either order works) triggers the jump once both are set.
    private readonly Panel _pnlGoToTime = new() { Location = new Point(590, 104), Size = new Size(440, 60) };
    private int? _goToHour;
    private int? _goToMinute;

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
        Width         = 1040;
        Height        = 1000;
        StartPosition = FormStartPosition.CenterScreen;

        BuildChainColumns();
        BuildTradesColumns();
        BuildCountsAndContractsGroups();
        BuildGoToTimeButtons();
        BuildSmaEventControls();

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
        Controls.Add(_grpCounts);
        Controls.Add(_grpContracts);
        Controls.Add(_pnlGoToTime);
        Controls.Add(_pnlSmaEvents);
        Controls.Add(_txtEventLog);
        Controls.Add(_chartsHost);
        Controls.Add(_dgvTrades);

        _cmbSymbol.SelectedIndexChanged += (s, e) => RefreshAvailableDates();
        _btnCargar.Click    += (s, e) => LoadSelectedDay();
        _btnAtras.Click     += (s, e) => Step(-1);
        _btnAdelante.Click  += (s, e) => Step(1);
        _dgvChain.CellClick     += DgvChain_CellClick;
        _dgvChain.CellPainting  += DgvChain_CellPainting;
        _dgvChain.CellFormatting += DgvChain_CellFormatting;

        Load += (s, e) => LoadSymbols();
    }

    // Same options as Form1's grpCounts (3-10, In Range) and grpContracts (1-6, PositionSize),
    // built programmatically instead of via the designer since this form has none. Selecting one
    // just updates the local field and re-renders the current step — no settings file touched.
    private void BuildCountsAndContractsGroups()
    {
        var counts = new[] { "3", "4", "5", "6", "In Range" };
        for (int i = 0; i < counts.Length; i++)
        {
            var rb = new RadioButton
            {
                Text     = counts[i],
                Checked  = counts[i] == _selectedCounts,
                AutoSize = true,
                Location = new Point(6 + (i % 3) * 58, 20 + (i / 3) * 20)
            };
            rb.CheckedChanged += (s, e) =>
            {
                if (rb.Checked) { _selectedCounts = rb.Text; ApplyRadioStyle(_grpCounts); RenderCurrentStep(); }
            };
            _grpCounts.Controls.Add(rb);
        }
        ApplyRadioStyle(_grpCounts);

        var contracts = new[] { "1", "2", "3", "4", "5", "6", "PositionSize" };
        for (int i = 0; i < contracts.Length; i++)
        {
            var rb = new RadioButton
            {
                Text     = contracts[i],
                Checked  = contracts[i] == _selectedContracts,
                Location = contracts[i] == "PositionSize" ? new Point(6, 64) : new Point(6 + (i % 3) * 32, 20 + (i / 3) * 20),
                Size     = contracts[i] == "PositionSize" ? new Size(93, 18) : new Size(28, 18)
            };
            rb.CheckedChanged += (s, e) =>
            {
                if (rb.Checked) { _selectedContracts = rb.Text; ApplyRadioStyle(_grpContracts); RenderCurrentStep(); }
            };
            _grpContracts.Controls.Add(rb);
        }
        ApplyRadioStyle(_grpContracts);
    }

    private static void ApplyRadioStyle(GroupBox group)
    {
        foreach (var rb in group.Controls.OfType<RadioButton>())
        {
            rb.ForeColor = rb.Checked ? Color.Green : SystemColors.ControlText;
            rb.Font = new Font(rb.Font, rb.Checked ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    // Same formula as Form1's private CalcContracts/GetPositionSize — position size % and
    // account balance are read (not written) from the real stores, since those aren't part of
    // what this feature asked to keep simulator-local; only Counts/Contracts stay local.
    private static string CalcSimContracts(decimal positionSize, decimal ask)
    {
        if (ask <= 0 || positionSize <= 0) return string.Empty;
        var contracts = Math.Floor(positionSize / (ask * 100));
        return contracts > 0 ? contracts.ToString("F0") : string.Empty;
    }

    private string GetSimContractsValue(decimal ask)
    {
        if (_selectedContracts != "PositionSize" && int.TryParse(_selectedContracts, out var fixedCount))
            return fixedCount.ToString();

        var balance = BalanceStore.Load();
        decimal.TryParse(PositionSizeSettingsStore.Load(), out var pct);
        return CalcSimContracts(balance * pct / 100m, ask);
    }

    // Cross-SMA (20/40/100/200) + T-Line/Clear on the 1h simulated chart — same toggle mechanics
    // as MultiChartForm's buttons, wired to SimulatedChartPanel's ported copies of the live
    // detection logic. Everything they detect (plus the once-per-load Rebote Diario check) goes
    // to _txtEventLog only.
    private void BuildSmaEventControls()
    {
        var periods = new[] { 20, 40, 100, 200 };
        var smaButtons = new List<Button>();
        for (int i = 0; i < periods.Length; i++)
        {
            var period = periods[i];
            var btn = new Button { Text = period.ToString(), Location = new Point(i * 46, 0), Size = new Size(42, 24) };
            btn.Click += (s, e) =>
            {
                var (armed, up) = _hourlyChart.ToggleCrossMonitor(period);
                btn.Text = armed ? $"{(up ? "↑" : "↓")}{period}" : period.ToString();
                btn.BackColor = armed ? (up ? Color.LightGreen : Color.LightSalmon) : SystemColors.Control;
            };
            smaButtons.Add(btn);
            _pnlSmaEvents.Controls.Add(btn);
        }

        var btnTLine = new Button { Text = "T-Line", Location = new Point(190, 0), Size = new Size(60, 24) };
        btnTLine.Click += async (s, e) =>
        {
            var on = await _hourlyChart.ToggleTLineModeAsync();
            btnTLine.BackColor = on ? Color.Orange : SystemColors.Control;
        };
        var btnClear = new Button { Text = "Clear", Location = new Point(254, 0), Size = new Size(60, 24) };
        btnClear.Click += async (s, e) =>
        {
            await _hourlyChart.ClearTLineAsync();
            btnTLine.BackColor = SystemColors.Control;
        };
        _pnlSmaEvents.Controls.Add(btnTLine);
        _pnlSmaEvents.Controls.Add(btnClear);

        _hourlyChart.OnCrossSequenceEvent += msg => LogSimEvent(msg);
        _hourlyChart.OnTLineSignalEvent   += msg => LogSimEvent(msg);
        _hourlyChart.OnCrossSequenceFinished += () =>
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                foreach (var (btn, period) in smaButtons.Zip(periods))
                {
                    btn.Text = period.ToString();
                    btn.BackColor = SystemColors.Control;
                }
            });
        };
    }

    private void LogSimEvent(string message)
    {
        if (IsDisposed) return;
        void Append() => _txtEventLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        if (InvokeRequired) BeginInvoke(Append); else Append();
    }

    private void BuildGoToTimeButtons()
    {
        var hourButtons = new List<Button>();
        var hours = new[] { 9, 10, 11, 12, 13, 14, 15 }; // RTH: 9:30-16:00 ET
        for (int i = 0; i < hours.Length; i++)
        {
            var hour = hours[i];
            var btn = new Button { Text = hour.ToString(), Location = new Point(i * 40, 0), Size = new Size(36, 24) };
            btn.Click += (s, e) =>
            {
                _goToHour = hour;
                foreach (var b in hourButtons) b.BackColor = SystemColors.Control;
                btn.BackColor = Color.LightGreen;
                TryJumpToSelectedTime();
            };
            hourButtons.Add(btn);
            _pnlGoToTime.Controls.Add(btn);
        }

        var minuteButtons = new List<Button>();
        var minutes = new[] { 0, 15, 30, 45 };
        for (int i = 0; i < minutes.Length; i++)
        {
            var minute = minutes[i];
            var btn = new Button { Text = minute.ToString("D2"), Location = new Point(i * 40, 30), Size = new Size(36, 24) };
            btn.Click += (s, e) =>
            {
                _goToMinute = minute;
                foreach (var b in minuteButtons) b.BackColor = SystemColors.Control;
                btn.BackColor = Color.LightGreen;
                TryJumpToSelectedTime();
            };
            minuteButtons.Add(btn);
            _pnlGoToTime.Controls.Add(btn);
        }
    }

    // Jumps to whichever loaded step is closest to hour:minute on the currently loaded
    // _simDate — steps are ~6s poll snapshots, not exactly on the minute, so "closest" (not
    // "exact match") is what actually lands on the requested 15-min candle.
    private void TryJumpToSelectedTime()
    {
        if (_goToHour is not { } hour || _goToMinute is not { } minute) return;
        if (_steps.Count == 0) return;

        var targetEastern = _simDate.ToDateTime(new TimeOnly(hour, minute));
        var targetUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(targetEastern, DateTimeKind.Unspecified), EasternZone);

        var closestIndex = 0;
        var closestDiff = TimeSpan.MaxValue;
        for (int i = 0; i < _steps.Count; i++)
        {
            var diff = (_steps[i].Time - targetUtc).Duration();
            if (diff < closestDiff) { closestDiff = diff; closestIndex = i; }
        }

        _currentIndex = closestIndex;
        RenderCurrentStep();
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

    private async void LoadSelectedDay()
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

        _txtEventLog.Clear();
        EvaluateDailyBounce();

        // A new day's candles are about to load — clear each chart's stuck pan/zoom from
        // whatever day was showing before, so this day's 9:30 candle lands at the real open
        // instead of wherever the previous day's view happened to be scrolled/zoomed to.
        await Task.WhenAll(
            _hourlyChart.ResetViewForNewDayAsync(),
            _rthChart.ResetViewForNewDayAsync(),
            _fullChart.ResetViewForNewDayAsync());

        UpdateStepButtons();
        RenderCurrentStep();
    }

    // Ported from ChartPanel.EvaluateDailyBounce, once per load (not per step) — checks the last
    // daily candle BEFORE _simDate (i.e. "yesterday" relative to the replayed day) against the
    // daily SMA20, same case-1/case-2 bounce formula as Cross-SMA. Log-only, no Telegram.
    private void EvaluateDailyBounce()
    {
        var daily = CandleAggregation.AggregateToDaily(_hourlyCandles).Where(d => d.Date < _simDate).ToList();
        const int period = 20;
        if (daily.Count < period + 1) return;

        var bars = daily.Select(d => d.Candle).ToList();
        var justClosed = bars[^1];

        decimal sum = 0;
        for (int i = bars.Count - period; i < bars.Count; i++) sum += bars[i].Close;
        var sma20 = sum / period;

        var isGreen = justClosed.Close > justClosed.Open;
        var isRed   = justClosed.Close < justClosed.Open;
        const decimal proximityRatio = 0.30m;

        var bouncedDown = justClosed.Open < sma20 && isRed &&
            (justClosed.High > sma20
                ? justClosed.Close < sma20
                : (sma20 - justClosed.High) < proximityRatio * (justClosed.High - justClosed.Close));

        var bouncedUp = justClosed.Open > sma20 && isGreen &&
            (justClosed.Low < sma20
                ? justClosed.Close > sma20
                : (justClosed.Low - sma20) < proximityRatio * (justClosed.Close - justClosed.Low));

        if (!bouncedDown && !bouncedUp) return;

        var direction = bouncedUp ? "al alza" : "a la baja";
        LogSimEvent($"Rebote {direction} en Diario");
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

        Form1.PopulateQuotesGrid(_dgvChain, step.Quotes, _ticker, applyCountsFilter: true, selectedCounts: _selectedCounts);

        // PopulateQuotesGrid computes its own Conts column from the REAL (persisted)
        // ContractsSettingsStore — override it here with the simulator's own local Contracts
        // selection instead, against the same Ask shown in this grid.
        foreach (DataGridViewRow row in _dgvChain.Rows)
        {
            var askCol = row.Tag?.ToString() == "PUT" ? "colPutAsk" : "colCallAsk";
            decimal.TryParse(row.Cells[askCol].Value?.ToString(), out var ask);
            row.Cells["colContracts"].Value = GetSimContractsValue(ask);
        }

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

        // Same per-column widths as Form1's real dgvQuotes (same column order too), instead of a
        // uniform halved width — so the chain grid reads exactly like the live one.
        int[] widths = { 80, 62, 33, 33, 33, 50, 60, 33, 33, 33, 48, 45 };
        for (int i = 0; i < _dgvChain.Columns.Count; i++) _dgvChain.Columns[i].Width = widths[i];
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
        _dgvTrades.Columns.Add("colSimMoneyness", "OTM/ITM");
        _dgvTrades.CellContentClick += DgvTrades_CellContentClick;
    }

    // Same rendering as Form1.DgvQuotes_CellPainting — a colored button (green CALL / red PUT /
    // gray if illiquid) instead of plain text, since the Strike cell is clickable here too
    // (DgvChain_CellClick below).
    // Same rules as Form1.DgvQuotes_CellFormatting: Sprd bold red, Ask bold dark green, and Bid
    // background light green when that side's Sprd <= 2 (FormatSprd strips the decimal point, so
    // "2" == a real spread of $0.02 — this is the "bid/ask difference < 0.02" rule).
    private void DgvChain_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _dgvChain.Rows[e.RowIndex];

        var callSprdCol = _dgvChain.Columns["colCallSprd"].Index;
        var putSprdCol  = _dgvChain.Columns["colPutSprd"].Index;
        var callBidCol  = _dgvChain.Columns["colCallBid"].Index;
        var putBidCol   = _dgvChain.Columns["colPutBid"].Index;
        var callAskCol  = _dgvChain.Columns["colCallAsk"].Index;
        var putAskCol   = _dgvChain.Columns["colPutAsk"].Index;

        if (e.ColumnIndex == callSprdCol || e.ColumnIndex == putSprdCol)
        {
            e.CellStyle.ForeColor = Color.Red;
            e.CellStyle.Font = new Font(_dgvChain.Font, FontStyle.Bold);
        }

        if (e.ColumnIndex == callAskCol || e.ColumnIndex == putAskCol)
        {
            e.CellStyle.ForeColor = Color.DarkGreen;
            e.CellStyle.Font = new Font(_dgvChain.Font, FontStyle.Bold);
        }

        if (e.ColumnIndex == callBidCol)
        {
            e.CellStyle.BackColor = decimal.TryParse(row.Cells["colCallSprd"].Value?.ToString(), out var callSprd) && callSprd <= 2
                ? Color.LightGreen : _dgvChain.DefaultCellStyle.BackColor;
        }

        if (e.ColumnIndex == putBidCol)
        {
            e.CellStyle.BackColor = decimal.TryParse(row.Cells["colPutSprd"].Value?.ToString(), out var putSprd) && putSprd <= 2
                ? Color.LightGreen : _dgvChain.DefaultCellStyle.BackColor;
        }
    }

    private void DgvChain_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (e.ColumnIndex != _dgvChain.Columns["colStrikePrice"].Index) return;

        var val      = e.Value?.ToString();
        var row      = _dgvChain.Rows[e.RowIndex];
        var rowType  = row.Tag?.ToString();
        var bidCol   = rowType == "PUT" ? "colPutBid" : "colCallBid";
        var disabled = !decimal.TryParse(row.Cells[bidCol].Value?.ToString(), out var bid) || bid == 0m;

        e.PaintBackground(e.ClipBounds, true);

        if (!string.IsNullOrEmpty(val))
        {
            var btnColor  = disabled ? Color.LightGray : (rowType == "PUT" ? Color.Red : Color.DarkGreen);
            var textColor = disabled ? Color.Gray : Color.White;
            var btnRect   = Rectangle.Inflate(e.CellBounds, -3, -3);

            using var fillBrush = new SolidBrush(btnColor);
            using var borderPen = new Pen(ControlPaint.Dark(btnColor, 0.2f));
            using var textFont  = new Font(_dgvChain.Font, FontStyle.Bold);

            e.Graphics!.FillRectangle(fillBrush, btnRect);
            e.Graphics.DrawRectangle(borderPen, btnRect);

            TextRenderer.DrawText(
                e.Graphics, val, textFont, btnRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        e.Handled = true;
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
        SetSimMoneyness(gridRow, rowType, strike, step.UnderlyingPrice);

        // Green "Stk=xxx" line on all 3 simulated charts — same as the real app's demo/real trades.
        _ = _hourlyChart.MarkStrikeAsync(strike);
        _ = _rthChart.MarkStrikeAsync(strike);
        _ = _fullChart.MarkStrikeAsync(strike);
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
            SetSimMoneyness(trade.Row, trade.OptionType, trade.StrikePrice, step.UnderlyingPrice);
        }
    }

    // Same rule as Form1's SetMoneyness: CALL is ITM once spot > strike, PUT once spot < strike.
    private static void SetSimMoneyness(DataGridViewRow row, string optionType, decimal strike, decimal spot)
    {
        var cell  = row.Cells["colSimMoneyness"];
        var isItm = optionType == "CALL" ? spot > strike : spot < strike;
        cell.Value           = isItm ? "ITM" : "OTM";
        cell.Style.ForeColor = isItm ? Color.Green : Color.Red;
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
