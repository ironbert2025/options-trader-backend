using OptionsTrader.Application.DTOs.Options;
using OptionsTrader.Application.DTOs.Streaming;

namespace OptionsTrader.WinForms;

// Standalone "replay" window for the 4 core ETFs (SPY, QQQ, IWM, DIA) — shows only the 15m
// RTH+Overnight price action for each, side by side (SPY/QQQ on top, IWM/DIA on bottom), with a
// single shared Demand/Supply zone drawing toggle across all 4 charts. Disk-only, same as
// SimulatorForm — no live streaming/polling connection, so it can be open at the same time as
// everything else.
//
// Unlike SimulatorForm there is no single options-chain data source to derive a step timeline
// from here (4 symbols, not 1). The raw tick store has tens of thousands of individual price
// ticks per symbol per day — stepping through every single one (union across all 4 symbols) made
// Play both slow (a full re-aggregate of all 4 series per tick) and uneven (each merged-timeline
// step only ever belongs to ONE symbol, leaving the other 3 charts frozen until their own next
// tick happened to come up). So the master timeline here is a simulated-time cursor at a fixed
// 1-second cadence instead — every step advances all 4 charts together, each independently
// aggregating only ITS OWN ticks up to that instant. The options-chain grid (one symbol at a
// time, picked via the selector buttons) rides the same cursor, showing the nearest recorded
// poll snapshot at or before it.
public class FourEtfSimulatorForm : Form
{
    private static readonly string[] Symbols = { "SPY", "QQQ", "IWM", "DIA" };

    private readonly ComboBox _cmbDate  = new() { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(8, 8), Size = new Size(120, 24) };
    private readonly Button _btnCargar  = new() { Text = "Cargar", Location = new Point(136, 8), Size = new Size(70, 24) };
    private readonly Button _btnPlayPause = new() { Text = "Play", Location = new Point(216, 8), Size = new Size(70, 24), Enabled = false };
    private readonly Button _btnDzSz    = new() { Text = "DZ/SZ", Location = new Point(296, 8), Size = new Size(80, 26) };
    private readonly Label _lblStep     = new() { Location = new Point(386, 14), Size = new Size(320, 20), Text = "Sin datos cargados" };
    private readonly Button _btnAtras   = new() { Text = "◀ Atrás", Location = new Point(8, 40), Size = new Size(90, 26), Enabled = false };
    private readonly Button _btnAdelante = new() { Text = "Adelante ▶", Location = new Point(102, 40), Size = new Size(90, 26), Enabled = false };

    private readonly System.Windows.Forms.Timer _playTimer = new();
    private bool _isPlaying;
    private int _ticksPerSecond = 60;
    private readonly GroupBox _grpSpeed = new() { Text = "Speed", Location = new Point(620, 4), Size = new Size(130, 96) };

    // 2x2 grid, each cell the same size as SimulatorForm's RTH+Overnight panel (~460x400) — SPY/QQQ
    // top, IWM/DIA bottom. Single shared DZ/SZ toggle (above) arms drawing on all 4 at once; each
    // chart still selects/deletes (click + Del) its own zones independently, same as always.
    private readonly TableLayoutPanel _chartsHost = new()
    {
        Location = new Point(8, 104), Size = new Size(920, 800),
        ColumnCount = 2, RowCount = 2
    };

    private readonly Dictionary<string, SimulatedChartPanel> _charts = new();
    // Hora (ET) del último tick realmente usado en el render de cada chart en el paso actual —
    // permite ver si algún símbolo se está quedando atrás del cursor compartido (cada uno solo
    // tiene los ticks reales que le tocaron, no todos avanzan parejo tick a tick).
    private readonly Dictionary<string, Label> _timeLabels = new();
    private readonly Dictionary<string, List<CandleData>> _candlesBySymbol = new();
    private readonly Dictionary<string, List<SimulationStep>> _optionStepsBySymbol = new();
    private readonly Dictionary<string, TickerEntry> _tickers = new();

    // Options-chain grid — one symbol at a time, picked via the selector buttons below.
    private readonly Panel _pnlSymbolSelector = new() { Location = new Point(940, 104), Size = new Size(332, 34) };
    private readonly Dictionary<string, Button> _symbolSelectorButtons = new();
    private string? _selectedGridSymbol;
    private readonly DataGridView _dgvChain = new()
    {
        Location = new Point(940, 144), Size = new Size(332, 400),
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect
    };

    // "Rebote en Zona" log — the only event logged for now, per explicit request.
    private readonly TextBox _txtEventLog = new()
    {
        Location = new Point(8, 914), Size = new Size(1264, 90),
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 8.5F), BackColor = Color.Black, ForeColor = Color.LightGreen
    };

    // Fixed 1-second-of-simulated-market-time cadence — "N tick/seg" means N of these steps
    // advance per real second, i.e. N simulated seconds per real second (playback speed), not N
    // raw ticks.
    private const int StepTimeSeconds = 1;

    private List<DateOnly> _availableDates = new();
    private DateTime _sessionStartUtc;
    private DateTime _sessionEndUtc;
    private int _totalSteps;
    private int _currentIndex = -1;
    private DateOnly _simDate;

    public FourEtfSimulatorForm()
    {
        Text          = "Simulador 4 ETF";
        Width         = 1300;
        Height        = 1080;
        StartPosition = FormStartPosition.CenterScreen;

        foreach (var symbol in Symbols)
            _charts[symbol] = new SimulatedChartPanel($"{symbol} — 15m RTH+Overnight", ChartPanelMode.Fifteen_Full) { Dock = DockStyle.Fill };

        _chartsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _chartsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _chartsHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        _chartsHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        _chartsHost.Controls.Add(BuildChartCell("SPY"), 0, 0);
        _chartsHost.Controls.Add(BuildChartCell("QQQ"), 1, 0);
        _chartsHost.Controls.Add(BuildChartCell("IWM"), 0, 1);
        _chartsHost.Controls.Add(BuildChartCell("DIA"), 1, 1);

        BuildChainColumns();
        BuildSymbolSelector();

        Controls.Add(_cmbDate);
        Controls.Add(_btnCargar);
        Controls.Add(_btnPlayPause);
        Controls.Add(_btnDzSz);
        Controls.Add(_lblStep);
        Controls.Add(_btnAtras);
        Controls.Add(_btnAdelante);
        Controls.Add(_grpSpeed);
        BuildSpeedControls();
        Controls.Add(_chartsHost);
        Controls.Add(_pnlSymbolSelector);
        Controls.Add(_dgvChain);
        Controls.Add(_txtEventLog);

        _btnCargar.Click    += (s, e) => LoadSelectedDay();
        _btnPlayPause.Click += (s, e) => TogglePlay();
        _btnAtras.Click     += (s, e) => Step(-1);
        _btnAdelante.Click  += (s, e) => Step(1);
        _playTimer.Tick     += PlayTimer_Tick;
        _dgvChain.CellPainting   += DgvChain_CellPainting;
        _dgvChain.CellFormatting += DgvChain_CellFormatting;

        // Single shared toggle — arms/disarms DZ/SZ drawing mode on all 4 charts together. Each
        // chart still tracks its own zones/selection independently; deleting one (click + Del)
        // never touches the others.
        _btnDzSz.Click += async (s, e) =>
        {
            // All 4 charts only ever get toggled through this one shared button, so they stay in
            // lockstep — toggling each one flips them together.
            bool on = false;
            foreach (var chart in _charts.Values) on = await chart.ToggleDzSzModeAsync();
            _btnDzSz.BackColor = on ? Color.LightGreen : SystemColors.Control;
        };

        // "Rebote en Zona" — the only event logged for now, one subscription per chart/symbol so
        // the log line can be prefixed with which of the 4 ETFs it came from.
        foreach (var (symbol, chart) in _charts)
        {
            chart.OnDemandZoneReboundEvent += (caption, price, proximal, distal) => LogEvent(symbol, caption);
            chart.OnSupplyZoneReboundEvent += (caption, price, proximal, distal) => LogEvent(symbol, caption);
        }

        Load += (s, e) => { RefreshAvailableDates(); LoadTickers(); };
    }

    // Wraps each chart with a label on top showing the timestamp (ET) of the last tick actually
    // rendered for that symbol in the current step — lets you see at a glance whether one of the
    // 4 is lagging behind the shared time cursor (each symbol only has whatever real ticks it got).
    private Panel BuildChartCell(string symbol)
    {
        var lbl = new Label
        {
            Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Consolas", 9F, FontStyle.Bold),
            Text = $"{symbol} — sin datos"
        };
        _timeLabels[symbol] = lbl;

        var cell = new Panel { Dock = DockStyle.Fill };
        cell.Controls.Add(_charts[symbol]);
        cell.Controls.Add(lbl);
        return cell;
    }

    private void LogEvent(string symbol, string message)
    {
        if (IsDisposed) return;
        void Append() => _txtEventLog.AppendText($"{DateTime.Now:HH:mm:ss}  [{symbol}] {message}{Environment.NewLine}");
        if (InvokeRequired) BeginInvoke(Append); else Append();
    }

    private void LoadTickers()
    {
        var tickers = TickerSettingsStore.Load();
        _tickers.Clear();
        foreach (var symbol in Symbols)
        {
            var ticker = tickers.FirstOrDefault(t => t.Symbol == symbol);
            if (ticker != null) _tickers[symbol] = ticker;
        }
    }

    // 4 small toggle buttons — clicking one picks which symbol's options chain the single grid
    // shows; doesn't touch the 4 charts at all.
    private void BuildSymbolSelector()
    {
        for (int i = 0; i < Symbols.Length; i++)
        {
            var symbol = Symbols[i];
            var btn = new Button { Text = symbol, Location = new Point(i * 82, 0), Size = new Size(78, 28) };
            btn.Click += (s, e) =>
            {
                _selectedGridSymbol = symbol;
                foreach (var (sym, b) in _symbolSelectorButtons) b.BackColor = sym == symbol ? Color.LightGreen : SystemColors.Control;
                RenderOptionsGrid();
            };
            _symbolSelectorButtons[symbol] = btn;
            _pnlSymbolSelector.Controls.Add(btn);
        }
        // No symbol pre-selected — the grid stays empty until the user explicitly picks one.
    }

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

        int[] widths = { 60, 55, 30, 30, 30, 45, 55, 30, 30, 30, 40, 40 };
        for (int i = 0; i < _dgvChain.Columns.Count; i++) _dgvChain.Columns[i].Width = widths[i];
    }

    // Same rendering as SimulatorForm.DgvChain_CellPainting/CellFormatting — colored Strike
    // button (green CALL / red PUT / gray if illiquid), Sprd bold red, Ask bold dark green — so
    // this grid reads exactly like the individual simulator's.
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
            TextRenderer.DrawText(e.Graphics, val, textFont, btnRect, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        e.Handled = true;
    }

    private void DgvChain_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var callSprdCol = _dgvChain.Columns["colCallSprd"].Index;
        var putSprdCol  = _dgvChain.Columns["colPutSprd"].Index;
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
    }

    private void BuildSpeedControls()
    {
        var speeds = new (string Label, int TicksPerSecond)[] { ("60 tick/seg", 60), ("120 tick/seg", 120), ("180 tick/seg", 180), ("240 tick/seg", 240) };
        for (int i = 0; i < speeds.Length; i++)
        {
            var (label, ticksPerSecond) = speeds[i];
            var rb = new RadioButton
            {
                Text     = label,
                Checked  = ticksPerSecond == _ticksPerSecond,
                AutoSize = true,
                Location = new Point(6, 18 + i * 18)
            };
            rb.CheckedChanged += (s, e) =>
            {
                if (!rb.Checked) return;
                _ticksPerSecond = ticksPerSecond;
            };
            _grpSpeed.Controls.Add(rb);
        }
    }

    // Days with actual underlying tick data for at least one of the 4 symbols — the options-chain
    // (IV-folder) based GetAvailableDates would show dates with no underlying ticks to replay.
    private void RefreshAvailableDates()
    {
        _cmbDate.Items.Clear();
        _availableDates = Symbols
            .SelectMany(SimulationDataLoader.GetAvailableTickDates)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();
        foreach (var d in _availableDates) _cmbDate.Items.Add(d.ToString("yyyy-MM-dd"));
        if (_cmbDate.Items.Count > 0) _cmbDate.SelectedIndex = 0;
    }

    private async void LoadSelectedDay()
    {
        if (_cmbDate.SelectedItem is not string dateStr || !DateOnly.TryParse(dateStr, out var date)) return;

        PausePlay();
        _simDate = date;

        // Grid goes back to empty on a new day load — the user has to pick a symbol again.
        _selectedGridSymbol = null;
        foreach (var b in _symbolSelectorButtons.Values) b.BackColor = SystemColors.Control;

        _candlesBySymbol.Clear();
        _optionStepsBySymbol.Clear();
        foreach (var symbol in Symbols)
        {
            _candlesBySymbol[symbol]     = SimulationDataLoader.LoadUnderlyingCandlesWithContext(symbol, date, contextDays: 3);
            _optionStepsBySymbol[symbol] = SimulationDataLoader.LoadDay(symbol, date);
        }

        // Session window = 9:30 ET onward (the raw tick store also has pre-market ticks from as
        // early as 4am, which aren't real trading steps to click through) up to the last tick any
        // of the 4 symbols actually recorded that day.
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var sessionStartEastern = date.ToDateTime(new TimeOnly(9, 30));
        _sessionStartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(sessionStartEastern, DateTimeKind.Unspecified), eastern);
        var dayEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var lastTick = _candlesBySymbol.Values
            .SelectMany(c => c)
            .Select(c => c.Time)
            .Where(t => t >= _sessionStartUtc && t < dayEnd)
            .DefaultIfEmpty(_sessionStartUtc)
            .Max();
        _sessionEndUtc = lastTick;

        _totalSteps = Math.Max(0, (int)(_sessionEndUtc - _sessionStartUtc).TotalSeconds / StepTimeSeconds) + 1;
        _currentIndex = _totalSteps > 0 ? 0 : -1;

        if (_totalSteps <= 1)
        {
            MessageBox.Show(
                $"Ninguno de los 4 símbolos tiene ticks de precio grabados en sesión RTH para {date:yyyy-MM-dd}.",
                "Simulador 4 ETF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        await Task.WhenAll(_charts.Values.Select(c => c.ResetViewForNewDayAsync()));

        UpdateStepButtons();
        RenderCurrentStep();
    }

    private void TogglePlay()
    {
        if (_isPlaying) PausePlay();
        else StartPlay();
    }

    // Rendering all 4 charts (+ options grid) into WebView2 is far too heavy to do 60-240
    // times/sec — that's what made "N tick/seg" feel wrong (the timer couldn't keep up, so actual
    // playback speed lagged well below what was selected). So the render rate is fixed and low
    // (10/sec), and each render jumps the simulated-time cursor forward by however many
    // 1-second steps "N tick/seg" implies per render — "60 tick/seg" means 60 simulated seconds
    // (~1 real minute of market data) advance per real second, spread over 10 renders, i.e. 6
    // steps per render — instead of trying to render 60 times/sec.
    private const int RenderHz = 10;

    private void StartPlay()
    {
        if (_currentIndex < 0 || _currentIndex >= _totalSteps - 1) return;
        _isPlaying = true;
        _btnPlayPause.Text = "Pause";
        _playTimer.Interval = 1000 / RenderHz;
        _playTimer.Start();
        UpdateStepButtons();
    }

    private void PausePlay()
    {
        if (!_isPlaying) return;
        _isPlaying = false;
        _btnPlayPause.Text = "Play";
        _playTimer.Stop();
        UpdateStepButtons();
    }

    private void PlayTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentIndex >= _totalSteps - 1) { PausePlay(); return; }
        var stepsPerTick = Math.Max(1, _ticksPerSecond / RenderHz);
        _currentIndex = Math.Min(_currentIndex + stepsPerTick, _totalSteps - 1);
        RenderCurrentStep();
        if (_currentIndex >= _totalSteps - 1) PausePlay();
    }

    private void Step(int direction)
    {
        if (_totalSteps == 0) return;
        var next = _currentIndex + direction;
        if (next < 0 || next >= _totalSteps) return;
        _currentIndex = next;
        RenderCurrentStep();
    }

    private void UpdateStepButtons()
    {
        _btnAtras.Enabled     = !_isPlaying && _currentIndex > 0;
        _btnAdelante.Enabled  = !_isPlaying && _currentIndex >= 0 && _currentIndex < _totalSteps - 1;
        _btnPlayPause.Enabled = _isPlaying || (_currentIndex >= 0 && _currentIndex < _totalSteps - 1);
    }

    // Fast cutoff for "candles up to this instant" — each symbol's list is already sorted by Time
    // (LoadUnderlyingCandlesWithContext concatenates then OrderBy's), so binary search + a single
    // GetRange replaces a full linear Where() scan over the whole day on every one of the ~10
    // renders/sec Play can trigger.
    private static List<CandleData> CandlesUpTo(List<CandleData> sorted, DateTime cutoff)
    {
        var index = sorted.BinarySearch(new CandleData { Time = cutoff }, CandleTimeComparer.Instance);
        var count = index >= 0 ? index + 1 : ~index;
        return count <= 0 ? new List<CandleData>() : sorted.GetRange(0, count);
    }

    private sealed class CandleTimeComparer : IComparer<CandleData>
    {
        public static readonly CandleTimeComparer Instance = new();
        public int Compare(CandleData? x, CandleData? y) => x!.Time.CompareTo(y!.Time);
    }

    // Nearest recorded options-chain poll snapshot at or before cutoff — steps are sorted
    // ascending (SimulationDataLoader.LoadDay groups by time in order), same lookup shape as
    // CandlesUpTo above.
    private static SimulationStep? StepAtOrBefore(List<SimulationStep> steps, DateTime cutoff)
    {
        var index = steps.BinarySearch(new SimulationStep { Time = cutoff }, StepTimeComparer.Instance);
        var count = index >= 0 ? index + 1 : ~index;
        return count <= 0 ? null : steps[count - 1];
    }

    private sealed class StepTimeComparer : IComparer<SimulationStep>
    {
        public static readonly StepTimeComparer Instance = new();
        public int Compare(SimulationStep? x, SimulationStep? y) => x!.Time.CompareTo(y!.Time);
    }

    private DateTime CurrentStepTime() => _sessionStartUtc.AddSeconds((double)_currentIndex * StepTimeSeconds);

    private void RenderCurrentStep()
    {
        UpdateStepButtons();
        if (_currentIndex < 0)
        {
            _lblStep.Text = "Sin datos cargados";
            return;
        }

        var stepTime = CurrentStepTime();
        var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var eastern = TimeZoneInfo.ConvertTimeFromUtc(stepTime, easternZone);
        _lblStep.Text = $"Paso {_currentIndex + 1}/{_totalSteps} — {eastern:HH:mm:ss}";

        foreach (var symbol in Symbols)
        {
            var candlesUpToNow = CandlesUpTo(_candlesBySymbol[symbol], stepTime);
            var fifteenMin = CandleAggregation.AggregateToInterval(candlesUpToNow, 15, rthOnly: false);
            _ = _charts[symbol].CargarHastaPasoAsync(fifteenMin, visibleDays: 3);

            var lastTick = candlesUpToNow.Count > 0 ? candlesUpToNow[^1].Time : (DateTime?)null;
            _timeLabels[symbol].Text = lastTick is { } t
                ? $"{symbol} — {TimeZoneInfo.ConvertTimeFromUtc(t, easternZone):HH:mm:ss}"
                : $"{symbol} — sin datos";
        }

        RenderOptionsGrid();
    }

    private void RenderOptionsGrid()
    {
        // No symbol picked yet via the selector buttons — leave the grid empty.
        if (_selectedGridSymbol == null || _currentIndex < 0 || !_tickers.TryGetValue(_selectedGridSymbol, out var ticker))
        {
            _dgvChain.Rows.Clear();
            return;
        }

        var steps = _optionStepsBySymbol.TryGetValue(_selectedGridSymbol, out var s) ? s : new List<SimulationStep>();
        var step = StepAtOrBefore(steps, CurrentStepTime());
        if (step == null)
        {
            _dgvChain.Rows.Clear();
            return;
        }

        Form1.PopulateQuotesGrid(_dgvChain, step.Quotes, ticker, applyCountsFilter: true, selectedCounts: "6");
    }
}
