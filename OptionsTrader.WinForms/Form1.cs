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
    string? AccountHash = null, string? OccSymbol = null, int Quantity = 0, long? ExitOrderId = null,
    DateOnly ExpirationDate = default);

public partial class Form1 : Form
{
    private readonly SchwabAuthService _schwabAuth = new(new HttpClient());
    private readonly HttpClient _marketHttpClient = new();
    private readonly HttpClient _apiHttpClient    = new();
    private const string ApiBaseUrl = "http://3.133.58.172:5000/api";
    private System.Windows.Forms.Timer? _pollingTimer;
    private System.Windows.Forms.Timer? _marketOpenTimer;
    private System.Windows.Forms.Timer? _autoCaptureTimer;
    private System.Windows.Forms.Timer? _marketSnapshotTimer;
    private DateOnly? _openSnapshotDoneFor;
    private DateOnly? _closeSnapshotDoneFor;
    private DateOnly? _expiredTradesClosedFor;
    private DateOnly? _wsForcedDisconnectDoneFor;

    // True only for the one instance/process that actually won the hub race and owns the real
    // Schwab streaming connection (SetUpLiveFeedAsync) — every other instance relays through it
    // via CandleHubClient instead, so only the hub instance should ever force-disconnect it.
    private bool _isWebSocketHub;
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

    // Live-chart streaming: Schwab allows only ONE streaming connection per account, but running
    // one app instance per ticker means several PROCESSES need candle data at once. The first
    // instance to bind LiveHubPort becomes the "hub" — it owns the real SchwabStreamerClient
    // (WebSocket) and rebroadcasts every candle over that local port. Every other instance is a
    // "client" — it connects to the hub via CandleHubClient instead of touching Schwab's socket
    // itself. Either way, _historyClient still does its own REST history fetches directly (no
    // per-account limit on that), and _liveFeed is whichever of the two is actually feeding ticks.
    // No cross-instance failover: if the hub instance closes, clients just show "disconnected"
    // until that instance (or another) is reopened.
    private const int LiveHubPort = 51919;
    private CandleHubServer? _candleHubServer;
    private CandleHubClient? _candleHubClient;
    private SchwabStreamerClient? _historyClient;
    private ICandleFeed? _liveFeed;
    private readonly Dictionary<string, MultiChartForm> _liveChartForms = new();
    private FourEtfChartsForm? _fourEtfChartsForm;

    private decimal _lastSpotPrice;
    private CsvLogger? _csvLogger;
    private CsvLogger? _csvLoggerNext;
    private List<BrokerAccountDto> _accounts = new();
    private string _selectedCounts = "6"; // session-only, always defaults to 6 on launch
    private List<OptionQuoteDto> _lastAllQuotes = new(); // current-expiration chain from the last fetch, for instant re-filtering

    public Form1()
    {
        InitializeComponent();
        FormClosing += async (s, e) =>
        {
            _csvLogger?.Dispose(); _csvLoggerNext?.Dispose(); _autoCaptureTimer?.Dispose(); _ivHistorialTimer?.Dispose();
            if (_historyClient != null) await _historyClient.DisposeAsync();
            if (_candleHubClient != null) await _candleHubClient.DisposeAsync();
            _candleHubServer?.Dispose();
        };

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
        StartMarketOpenCloseSnapshotScheduler();

        // Deletes every Telegram push this app has sent — across ALL ticker instances, since
        // they all share the same telegram_pushes.json file and the same channel. Added via code
        // (not the designer) so it doesn't require touching Form1.Designer.cs's layout.
        var btnDeleteTelegramPushes = new Button
        {
            Location = new Point(1020, 124),
            Size     = new Size(90, 25),
            Text     = "Del. Telegram"
        };
        btnDeleteTelegramPushes.Click += BtnDeleteTelegramPushes_Click;
        tabQuotes.Controls.Add(btnDeleteTelegramPushes);

        // "History" tab — Calendar (trading journal) + Trade Log views over TradeHistoryStore.
        // Built entirely in HistoryTabPanel (no designer file), same convention as
        // MultiChartForm/ChartPanel.
        var tabHistory = new TabPage("History") { Padding = new Padding(8) };
        tabHistory.Controls.Add(new HistoryTabPanel());
        tabControl.TabPages.Add(tabHistory);
    }

    private async void BtnDeleteTelegramPushes_Click(object? sender, EventArgs e)
    {
        var pushes = TelegramPushStore.Load();
        if (pushes.Count == 0)
        {
            MessageBox.Show("No hay pushes de Telegram guardados para borrar.", "Eliminar pushes de Telegram",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Se van a borrar {pushes.Count} mensajes enviados a Telegram (de todas las instancias). Esta acción no se puede deshacer.\n\n¿Continuar?",
            "Eliminar pushes de Telegram", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        var (botToken, chatId) = TelegramSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            MessageBox.Show("Telegram no está configurado (falta Bot Token o Chat ID).", "Eliminar pushes de Telegram",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int ok = 0, failed = 0;
        foreach (var push in pushes)
        {
            var (deleted, detail) = await TelegramNotifier.DeleteMessageAsync(botToken, push.ChatId, push.MessageId);
            if (deleted) ok++;
            else { failed++; LogLine($"{DateTime.Now:HH:mm:ss} [Telegram] No se pudo borrar mensaje {push.MessageId} ({push.Symbol}/{push.Kind}): {detail}", Color.Orange); }
        }

        // Best-effort sweep — clear regardless of individual failures (a message Telegram
        // rejected, e.g. older than ~48h, will never succeed on a later retry either).
        TelegramPushStore.Clear();

        LogLine($"{DateTime.Now:HH:mm:ss} [Telegram] Borrados {ok}/{pushes.Count} pushes ({failed} fallaron).", Color.Cyan);
        MessageBox.Show($"Borrados {ok} de {pushes.Count} mensajes.{(failed > 0 ? $"\n{failed} fallaron (ver log)." : string.Empty)}",
            "Eliminar pushes de Telegram", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Anchored user — logs straight in against the API on startup, no LoginForm prompt.
    // Change these two constants to switch which of the 5 fixed app users this instance runs as.
    private const string AnchoredUsername = "user1";
    private const string AnchoredPassword = "Pass1234!";

    private async void Form1_Load(object? sender, EventArgs e)
    {
        try
        {
            var response = await _apiHttpClient.PostAsJsonAsync($"{ApiBaseUrl}/auth/login",
                new { username = AnchoredUsername, password = AnchoredPassword });

            var result = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<LoginResult>()
                : null;

            if (result?.AccessToken == null)
            {
                MessageBox.Show($"Login failed for anchored user '{AnchoredUsername}'.", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(0);
                return;
            }

            _apiHttpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.AccessToken);

            lblStatusUser.Text = $"User: {result.Name} {result.LastName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Login failed: {ex.Message}", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(0);
        }

        // Decide hub-vs-client (and become the token hub if we win the race) as soon as the app
        // opens, instead of waiting for the user to open a Live Chart window — token renewal
        // depends on _isWebSocketHub being settled early, not just streaming.
        try
        {
            await EnsureLiveFeedReadyAsync();
        }
        catch (Exception ex)
        {
            _liveFeedReadyTask = null;
            LogLine($"{DateTime.Now:HH:mm:ss} [Hub] No se pudo inicializar streaming al arrancar: {ex.Message}", Color.OrangeRed);
        }
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

    // Saves a combined 3-chart snapshot for every symbol that currently has a Live Chart window
    // open (_liveChartForms), at market open (9:30 AM ET) and close (4:00 PM ET) — skips silently
    // for any symbol that isn't open, never opens one just to capture it. Runs independently in
    // every app instance (each only knows about its OWN _liveChartForms, unlike the shared hub).
    private void StartMarketOpenCloseSnapshotScheduler()
    {
        _marketSnapshotTimer = new System.Windows.Forms.Timer { Interval = 20000 }; // check every 20s
        _marketSnapshotTimer.Tick += (s, e) =>
        {
            var today = DateOnly.FromDateTime(MarketHours.NowEst);

            if (MarketHours.IsInMarketOpenSnapshotWindow && _openSnapshotDoneFor != today)
            {
                _openSnapshotDoneFor = today;
                _ = CaptureOpenCloseSnapshotsAsync("Open");
            }

            if (MarketHours.IsInMarketCloseSnapshotWindow && _closeSnapshotDoneFor != today)
            {
                _closeSnapshotDoneFor = today;
                _ = CaptureOpenCloseSnapshotsAsync("Close");
            }

            if (MarketHours.IsInMarketCloseSnapshotWindow && _expiredTradesClosedFor != today)
            {
                _expiredTradesClosedFor = today;
                _ = CloseExpiredTradesAsync();
            }

            if (_isWebSocketHub && MarketHours.IsInMarketCloseSnapshotWindow && _wsForcedDisconnectDoneFor != today)
            {
                _wsForcedDisconnectDoneFor = today;
                _ = ForceDisconnectWebSocketAsync();
            }
        };
        _marketSnapshotTimer.Start();
    }

    // Only this (the hub) instance's Schwab connection stays open past 4pm ET otherwise — force
    // it closed so the socket is only ever live from whenever this instance opened in the morning
    // until market close, instead of sitting connected all night for no reason.
    private async Task ForceDisconnectWebSocketAsync()
    {
        if (_historyClient == null) return;
        RaiseWsStatusEvent("Market closed — forcing disconnect from Schwab streamer");
        await _historyClient.StopAsync();
        RaiseWsStatusEvent("Disconnected");
    }

    // Buffers every WS event line for the session (the streamer connects once, when the FIRST
    // Live Charts window is opened — before that window even exists — so later windows need a
    // way to catch up on what already happened). Locked since reconnect events fire from the
    // streamer's own background thread.
    private readonly List<string> _wsEventLog = new();
    private readonly object _wsEventLogLock = new();

    // Only the hub instance ever calls this directly (ForceDisconnectWebSocketAsync, and the
    // OnWsStatusEvent hookup in SetUpLiveFeedAsync) — it both shows the line locally AND relays
    // it to every other instance's process via the hub TCP socket. Client instances instead relay
    // an already-hub-broadcast line straight into BroadcastWebSocketEventToCharts.
    private void RaiseWsStatusEvent(string text)
    {
        _candleHubServer?.BroadcastWsEvent(text);
        BroadcastWebSocketEventToCharts(text);
    }

    // Forwards a WS connect/disconnect/reconnect line to every currently open Live Charts child
    // window's own event log — the connection itself is owned by Form1 (the hub instance), not
    // any single chart window, so every open one gets the same line.
    private void BroadcastWebSocketEventToCharts(string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  [WS] {text}";
        lock (_wsEventLogLock) _wsEventLog.Add(line);

        foreach (var chartForm in _liveChartForms.Values.ToList())
        {
            if (!chartForm.IsDisposed)
                chartForm.LogWebSocketEvent(line);
        }
    }

    // At 4pm ET, any still-open trade whose own expiration date is today is worthless — close it
    // (PnL/Telegram push flow exactly like any other close) and mark the 1h chart with a red
    // "Expired!!!" label. Trades expiring on a later date are left untouched; RestoreOpenTrades
    // already reopens/keeps showing them on the next app start if still valid.
    private async Task CloseExpiredTradesAsync()
    {
        var today = DateOnly.FromDateTime(MarketHours.NowEst);

        var expiredRows = dgvTrades.Rows.Cast<DataGridViewRow>()
            .Where(r => r.Tag is TradeRowTag { ExpirationDate: var exp } && exp == today)
            .Where(r => string.IsNullOrEmpty(r.Cells["colTradeExitTime"].Value?.ToString()))
            .ToList();

        foreach (var row in expiredRows)
            await CloseTradeRowAsync(row, "EXPIRED");
    }

    private async Task CaptureOpenCloseSnapshotsAsync(string tag)
    {
        foreach (var (symbol, chartForm) in _liveChartForms.ToList())
        {
            if (chartForm.IsDisposed) continue;

            try
            {
                using var combined = await chartForm.CaptureCombinedChartImageAsync();
                if (combined == null) continue;

                var folder = Path.Combine(@"C:\OptionsData\ChartSnapshots", symbol);
                Directory.CreateDirectory(folder);
                var fileName = $"{symbol}_{MarketHours.NowEst:yyyyMMdd}_{tag}.png";
                combined.Save(Path.Combine(folder, fileName), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch
            {
                // Best-effort — a snapshot failure for one symbol must never affect trading or
                // the other symbols' snapshots.
            }
        }
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
        PopulateQuotesGrid(dgvQuotes, _lastAllQuotes, _selectedTicker, applyCountsFilter: true,
            selectedCounts: _selectedCounts, callOnly: chkCallFilter.Checked && !chkPutFilter.Checked, putOnly: chkPutFilter.Checked && !chkCallFilter.Checked);
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
        for (int i = 0; i < 6; i++)
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
                // Expired — close it out in the backend (PnL = 0, since it's worthless past
                // expiration) but don't add a row to the grid at all; the user only wants to see
                // trades that are still open/valid, not a "Closed" row for something that just
                // expired unattended.
                var entryTime = t.EntryTime;
                var now       = DateTime.Now;
                var duration  = now - entryTime;
                var pnl       = Math.Round((0m - t.EntryPrice) * decimal.Parse(t.Contracts) * 100, 2);
                var pnlPct    = t.EntryPrice > 0 ? Math.Round(pnl / (t.EntryPrice * decimal.Parse(t.Contracts) * 100) * 100, 1) : 0m;

                OpenTradesStore.Remove(t.TradeId);
                if (t.TradeId != 0) // 0 == entry never even got a local id (SaveTradeToApiAsync failed outright) — nothing to close
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
                restoredRow.Tag = new TradeRowTag(t.TradeId, t.EntryTime, ExpirationDate: t.ExpirationDate);
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
        rbNoTradeTarget.ForeColor = rbNoTradeTarget.Checked ? Color.DarkOrange : SystemColors.ControlText;
        rbNoTradeTarget.Font = new Font(rbNoTradeTarget.Font, rbNoTradeTarget.Checked ? FontStyle.Bold : FontStyle.Regular);
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

        // Wire logger so token events (hub renewals, non-hub reads/waits) appear in the log
        // panel — GetAccessTokenAsync can fire this from a background thread, hence the Invoke.
        _schwabAuth.SetLogCallback(msg =>
        {
            if (IsHandleCreated) Invoke(() => LogLine(msg, Color.Yellow));
        });

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

            // While LEVEL_ONE_EQUITIES stays disabled (see SchwabStreamerClient), feed this
            // polling cycle's spot price into the live chart's forming candle instead, if one
            // happens to be open for this symbol — every ~6s instead of waiting a full minute for
            // the next CHART_EQUITY bar.
            if (_lastSpotPrice > 0 && _liveChartForms.TryGetValue(_selectedTicker.Symbol, out var chartForOwnTicker) && !chartForOwnTicker.IsDisposed)
                chartForOwnTicker.FeedPollingPrice(_lastSpotPrice, DateTime.UtcNow);

            // Primary chain (current ExpDate)
            if (chkSaveToCsv.Checked)
            {
                _csvLogger?.AppendRows(allQuotes);
                // Try right away (not just on the 5-min scheduler tick) so the IVR/IVP opening
                // snapshot is captured on the very poll where the 9:30-9:35 window fills in.
                TryAppendIvHistorialSnapshot();
            }

            PopulateQuotesGrid(dgvQuotes, allQuotes, _selectedTicker, applyCountsFilter: true,
                selectedCounts: _selectedCounts, callOnly: chkCallFilter.Checked && !chkPutFilter.Checked, putOnly: chkPutFilter.Checked && !chkCallFilter.Checked);

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
    // Static (no Form1 instance state) so it's reusable from SimulatorForm's own grid too —
    // selectedCounts/callOnly/putOnly used to live on Form1's fields/checkboxes, now explicit
    // parameters instead (same values, callers just pass them through).
    internal static (List<OptionQuoteDto> otmCalls, List<OptionQuoteDto> otmPuts) PopulateQuotesGrid(
        DataGridView grid, List<OptionQuoteDto> allQuotes, TickerEntry ticker, bool applyCountsFilter = false,
        string? selectedCounts = null, bool callOnly = false, bool putOnly = false)
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

        if (applyCountsFilter && int.TryParse(selectedCounts, out var count))
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

    // Guards the Strike button on the tradable (current-expiration) grid: blocks the trade if
    // the option is illiquid (bid = 0), the spread is too wide (Sprd >= 5, same cents units
    // shown in the Sprd column), or there's no room for even 1 contract at the current Position
    // Size (Conts = 0) — any one of these makes clicking Strike a guaranteed-bad trade.
    private static bool IsRowTradeBlocked(DataGridViewRow row, string callBidColName, string putBidColName)
    {
        if (IsRowBidZero(row, callBidColName, putBidColName)) return true;

        var sprdColName = row.Tag?.ToString() == "PUT" ? "colPutSprd" : "colCallSprd";
        if (decimal.TryParse(row.Cells[sprdColName].Value?.ToString(), out var sprd) && sprd >= 5) return true;

        if (!decimal.TryParse(row.Cells["colContracts"].Value?.ToString(), out var contracts) || contracts == 0) return true;

        return false;
    }

    private void DgvQuotes_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (e.ColumnIndex != dgvQuotes.Columns["colStrikePrice"].Index) return;

        var val     = e.Value?.ToString();
        var row     = dgvQuotes.Rows[e.RowIndex];
        var rowType = row.Tag?.ToString();
        var disabled = IsRowTradeBlocked(row, "colCallBid", "colPutBid");

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

        // Block clicks on illiquid/unsafe options (bid = 0, spread too wide, or 0 contracts)
        if (IsRowTradeBlocked(dgvQuotes.Rows[e.RowIndex], "colCallBid", "colPutBid")) return;

        // rbNoTrade ("No Trade") runs free, no target auto-close; rbNoTradeTarget
        // ("No Trade-Target") is the one that auto-closes at target — swapped 2026-08-03, the
        // wiring had these backwards since rbNoTradeTarget was added.
        if (rbNoTrade.Checked)
            OpenSimulatedTradeNoTarget(e.RowIndex);
        else if (rbTrade.Checked)
            _ = PlaceRealTradeAsync(e.RowIndex, withTarget: false);
        else if (rbTradeTarget.Checked)
            _ = PlaceRealTradeAsync(e.RowIndex, withTarget: true);
        else if (rbNoTradeTarget.Checked)
            OpenSimulatedTrade(e.RowIndex);
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

    // Same as OpenSimulatedTrade, but with suppressAutoClose: true — no target% auto-close, the
    // demo trade just runs until closed manually or auto-closed at 4pm ET if it expires today.
    private async void OpenSimulatedTradeNoTarget(int rowIndex)
    {
        var row       = dgvQuotes.Rows[rowIndex];
        var rowType   = row.Tag?.ToString() ?? "CALL";
        var strike    = row.Cells["colStrikePrice"].Value?.ToString() ?? string.Empty;
        var contracts = row.Cells["colContracts"].Value?.ToString() ?? "0";
        var level     = row.Cells["colLevel"].Value?.ToString() ?? string.Empty;
        var symbol    = _selectedTicker?.Symbol ?? "UNK";

        var (bid, ask) = ReadRowBidAsk(row, rowType);
        if (ask <= 0) return;

        await RecordEntryAsync(symbol, rowType, strike, level, bid, ask, contracts, "Trade Manual", isDemo: true, suppressAutoClose: true);
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
        if (decimal.TryParse(strike, out var strikeForMoneyness))
            SetMoneyness(newRow, rowType, strikeForMoneyness, _lastSpotPrice);

        // Premium = riesgo máximo de la posición = precio de entrada * 100 (por contrato) *
        // cantidad de contratos. "ask" ya es el valor usado como EntryPrice acá mismo (ver
        // entryStr abajo) tanto para demo como para real — el real recibe su propio log aparte
        // cuando se confirma el fill ("Real EntryPrice confirmed"), esto es solo el de apertura.
        decimal.TryParse(contracts, out var contractsForPremium);
        var premium = ask * 100 * contractsForPremium;
        LogLine($"{now} {entryLabel} ({rowType})  SpotPrice: {_lastSpotPrice:F2}  StrikePrice: {strike}  Ask: {ask:F2}  Contracts: {contracts}  Level: {level}  Premium={premium:F2}", Color.White);
        LogLine($"{now} EntryPrice: {entryStr}", Color.LimeGreen);
        LogLine($"{now} Set Target: {tBid:F2}", Color.Orange);
        System.Windows.Forms.Application.DoEvents();

        int.TryParse(level, out var levelInt);
        int.TryParse(contracts, out var contractsInt);
        var tradeId = await SaveTradeToApiAsync(symbol, rowType, strike, ask, contractsInt, levelInt, targetPct, entryTime, isDemo);
        var expDate = ExpirationDateResolver.Resolve(_selectedTicker?.ExpDate ?? string.Empty);
        newRow.Tag = new TradeRowTag(tradeId, entryTime, suppressAutoClose, accountHash, occSymbol, quantity, ExpirationDate: expDate);
        PadWithBlankRows(dgvTrades, 4);

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

        _ = UploadEntryChartSnapshotAsync(symbol, rowType, tradeId, now);

        // Green "Stk=xxx" line on all 3 charts — demo and real trades both flow through here.
        if (decimal.TryParse(strike, out var strikeVal) && _liveChartForms.TryGetValue(symbol, out var chartFormForStrike) && !chartFormForStrike.IsDisposed)
            _ = chartFormForStrike.MarkStrikeOnAllChartsAsync(strikeVal);

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

    // Which instance on THIS machine is allowed to actually call Schwab to renew the access
    // token — every other instance just re-reads whatever this one last wrote to
    // SchwabTokenStore (local %AppData% file, never shared over the network — each PC keeps and
    // renews its own copy of the same refresh token, which gets copied over manually about once
    // a week). Two cases:
    //   - This PC hosts the real streaming connection (_isWebSocketHub) — the hub instance is
    //     also the token authority, same as before.
    //   - This PC is a pure remote client of another PC's hub (Hub Host configured) — it never
    //     contends for the streaming port, so _isWebSocketHub is always false here. Falls back to
    //     IsPrimaryTickerInstance() (first ticker in this PC's own TickerSettingsStore) instead,
    //     so exactly one instance on this PC is still designated.
    // The Hub Host gate matters: without it, a PC that DOES host the hub could end up with two
    // simultaneous authorities (the hub winner AND whichever instance happens to be "primary
    // ticker") if they're different processes — racing each other to renew.
    private bool IsTokenAuthority() =>
        _isWebSocketHub || (!string.IsNullOrWhiteSpace(HubHostSettingsStore.Load()) && IsPrimaryTickerInstance());

    private static (string AccessToken, DateTime ExpiresAt) ReloadTokenFromDisk()
    {
        var t = SchwabTokenStore.Load();
        return (t?.AccessToken ?? string.Empty, t?.AccessTokenExpiresAt ?? DateTime.MinValue);
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
            OnSchwabTokenRenewed,
            IsTokenAuthority(), ReloadTokenFromDisk);
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
            OnSchwabTokenRenewed, enableDumps,
            IsTokenAuthority(), ReloadTokenFromDisk);
    }

    // allowRefresh defaults to this instance's own IsTokenAuthority() — the one call site in
    // SetUpLiveFeedAsync that wins the streaming port race passes true explicitly (before
    // _isWebSocketHub itself is set, so IsTokenAuthority() can't see it yet); every other caller
    // (client instances that just need a SchwabStreamerClient for REST history fetches) leaves it
    // at the default so they never race the authority to renew the access token themselves.
    private SchwabStreamerClient CreateSchwabStreamerClient(bool? allowRefresh = null)
    {
        var creds  = SchwabCredentialsStore.Load();
        var tokens = SchwabTokenStore.Load();
        var effectiveAllowRefresh = allowRefresh ?? IsTokenAuthority();
        return new SchwabStreamerClient(
            _marketHttpClient, _schwabAuth,
            creds.ApiKey, creds.ApiSecret,
            tokens?.RefreshToken ?? string.Empty,
            tokens?.AccessToken ?? string.Empty,
            tokens?.AccessTokenExpiresAt ?? DateTime.MinValue,
            OnSchwabTokenRenewed,
            effectiveAllowRefresh, ReloadTokenFromDisk);
    }

    // Opens a live-chart window (candles only, streamed via WebSocket) for the currently
    // selected ticker. Fully separate from the existing polling-based Quotes tab — doesn't touch
    // any of that state. Reuses one shared streaming connection across all 4 tickers.
    private async void BtnLiveChart_Click(object? sender, EventArgs e)
    {
        if (_selectedTicker == null)
        {
            MessageBox.Show("Please select a ticker first.", "No Ticker Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Already have a window open for this symbol — just bring it forward instead of
        // touching the streamer again.
        if (_liveChartForms.TryGetValue(_selectedTicker.Symbol, out var existing) && !existing.IsDisposed)
        {
            existing.Activate();
            return;
        }

        var creds = SchwabCredentialsStore.Load();
        if (string.IsNullOrEmpty(creds.ApiKey) || string.IsNullOrEmpty(creds.ApiSecret))
        {
            MessageBox.Show("Schwab API credentials are not configured.", "Missing Credentials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await EnsureLiveFeedReadyAsync();
        }
        catch (Exception ex)
        {
            _liveFeedReadyTask = null; // let the next click retry instead of staying stuck on a faulted attempt
            MessageBox.Show($"Could not start live streaming:\n\n{ex.Message}",
                "Live Chart Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // One window, 3 chart panels side by side (1h / 15m RTH / 15m RTH+Overnight), all fed by
        // _historyClient (REST) + _liveFeed (hub or relay) set up in EnsureLiveFeedReadyAsync.
        var symbol = _selectedTicker.Symbol;
        var multiChartForm = new MultiChartForm(symbol, _historyClient!, _liveFeed!);
        multiChartForm.FormClosed += (s, e2) => _liveChartForms.Remove(symbol);
        _liveChartForms[symbol] = multiChartForm;
        lock (_wsEventLogLock) multiChartForm.ReplayWebSocketEvents(_wsEventLog);
        multiChartForm.Show();
    }

    // Opens the single "1h Charts — SPY/QQQ/DIA/IWM" window (no per-ticker selection needed —
    // always the same 4 symbols). Same shared-streamer setup as BtnLiveChart_Click.
    private async void BtnFourEtfCharts_Click(object? sender, EventArgs e)
    {
        if (_fourEtfChartsForm is { IsDisposed: false })
        {
            _fourEtfChartsForm.Activate();
            return;
        }

        var creds = SchwabCredentialsStore.Load();
        if (string.IsNullOrEmpty(creds.ApiKey) || string.IsNullOrEmpty(creds.ApiSecret))
        {
            MessageBox.Show("Schwab API credentials are not configured.", "Missing Credentials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await EnsureLiveFeedReadyAsync();
        }
        catch (Exception ex)
        {
            _liveFeedReadyTask = null;
            MessageBox.Show($"Could not start live streaming:\n\n{ex.Message}",
                "Live Chart Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var form = new FourEtfChartsForm(_historyClient!, _liveFeed!);
        form.FormClosed += (s, e2) => _fourEtfChartsForm = null;
        _fourEtfChartsForm = form;
        form.Show();
    }

    // Lets the user configure a REMOTE hub IP (another computer on the LAN running this same app
    // as the hub) so THIS instance connects to it instead of deciding locally. Only takes effect
    // on the next connect (EnsureLiveFeedReadyAsync hasn't run yet, or the app is restarted) —
    // if streaming already started this session under the old setting, it keeps running as-is.
    private void BtnHubHost_Click(object? sender, EventArgs e)
    {
        var current = HubHostSettingsStore.Load();

        using var dialog = new Form
        {
            Text = "Remote Hub Host",
            Width = 380,
            Height = 160,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var lbl = new Label
        {
            Text = "IP de la PC que hace de hub (vacío = esta PC decide sola):",
            Location = new Point(10, 12),
            Size = new Size(350, 32)
        };
        var txt = new TextBox { Text = current, Location = new Point(10, 48), Size = new Size(345, 24) };
        var btnOk = new Button { Text = "Guardar", Location = new Point(190, 82), Size = new Size(80, 28), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancelar", Location = new Point(275, 82), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
        dialog.Controls.Add(lbl);
        dialog.Controls.Add(txt);
        dialog.Controls.Add(btnOk);
        dialog.Controls.Add(btnCancel);
        dialog.AcceptButton = btnOk;
        dialog.CancelButton = btnCancel;

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            HubHostSettingsStore.Save(txt.Text.Trim());
            MessageBox.Show(
                "Guardado. Se va a usar la próxima vez que esta instancia se conecte al streaming (cerrá y volvé a abrir la app si ya estaba conectada).",
                "Hub Host", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // Opens the price/options replay simulator — reads previously-recorded data from disk only,
    // no live streaming/polling connection involved, so it can be open at the same time as
    // everything else in this form without any interaction between them.
    private void BtnSimulator_Click(object? sender, EventArgs e)
    {
        var simulatorForm = new SimulatorForm();
        simulatorForm.Show();
    }

    // Lazily decides whether this instance is the hub or a client, then sets up _historyClient +
    // _liveFeed accordingly — cheap to call repeatedly, does nothing once already set up.
    //
    // Caches the in-flight Task itself (not just checking "_liveFeed != null" after the fact) so
    // clicking "Live Chart" for two different tickers in quick succession can't race: without
    // this, both calls would see _liveFeed still null while the first attempt is awaiting, and
    // each would try to become the hub / connect independently (confirmed in ws_raw.log: two
    // interleaved requestid sequences fighting over the same Schwab login).
    private Task? _liveFeedReadyTask;

    private Task EnsureLiveFeedReadyAsync()
    {
        _liveFeedReadyTask ??= SetUpLiveFeedAsync();
        return _liveFeedReadyTask;
    }

    private async Task SetUpLiveFeedAsync()
    {
        var symbols = TickerSettingsStore.Load().Select(t => t.Symbol).ToList();
        if (symbols.Count == 0 && _selectedTicker != null) symbols.Add(_selectedTicker.Symbol);

        // DIA/IWM aren't in TickerSettingsStore yet — added by hand for the SPY/QQQ/DIA/IWM live
        // chart window (FourEtfChartsForm) until that comes from the tickers table instead.
        symbols.AddRange(FourEtfChartsForm.Symbols);
        symbols = symbols.Distinct().ToList();

        // If a REMOTE hub IP is configured (HubHostSettingsStore — set via "Hub Host" button,
        // meant for an instance running on a DIFFERENT computer that wants to read another
        // machine's hub instead of deciding locally), always connect there as a client. This
        // instance never tries to bind the port itself/become a hub in that case.
        var remoteHost = HubHostSettingsStore.Load();
        if (!string.IsNullOrWhiteSpace(remoteHost))
        {
            var remoteHubClient = new CandleHubClient();
            remoteHubClient.OnWsStatusEvent += msg => BroadcastWebSocketEventToCharts(msg);
            await remoteHubClient.ConnectAsync(LiveHubPort, remoteHost);

            _candleHubClient = remoteHubClient;
            _historyClient   = CreateSchwabStreamerClient();
            _liveFeed        = remoteHubClient;
            return;
        }

        // No remote hub configured — try to become the hub first — whichever app instance/
        // process ON THIS MACHINE gets there first binds the port and owns the real Schwab
        // connection for as long as it stays open.
        var hubServer = new CandleHubServer();
        if (hubServer.TryStart(LiveHubPort))
        {
            _candleHubServer = hubServer;

            var streamer = CreateSchwabStreamerClient(allowRefresh: true);
            _isWebSocketHub = true;
            // Shown only in each open Live Charts child window's own event log (crossLog), not
            // the main form's log — LogWebSocketEvent is itself safe to call from any thread,
            // which matters here since reconnects fire from the receive loop's background thread.
            // Also relayed to every OTHER instance via the hub TCP socket (BroadcastWsEvent) —
            // only the hub instance owns the real Schwab connection, but every ticker's own
            // process/window should still be able to show its connection history.
            streamer.OnWsStatusEvent += RaiseWsStatusEvent;
            await streamer.ConnectAsync();
            await streamer.SubscribeChartEquity(symbols);
            // Re-enabled 2026-07-30 — root cause of the earlier "Bad command formatting"
            // rejection was the service name itself ("LEVEL_ONE_EQUITIES" instead of the correct
            // "LEVELONE_EQUITIES", confirmed against a working third-party SDK). Watch ws_raw.log
            // on first use after this change to confirm the ADD actually succeeds this time.
            await streamer.SubscribeLevelOneEquity(symbols);
            streamer.OnNewCandle += (symbol, candle) => hubServer.Broadcast(symbol, candle);
            streamer.OnLevelOneTick += (symbol, price, time) => hubServer.BroadcastLevelOne(symbol, price, time);

            _historyClient = streamer;
            _liveFeed      = streamer; // this instance's own connection IS the live feed
            return;
        }

        // Another instance on this machine already owns the port — connect to it as a client
        // instead. Still need our OWN SchwabStreamerClient for REST history fetches (no
        // per-account limit on that, unlike the streaming socket) — it's never connected/subscribed.
        var hubClient = new CandleHubClient();
        hubClient.OnWsStatusEvent += msg => BroadcastWebSocketEventToCharts(msg);
        await hubClient.ConnectAsync(LiveHubPort);

        _candleHubClient = hubClient;
        _historyClient   = CreateSchwabStreamerClient();
        _liveFeed        = hubClient;
    }

    private async Task PlaceRealTradeAsync(int rowIndex, bool withTarget)
    {
        var row          = dgvQuotes.Rows[rowIndex];
        var rowType      = row.Tag?.ToString() ?? "CALL";
        var strikeStr    = row.Cells["colStrikePrice"].Value?.ToString() ?? string.Empty;
        var contractsStr = row.Cells["colContracts"].Value?.ToString() ?? "0";
        var level        = row.Cells["colLevel"].Value?.ToString() ?? string.Empty;

        var (bid, ask) = ReadRowBidAsk(row, rowType);
        if (!decimal.TryParse(strikeStr, out var strike)) return;

        await PlaceRealTradeCoreAsync(rowType, strike, contractsStr, level, bid, ask, withTarget);
    }

    // Places a REAL market BUY_TO_OPEN order for the given option (row click in the Quotes tab
    // grid; quantity/level already computed by PopulateQuotesGrid).
    private async Task PlaceRealTradeCoreAsync(string rowType, decimal strike, string contractsStr, string level, decimal bid, decimal ask, bool withTarget)
    {
        var account = SelectedAccountStore.Load();
        if (account == null || string.IsNullOrEmpty(account.HashValue))
        {
            MessageBox.Show("No broker account selected. Go to Settings → Broker Accounts, click Refresh Accounts and pick a default.",
                "No Account Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_selectedTicker == null) return;

        var symbol    = _selectedTicker.Symbol;
        var strikeStr = FormatStrike(strike);

        if (ask <= 0) return;
        if (!int.TryParse(contractsStr, out var qty) || qty <= 0)
        {
            MessageBox.Show("Invalid contract quantity.", "Order", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

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

        // Recalculate PnL at close time. Priority: confirmed real fill price (real MANUAL close)
        // > T_Bid (TARGET close — the position closes AT the target price by definition, whether
        // that's the real LIMIT order resting at the broker or just the auto-close trigger for a
        // demo trade) > last polled bid shown on screen (plain manual close fallback).
        var entryPriceStr = row.Cells["colTradeEntryPrice"].Value?.ToString() ?? "0";
        var contractsStr  = row.Cells["colTradeContracts"].Value?.ToString() ?? "0";
        var tBidStr       = row.Cells["colTradeTBid"].Value?.ToString() ?? string.Empty;
        decimal.TryParse(cBid, out var cBidParsed);
        decimal? targetClosePrice = closeType == "TARGET" && decimal.TryParse(tBidStr, out var tBidParsed) ? tBidParsed : null;
        var exitBid = realClosePrice ?? targetClosePrice ?? cBidParsed;
        decimal.TryParse(entryPriceStr, out var entryPrice);
        decimal.TryParse(contractsStr, out var contractsForPnl);
        var pnlVal    = Math.Round((exitBid - entryPrice) * contractsForPnl * 100, 2);
        var pnlPctVal = entryPrice > 0 ? Math.Round((exitBid - entryPrice) / entryPrice * 100, 1) : 0m;
        pnl    = pnlVal.ToString("F2");
        pnlPct = pnlPctVal.ToString("F1");

        row.Cells["colTradePnL"].Value        = pnl;
        row.Cells["colTradePnLPercent"].Value = pnlPct;
        // Closing PnL% (real fill price, or target price for TARGET closes) can be a new
        // low/high the live ticks never saw.
        UpdatePnLMinMax(row, pnlPctVal);
        row.Cells["colTradeExitTime"].Value   = nowStr;
        row.Cells["colTradeClose"].Value      = "Closed";
        row.DefaultCellStyle.ForeColor        = Color.Gray;

        var pnlColor = pnlVal >= 0 ? Color.LimeGreen : Color.Red;

        var realCloseLog = realClosePrice.HasValue ? $"  RealClose: {realClosePrice.Value:F2}"
            : targetClosePrice.HasValue ? $"  TargetClose: {targetClosePrice.Value:F2}"
            : string.Empty;
        LogLine(string.Empty, Color.White);
        LogLine($"{nowStr} Close {closeType} ({type})  SpotPrice: {spotPrice}  Strike: {strike}  C_Bid: {cBid}{realCloseLog}", Color.White);
        LogLine($"{nowStr} PnL: {pnl}  PnL_Percent: {pnlPct}", pnlColor);
        LogLine($"{nowStr} Duration: {duration:hh\\:mm\\:ss}", Color.White);
        System.Windows.Forms.Application.DoEvents();

        // Remove from local persistence
        if (tradeId != 0)
            OpenTradesStore.Remove(tradeId);

        // Close trade (API PATCH if tradeId is a real API id, always updates TradeHistoryStore
        // locally regardless — see CloseTradeInApiAsync)
        if (tradeId != 0)
        {
            var exitPrice = exitBid;
            await CloseTradeInApiAsync(tradeId, exitPrice, pnlVal, pnlPctVal, duration);
        }

        // Red "Expired!!!" marker on the 15m RTH (middle) chart — only for the 4pm expiration
        // auto-close, and awaited BEFORE the snapshot below so it's actually drawn on the chart
        // in time to be captured, instead of racing the capture (it used to be fire-and-forget
        // AFTER the snapshot, so the marker never made it into the uploaded/pushed image).
        if (closeType == "EXPIRED" && _liveChartForms.TryGetValue(symbol, out var chartFormExpired) && !chartFormExpired.IsDisposed)
        {
            await chartFormExpired.MarkExpiredOnRthChartAsync();
            await Task.Delay(100); // let the WebView2 repaint before capturing it
        }

        // 3-chart snapshot at close ("_Close") — captured once and reused both for the S3 upload
        // and the Telegram push below, instead of each capturing its own copy.
        var closeChartPath = await SaveTradeChartSnapshotAsync(symbol, type, "Close");

        // Telegram push: the 3-chart snapshot + a caption describing the close (symbol, PnL%, etc).
        if (tradeId != 0)
            _ = SendTradeCloseTelegramPushAsync(symbol, tradeId, type, strike, closeType, entryPrice, exitBid, pnlVal, pnlPctVal, duration, closeChartPath);

        // Screenshot TradeLog (Trades + Logger section of the form)
        await Task.Delay(100); // let UI settle
        var tradeLogPath = CaptureTradeLogScreenshot(symbol, type);
        // LogLine($"{nowStr} Screenshot: {tradeLogPath}", Color.DimGray);

        // Uploads Close + TradeLog (fire-and-forget, doesn't block the row from showing "Closed"),
        // then appends today's Obsidian daily-trade-log entry once both S3 URLs are actually known
        // — EntryImageUrl is already set from when the trade opened.
        _ = UploadCloseTradeLogAndAppendDailyAsync(closeChartPath, tradeLogPath, symbol, type, tradeId, nowStr);
    }

    private async Task UploadCloseTradeLogAndAppendDailyAsync(string? closeChartPath, string tradeLogPath,
        string symbol, string type, int tradeId, string nowStr)
    {
        if (closeChartPath != null)
            await UploadScreenshotAsync(closeChartPath, symbol, type, tradeId, nowStr, TradeImageKind.Close);
        await UploadScreenshotAsync(tradeLogPath, symbol, type, tradeId, nowStr, TradeImageKind.TradeLog);

        if (tradeId == 0) return;
        var trade = TradeHistoryStore.Load().FirstOrDefault(t => t.Id == tradeId);
        if (trade != null) DailyTradeLogWriter.AppendTrade(trade);
    }

    // Combined snapshot of the 3 live charts (1h / 15m RTH / 15m RTH+Overnight) rendered via the
    // WebView2 form itself — not a screen capture — only if a MultiChartForm for this symbol
    // happens to be open. Best-effort: never blocks the trade flow. Filename matches
    // CaptureTradeLogScreenshot's format ({Symbol}_{OptionType}_{timestamp}_{Tag}.png) so all 3
    // screenshots a trade ever gets (Entry, Close, TradeLog) look consistent. Returns the saved
    // file path (or null if nothing was captured) so callers can upload/reuse it.
    private async Task<string?> SaveTradeChartSnapshotAsync(string symbol, string optionType, string tag)
    {
        try
        {
            if (!_liveChartForms.TryGetValue(symbol, out var chartForm) || chartForm.IsDisposed) return null;

            using var combined = await chartForm.CaptureCombinedChartImageAsync();
            if (combined == null) return null;

            var folder = Path.Combine(@"C:\OptionsData\ChartSnapshots", symbol);
            Directory.CreateDirectory(folder);
            var fileName = $"{symbol}_{optionType}_{DateTime.Now:yyyyMMdd_HHmmss}_{tag}.png";
            var filePath = Path.Combine(folder, fileName);
            combined.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            return filePath;
        }
        catch
        {
            // Best-effort — a chart-snapshot failure (WebView2 not ready, form closing, etc.)
            // must never block or fail the trade flow.
            return null;
        }
    }

    // Captures the "_Entry" 3-chart snapshot and uploads it to S3 — mirrors the "_Close" path in
    // CloseTradeRowAsync. Fire-and-forget from RecordEntryAsync, same as every other upload here.
    private async Task UploadEntryChartSnapshotAsync(string symbol, string optionType, int tradeId, string timeStr)
    {
        var path = await SaveTradeChartSnapshotAsync(symbol, optionType, "Entry");
        if (path != null)
            await UploadScreenshotAsync(path, symbol, optionType, tradeId, timeStr, TradeImageKind.Entry);
    }

    // Pushes a Telegram photo (the "_Close" 3-chart snapshot CloseTradeRowAsync already captured
    // and uploaded — imagePath is null if no Live Chart was open, in which case there's nothing
    // to attach) with a caption describing the closed trade — symbol, side, strike, entry/exit,
    // PnL$ and PnL%, duration. Best-effort, fire-and-forget from the caller: a failed/misconfigured
    // push must never affect the trade-close flow itself (which has already committed by now).
    private async Task SendTradeCloseTelegramPushAsync(
        string symbol, int tradeId, string optionType, string strike, string closeType,
        decimal entryPrice, decimal exitPrice, decimal pnl, decimal pnlPercent, TimeSpan duration,
        string? imagePath)
    {
        try
        {
            if (imagePath == null) return; // no Live Chart open for this symbol — nothing to attach

            var (botToken, chatId) = TelegramSettingsStore.Load();
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId)) return;

            var pnlSign = pnl >= 0 ? "+" : string.Empty;
            var caption =
                $"{symbol} {optionType} {strike} — Closed ({closeType})\n" +
                $"Entry: {entryPrice:F2}  Exit: {exitPrice:F2}\n" +
                $"PnL: {pnlSign}{pnl:F2} ({pnlSign}{pnlPercent:F1}%)\n" +
                $"Duration: {duration:hh\\:mm\\:ss}";

            var (ok, _, messageId) = await TelegramNotifier.SendPhotoAsync(botToken, chatId, imagePath, caption);
            if (ok && messageId.HasValue)
                TelegramPushStore.Append(new TelegramPush(messageId.Value, chatId, symbol, "TradeClose", DateTime.Now));
        }
        catch
        {
            // Best-effort — never let a Telegram/network failure affect the already-closed trade.
        }
    }

    private string CaptureTradeLogScreenshot(string symbol, string optionType)
    {
        var folder = Path.Combine(@"C:\Screenshots", DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(folder);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName  = $"{symbol}_{optionType}_{timestamp}_TradeLog.png";
        var filePath  = Path.Combine(folder, fileName);

        // Renders grpTrades + grpLogger directly via the form's own drawing (Control.DrawToBitmap)
        // instead of CopyFromScreen — works even if the window is minimized, occluded, or off-screen.
        using var tradesBmp = new Bitmap(grpTrades.Width, grpTrades.Height);
        grpTrades.DrawToBitmap(tradesBmp, new Rectangle(Point.Empty, grpTrades.Size));

        using var loggerBmp = new Bitmap(grpLogger.Width, grpLogger.Height);
        grpLogger.DrawToBitmap(loggerBmp, new Rectangle(Point.Empty, grpLogger.Size));

        var width  = Math.Max(tradesBmp.Width, loggerBmp.Width);
        var height = tradesBmp.Height + loggerBmp.Height;
        using var combined = new Bitmap(width, height);
        using (var g = Graphics.FromImage(combined))
        {
            g.Clear(Color.White);
            g.DrawImage(tradesBmp, 0, 0);
            g.DrawImage(loggerBmp, 0, tradesBmp.Height);
        }
        combined.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

        return filePath;
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

    // Dual-writes every trade to TradeHistoryStore alongside the API call — first step toward
    // being able to run without the EC2 backend. The API stays the source of truth for the id
    // whenever it's reachable; if it isn't (apiTradeId stays 0), TradeHistoryStore.Add assigns a
    // local negative id instead of losing the trade, and THAT becomes the id used everywhere else
    // (OpenTradesStore, screenshots, close) for the rest of this trade's life.
    private async Task<int> SaveTradeToApiAsync(string symbol, string rowType, string strike, decimal ask,
        int contracts, int level, decimal targetPct, DateTime entryTime, bool isDemo = false)
    {
        var ticker = _selectedTicker!;
        var expDate = ExpirationDateResolver.Resolve(ticker.ExpDate);
        var apiTradeId = 0;

        try
        {
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
                this.Invoke(() => LogLine($"API Error {(int)response.StatusCode}: {json}", Color.Red));
            else
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                apiTradeId = doc.RootElement.GetProperty("id").GetInt32();
            }
        }
        catch (Exception ex)
        {
            this.Invoke(() => LogLine($"API Exception: {ex.Message}", Color.Red));
        }

        return TradeHistoryStore.Add(new TradeRecord(
            Id: apiTradeId, Symbol: symbol, OptionType: rowType, StrikePrice: decimal.Parse(strike),
            SpotPrice: _lastSpotPrice, ExpirationDate: expDate, EntryPrice: ask, ExitPrice: null,
            EntryTime: entryTime, Contracts: contracts, Level: level, TargetPercent: targetPct,
            Duration: null, Pnl: null, PnlPercent: null, IsDemo: isDemo, Broker: "Schwab"));
    }

    private async Task CloseTradeInApiAsync(int tradeId, decimal exitPrice, decimal pnl, decimal pnlPercent, TimeSpan duration)
    {
        // tradeId can be a local-only negative id (see SaveTradeToApiAsync) if the API was
        // unreachable at open time — nothing to PATCH on the API for those, but still record the
        // close locally so the trade's full lifecycle isn't lost.
        if (tradeId > 0)
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

        TradeHistoryStore.Close(tradeId, exitPrice, pnl, pnlPercent, duration);
    }

    // kind, when given, also saves the resulting S3 URL onto the local TradeHistoryStore record
    // (EntryImageUrl/CloseImageUrl/TradeLogImageUrl) — this is what lets the trade-detail view
    // show all 3 images without needing the API, once a trade is uploaded through this path.
    private async Task UploadScreenshotAsync(string localPath, string symbol, string optionType, int tradeId, string timeStr, TradeImageKind? kind = null)
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

            if (kind.HasValue && tradeId != 0)
                TradeHistoryStore.SetImageUrl(tradeId, kind.Value, s3Url);

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


    // CALL is ITM once spot crosses above the strike; PUT is ITM once spot crosses below it.
    // Colored red (OTM) / green (ITM) — called both at entry (with that moment's spot) and on
    // every poll tick while the trade is open.
    private static void SetMoneyness(DataGridViewRow row, string rowType, decimal strike, decimal spot)
    {
        var cell = row.Cells["colTradeMoneyness"];
        var isItm = rowType == "CALL" ? spot > strike : spot < strike;
        cell.Value           = isItm ? "ITM" : "OTM";
        cell.Style.ForeColor = isItm ? Color.Green : Color.Red;
    }

    // Extends the row's Min/Max PnL% columns if the given value is a new low/high — but Min only
    // ever tracks NEGATIVE values and Max only ever tracks POSITIVE ones. A trade that's never
    // been profitable leaves Max blank (no positive value to show) rather than showing "the least
    // negative point reached"; same idea mirrored for Min if it's never gone negative.
    // Session-only — not persisted to OpenTradesStore, so it resets if the app restarts mid-trade.
    private static void UpdatePnLMinMax(DataGridViewRow row, decimal pnlPct)
    {
        var minCell = row.Cells["colTradePnLMin"];
        var maxCell = row.Cells["colTradePnLMax"];

        if (pnlPct < 0 && (!decimal.TryParse(minCell.Value?.ToString(), out var min) || pnlPct < min))
        {
            minCell.Value             = pnlPct.ToString("F1");
            minCell.Style.ForeColor   = Color.Red;
        }

        if (pnlPct > 0 && (!decimal.TryParse(maxCell.Value?.ToString(), out var max) || pnlPct > max))
        {
            maxCell.Value             = pnlPct.ToString("F1");
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

            UpdatePnLMinMax(row, pnlPct);
            SetMoneyness(row, type, strike, _lastSpotPrice);

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

    private static decimal GetPositionSize()
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
        if (ask <= 0 || positionSize <= 0) return "0";
        // Floor, not round — Position Size % is a risk cap, so rounding up could spend more
        // than the configured budget (e.g. $300 @ 1.18 = 2.54 contracts must stay at 2, not 3).
        var contracts = Math.Floor(positionSize / (ask * 100));
        return contracts.ToString("F0");
    }

    private static string GetContractsValue(decimal ask)
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
