namespace OptionsTrader.WinForms;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        tabControl = new TabControl();
        tabOptions = new TabPage();
        tabQuotes = new TabPage();
        tabSettings = new TabPage();
        dgvQuotes = new DataGridView();
        colQType = new DataGridViewTextBoxColumn();
        colQSymbol = new DataGridViewTextBoxColumn();
        colQSpot = new DataGridViewTextBoxColumn();
        colQStrike = new DataGridViewTextBoxColumn();
        colQBid = new DataGridViewTextBoxColumn();
        colQAsk = new DataGridViewTextBoxColumn();
        colQExpDate = new DataGridViewTextBoxColumn();
        btnFetchQuotes = new Button();
        grpBalance = new GroupBox();
        txtBalance = new TextBox();
        lblPositionAmount = new Label();
        grpBroker = new GroupBox();
        rbSchwab = new RadioButton();
        rbIBKR = new RadioButton();
        rbETrade = new RadioButton();
        grpTickers = new GroupBox();
        dgvTickers = new DataGridView();
        colSymbol = new DataGridViewTextBoxColumn();
        colLow = new DataGridViewTextBoxColumn();
        colHigh = new DataGridViewTextBoxColumn();
        colExpDate = new DataGridViewTextBoxColumn();
        btnSaveTickers = new Button();
        grpPositionSize = new GroupBox();
        rbPosition25 = new RadioButton();
        rbPosition5 = new RadioButton();
        rbPosition10 = new RadioButton();
        grpTarget = new GroupBox();
        rbTarget10 = new RadioButton();
        rbTarget35 = new RadioButton();
        rbTarget100 = new RadioButton();
        grpSchwabCredentials = new GroupBox();
        lblApiKey = new Label();
        txtApiKey = new TextBox();
        lblApiSecret = new Label();
        txtApiSecret = new TextBox();
        btnSaveCredentials = new Button();

        tabControl.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvQuotes).BeginInit();
        grpBalance.SuspendLayout();
        grpBroker.SuspendLayout();
        grpTickers.SuspendLayout();
        grpPositionSize.SuspendLayout();
        grpTarget.SuspendLayout();
        grpSchwabCredentials.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvTickers).BeginInit();
        SuspendLayout();

        // tabControl
        tabControl.Controls.Add(tabOptions);
        tabControl.Controls.Add(tabQuotes);
        tabControl.Controls.Add(tabSettings);
        tabControl.Dock = DockStyle.Fill;
        tabControl.Name = "tabControl";
        tabControl.SelectedIndex = 0;

        // tabOptions
        tabOptions.Name = "tabOptions";
        tabOptions.Padding = new Padding(8);
        tabOptions.Text = "Options Data";

        // tabQuotes
        tabQuotes.Controls.Add(dgvQuotes);
        tabQuotes.Controls.Add(btnFetchQuotes);
        tabQuotes.Controls.Add(grpBalance);
        tabQuotes.Name = "tabQuotes";
        tabQuotes.Padding = new Padding(8);
        tabQuotes.Text = "Quotes";

        // dgvQuotes
        dgvQuotes.AllowUserToAddRows = false;
        dgvQuotes.AllowUserToDeleteRows = false;
        dgvQuotes.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgvQuotes.Columns.AddRange(colQType, colQSymbol, colQSpot, colQStrike, colQBid, colQAsk, colQExpDate);
        dgvQuotes.Location = new Point(8, 84);
        dgvQuotes.Name = "dgvQuotes";
        dgvQuotes.ReadOnly = true;
        dgvQuotes.RowHeadersVisible = false;
        dgvQuotes.RowTemplate.Height = 22;
        dgvQuotes.Size = new Size(1000, 476);
        dgvQuotes.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // colQType
        colQType.HeaderText = "Type";
        colQType.Name = "colQType";
        colQType.Width = 60;

        // colQSymbol
        colQSymbol.HeaderText = "Symbol";
        colQSymbol.Name = "colQSymbol";
        colQSymbol.Width = 80;

        // colQSpot
        colQSpot.HeaderText = "Spot Price";
        colQSpot.Name = "colQSpot";
        colQSpot.Width = 90;

        // colQStrike
        colQStrike.HeaderText = "Strike";
        colQStrike.Name = "colQStrike";
        colQStrike.Width = 80;

        // colQBid
        colQBid.HeaderText = "Bid";
        colQBid.Name = "colQBid";
        colQBid.Width = 80;

        // colQAsk
        colQAsk.HeaderText = "Ask";
        colQAsk.Name = "colQAsk";
        colQAsk.Width = 80;

        // colQExpDate
        colQExpDate.HeaderText = "Expiration";
        colQExpDate.Name = "colQExpDate";
        colQExpDate.Width = 100;

        // grpBalance
        grpBalance.Controls.Add(txtBalance);
        grpBalance.Controls.Add(lblPositionAmount);
        grpBalance.Location = new Point(8, 8);
        grpBalance.Name = "grpBalance";
        grpBalance.Size = new Size(160, 68);
        grpBalance.TabStop = false;
        grpBalance.Text = "Balance";

        // txtBalance
        txtBalance.Location = new Point(8, 20);
        txtBalance.Name = "txtBalance";
        txtBalance.Size = new Size(72, 23);
        txtBalance.TextAlign = HorizontalAlignment.Left;
        txtBalance.TextChanged += TxtBalance_TextChanged;

        // lblPositionAmount
        lblPositionAmount.Location = new Point(8, 48);
        lblPositionAmount.Name = "lblPositionAmount";
        lblPositionAmount.Size = new Size(144, 16);
        lblPositionAmount.Font = new Font(lblPositionAmount.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
        lblPositionAmount.Text = string.Empty;

        // btnFetchQuotes
        btnFetchQuotes.Location = new Point(180, 22);
        btnFetchQuotes.Name = "btnFetchQuotes";
        btnFetchQuotes.Size = new Size(120, 26);
        btnFetchQuotes.Text = "Fetch Quotes";
        btnFetchQuotes.Click += BtnFetchQuotes_Click;

        // tabSettings
        tabSettings.Controls.Add(grpBroker);
        tabSettings.Controls.Add(grpTickers);
        tabSettings.Controls.Add(grpPositionSize);
        tabSettings.Controls.Add(grpTarget);
        tabSettings.Controls.Add(grpSchwabCredentials);
        tabSettings.Name = "tabSettings";
        tabSettings.Padding = new Padding(8);
        tabSettings.Text = "Settings";

        // grpBroker
        grpBroker.Controls.Add(rbSchwab);
        grpBroker.Controls.Add(rbIBKR);
        grpBroker.Controls.Add(rbETrade);
        grpBroker.Location = new Point(8, 8);
        grpBroker.Name = "grpBroker";
        grpBroker.Size = new Size(155, 105);
        grpBroker.TabStop = false;
        grpBroker.Text = "Broker";

        // rbSchwab
        rbSchwab.Location = new Point(12, 22);
        rbSchwab.Name = "rbSchwab";
        rbSchwab.Size = new Size(135, 20);
        rbSchwab.Text = "Charles Schwab";
        rbSchwab.CheckedChanged += BrokerRadioButton_CheckedChanged;

        // rbIBKR
        rbIBKR.Location = new Point(12, 46);
        rbIBKR.Name = "rbIBKR";
        rbIBKR.Size = new Size(135, 20);
        rbIBKR.Text = "Interactive Broker";
        rbIBKR.CheckedChanged += BrokerRadioButton_CheckedChanged;

        // rbETrade
        rbETrade.Location = new Point(12, 70);
        rbETrade.Name = "rbETrade";
        rbETrade.Size = new Size(135, 20);
        rbETrade.Text = "ETrade";
        rbETrade.CheckedChanged += BrokerRadioButton_CheckedChanged;

        // grpTickers
        grpTickers.Controls.Add(dgvTickers);
        grpTickers.Controls.Add(btnSaveTickers);
        grpTickers.Location = new Point(175, 8);
        grpTickers.Name = "grpTickers";
        grpTickers.Size = new Size(340, 168);
        grpTickers.TabStop = false;
        grpTickers.Text = "Tickers";

        // dgvTickers
        dgvTickers.AllowUserToAddRows = false;
        dgvTickers.AllowUserToDeleteRows = false;
        dgvTickers.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgvTickers.Columns.AddRange(colSymbol, colLow, colHigh, colExpDate);
        dgvTickers.Location = new Point(8, 20);
        dgvTickers.Name = "dgvTickers";
        dgvTickers.RowHeadersVisible = false;
        dgvTickers.RowTemplate.Height = 22;
        dgvTickers.Size = new Size(318, 112);
        dgvTickers.ScrollBars = ScrollBars.None;

        // colSymbol
        colSymbol.HeaderText = "Symbol";
        colSymbol.Name = "colSymbol";
        colSymbol.Width = 80;

        // colLow
        colLow.HeaderText = "Low";
        colLow.Name = "colLow";
        colLow.Width = 74;

        // colHigh
        colHigh.HeaderText = "High";
        colHigh.Name = "colHigh";
        colHigh.Width = 74;

        // colExpDate
        colExpDate.HeaderText = "ExpDate";
        colExpDate.Name = "colExpDate";
        colExpDate.Width = 90;

        // btnSaveTickers
        btnSaveTickers.Location = new Point(250, 138);
        btnSaveTickers.Name = "btnSaveTickers";
        btnSaveTickers.Size = new Size(80, 23);
        btnSaveTickers.Text = "Save";
        btnSaveTickers.Click += BtnSaveTickers_Click;

        // grpPositionSize
        grpPositionSize.Controls.Add(rbPosition25);
        grpPositionSize.Controls.Add(rbPosition5);
        grpPositionSize.Controls.Add(rbPosition10);
        grpPositionSize.Location = new Point(525, 8);
        grpPositionSize.Name = "grpPositionSize";
        grpPositionSize.Size = new Size(110, 105);
        grpPositionSize.TabStop = false;
        grpPositionSize.Text = "Position Size (%)";

        // rbPosition25
        rbPosition25.Location = new Point(12, 22);
        rbPosition25.Name = "rbPosition25";
        rbPosition25.Size = new Size(90, 20);
        rbPosition25.Text = "2.5";
        rbPosition25.CheckedChanged += PositionSizeRadioButton_CheckedChanged;

        // rbPosition5
        rbPosition5.Location = new Point(12, 46);
        rbPosition5.Name = "rbPosition5";
        rbPosition5.Size = new Size(90, 20);
        rbPosition5.Text = "5";
        rbPosition5.CheckedChanged += PositionSizeRadioButton_CheckedChanged;

        // rbPosition10
        rbPosition10.Location = new Point(12, 70);
        rbPosition10.Name = "rbPosition10";
        rbPosition10.Size = new Size(90, 20);
        rbPosition10.Text = "10";
        rbPosition10.CheckedChanged += PositionSizeRadioButton_CheckedChanged;

        // grpTarget
        grpTarget.Controls.Add(rbTarget10);
        grpTarget.Controls.Add(rbTarget35);
        grpTarget.Controls.Add(rbTarget100);
        grpTarget.Location = new Point(643, 8);
        grpTarget.Name = "grpTarget";
        grpTarget.Size = new Size(110, 105);
        grpTarget.TabStop = false;
        grpTarget.Text = "Target (%)";

        // rbTarget10
        rbTarget10.Location = new Point(12, 22);
        rbTarget10.Name = "rbTarget10";
        rbTarget10.Size = new Size(90, 20);
        rbTarget10.Text = "10";
        rbTarget10.CheckedChanged += TargetRadioButton_CheckedChanged;

        // rbTarget35
        rbTarget35.Location = new Point(12, 46);
        rbTarget35.Name = "rbTarget35";
        rbTarget35.Size = new Size(90, 20);
        rbTarget35.Text = "35";
        rbTarget35.CheckedChanged += TargetRadioButton_CheckedChanged;

        // rbTarget100
        rbTarget100.Location = new Point(12, 70);
        rbTarget100.Name = "rbTarget100";
        rbTarget100.Size = new Size(90, 20);
        rbTarget100.Text = "100";
        rbTarget100.CheckedChanged += TargetRadioButton_CheckedChanged;

        // grpSchwabCredentials — below grpBroker on the left column
        grpSchwabCredentials.Controls.Add(lblApiKey);
        grpSchwabCredentials.Controls.Add(txtApiKey);
        grpSchwabCredentials.Controls.Add(lblApiSecret);
        grpSchwabCredentials.Controls.Add(txtApiSecret);
        grpSchwabCredentials.Controls.Add(btnSaveCredentials);
        grpSchwabCredentials.Location = new Point(8, 180);
        grpSchwabCredentials.Name = "grpSchwabCredentials";
        grpSchwabCredentials.Size = new Size(507, 105);
        grpSchwabCredentials.TabStop = false;
        grpSchwabCredentials.Text = "Schwab Credentials";

        // lblApiKey
        lblApiKey.Location = new Point(12, 24);
        lblApiKey.Name = "lblApiKey";
        lblApiKey.Size = new Size(65, 20);
        lblApiKey.Text = "API Key:";

        // txtApiKey
        txtApiKey.Location = new Point(80, 22);
        txtApiKey.Name = "txtApiKey";
        txtApiKey.Size = new Size(410, 23);

        // lblApiSecret
        lblApiSecret.Location = new Point(12, 54);
        lblApiSecret.Name = "lblApiSecret";
        lblApiSecret.Size = new Size(65, 20);
        lblApiSecret.Text = "API Secret:";

        // txtApiSecret
        txtApiSecret.Location = new Point(80, 52);
        txtApiSecret.Name = "txtApiSecret";
        txtApiSecret.Size = new Size(410, 23);
        txtApiSecret.UseSystemPasswordChar = true;

        // btnSaveCredentials
        btnSaveCredentials.Location = new Point(415, 76);
        btnSaveCredentials.Name = "btnSaveCredentials";
        btnSaveCredentials.Size = new Size(80, 23);
        btnSaveCredentials.Text = "Save";
        btnSaveCredentials.Click += BtnSaveCredentials_Click;

        // Form1
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1024, 600);
        Controls.Add(tabControl);
        Name = "Form1";
        Text = "Options Trader";

        tabControl.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)dgvQuotes).EndInit();
        grpBalance.ResumeLayout(false);
        grpBalance.PerformLayout();
        grpBroker.ResumeLayout(false);
        grpBroker.PerformLayout();
        grpTickers.ResumeLayout(false);
        grpPositionSize.ResumeLayout(false);
        grpPositionSize.PerformLayout();
        grpTarget.ResumeLayout(false);
        grpTarget.PerformLayout();
        grpSchwabCredentials.ResumeLayout(false);
        grpSchwabCredentials.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)dgvTickers).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private TabControl tabControl;
    private TabPage tabOptions;
    private TabPage tabQuotes;
    private TabPage tabSettings;
    private DataGridView dgvQuotes;
    private DataGridViewTextBoxColumn colQType;
    private DataGridViewTextBoxColumn colQSymbol;
    private DataGridViewTextBoxColumn colQSpot;
    private DataGridViewTextBoxColumn colQStrike;
    private DataGridViewTextBoxColumn colQBid;
    private DataGridViewTextBoxColumn colQAsk;
    private DataGridViewTextBoxColumn colQExpDate;
    private Button btnFetchQuotes;
    private GroupBox grpBalance;
    private TextBox txtBalance;
    private Label lblPositionAmount;
    private GroupBox grpBroker;
    private RadioButton rbSchwab;
    private RadioButton rbIBKR;
    private RadioButton rbETrade;
    private GroupBox grpTickers;
    private DataGridView dgvTickers;
    private DataGridViewTextBoxColumn colSymbol;
    private DataGridViewTextBoxColumn colLow;
    private DataGridViewTextBoxColumn colHigh;
    private DataGridViewTextBoxColumn colExpDate;
    private Button btnSaveTickers;
    private GroupBox grpPositionSize;
    private RadioButton rbPosition25;
    private RadioButton rbPosition5;
    private RadioButton rbPosition10;
    private GroupBox grpTarget;
    private RadioButton rbTarget10;
    private RadioButton rbTarget35;
    private RadioButton rbTarget100;
    private GroupBox grpSchwabCredentials;
    private Label lblApiKey;
    private TextBox txtApiKey;
    private Label lblApiSecret;
    private TextBox txtApiSecret;
    private Button btnSaveCredentials;
}
