using OptionsTrader.Application.DTOs.Options;
using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

public partial class Form1 : Form
{
    private readonly SchwabAuthService _schwabAuth = new(new HttpClient());
    private readonly HttpClient _marketHttpClient = new();
    private System.Windows.Forms.Timer? _pollingTimer;
    private System.Windows.Forms.Timer? _marketOpenTimer;
    private bool _isPolling;

    private TickerEntry? _selectedTicker;
    private decimal _lastSpotPrice;
    private CsvLogger? _csvLogger;

    public Form1()
    {
        InitializeComponent();
        FormClosing += (s, e) => { _csvLogger?.Dispose(); };
        LoadBrokerSelection();
        LoadTickers();
        LoadRadioSelection(grpPositionSize, PositionSizeSettingsStore.Load());
        LoadRadioSelection(grpTarget, TargetSettingsStore.Load());
        LoadSchwabCredentials();
        LoadBalance();
        LoadTickerButtons();
    }

    private void LoadBrokerSelection()
    {
        var saved = BrokerSettingsStore.Load();
        var match = grpBroker.Controls
            .OfType<RadioButton>()
            .FirstOrDefault(rb => rb.Text == saved);

        if (match != null)
        {
            match.Checked = true;
            match.ForeColor = Color.Green;
            match.Font = new Font(match.Font, FontStyle.Bold);
        }
    }

    private static void LoadRadioSelection(GroupBox group, string saved)
    {
        var match = group.Controls
            .OfType<RadioButton>()
            .FirstOrDefault(rb => rb.Text == saved);

        if (match != null)
        {
            match.Checked = true;
            match.ForeColor = Color.Green;
            match.Font = new Font(match.Font, FontStyle.Bold);
        }
    }

    private void BrokerRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyRadioStyle(grpBroker);
        if (sender is RadioButton { Checked: true } selected)
            BrokerSettingsStore.Save(selected.Text);
    }

    private void PositionSizeRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyRadioStyle(grpPositionSize);
        if (sender is RadioButton { Checked: true } selected)
            PositionSizeSettingsStore.Save(selected.Text);
    }

    private void TargetRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyRadioStyle(grpTarget);
        if (sender is RadioButton { Checked: true } selected)
            TargetSettingsStore.Save(selected.Text);
    }

    private static void ApplyRadioStyle(GroupBox group)
    {
        foreach (var rb in group.Controls.OfType<RadioButton>())
        {
            rb.ForeColor = rb.Checked ? Color.Green : SystemColors.ControlText;
            rb.Font = new Font(rb.Font, rb.Checked ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    private void LoadTickers()
    {
        var tickers = TickerSettingsStore.Load();

        dgvTickers.Rows.Clear();
        for (int i = 0; i < 4; i++)
        {
            if (i < tickers.Count)
                dgvTickers.Rows.Add(tickers[i].Symbol, tickers[i].Low, tickers[i].High, tickers[i].ExpDate);
            else
                dgvTickers.Rows.Add(string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    private void LoadTickerButtons()
    {
        flpTickers.Controls.Clear();
        _selectedTicker = null;

        var tickers = TickerSettingsStore.Load()
            .Where(t => !string.IsNullOrWhiteSpace(t.Symbol))
            .ToList();

        foreach (var ticker in tickers)
        {
            var btn = new Button
            {
                Text = ticker.Symbol,
                Size = new Size(60, 30),
                Tag = ticker,
                BackColor = SystemColors.Control
            };
            btn.Click += TickerButton_Click;
            flpTickers.Controls.Add(btn);
        }
    }

    private void TickerButton_Click(object? sender, EventArgs e)
    {
        if (sender is not Button clicked) return;

        foreach (var btn in flpTickers.Controls.OfType<Button>())
        {
            btn.BackColor = SystemColors.Control;
            btn.ForeColor = SystemColors.ControlText;
            btn.Font = new Font(btn.Font, FontStyle.Regular);
        }

        clicked.BackColor = Color.SteelBlue;
        clicked.ForeColor = Color.White;
        clicked.Font = new Font(clicked.Font, FontStyle.Bold);

        _selectedTicker = clicked.Tag as TickerEntry;
    }

    private void TradeRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        rbNoTrade.ForeColor = rbNoTrade.Checked ? Color.DarkOrange : SystemColors.ControlText;
        rbNoTrade.Font = new Font(rbNoTrade.Font, rbNoTrade.Checked ? FontStyle.Bold : FontStyle.Regular);
        rbTrade.ForeColor = rbTrade.Checked ? Color.Green : SystemColors.ControlText;
        rbTrade.Font = new Font(rbTrade.Font, rbTrade.Checked ? FontStyle.Bold : FontStyle.Regular);
        rbTradeTarget.ForeColor = rbTradeTarget.Checked ? Color.Green : SystemColors.ControlText;
        rbTradeTarget.Font = new Font(rbTradeTarget.Font, rbTradeTarget.Checked ? FontStyle.Bold : FontStyle.Regular);
    }

    private void LoadBalance()
    {
        var balance = BalanceStore.Load();
        txtBalance.Text = balance > 0 ? balance.ToString("F0") : string.Empty;
        UpdatePositionAmount();
    }

    private void TxtBalance_TextChanged(object? sender, EventArgs e)
    {
        UpdatePositionAmount();
        if (decimal.TryParse(txtBalance.Text, out var balance))
            BalanceStore.Save(balance);
    }

    private void UpdatePositionAmount()
    {
        var positionSizeStr = PositionSizeSettingsStore.Load();
        if (!decimal.TryParse(txtBalance.Text, out var balance) ||
            !decimal.TryParse(positionSizeStr, out var positionPct))
        {
            lblPositionAmount.Text = string.Empty;
            return;
        }

        var amount = balance * positionPct / 100m;
        lblPositionAmount.Text = $"{positionPct}% : {amount:F0}";
    }

    private void LoadSchwabCredentials()
    {
        var creds = SchwabCredentialsStore.Load();
        var hasCreds = !string.IsNullOrEmpty(creds.ApiKey) && !string.IsNullOrEmpty(creds.ApiSecret);
        lblCredentialsSaved.Visible = hasCreds;
        // Leave textboxes empty — only used when entering new credentials
    }

    private void BtnSaveCredentials_Click(object? sender, EventArgs e)
    {
        var key = txtApiKey.Text.Trim();
        var secret = txtApiSecret.Text.Trim();

        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(secret))
        {
            MessageBox.Show("Please enter both API Key and API Secret.", "Missing Fields", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SchwabCredentialsStore.Save(new SchwabCredentials(key, secret));
        txtApiKey.Clear();
        txtApiSecret.Clear();
        lblCredentialsSaved.Visible = true;
    }

    private void BtnStartPolling_Click(object? sender, EventArgs e)
    {
            if (_isPolling)
        {
            StopPolling();
            return;
        }

        if (_selectedTicker == null)
        {
            MessageBox.Show("Please select a ticker first.", "No Ticker Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var creds = SchwabCredentialsStore.Load();
        if (string.IsNullOrEmpty(creds.ApiKey) || string.IsNullOrEmpty(creds.ApiSecret))
        {
            MessageBox.Show("Schwab API credentials are not configured.", "Missing Credentials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _isPolling = true;
        btnStartPolling.Text = "Stop Polling";
        btnStartPolling.BackColor = Color.DarkRed;

        // Open CSV logger if enabled
        if (chkSaveToCsv.Checked && _selectedTicker != null)
        {
            var expDate = ExpirationDateResolver.Resolve(_selectedTicker.ExpDate);
            _csvLogger = new CsvLogger();
            _csvLogger.Open(_selectedTicker.Symbol, DateOnly.FromDateTime(DateTime.Today), expDate);
        }

        if (MarketHours.IsOpen)
        {
            StartPollingTimer();
        }
        else
        {
            // Wait until market opens
            var msUntilOpen = MarketHours.MillisecondsUntilOpen;
            _marketOpenTimer = new System.Windows.Forms.Timer { Interval = (int)Math.Min(msUntilOpen, int.MaxValue) };
            _marketOpenTimer.Tick += (s, e) =>
            {
                _marketOpenTimer.Stop();
                _marketOpenTimer.Dispose();
                _marketOpenTimer = null;
                if (_isPolling) StartPollingTimer();
            };
            _marketOpenTimer.Start();
        }
    }

    private void StartPollingTimer()
    {
        // Fetch immediately then every 6 seconds
        _ = FetchAndUpdateQuotesAsync();

        _pollingTimer = new System.Windows.Forms.Timer { Interval = 6000 };
        _pollingTimer.Tick += async (s, e) =>
        {
            if (!MarketHours.IsOpen)
            {
                StopPolling();
                return;
            }
            await FetchAndUpdateQuotesAsync();
        };
        _pollingTimer.Start();
    }

    private void StopPolling()
    {
        _isPolling = false;
        _pollingTimer?.Stop();
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        _marketOpenTimer?.Stop();
        _marketOpenTimer?.Dispose();
        _marketOpenTimer = null;
        btnStartPolling.Text = "Start Polling";
        btnStartPolling.BackColor = Color.DarkGreen;

        _csvLogger?.Close();
        _csvLogger = null;
    }

    private async Task FetchAndUpdateQuotesAsync()
    {
        if (_selectedTicker == null) return;

        var creds = SchwabCredentialsStore.Load();
        var expDate = ExpirationDateResolver.Resolve(_selectedTicker.ExpDate);
        lblExpDate.Text = $"ExpDate: {expDate:yyyy-MM-dd}";
        lblLastUpdate.Text = DateTime.Now.ToString("hh:mm:ss tt");

        try
        {
            var service = new SchwabMarketDataService(_marketHttpClient, _schwabAuth, creds.ApiKey, creds.ApiSecret);
            var allQuotes = (await service.GetOptionsChainAsync(_selectedTicker.Symbol, expDate)).ToList();
            _lastSpotPrice = allQuotes.FirstOrDefault()?.SpotPrice ?? _lastSpotPrice;

            // Append to CSV if logging is enabled
            if (chkSaveToCsv.Checked)
                _csvLogger?.AppendRows(allQuotes);

            // Parse range from ticker settings
            decimal.TryParse(_selectedTicker.Low,  out var rangeLow);
            decimal.TryParse(_selectedTicker.High, out var rangeHigh);
            var rangeText = $"{_selectedTicker.Low} - {_selectedTicker.High}";

            // Level lookup: rank among ALL OTM strikes (before range filter)
            var allOtmCallStrikes = allQuotes
                .Where(q => q.OptionType == OptionsTrader.Domain.Enums.OptionType.Call && !q.InTheMoney)
                .OrderBy(q => q.StrikePrice)   // ascending = closest first
                .Select(q => q.StrikePrice)
                .ToList();

            var allOtmPutStrikes = allQuotes
                .Where(q => q.OptionType == OptionsTrader.Domain.Enums.OptionType.Put && !q.InTheMoney)
                .OrderByDescending(q => q.StrikePrice)  // descending = closest first
                .Select(q => q.StrikePrice)
                .ToList();

            // Filter OTM options within range (range = Ask price Low-High)
            // CALLs: descending (farthest from spot first)
            var otmCalls = allQuotes
                .Where(q => q.OptionType == OptionsTrader.Domain.Enums.OptionType.Call
                         && !q.InTheMoney
                         && q.Ask >= rangeLow && q.Ask <= rangeHigh)
                .OrderByDescending(q => q.StrikePrice)
                .ToList();

            // PUTs: descending (closest to spot first)
            var otmPuts = allQuotes
                .Where(q => q.OptionType == OptionsTrader.Domain.Enums.OptionType.Put
                         && !q.InTheMoney
                         && q.Ask >= rangeLow && q.Ask <= rangeHigh)
                .OrderByDescending(q => q.StrikePrice)
                .ToList();

            // Calculate position size
            var positionSize = GetPositionSize();

            if (dgvQuotes.Rows.Count == 0)
            {
                // First load — one row per CALL (descending), then one row per PUT (descending)
                for (int i = 0; i < otmCalls.Count; i++)
                {
                    var call      = otmCalls[i];
                    var sprd      = FormatSprd(call.Ask - call.Bid);
                    var contracts = CalcContracts(positionSize, call.Ask);
                    var levelIdx  = allOtmCallStrikes.IndexOf(call.StrikePrice);
                    var level     = (levelIdx + 1).ToString();
                    dgvQuotes.Rows.Add(
                        _selectedTicker.Symbol, rangeText,
                        sprd, call.Bid.ToString("F2"), call.Ask.ToString("F2"),
                        call.SpotPrice.ToString("F2"),
                        call.StrikePrice.ToString("F2"),
                        string.Empty, string.Empty, string.Empty,
                        contracts, level);
                    dgvQuotes.Rows[dgvQuotes.Rows.Count - 1].Tag = "CALL";
                }

                for (int i = 0; i < otmPuts.Count; i++)
                {
                    var put       = otmPuts[i];
                    var sprd      = FormatSprd(put.Ask - put.Bid);
                    var contracts = CalcContracts(positionSize, put.Ask);
                    var levelIdx  = allOtmPutStrikes.IndexOf(put.StrikePrice);
                    var level     = (levelIdx + 1).ToString();
                    dgvQuotes.Rows.Add(
                        _selectedTicker.Symbol, rangeText,
                        string.Empty, string.Empty, string.Empty,
                        put.SpotPrice.ToString("F2"),
                        put.StrikePrice.ToString("F2"),
                        put.Bid.ToString("F2"), put.Ask.ToString("F2"), sprd,
                        contracts, level);
                    dgvQuotes.Rows[dgvQuotes.Rows.Count - 1].Tag = "PUT";
                }
            }
            else
            {
                // Subsequent loads — update Bid/Ask/Sprd and Contracts by strike and type
                var callMap = otmCalls.ToDictionary(q => q.StrikePrice);
                var putMap  = otmPuts.ToDictionary(q => q.StrikePrice);

                // Update PnL for open trades
                var callMapForTrades = otmCalls.ToDictionary(q => ("CALL", q.StrikePrice));
                var putMapForTrades  = otmPuts.ToDictionary(q => ("PUT", q.StrikePrice));
                UpdateTradesPnL(callMapForTrades, putMapForTrades);

                foreach (DataGridViewRow row in dgvQuotes.Rows)
                {
                    if (!decimal.TryParse(row.Cells["colStrikePrice"].Value?.ToString(), out var strike)) continue;
                    var rowType = row.Tag?.ToString();

                    if (rowType == "CALL" && callMap.TryGetValue(strike, out var call))
                    {
                        row.Cells["colSpotPrice"].Value = call.SpotPrice.ToString("F2");
                        row.Cells["colCallBid"].Value   = call.Bid.ToString("F2");
                        row.Cells["colCallAsk"].Value   = call.Ask.ToString("F2");
                        row.Cells["colCallSprd"].Value  = FormatSprd(call.Ask - call.Bid);
                        row.Cells["colContracts"].Value = CalcContracts(positionSize, call.Ask);
                    }
                    else if (rowType == "PUT" && putMap.TryGetValue(strike, out var put))
                    {
                        row.Cells["colSpotPrice"].Value = put.SpotPrice.ToString("F2");
                        row.Cells["colPutBid"].Value    = put.Bid.ToString("F2");
                        row.Cells["colPutAsk"].Value    = put.Ask.ToString("F2");
                        row.Cells["colPutSprd"].Value   = FormatSprd(put.Ask - put.Bid);
                        row.Cells["colContracts"].Value = CalcContracts(positionSize, put.Ask);
                    }
                }
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _pollingTimer?.Stop();
            lblLastUpdate.Text = "Rate limited — resuming in 60s...";
            await Task.Delay(60000);
            if (_isPolling) _pollingTimer?.Start();
        }
        catch (Exception ex)
        {
            StopPolling();
            MessageBox.Show($"Polling stopped due to error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DgvQuotes_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = dgvQuotes.Rows[e.RowIndex];

        var callSprdCol = dgvQuotes.Columns["colCallSprd"].Index;
        var putSprdCol  = dgvQuotes.Columns["colPutSprd"].Index;
        var callBidCol  = dgvQuotes.Columns["colCallBid"].Index;
        var putBidCol   = dgvQuotes.Columns["colPutBid"].Index;
        var callAskCol  = dgvQuotes.Columns["colCallAsk"].Index;
        var putAskCol   = dgvQuotes.Columns["colPutAsk"].Index;

        // Sprd columns: bold + red
        if (e.ColumnIndex == callSprdCol || e.ColumnIndex == putSprdCol)
        {
            e.CellStyle.ForeColor = Color.Red;
            e.CellStyle.Font = new Font(dgvQuotes.Font, FontStyle.Bold);
        }

        // Ask columns: bold + dark green
        if (e.ColumnIndex == callAskCol || e.ColumnIndex == putAskCol)
        {
            e.CellStyle.ForeColor = Color.DarkGreen;
            e.CellStyle.Font = new Font(dgvQuotes.Font, FontStyle.Bold);
        }

        // Call Sprd <= 2 → Call Bid background green
        if (e.ColumnIndex == callBidCol)
        {
            if (decimal.TryParse(row.Cells["colCallSprd"].Value?.ToString(), out var callSprd) && callSprd <= 2)
                e.CellStyle.BackColor = Color.LightGreen;
            else
                e.CellStyle.BackColor = dgvQuotes.DefaultCellStyle.BackColor;
        }

        // Put Sprd <= 2 → Put Bid background green
        if (e.ColumnIndex == putBidCol)
        {
            if (decimal.TryParse(row.Cells["colPutSprd"].Value?.ToString(), out var putSprd) && putSprd <= 2)
                e.CellStyle.BackColor = Color.LightGreen;
            else
                e.CellStyle.BackColor = dgvQuotes.DefaultCellStyle.BackColor;
        }
    }

    private void DgvQuotes_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != dgvQuotes.Columns["colStrikePrice"].Index) return;
        if (rbNoTrade.Checked)
            OpenSimulatedTrade(e.RowIndex);
        // Trade / Trade-Target: real order — coming soon
    }

    private void OpenSimulatedTrade(int rowIndex)
    {
        var row      = dgvQuotes.Rows[rowIndex];
        var rowType  = row.Tag?.ToString() ?? "CALL";
        var strike   = row.Cells["colStrikePrice"].Value?.ToString() ?? string.Empty;
        var contracts = row.Cells["colContracts"].Value?.ToString() ?? "0";

        decimal bid, ask;
        if (rowType == "CALL")
        {
            decimal.TryParse(row.Cells["colCallBid"].Value?.ToString(), out bid);
            decimal.TryParse(row.Cells["colCallAsk"].Value?.ToString(), out ask);
        }
        else
        {
            decimal.TryParse(row.Cells["colPutBid"].Value?.ToString(), out bid);
            decimal.TryParse(row.Cells["colPutAsk"].Value?.ToString(), out ask);
        }

        if (ask <= 0) return;

        decimal.TryParse(TargetSettingsStore.Load(), out var targetPct);
        var tBid     = Math.Round(ask * (1 + targetPct / 100m), 2);
        var entryStr = ask.ToString("F2");

        var now = DateTime.Now.ToString("HH:mm:ss");
        dgvTrades.Rows.Add(
            now, rowType, strike,
            bid.ToString("F2"), ask.ToString("F2"), contracts,
            entryStr, bid.ToString("F2"), tBid.ToString("F2"),
            "0.00", "0.00", targetPct.ToString("F0"),
            string.Empty, "Close");

        // Store entry time on the row tag for duration calc
        var newRow = dgvTrades.Rows[dgvTrades.Rows.Count - 1];
        newRow.Tag = DateTime.Now;

        // Color static cells
        newRow.Cells["colTradeEntryPrice"].Style.ForeColor = Color.DodgerBlue;
        newRow.Cells["colTradeCBid"].Style.ForeColor       = Color.Orange;
        newRow.Cells["colTradeTBid"].Style.ForeColor       = Color.LimeGreen;

        // Logger
        var level = row.Cells["colLevel"].Value?.ToString() ?? string.Empty;
        LogLine($"{now} Trade Manual ({rowType})  SpotPrice: {_lastSpotPrice:F2}  StrikePrice: {strike}  Ask: {ask:F2}  Contracts: {contracts}  Level: {level}", Color.White);
        LogLine($"{now} EntryPrice: {entryStr}", Color.LimeGreen);
        LogLine($"{now} Set Target: {tBid:F2}", Color.Orange);
    }

    private void DgvTrades_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != dgvTrades.Columns["colTradeClose"].Index) return;
        var row = dgvTrades.Rows[e.RowIndex];
        if (!string.IsNullOrEmpty(row.Cells["colTradeExitTime"].Value?.ToString())) return;

        CloseTradeRow(row, "MANUAL");
    }

    private void CloseTradeRow(DataGridViewRow row, string closeType)
    {
        var now      = DateTime.Now;
        var nowStr   = now.ToString("HH:mm:ss");
        var type     = row.Cells["colTradeType"].Value?.ToString() ?? string.Empty;
        var strike   = row.Cells["colTradeStrike"].Value?.ToString() ?? string.Empty;
        var cBid     = row.Cells["colTradeCBid"].Value?.ToString() ?? string.Empty;
        var pnl      = row.Cells["colTradePnL"].Value?.ToString() ?? string.Empty;
        var pnlPct   = row.Cells["colTradePnLPercent"].Value?.ToString() ?? string.Empty;
        var spotPrice = _lastSpotPrice > 0 ? _lastSpotPrice.ToString("F2") : string.Empty;

        // Duration
        var duration = TimeSpan.Zero;
        if (row.Tag is DateTime entryTime)
            duration = now - entryTime;

        row.Cells["colTradeExitTime"].Value = nowStr;
        row.Cells["colTradeClose"].Value    = "Closed";
        row.DefaultCellStyle.ForeColor      = Color.Gray;

        // Logger
        decimal.TryParse(pnl, out var pnlVal);
        var pnlColor = pnlVal >= 0 ? Color.LimeGreen : Color.Red;

        LogLine(string.Empty, Color.White);
        LogLine($"{nowStr} Close {closeType} ({type})  SpotPrice: {spotPrice}  Strike: {strike}  C_Bid: {cBid}", Color.White);
        LogLine($"{nowStr} PnL: {pnl}  PnL_Percent: {pnlPct}", pnlColor);
        LogLine($"{nowStr} Duration: {duration:hh\\:mm\\:ss}", Color.White);
    }

    private void LogLine(string text, Color color)
    {
        rtbLogger.SelectionStart  = rtbLogger.TextLength;
        rtbLogger.SelectionLength = 0;
        rtbLogger.SelectionColor  = color;
        rtbLogger.AppendText(text + Environment.NewLine);
        rtbLogger.ScrollToCaret();
    }

    private void UpdateTradesPnL(Dictionary<(string, decimal), OptionQuoteDto> callMap,
                                  Dictionary<(string, decimal), OptionQuoteDto> putMap)
    {
        foreach (DataGridViewRow row in dgvTrades.Rows)
        {
            // Skip closed trades
            if (!string.IsNullOrEmpty(row.Cells["colTradeExitTime"].Value?.ToString())) continue;

            var type = row.Cells["colTradeType"].Value?.ToString() ?? string.Empty;
            if (!decimal.TryParse(row.Cells["colTradeStrike"].Value?.ToString(), out var strike)) continue;
            if (!decimal.TryParse(row.Cells["colTradeEntryPrice"].Value?.ToString(), out var entryPrice)) continue;
            if (!decimal.TryParse(row.Cells["colTradeContracts"].Value?.ToString(), out var contracts)) continue;

            var key = (type, strike);
            decimal currentBid = 0;

            if (type == "CALL" && callMap.TryGetValue(key, out var callQ))
                currentBid = callQ.Bid;
            else if (type == "PUT" && putMap.TryGetValue(key, out var putQ))
                currentBid = putQ.Bid;
            else continue;

            var pnl        = Math.Round((currentBid - entryPrice) * contracts * 100, 2);
            var pnlPct     = entryPrice > 0 ? Math.Round((currentBid - entryPrice) / entryPrice * 100, 1) : 0;

            row.Cells["colTradeCBid"].Value                  = currentBid.ToString("F2");
            row.Cells["colTradeCBid"].Style.ForeColor        = Color.Orange;
            row.Cells["colTradePnL"].Value        = pnl.ToString("F2");
            row.Cells["colTradePnLPercent"].Value = pnlPct.ToString("F1");

            // Color PnL
            row.Cells["colTradePnL"].Style.ForeColor        = pnl >= 0 ? Color.Green : Color.Red;
            row.Cells["colTradePnLPercent"].Style.ForeColor = pnlPct >= 0 ? Color.Green : Color.Red;
        }
    }

    private async void BtnFetchQuotes_Click(object? sender, EventArgs e)
    {
        if (_selectedTicker == null)
        {
            MessageBox.Show("Please select a ticker first.", "No Ticker Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var creds = SchwabCredentialsStore.Load();
        if (string.IsNullOrEmpty(creds.ApiKey) || string.IsNullOrEmpty(creds.ApiSecret))
        {
            MessageBox.Show("Schwab API credentials are not configured.", "Missing Credentials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var expDate = ExpirationDateResolver.Resolve(_selectedTicker.ExpDate);
        lblExpDate.Text = $"ExpDate: {expDate:yyyy-MM-dd}";
        lblLastUpdate.Text = DateTime.Now.ToString("hh:mm:ss tt");

        btnFetchQuotes.Enabled = false;
        dgvQuotes.Rows.Clear();

        try
        {
            await FetchAndUpdateQuotesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error fetching quotes: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnFetchQuotes.Enabled = true;
        }
    }

    private decimal GetPositionSize()
    {
        var balance = BalanceStore.Load();
        if (!decimal.TryParse(PositionSizeSettingsStore.Load(), out var pct)) return 0;
        return balance * pct / 100m;
    }

    /// Formats spread: removes "0." prefix and leading zeros.
    /// 0.01 → "1", 0.05 → "5", 0.10 → "10", 1.00 → "100"
    private static string FormatSprd(decimal value)
    {
        // Round to 2 decimals first to avoid floating point issues (e.g. 0.36-0.35 = 0.009999...)
        var rounded = Math.Round(value, 2);
        var digits  = rounded.ToString("F2").Replace(".", ""); // "001", "005", "010", "100"
        var trimmed = digits.TrimStart('0');
        return string.IsNullOrEmpty(trimmed) ? "0" : trimmed;
    }

    private static string CalcContracts(decimal positionSize, decimal ask)
    {
        if (ask <= 0 || positionSize <= 0) return string.Empty;
        var contracts = Math.Round(positionSize / (ask * 100));
        return contracts > 0 ? contracts.ToString("F0") : string.Empty;
    }

    private void BtnSaveTickers_Click(object? sender, EventArgs e)
    {
        var tickers = dgvTickers.Rows
            .Cast<DataGridViewRow>()
            .Select(r => new TickerEntry(
                r.Cells["colSymbol"].Value?.ToString() ?? string.Empty,
                r.Cells["colLow"].Value?.ToString() ?? string.Empty,
                r.Cells["colHigh"].Value?.ToString() ?? string.Empty,
                r.Cells["colExpDate"].Value?.ToString() ?? string.Empty))
            .ToList();

        TickerSettingsStore.Save(tickers);
    }
}
