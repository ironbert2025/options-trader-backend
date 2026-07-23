using Amazon.S3;
using Amazon.S3.Model;
using OptionsTrader.Application.DTOs.Options;
using OptionsTrader.Application.DTOs.Trading;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Domain.Enums;
using OptionsTrader.Infrastructure.Schwab;
using System.Net.Http.Json;
using System.Text.Json;

namespace OptionsTrader.WinForms;

// AccountHash/OccSymbol/Quantity are only set for REAL broker trades — that's what tells
// CloseTradeRowAsync it needs to actually send a SELL_TO_CLOSE order, not just update the log.
// ExitOrderId is the pending Trade-Target LIMIT exit (if any), cancelled before a manual close.
file record TradeRowTag(int TradeId, DateTime EntryTime, bool SuppressAutoClose = false,
    string? AccountHash = null, string? OccSymbol = null, int Quantity = 0, long? ExitOrderId = null);

public partial class Form1 : Form
{
    private readonly SchwabAuthService _schwabAuth = new(new HttpClient());
    private readonly HttpClient _marketHttpClient = new();
    private readonly HttpClient _apiHttpClient    = new();
    private const string ApiBaseUrl = "http://3.133.58.172:5000/api";
    private System.Windows.Forms.Timer? _pollingTimer;
    private System.Windows.Forms.Timer? _marketOpenTimer;
    private System.Windows.Forms.Timer? _autoCaptureTimer;
    private System.Windows.Forms.Timer? _ivHistorialTimer;
    private bool _isPolling;
    private bool _autoCaptureSession; // true when polling was started by the 3:55 PM scheduler
    private bool _throttledAfter11;   // true once the 6s poll has downshifted to 1-minute after 11 AM

    // Screen coordinates capture state
    private TextBox? _coordsTarget1;
    private TextBox? _coordsTarget2;
    private int      _coordsClickCount;
    private System.Windows.Forms.Timer? _coordsCaptureTimer;
    // One row per Tickers-table symbol, rebuilt by LoadCoordsButtons() whenever tickers change.
    private readonly Dictionary<string, (TextBox T1, TextBox T2)> _coordsTextboxes = new();
    private readonly List<Button> _coordsButtons = new();

    private TickerEntry? _selectedTicker;
    private decimal _lastSpotPrice;
    private CsvLogger? _csvLogger;
    private CsvLogger? _csvLoggerNext;
    private List<BrokerAccountDto> _accounts = new();
    private string _selectedCounts = "In Range"; // session-only, always defaults to In Range on launch
    private List<OptionQuoteDto> _lastAllQuotes = new(); // current-expiration chain from the last fetch, for instant re-filtering

    public Form1()
    {
        InitializeComponent();
        FormClosing += (s, e) => { _csvLogger?.Dispose(); _csvLoggerNext?.Dispose(); _autoCaptureTimer?.Dispose(); _ivHistorialTimer?.Dispose(); };

        // Start with blank placeholder rows (no data, just empty cells) so the grids look like
        // ready spreadsheets instead of a solid gray box before the user picks a ticker.
        PadWithBlankRows(dgvQuotes, 8);
        PadWithBlankRows(dgvQuotesNext, 8);
        PadWithBlankRows(dgvTrades, 4);
        LoadBrokerSelection();
        LoadTickers();
        LoadRadioSelection(grpPositionSize, PositionSizeSettingsStore.Load());
        LoadRadioSelection(grpTarget, TargetSettingsStore.Load());
        LoadRadioSelection(grpContracts, ContractsSettingsStore.Load());
        ApplyRadioStyle(grpCounts);
        chkSaveDumps.Checked = DumpSettingsStore.Load();
        chkShowOrderConfirmation.Checked = OrderConfirmationSettingsStore.Load();
        grpOptionsChainNext.Visible = !chkHideNextExpDate.Checked;
        if (BrokerSettingsStore.Load() == BrokerName.Schwab)
            LoadSchwabCredentials();
        LoadAwsSettings();
        LoadCoordsButtons();
        LoadBalance();
        LoadTickerButtons();
        LoadCachedAccounts();
        Load += Form1_Load;
        StartAutoCaptureScheduler();
        StartIvHistorialScheduler();
    }

    // Prompts for one of the 5 fixed app users on startup instead of auto-logging in as a
    // hardcoded account. Canceling the login closes the whole app immediately.
    private void Form1_Load(object? sender, EventArgs e)
    {
        using var loginForm = new LoginForm(_apiHttpClient, ApiBaseUrl);
        if (loginForm.ShowDialog(this) != DialogResult.OK || loginForm.AccessToken == null)
        {
            Environment.Exit(0);
            return;
        }

        _apiHttpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginForm.AccessToken);

        lblStatusUser.Text = $"User: {loginForm.FirstName} {loginForm.LastName}";
    }

    // Periodically (every 5 min) tries to append today's 9:30-9:35 AM ATM IV snapshot for this
    // instance's own selected ticker to C:\OptionsData\IV_Historial_Apertura.csv — e.g. the SPY
    // instance only ever writes the SPY row, the QQQ instance only the QQQ row.
    private void StartIvHistorialScheduler()
    {
        TryAppendIvHistorialSnapshot();

        _ivHistorialTimer = new System.Windows.Forms.Timer { Interval = 300000 }; // every 5 min
        _ivHistorialTimer.Tick += (s, e) => TryAppendIvHistorialSnapshot();
        _ivHistorialTimer.Start();
    }

    private void TryAppendIvHistorialSnapshot()
    {
        if (_selectedTicker == null) return;
        IvHistorialWriter.TryAppendTodaysSnapshot(_selectedTicker.Symbol, _selectedTicker.ExpDate);
    }

    // Runs all day and auto-starts a capture session at 3:55 PM EST (5 min before close)
    // so the final pre-close chain is saved for backtesting — even if polling was stopped earlier.
    private void StartAutoCaptureScheduler()
    {
        _autoCaptureTimer = new System.Windows.Forms.Timer { Interval = 30000 }; // check every 30s
        _autoCaptureTimer.Tick += (s, e) =>
        {
            if (_isPolling) return;                       // a session is already running
            if (!chkSaveToCsv.Checked) return;            // only when CSV logging is enabled
            if (_selectedTicker == null) return;          // need a ticker to capture
            if (!MarketHours.IsInAutoCaptureWindow) return;

            var creds = SchwabCredentialsStore.Load();
            if (string.IsNullOrEmpty(creds.ApiKey) || string.IsNullOrEmpty(creds.ApiSecret)) return;

            // LogLine($"{DateTime.Now:HH:mm:ss} [Auto] 3:55 PM — starting pre-close capture for {_selectedTicker.Symbol}", Color.Cyan);
            BeginPolling(showWarnings: false, isAutoCapture: true);
        };
        _autoCaptureTimer.Start();
    }

    // Maps each broker radio button to its BrokerName via its control Name (rbSchwab → Schwab,
    // rbIBKR → IBKR, rbETrade → ETrade) rather than its display Text, so relabeling the UI
    // doesn't silently break broker selection.
    private static BrokerName? BrokerNameForRadio(RadioButton rb) =>
        Enum.TryParse<BrokerName>(rb.Name.Replace("rb", string.Empty), out var broker) ? broker : null;

    private void LoadBrokerSelection()
    {
        var saved = BrokerSettingsStore.Load();
        var match = grpBroker.Controls
            .OfType<RadioButton>()
            .FirstOrDefault(rb => BrokerNameForRadio(rb) == saved);

        if (match != null)
        {
            match.Checked = true;
            match.ForeColor = Color.Green;
            match.Font = new Font(match.Font, FontStyle.Bold);
        }

        UpdateSchwabGroupsVisibility(saved);
    }

    // Schwab Credentials / Broker Accounts only make sense for the active broker — IBKR/ETrade
    // have no implementation yet, so there's nothing to configure for them.
    private void UpdateSchwabGroupsVisibility(BrokerName broker)
    {
        var isSchwab = broker == BrokerName.Schwab;
        grpSchwabCredentials.Visible = isSchwab;
        grpAccounts.Visible = isSchwab;
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
        if (sender is RadioButton { Checked: true } selected && BrokerNameForRadio(selected) is { } broker)
        {
            BrokerSettingsStore.Save(broker);
            UpdateSchwabGroupsVisibility(broker);
        }
    }

    private void PositionSizeRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyRadioStyle(grpPositionSize);
        if (sender is RadioButton { Checked: true } selected)
            PositionSizeSettingsStore.Save(selected.Text);
        UpdatePositionAmount();
    }

    private void ChkSaveDumps_CheckedChanged(object? sender, EventArgs e)
    {
        DumpSettingsStore.Save(chkSaveDumps.Checked);
    }

    private void ChkShowOrderConfirmation_CheckedChanged(object? sender, EventArgs e)
    {
        OrderConfirmationSettingsStore.Save(chkShowOrderConfirmation.Checked);
    }

    private void ChkHideNextExpDate_CheckedChanged(object? sender, EventArgs e)
    {
        grpOptionsChainNext.Visible = !chkHideNextExpDate.Checked;
    }

    private void TargetRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyRadioStyle(grpTarget);
        if (sender is RadioButton { Checked: true } selected)
            TargetSettingsStore.Save(selected.Text);
    }

    private void ContractsRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyRadioStyle(grpContracts);
        if (sender is RadioButton { Checked: true } selected)
            ContractsSettingsStore.Save(selected.Text);
    }

    private void CountsRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyRadioStyle(grpCounts);
        if (sender is RadioButton { Checked: true } selected)
            _selectedCounts = selected.Text;
    }

    private void CallPutFilter_CheckedChanged(object? sender, EventArgs e)
    {
        if (_selectedTicker == null || _lastAllQuotes.Count == 0) return;
        PopulateQuotesGrid(dgvQuotes, _lastAllQuotes, _selectedTicker, applyCountsFilter: true);
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

        // Reset to blank placeholder rows — real quotes arrive once Start Polling/Fetch Quotes
        // is used, so keep the grids looking like ready tables in the meantime.
        dgvQuotes.Rows.Clear();
        dgvQuotesNext.Rows.Clear();
        dgvTrades.Rows.Clear();
        PadWithBlankRows(dgvQuotes, 8);
        PadWithBlankRows(dgvQuotesNext, 8);

        RestoreOpenTrades(_selectedTicker?.Symbol ?? string.Empty);
        PadWithBlankRows(dgvTrades, 4);
    }

    private void RestoreOpenTrades(string symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return;

        var saved = OpenTradesStore.Load()
            .Where(t => t.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (saved.Count == 0) return;

        var today = DateOnly.FromDateTime(DateTime.Today);

        foreach (var t in saved)
        {
            // Check if already visible in the Trades DGV (avoid duplicates)
            bool alreadyShown = dgvTrades.Rows
                .Cast<DataGridViewRow>()
                .Any(r => r.Tag is TradeRowTag tag && tag.TradeId == t.TradeId);
            if (alreadyShown) continue;

            if (t.ExpirationDate < today)
            {
                // Expired — close with Bid = 0
                var entryTime = t.EntryTime;
                var now       = DateTime.Now;
                var duration  = now - entryTime;
                var pnl       = Math.Round((0m - t.EntryPrice) * decimal.Parse(t.Contracts) * 100, 2);
                var pnlPct    = t.EntryPrice > 0 ? Math.Round(pnl / (t.EntryPrice * decimal.Parse(t.Contracts) * 100) * 100, 1) : 0m;

                dgvTrades.Rows.Add(
                    t.EntryTime.ToString("HH:mm:ss"), t.OptionType, t.StrikePrice,
                    string.Empty, t.EntryPrice.ToString("F2"), t.Contracts,
                    t.EntryPrice.ToString("F2"), "0.00", string.Empty,
                    pnl.ToString("F2"), pnlPct.ToString("F1"), t.PnlTarget,
                    now.ToString("HH:mm:ss"), "Closed");

                var expiredRow = dgvTrades.Rows[dgvTrades.Rows.Count - 1];
                expiredRow.Tag = new TradeRowTag(t.TradeId, t.EntryTime);
                expiredRow.DefaultCellStyle.ForeColor = Color.Gray;
                expiredRow.Cells["colTradeEntryPrice"].Style.ForeColor = Color.DodgerBlue;
                SetTradeTypeColor(expiredRow, t.OptionType);

                // LogLine($"{now:HH:mm:ss} Restored EXPIRED trade ({t.OptionType}) Strike: {t.StrikePrice}  Closed with Bid=0  PnL: {pnl:F2}", Color.Gray);

                OpenTradesStore.Remove(t.TradeId);
                if (t.TradeId > 0)
                    _ = CloseTradeInApiAsync(t.TradeId, 0m, pnl, pnlPct, duration);
            }
            else
            {
                // Still valid — restore as open trade
                decimal.TryParse(TargetSettingsStore.Load(), out var targetPct);
                var tBid = Math.Round(t.EntryPrice * (1 + targetPct / 100m), 2);

                dgvTrades.Rows.Add(
                    t.EntryTime.ToString("HH:mm:ss"), t.OptionType, t.StrikePrice,
                    string.Empty, t.EntryPrice.ToString("F2"), t.Contracts,
                    t.EntryPrice.ToString("F2"), t.EntryPrice.ToString("F2"), tBid.ToString("F2"),
                    "0.00", "0.00", t.PnlTarget,
                    string.Empty, "Close");

                var restoredRow = dgvTrades.Rows[dgvTrades.Rows.Count - 1];
                restoredRow.Tag = new TradeRowTag(t.TradeId, t.EntryTime);
                restoredRow.Cells["colTradeEntryPrice"].Style.ForeColor = Color.DodgerBlue;
                restoredRow.Cells["colTradeCBid"].Style.ForeColor       = Color.Orange;
                restoredRow.Cells["colTradeTBid"].Style.ForeColor       = Color.LimeGreen;
                SetTradeTypeColor(restoredRow, t.OptionType);

                // LogLine($"{DateTime.Now:HH:mm:ss} Restored open trade ({t.OptionType}) Strike: {t.StrikePrice}  Entry: {t.EntryPrice:F2}  Contracts: {t.Contracts}", Color.Cyan);
            }
        }
    }

    // Blank placeholder rows never get a Tag, unlike real trade rows (TradeRowTag) — use that
    // to strip them out right before a real row is added, so blanks never sit above real data.
    private static void RemoveBlankPlaceholderRows(DataGridView grid)
    {
        for (int i = grid.Rows.Count - 1; i >= 0; i--)
        {
            if (grid.Rows[i].Tag == null)
                grid.Rows.RemoveAt(i);
        }
    }

    private static void SetTradeTypeColor(DataGridViewRow row, string optionType)
    {
        row.Cells["colTradeType"].Style.ForeColor =
            optionType.Equals("CALL", StringComparison.OrdinalIgnoreCase) ? Color.Green : Color.Red;
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

        // Wire logger so token events appear in the log panel
        // _schwabAuth.SetLogCallback(msg => Invoke(() => LogLine(msg, Color.Yellow)));

        var tokens = SchwabTokenStore.Load();
        if (tokens != null && !string.IsNullOrEmpty(tokens.AccessToken))
        {
            lblTokenStatus.Text = $"Token OK — expires {tokens.AccessTokenExpiresAt.ToLocalTime():HH:mm:ss}";
            lblTokenStatus.ForeColor = Color.Green;

            // Only the first ticker instance checks refresh token expiry
            CheckRefreshTokenExpiry(tokens);
        }
        else
        {
            lblTokenStatus.Text = "No token — click Login";
            lblTokenStatus.ForeColor = Color.OrangeRed;
        }
    }

    private bool IsPrimaryTickerInstance()
    {
        var tickers = TickerSettingsStore.Load();
        if (tickers.Count == 0 || _selectedTicker == null) return false;
        return tickers[0].Symbol == _selectedTicker.Symbol;
    }

    private void CheckRefreshTokenExpiry(SchwabTokens tokens)
    {
        // Only the primary (first) ticker instance shows this warning
        if (!IsPrimaryTickerInstance()) return;

        var today    = DateTime.Today;
        var isMonday = today.DayOfWeek == DayOfWeek.Monday;
        var expiresLocal = tokens.RefreshTokenExpiresAt.ToLocalTime();
        var daysLeft = (expiresLocal.Date - today).TotalDays;

        if (daysLeft <= 1)
        {
            var msg = $"{DateTime.Now:HH:mm:ss} [Token] ⚠ REFRESH TOKEN expires {expiresLocal:yyyy-MM-dd} — click Login before 9:30 AM to renew!";
            // LogLine(msg, Color.OrangeRed);
            lblTokenStatus.Text = "Refresh token expiring — Login needed!";
            lblTokenStatus.ForeColor = Color.OrangeRed;
        }
        else if (isMonday && daysLeft <= 3)
        {
            var msg = $"{DateTime.Now:HH:mm:ss} [Token] Refresh token expires in {(int)daysLeft} days ({expiresLocal:yyyy-MM-dd})";
            // LogLine(msg, Color.Orange);
        }
    }

    private void BtnLogin_Click(object? sender, EventArgs e)
    {
        var creds = SchwabCredentialsStore.Load();
        if (string.IsNullOrEmpty(creds.ApiKey))
        {
            MessageBox.Show("Save your API Key first in Schwab Credentials.", "Missing API Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var authUrl = $"https://api.schwabapi.com/v1/oauth/authorize" +
                      $"?response_type=code" +
                      $"&client_id={Uri.EscapeDataString(creds.ApiKey)}" +
                      $"&redirect_uri={Uri.EscapeDataString("https://127.0.0.1")}";

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authUrl) { UseShellExecute = true });
        lblTokenStatus.Text = "Paste the full callback URL below and press Enter";
        lblTokenStatus.ForeColor = Color.DarkBlue;
        txtResponse.Text = string.Empty;
        txtResponse.Focus();
    }

    private async void TxtResponse_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;

        var input = txtResponse.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        // Extract the code from the callback URL: https://127.0.0.1?code=XXXX&session=...
        string code;
        try
        {
            var uri = new Uri(input.Contains("?") ? input : $"https://x.com?{input}");
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            code = query["code"] ?? string.Empty;
        }
        catch
        {
            code = input; // user pasted the code directly
        }

        if (string.IsNullOrEmpty(code))
        {
            lblTokenStatus.Text = "Could not extract code from URL";
            lblTokenStatus.ForeColor = Color.Red;
            return;
        }

        try
        {
            lblTokenStatus.Text = "Exchanging code...";
            lblTokenStatus.ForeColor = Color.DarkBlue;

            var creds = SchwabCredentialsStore.Load();
            var (accessToken, refreshToken, expiresIn) = await _schwabAuth.ExchangeCodeAsync(
                creds.ApiKey, creds.ApiSecret, code, "https://127.0.0.1");

            var tokens = new SchwabTokens(
                accessToken,
                refreshToken,
                DateTime.UtcNow.AddSeconds(expiresIn - 30),
                DateTime.UtcNow.AddDays(7));
            SchwabTokenStore.Save(tokens);
            // LogLine($"{DateTime.Now:HH:mm:ss} [Token] Refresh token saved — valid until {DateTime.Now.AddDays(7):yyyy-MM-dd}", Color.Yellow);

            txtResponse.Text = string.Empty;
            lblTokenStatus.Text = "Token saved successfully";
            lblTokenStatus.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            lblTokenStatus.Text = $"Error: {ex.Message}";
            lblTokenStatus.ForeColor = Color.Red;
        }
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

    private void LoadAwsSettings()
    {
        var aws = AwsSettingsStore.Load();
        var hasCreds = !string.IsNullOrEmpty(aws.AccessKey);
        lblAwsSaved.Visible = hasCreds;
        txtAwsBucket.Text = aws.BucketName;
        txtAwsRegion.Text = aws.Region;
    }

    private void BtnSaveAwsSettings_Click(object? sender, EventArgs e)
    {
        var accessKey = txtAwsAccessKey.Text.Trim();
        var secretKey = txtAwsSecretKey.Text.Trim();
        var bucket    = txtAwsBucket.Text.Trim();
        var region    = txtAwsRegion.Text.Trim();

        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
        {
            MessageBox.Show("Please enter Access Key and Secret Key.", "Missing Fields", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AwsSettingsStore.Save(new AwsSettings(accessKey, secretKey, bucket, region));
        txtAwsAccessKey.Clear();
        txtAwsSecretKey.Clear();
        lblAwsSaved.Visible = true;
    }

    private void BtnStartPolling_Click(object? sender, EventArgs e)
    {
        if (_isPolling)
        {
            StopPolling();
            return;
        }

        BeginPolling(showWarnings: true, isAutoCapture: false);
    }

    private void BeginPolling(bool showWarnings, bool isAutoCapture)
    {
        if (_selectedTicker == null)
        {
            if (showWarnings)
                MessageBox.Show("Please select a ticker first.", "No Ticker Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var creds = SchwabCredentialsStore.Load();
        if (string.IsNullOrEmpty(creds.ApiKey) || string.IsNullOrEmpty(creds.ApiSecret))
        {
            if (showWarnings)
                MessageBox.Show("Schwab API credentials are not configured.", "Missing Credentials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _isPolling = true;
        _autoCaptureSession = isAutoCapture;
        _throttledAfter11 = false;
        btnStartPolling.Text = "Stop Polling";
        btnStartPolling.BackColor = Color.DarkRed;

        // Open CSV loggers if enabled (one for current ExpDate, one for the next)
        if (chkSaveToCsv.Checked && _selectedTicker != null)
        {
            var today       = DateOnly.FromDateTime(DateTime.Today);
            var expDate     = ExpirationDateResolver.Resolve(_selectedTicker.ExpDate);
            var nextExpDate = ExpirationDateResolver.ResolveNext(_selectedTicker.ExpDate);

            _csvLogger = new CsvLogger();
            _csvLogger.Open(_selectedTicker.Symbol, today, expDate);

            if (!chkHideNextExpDate.Checked)
            {
                _csvLoggerNext = new CsvLogger();
                _csvLoggerNext.Open(_selectedTicker.Symbol, today, nextExpDate);
            }
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

            // After 11 AM, downshift from 6s to 1-minute polling (still saving to CSV) instead of
            // stopping — applies whether "Stop at 11:00 AM" is checked or not. The 3:55 PM
            // auto-capture session is exempt so it keeps running at full cadence to close.
            if (!_autoCaptureSession && !_throttledAfter11 && MarketHours.IsPastStopTime)
            {
                _pollingTimer!.Interval = 60000;
                _throttledAfter11 = true;
                // LogLine($"{DateTime.Now:HH:mm:ss} [Auto] 11:00 AM reached — polling throttled to 1/min", Color.Yellow);
            }

            await FetchAndUpdateQuotesAsync();
        };
        _pollingTimer.Start();
    }

    private void StopPolling()
    {
        _isPolling = false;
        _autoCaptureSession = false;
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
        _csvLoggerNext?.Close();
        _csvLoggerNext = null;
    }

    private async Task FetchAndUpdateQuotesAsync()
    {
        if (_selectedTicker == null) return;

        var expDate = ExpirationDateResolver.Resolve(_selectedTicker.ExpDate);
        lblExpDate.Text = $"ExpDate: {expDate:yyyy-MM-dd}";
        lblLastUpdate.Text = DateTime.Now.ToString("hh:mm:ss tt");

        try
        {
            var service = CreateMarketDataService(chkSaveDumps.Checked);

            var nextExpDate = ExpirationDateResolver.ResolveNext(_selectedTicker.ExpDate);
            lblExpDateNext.Text = $"ExpDate: {nextExpDate:yyyy-MM-dd}";

            // Single call covering today → nextExpDate, so both grids share one underlying (spot) snapshot.
            var fromDate  = DateOnly.FromDateTime(MarketHours.NowEst);
            var fullChain = (await service.GetOptionsChainAsync(_selectedTicker.Symbol, fromDate, nextExpDate)).ToList();

            var allQuotes     = fullChain.Where(q => q.ExpirationDate == expDate).ToList();
            var allQuotesNext = fullChain.Where(q => q.ExpirationDate == nextExpDate).ToList();

            _lastAllQuotes = allQuotes;
            _lastSpotPrice = fullChain.FirstOrDefault()?.SpotPrice ?? _lastSpotPrice;

            // Primary chain (current ExpDate)
            if (chkSaveToCsv.Checked)
            {
                _csvLogger?.AppendRows(allQuotes);
                // Try right away (not just on the 5-min scheduler tick) so the IVR/IVP opening
                // snapshot is captured on the very poll where the 9:30-9:35 window fills in.
                TryAppendIvHistorialSnapshot();
            }

            PopulateQuotesGrid(dgvQuotes, allQuotes, _selectedTicker, applyCountsFilter: true);

            // Update PnL for open trades against the FULL chain (not the range-filtered grid),
            // so a trade's current bid keeps updating even after its strike leaves the display range.
            var callMapForTrades = allQuotes
                .Where(q => q.OptionType == OptionsTrader.Domain.Enums.OptionType.Call)
                .GroupBy(q => q.StrikePrice)
                .ToDictionary(g => ("CALL", g.Key), g => g.First());
            var putMapForTrades = allQuotes
                .Where(q => q.OptionType == OptionsTrader.Domain.Enums.OptionType.Put)
                .GroupBy(q => q.StrikePrice)
                .ToDictionary(g => ("PUT", g.Key), g => g.First());
            UpdateTradesPnL(callMapForTrades, putMapForTrades);

            // Next chain (next ExpDate, e.g. tomorrow for daily) — skipped entirely while
            // "Hide Next ExpDate" is checked (no grid refresh, no CSV write).
            if (!chkHideNextExpDate.Checked)
            {
                if (chkSaveToCsv.Checked)
                    _csvLoggerNext?.AppendRows(allQuotesNext);

                PopulateQuotesGrid(dgvQuotesNext, allQuotesNext, _selectedTicker);
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

        // StrikePrice coloring is handled in DgvQuotes_CellPainting

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

    // Filters the chain to OTM strikes within the ticker's Ask range and (re)builds the given grid.
    // Returns the in-range OTM call/put lists so the caller can build trade-PnL maps if needed.
    private (List<OptionQuoteDto> otmCalls, List<OptionQuoteDto> otmPuts) PopulateQuotesGrid(
        DataGridView grid, List<OptionQuoteDto> allQuotes, TickerEntry ticker, bool applyCountsFilter = false)
    {
        decimal.TryParse(ticker.Low,  out var rangeLow);
        decimal.TryParse(ticker.High, out var rangeHigh);
        var rangeText = $"{ticker.Low} - {ticker.High}";

        // Level lookup: rank among ALL OTM strikes (before range/count filter)
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

        List<OptionQuoteDto> otmCalls;
        List<OptionQuoteDto> otmPuts;

        if (applyCountsFilter && int.TryParse(_selectedCounts, out var count))
        {
            // Show the N closest OTM strikes regardless of the Ask price range.
            var callStrikeSet = allOtmCallStrikes.Take(count).ToHashSet();
            var putStrikeSet  = allOtmPutStrikes.Take(count).ToHashSet();

            otmCalls = allQuotes
                .Where(q => q.OptionType == OptionsTrader.Domain.Enums.OptionType.Call && callStrikeSet.Contains(q.StrikePrice))
                .OrderByDescending(q => q.StrikePrice)
                .ToList();

            otmPuts = allQuotes
                .Where(q => q.OptionType == OptionsTrader.Domain.Enums.OptionType.Put && putStrikeSet.Contains(q.StrikePrice))
                .OrderByDescending(q => q.StrikePrice)
                .ToList();
        }
        else
        {
            // Filter OTM options within range (range = Ask price Low-High)
            otmCalls = allQuotes
                .Where(q => q.OptionType == OptionsTrader.Domain.Enums.OptionType.Call
                         && !q.InTheMoney
                         && q.Ask >= rangeLow && q.Ask <= rangeHigh)
                .OrderByDescending(q => q.StrikePrice)
                .ToList();

            otmPuts = allQuotes
                .Where(q => q.OptionType == OptionsTrader.Domain.Enums.OptionType.Put
                         && !q.InTheMoney
                         && q.Ask >= rangeLow && q.Ask <= rangeHigh)
                .OrderByDescending(q => q.StrikePrice)
                .ToList();
        }

        // CALL/PUT checkbox filter (current grid only): one checked shows only that side;
        // both checked or both unchecked shows both.
        if (applyCountsFilter)
        {
            var callOnly = chkCallFilter.Checked && !chkPutFilter.Checked;
            var putOnly  = chkPutFilter.Checked && !chkCallFilter.Checked;
            if (callOnly) otmPuts  = new List<OptionQuoteDto>();
            if (putOnly)  otmCalls = new List<OptionQuoteDto>();
        }

        // Always rebuild rows so strikes that move ITM/OTM are reflected immediately
        grid.Rows.Clear();

        foreach (var call in otmCalls)
        {
            var sprd      = FormatSprd(call.Ask - call.Bid);
            var contracts = GetContractsValue(call.Ask);
            var levelIdx  = allOtmCallStrikes.IndexOf(call.StrikePrice);
            var level     = (levelIdx + 1).ToString();
            grid.Rows.Add(
                ticker.Symbol, rangeText,
                sprd, call.Bid.ToString("F2"), call.Ask.ToString("F2"),
                call.SpotPrice.ToString("F2"),
                FormatStrike(call.StrikePrice),
                string.Empty, string.Empty, string.Empty,
                contracts, level);
            grid.Rows[grid.Rows.Count - 1].Tag = "CALL";
        }

        foreach (var put in otmPuts)
        {
            var sprd      = FormatSprd(put.Ask - put.Bid);
            var contracts = GetContractsValue(put.Ask);
            var levelIdx  = allOtmPutStrikes.IndexOf(put.StrikePrice);
            var level     = (levelIdx + 1).ToString();
            grid.Rows.Add(
                ticker.Symbol, rangeText,
                string.Empty, string.Empty, string.Empty,
                put.SpotPrice.ToString("F2"),
                FormatStrike(put.StrikePrice),
                put.Bid.ToString("F2"), put.Ask.ToString("F2"), sprd,
                contracts, level);
            grid.Rows[grid.Rows.Count - 1].Tag = "PUT";
        }

        PadWithBlankRows(grid, 8);

        return (otmCalls, otmPuts);
    }

    // Fills the grid with empty rows up to targetTotal so it still looks like a full table
    // when there aren't enough real quotes to fill the visible area.
    private static void PadWithBlankRows(DataGridView grid, int targetTotal)
    {
        for (int i = grid.Rows.Count; i < targetTotal; i++)
            grid.Rows.Add();
    }

    private void DgvQuotesNext_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = dgvQuotesNext.Rows[e.RowIndex];

        var callSprdCol = dgvQuotesNext.Columns["colCallSprdNext"].Index;
        var putSprdCol  = dgvQuotesNext.Columns["colPutSprdNext"].Index;
        var callBidCol  = dgvQuotesNext.Columns["colCallBidNext"].Index;
        var putBidCol   = dgvQuotesNext.Columns["colPutBidNext"].Index;
        var callAskCol  = dgvQuotesNext.Columns["colCallAskNext"].Index;
        var putAskCol   = dgvQuotesNext.Columns["colPutAskNext"].Index;

        if (e.ColumnIndex == callSprdCol || e.ColumnIndex == putSprdCol)
        {
            e.CellStyle.ForeColor = Color.Red;
            e.CellStyle.Font = new Font(dgvQuotesNext.Font, FontStyle.Bold);
        }

        if (e.ColumnIndex == callAskCol || e.ColumnIndex == putAskCol)
        {
            e.CellStyle.ForeColor = Color.DarkGreen;
            e.CellStyle.Font = new Font(dgvQuotesNext.Font, FontStyle.Bold);
        }

        if (e.ColumnIndex == callBidCol)
        {
            if (decimal.TryParse(row.Cells["colCallSprdNext"].Value?.ToString(), out var callSprd) && callSprd <= 2)
                e.CellStyle.BackColor = Color.LightGreen;
            else
                e.CellStyle.BackColor = dgvQuotesNext.DefaultCellStyle.BackColor;
        }

        if (e.ColumnIndex == putBidCol)
        {
            if (decimal.TryParse(row.Cells["colPutSprdNext"].Value?.ToString(), out var putSprd) && putSprd <= 2)
                e.CellStyle.BackColor = Color.LightGreen;
            else
                e.CellStyle.BackColor = dgvQuotesNext.DefaultCellStyle.BackColor;
        }
    }

    private void DgvQuotesNext_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (e.ColumnIndex != dgvQuotesNext.Columns["colStrikePriceNext"].Index) return;

        var val     = e.Value?.ToString();
        var row     = dgvQuotesNext.Rows[e.RowIndex];
        var rowType = row.Tag?.ToString();
        var disabled = IsRowBidZero(row, "colCallBidNext", "colPutBidNext");

        e.PaintBackground(e.ClipBounds, true);

        if (!string.IsNullOrEmpty(val))
        {
            var bgColor  = disabled ? Color.LightGray : (rowType == "PUT" ? Color.Red : Color.DarkGreen);
            var textColor = disabled ? Color.Gray : Color.White;
            var btnRect  = Rectangle.Inflate(e.CellBounds, -3, -3);

            using var fillBrush = new SolidBrush(bgColor);
            using var borderPen = new Pen(ControlPaint.Dark(bgColor, 0.2f));
            using var textFont  = new Font(dgvQuotesNext.Font, FontStyle.Bold);

            e.Graphics!.FillRectangle(fillBrush, btnRect);
            e.Graphics.DrawRectangle(borderPen, btnRect);

            TextRenderer.DrawText(
                e.Graphics, val, textFont, btnRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        e.Handled = true;
    }

    // A row's tradable bid is the Call Bid for CALL rows and the Put Bid for PUT rows.
    // A zero (or missing) bid means the option is illiquid — its Strike button must be disabled.
    private static bool IsRowBidZero(DataGridViewRow row, string callBidColName, string putBidColName)
    {
        var bidColName = row.Tag?.ToString() == "PUT" ? putBidColName : callBidColName;
        var bidStr = row.Cells[bidColName].Value?.ToString();
        return !decimal.TryParse(bidStr, out var bid) || bid == 0m;
    }

    private void DgvQuotes_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (e.ColumnIndex != dgvQuotes.Columns["colStrikePrice"].Index) return;

        var val     = e.Value?.ToString();
        var row     = dgvQuotes.Rows[e.RowIndex];
        var rowType = row.Tag?.ToString();
        var disabled = IsRowBidZero(row, "colCallBid", "colPutBid");

        // Paint default background
        e.PaintBackground(e.ClipBounds, true);

        if (!string.IsNullOrEmpty(val))
        {
            var btnColor = disabled ? Color.LightGray : (rowType == "PUT" ? Color.Red : Color.DarkGreen);
            var textColor = disabled ? Color.Gray : Color.White;
            var btnRect  = Rectangle.Inflate(e.CellBounds, -3, -3);

            using var fillBrush = new SolidBrush(btnColor);
            using var borderPen = new Pen(ControlPaint.Dark(btnColor, 0.2f));
            using var textFont  = new Font(dgvQuotes.Font, FontStyle.Bold);

            e.Graphics!.FillRectangle(fillBrush, btnRect);
            e.Graphics.DrawRectangle(borderPen, btnRect);

            TextRenderer.DrawText(
                e.Graphics, val, textFont, btnRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        e.Handled = true;
    }

    private void DgvQuotes_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != dgvQuotes.Columns["colStrikePrice"].Index) return;

        // Block clicks on illiquid options (bid = 0) to avoid opening a guaranteed-loss order
        if (IsRowBidZero(dgvQuotes.Rows[e.RowIndex], "colCallBid", "colPutBid")) return;

        if (rbNoTrade.Checked)
            OpenSimulatedTrade(e.RowIndex);
        else if (rbTrade.Checked)
            _ = PlaceRealTradeAsync(e.RowIndex, withTarget: false);
        else if (rbTradeTarget.Checked)
            _ = PlaceRealTradeAsync(e.RowIndex, withTarget: true);
    }

    private async void OpenSimulatedTrade(int rowIndex)
    {
        var row       = dgvQuotes.Rows[rowIndex];
        var rowType   = row.Tag?.ToString() ?? "CALL";
        var strike    = row.Cells["colStrikePrice"].Value?.ToString() ?? string.Empty;
        var contracts = row.Cells["colContracts"].Value?.ToString() ?? "0";
        var level     = row.Cells["colLevel"].Value?.ToString() ?? string.Empty;
        var symbol    = _selectedTicker?.Symbol ?? "UNK";

        var (bid, ask) = ReadRowBidAsk(row, rowType);
        if (ask <= 0) return;

        await RecordEntryAsync(symbol, rowType, strike, level, bid, ask, contracts, "Trade Manual", isDemo: true, suppressAutoClose: false);
    }

    private static (decimal bid, decimal ask) ReadRowBidAsk(DataGridViewRow row, string rowType)
    {
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
        return (bid, ask);
    }

    // Records an opened position in the Trades grid + backend API + local persistence + entry screenshot.
    // Shared by simulated trades and real (broker) trades. Returns the API trade id and the new grid row.
    private async Task<(int TradeId, DataGridViewRow Row)> RecordEntryAsync(string symbol, string rowType, string strike, string level,
        decimal bid, decimal ask, string contracts, string entryLabel, bool isDemo, bool suppressAutoClose = false,
        string? accountHash = null, string? occSymbol = null, int quantity = 0)
    {
        decimal.TryParse(TargetSettingsStore.Load(), out var targetPct);
        var tBid      = Math.Round(ask * (1 + targetPct / 100m), 2);
        var entryStr  = ask.ToString("F2");
        var entryTime = DateTime.Now;
        var now       = entryTime.ToString("HH:mm:ss");

        RemoveBlankPlaceholderRows(dgvTrades);

        dgvTrades.Rows.Add(
            now, rowType, strike,
            bid.ToString("F2"), ask.ToString("F2"), contracts,
            entryStr, bid.ToString("F2"), tBid.ToString("F2"),
            "0.00", "0.00", targetPct.ToString("F0"),
            string.Empty, "Close");

        var newRow = dgvTrades.Rows[dgvTrades.Rows.Count - 1];
        newRow.Cells["colTradeEntryPrice"].Style.ForeColor = Color.DodgerBlue;
        newRow.Cells["colTradeCBid"].Style.ForeColor       = Color.Orange;
        newRow.Cells["colTradeTBid"].Style.ForeColor       = Color.LimeGreen;
        SetTradeTypeColor(newRow, rowType);

        LogLine($"{now} {entryLabel} ({rowType})  SpotPrice: {_lastSpotPrice:F2}  StrikePrice: {strike}  Ask: {ask:F2}  Contracts: {contracts}  Level: {level}", Color.White);
        LogLine($"{now} EntryPrice: {entryStr}", Color.LimeGreen);
        LogLine($"{now} Set Target: {tBid:F2}", Color.Orange);
        System.Windows.Forms.Application.DoEvents();

        int.TryParse(level, out var levelInt);
        int.TryParse(contracts, out var contractsInt);
        var tradeId = await SaveTradeToApiAsync(symbol, rowType, strike, ask, contractsInt, levelInt, targetPct, entryTime, isDemo);
        newRow.Tag = new TradeRowTag(tradeId, entryTime, suppressAutoClose, accountHash, occSymbol, quantity);
        PadWithBlankRows(dgvTrades, 4);

        var expDate = ExpirationDateResolver.Resolve(_selectedTicker?.ExpDate ?? string.Empty);
        OpenTradesStore.Add(new PersistedTrade(
            TradeId:        tradeId,
            Symbol:         symbol,
            OptionType:     rowType,
            StrikePrice:    strike,
            EntryPrice:     ask,
            Contracts:      contracts,
            EntryTime:      entryTime,
            ExpirationDate: expDate,
            Level:          level,
            PnlTarget:      targetPct.ToString("F0")));

        var entryPath = CaptureScreenshot(symbol, rowType, "entry");
        // LogLine($"{now} Screenshot: {entryPath}", Color.DimGray);
        _ = UploadScreenshotAsync(entryPath, symbol, rowType, tradeId, now);

        return (tradeId, newRow);
    }

    // ----- Real broker order execution (Schwab) -----

    private async Task OnSchwabTokenRenewed(string newAccess, DateTime newExpires)
    {
        var current = SchwabTokenStore.Load();
        if (current == null) return;
        var updated = current with { AccessToken = newAccess, AccessTokenExpiresAt = newExpires };
        SchwabTokenStore.Save(updated);
        if (IsHandleCreated)
            Invoke(() =>
            {
                lblTokenStatus.Text = $"Token renewed — expires {newExpires.ToLocalTime():HH:mm:ss}";
                lblTokenStatus.ForeColor = Color.Green;
            });
    }

    // Resolves the trading service for whichever broker is selected in Settings. IBKR/ETrade
    // are recognized brokers (BrokerName enum) but have no implementation yet — selecting them
    // throws a clear error instead of silently falling back to Schwab.
    private ITradingService CreateTradingService()
    {
        var broker = BrokerSettingsStore.Load();
        return broker switch
        {
            BrokerName.Schwab => CreateSchwabTradingService(),
            _ => throw new NotSupportedException($"Broker '{broker}' is not implemented yet. Select Charles Schwab in Settings.")
        };
    }

    private SchwabTradingService CreateSchwabTradingService()
    {
        var creds  = SchwabCredentialsStore.Load();
        var tokens = SchwabTokenStore.Load();
        return new SchwabTradingService(
            _marketHttpClient, _schwabAuth,
            creds.ApiKey, creds.ApiSecret,
            tokens?.RefreshToken ?? string.Empty,
            tokens?.AccessToken ?? string.Empty,
            tokens?.AccessTokenExpiresAt ?? DateTime.MinValue,
            OnSchwabTokenRenewed);
    }

    // Same broker-dispatch pattern as CreateTradingService, for the market-data (quotes) side.
    private IMarketDataService CreateMarketDataService(bool enableDumps)
    {
        var broker = BrokerSettingsStore.Load();
        return broker switch
        {
            BrokerName.Schwab => CreateSchwabMarketDataService(enableDumps),
            _ => throw new NotSupportedException($"Broker '{broker}' is not implemented yet. Select Charles Schwab in Settings.")
        };
    }

    private SchwabMarketDataService CreateSchwabMarketDataService(bool enableDumps)
    {
        var creds  = SchwabCredentialsStore.Load();
        var tokens = SchwabTokenStore.Load();
        return new SchwabMarketDataService(
            _marketHttpClient, _schwabAuth,
            creds.ApiKey, creds.ApiSecret,
            tokens?.RefreshToken ?? string.Empty,
            tokens?.AccessToken ?? string.Empty,
            tokens?.AccessTokenExpiresAt ?? DateTime.MinValue,
            OnSchwabTokenRenewed, enableDumps);
    }

    private async Task PlaceRealTradeAsync(int rowIndex, bool withTarget)
    {
        var account = SelectedAccountStore.Load();
        if (account == null || string.IsNullOrEmpty(account.HashValue))
        {
            MessageBox.Show("No broker account selected. Go to Settings → Broker Accounts, click Refresh Accounts and pick a default.",
                "No Account Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_selectedTicker == null) return;

        var row          = dgvQuotes.Rows[rowIndex];
        var rowType      = row.Tag?.ToString() ?? "CALL";
        var strikeStr    = row.Cells["colStrikePrice"].Value?.ToString() ?? string.Empty;
        var contractsStr = row.Cells["colContracts"].Value?.ToString() ?? "0";
        var level        = row.Cells["colLevel"].Value?.ToString() ?? string.Empty;
        var symbol       = _selectedTicker.Symbol;

        var (bid, ask) = ReadRowBidAsk(row, rowType);
        if (ask <= 0) return;
        if (!int.TryParse(contractsStr, out var qty) || qty <= 0)
        {
            MessageBox.Show("Invalid contract quantity.", "Order", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!decimal.TryParse(strikeStr, out var strike)) return;

        var expDate = ExpirationDateResolver.Resolve(_selectedTicker.ExpDate);
        var occ     = OccOptionSymbol.Build(symbol, expDate, rowType, strike);
        var masked  = MaskAccount(account.AccountNumber);
        decimal.TryParse(TargetSettingsStore.Load(), out var targetPct);
        var targetLine = withTarget ? $"\nExit: SELL_TO_CLOSE LIMIT at +{targetPct:F0}% of fill" : string.Empty;

        // Show the confirmation dialog unless the user opted out in Settings — either way, this
        // is a REAL order (with or without a target), never a demo trade.
        if (chkShowOrderConfirmation.Checked)
        {
            var confirm = MessageBox.Show(
                $"Send a REAL market order to Schwab?\n\n" +
                $"Account: {masked}\n" +
                $"Symbol:  {symbol} {rowType}\n" +
                $"Strike:  {strikeStr}\n" +
                $"Expiry:  {expDate:yyyy-MM-dd}\n" +
                $"Qty:     {qty} contract(s)\n" +
                $"Entry:   MARKET BUY_TO_OPEN{targetLine}\n\n" +
                $"OCC: {occ}",
                "Confirm REAL Order", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;
        }

        try
        {
            var trading = CreateTradingService();
            var now = DateTime.Now.ToString("HH:mm:ss");
            LogLine($"{now} [Order] Sending MARKET BUY_TO_OPEN  {symbol} {rowType} {strikeStr} x{qty}  acct {masked}", Color.Cyan);

            var entryOrderId = await trading.PlaceOptionMarketOrderAsync(account.HashValue, occ, "BUY_TO_OPEN", qty);
            LogLine($"{now} [Order] Entry accepted — order id {entryOrderId}", Color.LimeGreen);

            // Record the real position in grid + backend + screenshot.
            // Trade-Target rows still auto-close in the log (mirrors the real LIMIT order closing
            // on the server); plain Trade rows are manual-close only.
            var (_, tradeRow) = await RecordEntryAsync(symbol, rowType, strikeStr, level, bid, ask, contractsStr, "Trade REAL (Schwab)", isDemo: false, suppressAutoClose: !withTarget,
                accountHash: account.HashValue, occSymbol: occ, quantity: qty);

            // Poll for the real fill, sync it into the log, then (if Trade-Target) send the LIMIT exit.
            _ = FinalizeRealEntryAsync(trading, account.HashValue, occ, qty, entryOrderId, targetPct, withTarget, tradeRow);
        }
        catch (Exception ex)
        {
            LogLine($"{DateTime.Now:HH:mm:ss} [Order] FAILED: {ex.Message}", Color.Red);
            MessageBox.Show($"Order failed: {ex.Message}", "Order Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Waits for the entry order to fill, syncs the real EntryPrice/Target into the Trades grid, and —
    // for Trade-Target — sends the SELL_TO_CLOSE LIMIT order so the position closes itself on the server.
    // The Trades grid will auto-close the same row once PnL_Percent reaches PnL_Target (see UpdateTradesPnL).
    private async Task FinalizeRealEntryAsync(ITradingService trading, string accountHash, string occ,
        int qty, long entryOrderId, decimal targetPct, bool withTarget, DataGridViewRow row)
    {
        decimal? fill = null;
        for (int i = 0; i < 5 && fill == null; i++)
        {
            await Task.Delay(1500);
            var order = await trading.GetOrderAsync(accountHash, entryOrderId);
            if (order.Status.Equals("FILLED", StringComparison.OrdinalIgnoreCase) && order.FilledPrice.HasValue)
                fill = order.FilledPrice;
        }

        if (fill == null)
        {
            LogLine($"{DateTime.Now:HH:mm:ss} [Order] Entry fill not confirmed yet — keeping Ask as EntryPrice. Target exit NOT sent.", Color.Orange);
            return;
        }

        var targetPrice = Math.Round(fill.Value * (1 + targetPct / 100m), 2);

        Invoke(() =>
        {
            if (!string.IsNullOrEmpty(row.Cells["colTradeExitTime"].Value?.ToString())) return; // already closed
            row.Cells["colTradeEntryPrice"].Value = fill.Value.ToString("F2");
            row.Cells["colTradeTBid"].Value        = targetPrice.ToString("F2");
            LogLine($"{DateTime.Now:HH:mm:ss} [Order] Real EntryPrice confirmed: {fill.Value:F2}  Target: {targetPrice:F2}", Color.LimeGreen);
        });

        if (withTarget)
        {
            var exitOrderId = await trading.PlaceOptionLimitOrderAsync(accountHash, occ, "SELL_TO_CLOSE", qty, targetPrice);
            LogLine($"{DateTime.Now:HH:mm:ss} [Order] Target exit LIMIT {targetPrice:F2} sent — order id {exitOrderId}", Color.LimeGreen);

            Invoke(() =>
            {
                if (row.Tag is TradeRowTag tag)
                    row.Tag = tag with { ExitOrderId = exitOrderId };
            });
        }
    }

    // ----- Broker accounts (Settings tab) -----

    private void LoadCachedAccounts()
    {
        var cached = AccountsCacheStore.Load();
        if (cached.Count > 0) PopulateAccountsGrid(cached, persist: false);
    }

    private async void BtnRefreshAccounts_Click(object? sender, EventArgs e)
    {
        var creds = SchwabCredentialsStore.Load();
        if (string.IsNullOrEmpty(creds.ApiKey) || string.IsNullOrEmpty(creds.ApiSecret))
        {
            MessageBox.Show("Schwab API credentials are not configured.", "Missing Credentials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnRefreshAccounts.Enabled = false;
        try
        {
            var service  = CreateTradingService();
            var accounts = (await service.GetAccountNumbersAsync()).ToList();
            PopulateAccountsGrid(accounts);
            // LogLine($"{DateTime.Now:HH:mm:ss} [Accounts] Loaded {accounts.Count} account(s)", Color.Yellow);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load accounts: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnRefreshAccounts.Enabled = true;
        }
    }

    private void PopulateAccountsGrid(List<BrokerAccountDto> accounts, bool persist = true)
    {
        _accounts = accounts;
        dgvAccounts.Rows.Clear();
        var selected = SelectedAccountStore.Load();

        if (persist) AccountsCacheStore.Save(accounts);

        foreach (var acct in accounts)
        {
            var isDefault = selected != null && selected.HashValue == acct.AccountId;
            dgvAccounts.Rows.Add(isDefault, MaskAccount(acct.AccountNumber), acct.AccountId);
        }

        // Default to the first account if none was persisted.
        if (selected == null && accounts.Count > 0)
        {
            dgvAccounts.Rows[0].Cells["colAccountDefault"].Value = true;
            SelectedAccountStore.Save(new SelectedAccount(accounts[0].AccountNumber, accounts[0].AccountId));
        }
    }

    private void DgvAccounts_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != dgvAccounts.Columns["colAccountDefault"].Index) return;

        dgvAccounts.CommitEdit(DataGridViewDataErrorContexts.Commit);

        // Radio behaviour: the clicked row is the only one checked (and can't be unchecked).
        for (int i = 0; i < dgvAccounts.Rows.Count; i++)
            dgvAccounts.Rows[i].Cells["colAccountDefault"].Value = (i == e.RowIndex);

        var hash   = dgvAccounts.Rows[e.RowIndex].Cells["colAccountHash"].Value?.ToString() ?? string.Empty;
        var number = _accounts.FirstOrDefault(a => a.AccountId == hash)?.AccountNumber ?? string.Empty;
        SelectedAccountStore.Save(new SelectedAccount(number, hash));
        // LogLine($"{DateTime.Now:HH:mm:ss} [Accounts] Default account set to {MaskAccount(number)}", Color.Yellow);
    }

    private static string MaskAccount(string number) =>
        string.IsNullOrEmpty(number) || number.Length <= 4
            ? number
            : new string('•', number.Length - 4) + number[^4..];

    private async void DgvTrades_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != dgvTrades.Columns["colTradeClose"].Index) return;
        var row = dgvTrades.Rows[e.RowIndex];
        if (!string.IsNullOrEmpty(row.Cells["colTradeExitTime"].Value?.ToString())) return;

        await CloseTradeRowAsync(row, "MANUAL");
    }

    private async Task CloseTradeRowAsync(DataGridViewRow row, string closeType)
    {
        var now       = DateTime.Now;
        var nowStr    = now.ToString("HH:mm:ss");
        var type      = row.Cells["colTradeType"].Value?.ToString() ?? string.Empty;
        var cBid      = row.Cells["colTradeCBid"].Value?.ToString() ?? string.Empty;

        // Confirmed fill price of the real SELL_TO_CLOSE order (MANUAL close only). Null means
        // either this isn't a real-broker MANUAL close, or the fill wasn't confirmed in time —
        // both cases fall back to cBid (the last polled bid shown on screen) for the PnL math.
        decimal? realClosePrice = null;

        // Real broker trades need an actual SELL_TO_CLOSE order sent to Schwab — updating the log
        // alone never touched the market (that was the bug: log said "Closed" but the position
        // stayed open at the broker). "TARGET" closes are exempt: that path only fires for
        // Trade-Target, where the LIMIT exit already resting at the broker is what's actually
        // closing the position, so sending another order here would just double-sell it.
        if (closeType == "MANUAL" && row.Tag is TradeRowTag { AccountHash: not null, OccSymbol: not null, Quantity: > 0 } realTag)
        {
            try
            {
                var trading = CreateTradingService();
                if (realTag.ExitOrderId is { } pendingExitId)
                {
                    await trading.CancelOrderAsync(realTag.AccountHash, pendingExitId);
                    LogLine($"{nowStr} [Order] Cancelled pending target exit — order id {pendingExitId}", Color.Orange);
                }

                LogLine($"{nowStr} [Order] Precio en pantalla al hacer click: {cBid}", Color.Cyan);

                var closeOrderId = await trading.PlaceOptionMarketOrderAsync(realTag.AccountHash, realTag.OccSymbol, "SELL_TO_CLOSE", realTag.Quantity);
                LogLine($"{nowStr} [Order] Sending MARKET SELL_TO_CLOSE x{realTag.Quantity} — order id {closeOrderId}", Color.Cyan);

                // Poll for the real fill price — same pattern as FinalizeRealEntryAsync uses for entries.
                for (int i = 0; i < 5 && realClosePrice == null; i++)
                {
                    await Task.Delay(1500);
                    var order = await trading.GetOrderAsync(realTag.AccountHash, closeOrderId);
                    if (order.Status.Equals("FILLED", StringComparison.OrdinalIgnoreCase) && order.FilledPrice.HasValue)
                        realClosePrice = order.FilledPrice;
                }

                if (realClosePrice.HasValue)
                    LogLine($"{DateTime.Now:HH:mm:ss} [Order] Precio real de cierre confirmado: {realClosePrice.Value:F2}", Color.LimeGreen);
                else
                    LogLine($"{DateTime.Now:HH:mm:ss} [Order] Cierre no confirmado a tiempo — usando último Bid visible como referencia.", Color.Orange);
            }
            catch (Exception ex)
            {
                LogLine($"{nowStr} [Order] FAILED to close at broker: {ex.Message}", Color.Red);
                MessageBox.Show(
                    $"The position could NOT be closed at the broker:\n\n{ex.Message}\n\nThe trade stays open here — close it manually in Schwab and try again, or retry Close.",
                    "Close Order Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return; // leave the row open — don't lie in the log about the position being closed
            }
        }

        var strike    = row.Cells["colTradeStrike"].Value?.ToString() ?? string.Empty;
        var pnl       = row.Cells["colTradePnL"].Value?.ToString() ?? string.Empty;
        var pnlPct    = row.Cells["colTradePnLPercent"].Value?.ToString() ?? string.Empty;
        var spotPrice = _lastSpotPrice > 0 ? _lastSpotPrice.ToString("F2") : string.Empty;
        var symbol    = _selectedTicker?.Symbol ?? "UNK";

        var duration = TimeSpan.Zero;
        int tradeId  = 0;
        if (row.Tag is TradeRowTag tag)
        {
            duration = now - tag.EntryTime;
            tradeId  = tag.TradeId;
        }

        // Recalculate PnL at close time using the confirmed real fill price when we have one
        // (real MANUAL close), otherwise fall back to the last polled bid shown on screen —
        // same behavior as before this change for demo trades / TARGET closes / unconfirmed fills.
        var entryPriceStr = row.Cells["colTradeEntryPrice"].Value?.ToString() ?? "0";
        var contractsStr  = row.Cells["colTradeContracts"].Value?.ToString() ?? "0";
        decimal.TryParse(cBid, out var cBidParsed);
        var exitBid = realClosePrice ?? cBidParsed;
        decimal.TryParse(entryPriceStr, out var entryPrice);
        decimal.TryParse(contractsStr, out var contractsForPnl);
        var pnlVal    = Math.Round((exitBid - entryPrice) * contractsForPnl * 100, 2);
        var pnlPctVal = entryPrice > 0 ? Math.Round((exitBid - entryPrice) / entryPrice * 100, 1) : 0m;
        pnl    = pnlVal.ToString("F2");
        pnlPct = pnlPctVal.ToString("F1");

        row.Cells["colTradePnL"].Value        = pnl;
        row.Cells["colTradePnLPercent"].Value = pnlPct;
        // Closing PnL (real fill price) can be a new low/high the live ticks never saw.
        UpdatePnLMinMax(row, pnlVal);
        row.Cells["colTradeExitTime"].Value   = nowStr;
        row.Cells["colTradeClose"].Value      = "Closed";
        row.DefaultCellStyle.ForeColor        = Color.Gray;

        var pnlColor = pnlVal >= 0 ? Color.LimeGreen : Color.Red;

        var realCloseLog = realClosePrice.HasValue ? $"  RealClose: {realClosePrice.Value:F2}" : string.Empty;
        LogLine(string.Empty, Color.White);
        LogLine($"{nowStr} Close {closeType} ({type})  SpotPrice: {spotPrice}  Strike: {strike}  C_Bid: {cBid}{realCloseLog}", Color.White);
        LogLine($"{nowStr} PnL: {pnl}  PnL_Percent: {pnlPct}", pnlColor);
        LogLine($"{nowStr} Duration: {duration:hh\\:mm\\:ss}", Color.White);
        System.Windows.Forms.Application.DoEvents();

        // Remove from local persistence
        if (tradeId > 0)
            OpenTradesStore.Remove(tradeId);

        // Close trade in API
        if (tradeId > 0)
        {
            var exitPrice = exitBid;
            await CloseTradeInApiAsync(tradeId, exitPrice, pnlVal, pnlPctVal, duration);
        }

        // Screenshot exit
        var exitPath = CaptureScreenshot(symbol, type, "exit");
        // LogLine($"{nowStr} Screenshot: {exitPath}", Color.DimGray);
        _ = UploadScreenshotAsync(exitPath, symbol, type, tradeId, nowStr);

        // Screenshot TradeLog (Trades + Logger section of the form)
        await Task.Delay(100); // let UI settle
        var tradeLogPath = CaptureTradeLogScreenshot(symbol, type);
        // LogLine($"{nowStr} Screenshot: {tradeLogPath}", Color.DimGray);
        _ = UploadScreenshotAsync(tradeLogPath, symbol, type, tradeId, nowStr);
    }

    private static string CaptureScreenshot(string symbol, string optionType, string tag)
    {
        var folder = Path.Combine(@"C:\Screenshots", DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(folder);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName  = $"{symbol}_{optionType}_{timestamp}_{tag}.png";
        var filePath  = Path.Combine(folder, fileName);

        var captureRect = GetCaptureRect(symbol);
        using var bmp = new Bitmap(captureRect.Width, captureRect.Height);
        using var g   = Graphics.FromImage(bmp);
        g.CopyFromScreen(new Point(captureRect.X, captureRect.Y), Point.Empty, captureRect.Size);
        bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

        return filePath;
    }

    private string CaptureTradeLogScreenshot(string symbol, string optionType)
    {
        var folder = Path.Combine(@"C:\Screenshots", DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(folder);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName  = $"{symbol}_{optionType}_{timestamp}_TradeLog.png";
        var filePath  = Path.Combine(folder, fileName);

        // Build bounding rect that covers grpTrades + grpLogger on screen
        var topLeft     = grpTrades.PointToScreen(Point.Empty);
        var bottomRight = grpLogger.PointToScreen(new Point(grpLogger.Width, grpLogger.Height));
        var rect        = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);

        using var bmp = new Bitmap(rect.Width, rect.Height);
        using var g   = Graphics.FromImage(bmp);
        g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
        bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

        return filePath;
    }

    private static Rectangle GetCaptureRect(string symbol)
    {
        var coords = ScreenCoordsStore.Load();
        coords.TryGetValue(symbol.ToUpperInvariant(), out var tc);

        if (tc is not null &&
            !string.IsNullOrWhiteSpace(tc.Coords1) &&
            !string.IsNullOrWhiteSpace(tc.Coords2))
        {
            var parts1 = tc.Coords1.Split(',');
            var parts2 = tc.Coords2.Split(',');
            if (parts1.Length == 2 && parts2.Length == 2 &&
                int.TryParse(parts1[0].Trim(), out int x1) &&
                int.TryParse(parts1[1].Trim(), out int y1) &&
                int.TryParse(parts2[0].Trim(), out int x2) &&
                int.TryParse(parts2[1].Trim(), out int y2))
            {
                int width  = Math.Abs(x2 - x1);
                int height = Math.Abs(y2 - y1);
                if (width > 0 && height > 0)
                    return new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2), width, height);
            }
        }

        // Fallback: full primary screen
        return Screen.PrimaryScreen!.Bounds;
    }

    // (Re)builds one row (button + 2 coord textboxes) per symbol currently in the Tickers table,
    // preloading whatever coordinates were already saved for that symbol. Called at startup and
    // whenever the Tickers table is saved, so adding/removing a ticker keeps this in sync.
    private void LoadCoordsButtons()
    {
        pnlCoordsRows.Controls.Clear();
        _coordsTextboxes.Clear();
        _coordsButtons.Clear();

        var saved = ScreenCoordsStore.Load();
        var symbols = TickerSettingsStore.Load()
            .Select(t => t.Symbol.ToUpperInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        var y = 2;
        foreach (var symbol in symbols)
        {
            var btn = new Button
            {
                Text     = symbol,
                Location = new Point(5, y),
                Size     = new Size(60, 23),
                Tag      = symbol
            };
            btn.Click += BtnCoords_Click;

            var t1 = new TextBox { Location = new Point(73, y),  Size = new Size(80, 23), ReadOnly = true };
            var t2 = new TextBox { Location = new Point(161, y), Size = new Size(80, 23), ReadOnly = true };
            if (saved.TryGetValue(symbol, out var tc))
            {
                t1.Text = tc.Coords1;
                t2.Text = tc.Coords2;
            }

            pnlCoordsRows.Controls.Add(btn);
            pnlCoordsRows.Controls.Add(t1);
            pnlCoordsRows.Controls.Add(t2);

            _coordsTextboxes[symbol] = (t1, t2);
            _coordsButtons.Add(btn);

            y += 32;
        }
    }

    private void BtnSaveCoords_Click(object? sender, EventArgs e)
    {
        var coords = _coordsTextboxes.ToDictionary(
            kv => kv.Key,
            kv => new TickerCoords(kv.Value.T1.Text, kv.Value.T2.Text));
        ScreenCoordsStore.Save(coords);

        MessageBox.Show("Coordinates saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BtnResetCoords_Click(object? sender, EventArgs e)
    {
        foreach (var (t1, t2) in _coordsTextboxes.Values)
            t1.Text = t2.Text = string.Empty;

        var current = ScreenCoordsStore.Load();
        foreach (var symbol in _coordsTextboxes.Keys)
            current[symbol] = new TickerCoords(string.Empty, string.Empty);
        ScreenCoordsStore.Save(current);
    }

    private void BtnCoords_Click(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string symbol) return;
        if (!_coordsTextboxes.TryGetValue(symbol, out var targets)) return;

        _coordsTarget1    = targets.T1;
        _coordsTarget2    = targets.T2;
        _coordsClickCount = 0;

        // Change cursor to crosshair to indicate capture mode
        this.Cursor = Cursors.Cross;

        // Poll mouse clicks via timer every 50ms
        _coordsCaptureTimer?.Stop();
        _coordsCaptureTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _coordsCaptureTimer.Tick += CoordsCaptureTick;
        _coordsCaptureTimer.Start();

        btn.BackColor = Color.Yellow;
        btn.Text = btn.Text + " ...";
    }

    private void CoordsCaptureTick(object? sender, EventArgs e)
    {
        if ((Control.MouseButtons & MouseButtons.Left) == 0) return;

        var pos = Control.MousePosition;

        if (_coordsClickCount == 0)
        {
            _coordsTarget1!.Text = $"{pos.X},{pos.Y}";
            _coordsClickCount = 1;
        }
        else
        {
            _coordsTarget2!.Text = $"{pos.X},{pos.Y}";
            StopCoordsCapture();
        }

        // Wait for mouse release before detecting next click
        while ((Control.MouseButtons & MouseButtons.Left) != 0)
            System.Windows.Forms.Application.DoEvents();
    }

    private void StopCoordsCapture()
    {
        _coordsCaptureTimer?.Stop();
        _coordsCaptureTimer = null;
        this.Cursor = Cursors.Default;

        // Reset every dynamically-generated coords button's appearance.
        foreach (var b in _coordsButtons)
        {
            b.BackColor = SystemColors.Control;
            b.Text = b.Text.Replace(" ...", string.Empty);
        }
    }

    private async Task<int> SaveTradeToApiAsync(string symbol, string rowType, string strike, decimal ask,
        int contracts, int level, decimal targetPct, DateTime entryTime, bool isDemo = false)
    {
        try
        {
            var ticker = _selectedTicker!;
            var expDate = ExpirationDateResolver.Resolve(ticker.ExpDate);
            var optionType = rowType == "CALL"
                ? OptionsTrader.Domain.Enums.OptionType.Call
                : OptionsTrader.Domain.Enums.OptionType.Put;

            var payload = new
            {
                Symbol         = symbol,
                OptionType     = (int)optionType,
                StrikePrice    = decimal.Parse(strike),
                SpotPrice      = _lastSpotPrice,
                ExpirationDate = expDate.ToString("yyyy-MM-dd"),
                EntryPrice     = ask,
                EntryTime      = entryTime,
                Contracts      = contracts,
                Level          = level,
                TargetPercent  = targetPct,
                IsDemo         = isDemo,
                Broker         = 0
            };

            var response = await _apiHttpClient.PostAsJsonAsync($"{ApiBaseUrl}/trades", payload);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                this.Invoke(() => LogLine($"API Error {(int)response.StatusCode}: {json}", Color.Red));
                return 0;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetInt32();
        }
        catch (Exception ex)
        {
            this.Invoke(() => LogLine($"API Exception: {ex.Message}", Color.Red));
            return 0;
        }
    }

    private async Task CloseTradeInApiAsync(int tradeId, decimal exitPrice, decimal pnl, decimal pnlPercent, TimeSpan duration)
    {
        try
        {
            var payload = new
            {
                ExitPrice  = exitPrice,
                PnL        = pnl,
                PnLPercent = pnlPercent,
                Duration   = TimeSpan.FromSeconds(Math.Floor(duration.TotalSeconds))
            };
            await _apiHttpClient.PatchAsJsonAsync($"{ApiBaseUrl}/trades/{tradeId}/close", payload);
        }
        catch { }
    }

    private async Task UploadScreenshotAsync(string localPath, string symbol, string optionType, int tradeId, string timeStr)
    {
        try
        {
            var aws = AwsSettingsStore.Load();
            if (string.IsNullOrEmpty(aws.AccessKey))
            {
                this.Invoke(() => LogLine($"{timeStr} Screenshot NOT uploaded — AWS credentials missing in Settings.", Color.Orange));
                return;
            }

            var s3Client = new AmazonS3Client(
                aws.AccessKey, aws.SecretKey,
                Amazon.RegionEndpoint.GetBySystemName(aws.Region));

            var folder    = DateTime.Now.ToString("yyyyMMdd");
            var fileName  = Path.GetFileName(localPath);
            var s3Key     = $"screenshots/{folder}/{fileName}";

            using var stream = File.OpenRead(localPath);
            await s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName  = aws.BucketName,
                Key         = s3Key,
                InputStream = stream,
                ContentType = "image/png"
            });

            var s3Url = $"https://{aws.BucketName}.s3.amazonaws.com/{s3Key}";

            if (tradeId > 0)
            {
                var payload = new { TradeId = tradeId, Symbol = symbol, S3Url = s3Url };
                var response = await _apiHttpClient.PostAsJsonAsync($"{ApiBaseUrl}/screenshots", payload);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    this.Invoke(() => LogLine($"Screenshot API Error {(int)response.StatusCode}: {err}", Color.Red));
                }
            }

            // this.Invoke(() => LogLine($"{timeStr} Uploaded: {s3Url}", Color.DimGray));
        }
        catch (Exception ex)
        {
            // this.Invoke(() => LogLine($"{timeStr} S3 upload failed: {ex.Message}", Color.Red));
        }
    }

    private void LogLine(string text, Color color)
    {
        rtbLogger.SelectionStart  = rtbLogger.TextLength;
        rtbLogger.SelectionLength = 0;
        rtbLogger.SelectionColor  = color;
        rtbLogger.AppendText(text + Environment.NewLine);
        rtbLogger.ScrollToCaret();
    }

    // Extends the row's Min/Max PnL columns if the given value is a new low/high. Session-only —
    // not persisted to OpenTradesStore, so it resets if the app restarts mid-trade.
    private static void UpdatePnLMinMax(DataGridViewRow row, decimal pnl)
    {
        var minCell = row.Cells["colTradePnLMin"];
        var maxCell = row.Cells["colTradePnLMax"];

        if (!decimal.TryParse(minCell.Value?.ToString(), out var min) || pnl < min)
        {
            minCell.Value             = pnl.ToString("F2");
            minCell.Style.ForeColor   = Color.Red;
        }

        if (!decimal.TryParse(maxCell.Value?.ToString(), out var max) || pnl > max)
        {
            maxCell.Value             = pnl.ToString("F2");
            maxCell.Style.ForeColor   = Color.Green;
        }
    }

    private void UpdateTradesPnL(Dictionary<(string, decimal), OptionQuoteDto> callMap,
                                  Dictionary<(string, decimal), OptionQuoteDto> putMap)
    {
        var rowsToClose = new List<DataGridViewRow>();

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

            UpdatePnLMinMax(row, pnl);

            // Auto-close when the current bid reaches the target price (T_Bid).
            // Plain real trades (no target order) are manual-close only; Trade-Target rows still
            // auto-close here to mirror the real LIMIT order closing on the server.
            var suppressAutoClose = row.Tag is TradeRowTag { SuppressAutoClose: true };
            if (!suppressAutoClose
                && decimal.TryParse(row.Cells["colTradeTBid"].Value?.ToString(), out var targetBid)
                && targetBid > 0 && currentBid >= targetBid)
            {
                rowsToClose.Add(row);
            }
        }

        // Fire target closes after iterating so the loop isn't re-entered mid-enumeration.
        foreach (var row in rowsToClose)
            _ = CloseTradeRowAsync(row, "TARGET");
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
    private static string FormatStrike(decimal value) =>
        value % 1 == 0 ? value.ToString("F0") : value.ToString("F2");

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
        // Floor, not round — Position Size % is a risk cap, so rounding up could spend more
        // than the configured budget (e.g. $300 @ 1.18 = 2.54 contracts must stay at 2, not 3).
        var contracts = Math.Floor(positionSize / (ask * 100));
        return contracts > 0 ? contracts.ToString("F0") : string.Empty;
    }

    private string GetContractsValue(decimal ask)
    {
        var selected = ContractsSettingsStore.Load();
        if (selected != "PositionSize" && int.TryParse(selected, out var fixedCount))
            return fixedCount.ToString();
        return CalcContracts(GetPositionSize(), ask);
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
        LoadTickerButtons();
        LoadCoordsButtons();
    }
}
