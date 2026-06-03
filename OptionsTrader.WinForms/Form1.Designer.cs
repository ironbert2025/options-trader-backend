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
        tabQuotes = new TabPage();
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
        grpTickerButtons = new GroupBox();
        flpTickers = new FlowLayoutPanel();
        tabSettings = new TabPage();
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
        tabQuotes.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvQuotes).BeginInit();
        grpBalance.SuspendLayout();
        grpTickerButtons.SuspendLayout();
        tabSettings.SuspendLayout();
        grpBroker.SuspendLayout();
        grpTickers.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvTickers).BeginInit();
        grpPositionSize.SuspendLayout();
        grpTarget.SuspendLayout();
        grpSchwabCredentials.SuspendLayout();
        SuspendLayout();
        // 
        // tabControl
        // 
        tabControl.Controls.Add(tabQuotes);
        tabControl.Controls.Add(tabSettings);
        tabControl.Dock = DockStyle.Fill;
        tabControl.Location = new Point(0, 0);
        tabControl.Name = "tabControl";
        tabControl.SelectedIndex = 0;
        tabControl.Size = new Size(1024, 600);
        tabControl.TabIndex = 0;
        // 
        // tabQuotes
        // 
        tabQuotes.Controls.Add(dgvQuotes);
        tabQuotes.Controls.Add(btnFetchQuotes);
        tabQuotes.Controls.Add(grpBalance);
        tabQuotes.Controls.Add(grpTickerButtons);
        tabQuotes.Location = new Point(4, 24);
        tabQuotes.Name = "tabQuotes";
        tabQuotes.Padding = new Padding(8);
        tabQuotes.Size = new Size(1016, 572);
        tabQuotes.TabIndex = 1;
        tabQuotes.Text = "Options Quotes";
        // 
        // dgvQuotes
        // 
        dgvQuotes.AllowUserToAddRows = false;
        dgvQuotes.AllowUserToDeleteRows = false;
        dgvQuotes.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgvQuotes.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgvQuotes.Columns.AddRange(new DataGridViewColumn[] { colQType, colQSymbol, colQSpot, colQStrike, colQBid, colQAsk, colQExpDate });
        dgvQuotes.Location = new Point(8, 84);
        dgvQuotes.Name = "dgvQuotes";
        dgvQuotes.ReadOnly = true;
        dgvQuotes.RowHeadersVisible = false;
        dgvQuotes.RowTemplate.Height = 22;
        dgvQuotes.Size = new Size(1000, 476);
        dgvQuotes.TabIndex = 0;
        // 
        // colQType
        // 
        colQType.HeaderText = "Type";
        colQType.Name = "colQType";
        colQType.ReadOnly = true;
        // 
        // colQSymbol
        // 
        colQSymbol.HeaderText = "Symbol";
        colQSymbol.Name = "colQSymbol";
        colQSymbol.ReadOnly = true;
        // 
        // colQSpot
        // 
        colQSpot.HeaderText = "Spot Price";
        colQSpot.Name = "colQSpot";
        colQSpot.ReadOnly = true;
        // 
        // colQStrike
        // 
        colQStrike.HeaderText = "Strike";
        colQStrike.Name = "colQStrike";
        colQStrike.ReadOnly = true;
        // 
        // colQBid
        // 
        colQBid.HeaderText = "Bid";
        colQBid.Name = "colQBid";
        colQBid.ReadOnly = true;
        // 
        // colQAsk
        // 
        colQAsk.HeaderText = "Ask";
        colQAsk.Name = "colQAsk";
        colQAsk.ReadOnly = true;
        // 
        // colQExpDate
        // 
        colQExpDate.HeaderText = "Expiration";
        colQExpDate.Name = "colQExpDate";
        colQExpDate.ReadOnly = true;
        // 
        // grpTickerButtons
        //
        grpTickerButtons.Controls.Add(flpTickers);
        grpTickerButtons.Location = new Point(128, 4);
        grpTickerButtons.Name = "grpTickerButtons";
        grpTickerButtons.Size = new Size(360, 68);
        grpTickerButtons.TabStop = false;
        grpTickerButtons.Text = "Tickers";
        //
        // flpTickers
        //
        flpTickers.Dock = DockStyle.Fill;
        flpTickers.Location = new Point(3, 19);
        flpTickers.Name = "flpTickers";
        flpTickers.Padding = new Padding(4);
        flpTickers.Size = new Size(354, 46);
        //
        // btnFetchQuotes
        //
        btnFetchQuotes.Location = new Point(500, 22);
        btnFetchQuotes.Name = "btnFetchQuotes";
        btnFetchQuotes.Size = new Size(120, 26);
        btnFetchQuotes.TabIndex = 1;
        btnFetchQuotes.Text = "Fetch Quotes";
        btnFetchQuotes.Click += BtnFetchQuotes_Click;
        // 
        // grpBalance
        // 
        grpBalance.Controls.Add(txtBalance);
        grpBalance.Controls.Add(lblPositionAmount);
        grpBalance.Location = new Point(8, 8);
        grpBalance.Name = "grpBalance";
        grpBalance.Size = new Size(112, 68);
        grpBalance.TabIndex = 2;
        grpBalance.TabStop = false;
        grpBalance.Text = "Balance";
        // 
        // txtBalance
        // 
        txtBalance.Location = new Point(8, 20);
        txtBalance.Name = "txtBalance";
        txtBalance.Size = new Size(72, 23);
        txtBalance.TabIndex = 0;
        txtBalance.TextChanged += TxtBalance_TextChanged;
        // 
        // lblPositionAmount
        // 
        lblPositionAmount.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        lblPositionAmount.Location = new Point(8, 48);
        lblPositionAmount.Name = "lblPositionAmount";
        lblPositionAmount.Size = new Size(86, 16);
        lblPositionAmount.TabIndex = 1;
        // 
        // tabSettings
        // 
        tabSettings.Controls.Add(grpBroker);
        tabSettings.Controls.Add(grpTickers);
        tabSettings.Controls.Add(grpPositionSize);
        tabSettings.Controls.Add(grpTarget);
        tabSettings.Controls.Add(grpSchwabCredentials);
        tabSettings.Location = new Point(4, 24);
        tabSettings.Name = "tabSettings";
        tabSettings.Padding = new Padding(8);
        tabSettings.Size = new Size(1016, 572);
        tabSettings.TabIndex = 2;
        tabSettings.Text = "Settings";
        // 
        // grpBroker
        // 
        grpBroker.Controls.Add(rbSchwab);
        grpBroker.Controls.Add(rbIBKR);
        grpBroker.Controls.Add(rbETrade);
        grpBroker.Location = new Point(8, 8);
        grpBroker.Name = "grpBroker";
        grpBroker.Size = new Size(155, 105);
        grpBroker.TabIndex = 0;
        grpBroker.TabStop = false;
        grpBroker.Text = "Broker";
        // 
        // rbSchwab
        // 
        rbSchwab.Location = new Point(12, 22);
        rbSchwab.Name = "rbSchwab";
        rbSchwab.Size = new Size(135, 20);
        rbSchwab.TabIndex = 0;
        rbSchwab.Text = "Charles Schwab";
        rbSchwab.CheckedChanged += BrokerRadioButton_CheckedChanged;
        // 
        // rbIBKR
        // 
        rbIBKR.Location = new Point(12, 46);
        rbIBKR.Name = "rbIBKR";
        rbIBKR.Size = new Size(135, 20);
        rbIBKR.TabIndex = 1;
        rbIBKR.Text = "Interactive Broker";
        rbIBKR.CheckedChanged += BrokerRadioButton_CheckedChanged;
        // 
        // rbETrade
        // 
        rbETrade.Location = new Point(12, 70);
        rbETrade.Name = "rbETrade";
        rbETrade.Size = new Size(135, 20);
        rbETrade.TabIndex = 2;
        rbETrade.Text = "ETrade";
        rbETrade.CheckedChanged += BrokerRadioButton_CheckedChanged;
        // 
        // grpTickers
        // 
        grpTickers.Controls.Add(dgvTickers);
        grpTickers.Controls.Add(btnSaveTickers);
        grpTickers.Location = new Point(175, 8);
        grpTickers.Name = "grpTickers";
        grpTickers.Size = new Size(340, 168);
        grpTickers.TabIndex = 1;
        grpTickers.TabStop = false;
        grpTickers.Text = "Tickers";
        // 
        // dgvTickers
        // 
        dgvTickers.AllowUserToAddRows = false;
        dgvTickers.AllowUserToDeleteRows = false;
        dgvTickers.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgvTickers.Columns.AddRange(new DataGridViewColumn[] { colSymbol, colLow, colHigh, colExpDate });
        dgvTickers.Location = new Point(8, 20);
        dgvTickers.Name = "dgvTickers";
        dgvTickers.RowHeadersVisible = false;
        dgvTickers.RowTemplate.Height = 22;
        dgvTickers.ScrollBars = ScrollBars.None;
        dgvTickers.Size = new Size(318, 112);
        dgvTickers.TabIndex = 0;
        // 
        // colSymbol
        // 
        colSymbol.HeaderText = "Symbol";
        colSymbol.Name = "colSymbol";
        colSymbol.Width = 80;
        // 
        // colLow
        // 
        colLow.HeaderText = "Low";
        colLow.Name = "colLow";
        colLow.Width = 74;
        // 
        // colHigh
        // 
        colHigh.HeaderText = "High";
        colHigh.Name = "colHigh";
        colHigh.Width = 74;
        // 
        // colExpDate
        // 
        colExpDate.HeaderText = "ExpDate";
        colExpDate.Name = "colExpDate";
        colExpDate.Width = 90;
        // 
        // btnSaveTickers
        // 
        btnSaveTickers.Location = new Point(250, 138);
        btnSaveTickers.Name = "btnSaveTickers";
        btnSaveTickers.Size = new Size(80, 23);
        btnSaveTickers.TabIndex = 1;
        btnSaveTickers.Text = "Save";
        btnSaveTickers.Click += BtnSaveTickers_Click;
        // 
        // grpPositionSize
        // 
        grpPositionSize.Controls.Add(rbPosition25);
        grpPositionSize.Controls.Add(rbPosition5);
        grpPositionSize.Controls.Add(rbPosition10);
        grpPositionSize.Location = new Point(525, 8);
        grpPositionSize.Name = "grpPositionSize";
        grpPositionSize.Size = new Size(110, 105);
        grpPositionSize.TabIndex = 2;
        grpPositionSize.TabStop = false;
        grpPositionSize.Text = "Position Size (%)";
        // 
        // rbPosition25
        // 
        rbPosition25.Location = new Point(12, 22);
        rbPosition25.Name = "rbPosition25";
        rbPosition25.Size = new Size(90, 20);
        rbPosition25.TabIndex = 0;
        rbPosition25.Text = "2.5";
        rbPosition25.CheckedChanged += PositionSizeRadioButton_CheckedChanged;
        // 
        // rbPosition5
        // 
        rbPosition5.Location = new Point(12, 46);
        rbPosition5.Name = "rbPosition5";
        rbPosition5.Size = new Size(90, 20);
        rbPosition5.TabIndex = 1;
        rbPosition5.Text = "5";
        rbPosition5.CheckedChanged += PositionSizeRadioButton_CheckedChanged;
        // 
        // rbPosition10
        // 
        rbPosition10.Location = new Point(12, 70);
        rbPosition10.Name = "rbPosition10";
        rbPosition10.Size = new Size(90, 20);
        rbPosition10.TabIndex = 2;
        rbPosition10.Text = "10";
        rbPosition10.CheckedChanged += PositionSizeRadioButton_CheckedChanged;
        // 
        // grpTarget
        // 
        grpTarget.Controls.Add(rbTarget10);
        grpTarget.Controls.Add(rbTarget35);
        grpTarget.Controls.Add(rbTarget100);
        grpTarget.Location = new Point(643, 8);
        grpTarget.Name = "grpTarget";
        grpTarget.Size = new Size(110, 105);
        grpTarget.TabIndex = 3;
        grpTarget.TabStop = false;
        grpTarget.Text = "Target (%)";
        // 
        // rbTarget10
        // 
        rbTarget10.Location = new Point(12, 22);
        rbTarget10.Name = "rbTarget10";
        rbTarget10.Size = new Size(90, 20);
        rbTarget10.TabIndex = 0;
        rbTarget10.Text = "10";
        rbTarget10.CheckedChanged += TargetRadioButton_CheckedChanged;
        // 
        // rbTarget35
        // 
        rbTarget35.Location = new Point(12, 46);
        rbTarget35.Name = "rbTarget35";
        rbTarget35.Size = new Size(90, 20);
        rbTarget35.TabIndex = 1;
        rbTarget35.Text = "35";
        rbTarget35.CheckedChanged += TargetRadioButton_CheckedChanged;
        // 
        // rbTarget100
        // 
        rbTarget100.Location = new Point(12, 70);
        rbTarget100.Name = "rbTarget100";
        rbTarget100.Size = new Size(90, 20);
        rbTarget100.TabIndex = 2;
        rbTarget100.Text = "100";
        rbTarget100.CheckedChanged += TargetRadioButton_CheckedChanged;
        // 
        // grpSchwabCredentials
        // 
        grpSchwabCredentials.Controls.Add(lblApiKey);
        grpSchwabCredentials.Controls.Add(txtApiKey);
        grpSchwabCredentials.Controls.Add(lblApiSecret);
        grpSchwabCredentials.Controls.Add(txtApiSecret);
        grpSchwabCredentials.Controls.Add(btnSaveCredentials);
        grpSchwabCredentials.Location = new Point(8, 180);
        grpSchwabCredentials.Name = "grpSchwabCredentials";
        grpSchwabCredentials.Size = new Size(507, 105);
        grpSchwabCredentials.TabIndex = 4;
        grpSchwabCredentials.TabStop = false;
        grpSchwabCredentials.Text = "Schwab Credentials";
        // 
        // lblApiKey
        // 
        lblApiKey.Location = new Point(12, 24);
        lblApiKey.Name = "lblApiKey";
        lblApiKey.Size = new Size(65, 20);
        lblApiKey.TabIndex = 0;
        lblApiKey.Text = "API Key:";
        // 
        // txtApiKey
        // 
        txtApiKey.Location = new Point(80, 22);
        txtApiKey.Name = "txtApiKey";
        txtApiKey.Size = new Size(410, 23);
        txtApiKey.TabIndex = 1;
        // 
        // lblApiSecret
        // 
        lblApiSecret.Location = new Point(12, 54);
        lblApiSecret.Name = "lblApiSecret";
        lblApiSecret.Size = new Size(65, 20);
        lblApiSecret.TabIndex = 2;
        lblApiSecret.Text = "API Secret:";
        // 
        // txtApiSecret
        // 
        txtApiSecret.Location = new Point(80, 52);
        txtApiSecret.Name = "txtApiSecret";
        txtApiSecret.Size = new Size(410, 23);
        txtApiSecret.TabIndex = 3;
        txtApiSecret.UseSystemPasswordChar = true;
        // 
        // btnSaveCredentials
        // 
        btnSaveCredentials.Location = new Point(415, 76);
        btnSaveCredentials.Name = "btnSaveCredentials";
        btnSaveCredentials.Size = new Size(80, 23);
        btnSaveCredentials.TabIndex = 4;
        btnSaveCredentials.Text = "Save";
        btnSaveCredentials.Click += BtnSaveCredentials_Click;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1024, 600);
        Controls.Add(tabControl);
        Name = "Form1";
        Text = "Options Trader";
        tabControl.ResumeLayout(false);
        tabQuotes.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)dgvQuotes).EndInit();
        grpBalance.ResumeLayout(false);
        grpBalance.PerformLayout();
        grpTickerButtons.ResumeLayout(false);
        tabSettings.ResumeLayout(false);
        grpBroker.ResumeLayout(false);
        grpTickers.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)dgvTickers).EndInit();
        grpPositionSize.ResumeLayout(false);
        grpTarget.ResumeLayout(false);
        grpSchwabCredentials.ResumeLayout(false);
        grpSchwabCredentials.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TabControl tabControl;
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
    private GroupBox grpTickerButtons;
    private FlowLayoutPanel flpTickers;
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
