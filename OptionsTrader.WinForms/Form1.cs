using OptionsTrader.Infrastructure.Schwab;

namespace OptionsTrader.WinForms;

public partial class Form1 : Form
{
    private readonly SchwabAuthService _schwabAuth = new(new HttpClient());
    private readonly HttpClient _marketHttpClient = new();

    public Form1()
    {
        InitializeComponent();
        LoadBrokerSelection();
        LoadTickers();
        LoadRadioSelection(grpPositionSize, PositionSizeSettingsStore.Load());
        LoadRadioSelection(grpTarget, TargetSettingsStore.Load());
        LoadSchwabCredentials();
        LoadBalance();
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
        lblPositionAmount.Text = $"{positionPct}%: {amount:F2}";
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
        var creds = SchwabCredentialsStore.Load();
        if (string.IsNullOrEmpty(creds.ApiKey) || string.IsNullOrEmpty(creds.ApiSecret))
        {
            MessageBox.Show("Schwab API credentials are not configured.", "Missing Credentials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var tickers = TickerSettingsStore.Load();
        if (tickers.Count == 0)
        {
            MessageBox.Show("No tickers configured in Settings.", "No Tickers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnFetchQuotes.Enabled = false;
        dgvQuotes.Rows.Clear();

        try
        {
            var service = new SchwabMarketDataService(_marketHttpClient, _schwabAuth, creds.ApiKey, creds.ApiSecret);

            foreach (var ticker in tickers.Where(t => !string.IsNullOrEmpty(t.Symbol)))
            {
                if (!DateOnly.TryParse(ticker.ExpDate, out var expDate)) continue;

                var quotes = await service.GetOptionsChainAsync(ticker.Symbol, expDate);

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
