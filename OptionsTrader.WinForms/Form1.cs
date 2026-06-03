using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

public partial class Form1 : Form
{
    private readonly SchwabAuthService _schwabAuth = new(new HttpClient());
    private readonly HttpClient _marketHttpClient = new();

    private TickerEntry? _selectedTicker;

    public Form1()
    {
        InitializeComponent();
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
        txtApiKey.Text = creds.ApiKey;
        txtApiSecret.Text = creds.ApiSecret;
    }

    private void BtnSaveCredentials_Click(object? sender, EventArgs e)
    {
        var creds = new SchwabCredentials(txtApiKey.Text.Trim(), txtApiSecret.Text.Trim());
        SchwabCredentialsStore.Save(creds);
        MessageBox.Show("Credentials saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        if (!DateOnly.TryParse(_selectedTicker.ExpDate, out var expDate))
        {
            MessageBox.Show($"Invalid expiration date for {_selectedTicker.Symbol}.", "Invalid Date", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnFetchQuotes.Enabled = false;
        dgvQuotes.Rows.Clear();

        try
        {
            var service = new SchwabMarketDataService(_marketHttpClient, _schwabAuth, creds.ApiKey, creds.ApiSecret);
            var quotes = await service.GetOptionsChainAsync(_selectedTicker.Symbol, expDate);

            foreach (var q in quotes)
            {
                dgvQuotes.Rows.Add(
                    q.OptionType.ToString(),
                    q.Symbol,
                    q.SpotPrice.ToString("F2"),
                    q.StrikePrice.ToString("F2"),
                    q.Bid.ToString("F2"),
                    q.Ask.ToString("F2"),
                    q.ExpirationDate.ToString("yyyy-MM-dd"));
            }
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
