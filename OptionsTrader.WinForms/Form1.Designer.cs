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
        grpLogger = new GroupBox();
        rtbLogger = new RichTextBox();
        grpTrades = new GroupBox();
        dgvTrades = new DataGridView();
        colTradeTime = new DataGridViewTextBoxColumn();
        colTradeType = new DataGridViewTextBoxColumn();
        colTradeStrike = new DataGridViewTextBoxColumn();
        colTradeBid = new DataGridViewTextBoxColumn();
        colTradeAsk = new DataGridViewTextBoxColumn();
        colTradeContracts = new DataGridViewTextBoxColumn();
        colTradeEntryPrice = new DataGridViewTextBoxColumn();
        colTradeCBid = new DataGridViewTextBoxColumn();
        colTradeTBid = new DataGridViewTextBoxColumn();
        colTradePnL = new DataGridViewTextBoxColumn();
        colTradePnLPercent = new DataGridViewTextBoxColumn();
        colTradePnLTarget = new DataGridViewTextBoxColumn();
        colTradeExitTime = new DataGridViewTextBoxColumn();
        colTradeClose = new DataGridViewButtonColumn();
        grpOptionsChain = new GroupBox();
        dgvQuotes = new DataGridView();
        colSymbolQ = new DataGridViewTextBoxColumn();
        colRange = new DataGridViewTextBoxColumn();
        colCallSprd = new DataGridViewTextBoxColumn();
        colCallBid = new DataGridViewTextBoxColumn();
        colCallAsk = new DataGridViewTextBoxColumn();
        colSpotPrice = new DataGridViewTextBoxColumn();
        colStrikePrice = new DataGridViewButtonColumn();
        colPutBid = new DataGridViewTextBoxColumn();
        colPutAsk = new DataGridViewTextBoxColumn();
        colPutSprd = new DataGridViewTextBoxColumn();
        colContracts = new DataGridViewTextBoxColumn();
        colLevel = new DataGridViewTextBoxColumn();
        lblCallHeader = new Label();
        lblPutHeader = new Label();
        chkStopAt11 = new CheckBox();
        chkSaveToCsv = new CheckBox();
        btnFetchQuotes = new Button();
        btnStartPolling = new Button();
        lblExpDate = new Label();
        lblLastUpdate = new Label();
        grpBalance = new GroupBox();
        txtBalance = new TextBox();
        lblPositionAmount = new Label();
        grpTickerButtons = new GroupBox();
        flpTickers = new FlowLayoutPanel();
        grpTrade = new GroupBox();
        rbNoTrade = new RadioButton();
        rbTrade = new RadioButton();
        rbTradeTarget = new RadioButton();
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
        lblCredentialsSaved = new Label();
        tabControl.SuspendLayout();
        tabQuotes.SuspendLayout();
        grpLogger.SuspendLayout();
        grpTrades.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvTrades).BeginInit();
        grpOptionsChain.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvQuotes).BeginInit();
        grpBalance.SuspendLayout();
        grpTickerButtons.SuspendLayout();
        grpTrade.SuspendLayout();
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
        tabQuotes.Controls.Add(grpLogger);
        tabQuotes.Controls.Add(grpTrades);
        tabQuotes.Controls.Add(grpOptionsChain);
        tabQuotes.Controls.Add(chkSaveToCsv);
        tabQuotes.Controls.Add(btnFetchQuotes);
        tabQuotes.Controls.Add(btnStartPolling);
        tabQuotes.Controls.Add(lblExpDate);
        tabQuotes.Controls.Add(lblLastUpdate);
        tabQuotes.Controls.Add(grpBalance);
        tabQuotes.Controls.Add(grpTickerButtons);
        tabQuotes.Controls.Add(grpTrade);
        tabQuotes.Location = new Point(4, 24);
        tabQuotes.Name = "tabQuotes";
        tabQuotes.Padding = new Padding(8);
        tabQuotes.Size = new Size(1016, 572);
        tabQuotes.TabIndex = 1;
        tabQuotes.Text = "Options Quotes";
        // 
        // grpLogger
        // 
        grpLogger.Controls.Add(rtbLogger);
        grpLogger.Location = new Point(8, 415);
        grpLogger.Name = "grpLogger";
        grpLogger.Size = new Size(1000, 149);
        grpLogger.TabIndex = 0;
        grpLogger.TabStop = false;
        grpLogger.Text = "Logger";
        // 
        // rtbLogger
        // 
        rtbLogger.BackColor = Color.Black;
        rtbLogger.Dock = DockStyle.Fill;
        rtbLogger.Font = new Font("Consolas", 8.5F);
        rtbLogger.Location = new Point(3, 19);
        rtbLogger.Name = "rtbLogger";
        rtbLogger.ReadOnly = true;
        rtbLogger.ScrollBars = RichTextBoxScrollBars.Vertical;
        rtbLogger.Size = new Size(994, 127);
        rtbLogger.TabIndex = 0;
        rtbLogger.Text = "";
        rtbLogger.WordWrap = false;
        // 
        // grpTrades
        // 
        grpTrades.Controls.Add(dgvTrades);
        grpTrades.Location = new Point(11, 306);
        grpTrades.Name = "grpTrades";
        grpTrades.Size = new Size(1000, 90);
        grpTrades.TabIndex = 1;
        grpTrades.TabStop = false;
        grpTrades.Text = "Trades";
        // 
        // dgvTrades
        // 
        dgvTrades.AllowUserToAddRows = false;
        dgvTrades.AllowUserToDeleteRows = false;
        dgvTrades.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgvTrades.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgvTrades.Columns.AddRange(new DataGridViewColumn[] { colTradeTime, colTradeType, colTradeStrike, colTradeBid, colTradeAsk, colTradeContracts, colTradeEntryPrice, colTradeCBid, colTradeTBid, colTradePnL, colTradePnLPercent, colTradePnLTarget, colTradeExitTime, colTradeClose });
        dgvTrades.Dock = DockStyle.Fill;
        dgvTrades.Location = new Point(3, 19);
        dgvTrades.Name = "dgvTrades";
        dgvTrades.RowHeadersVisible = false;
        dgvTrades.RowTemplate.Height = 22;
        dgvTrades.Size = new Size(994, 68);
        dgvTrades.TabIndex = 0;
        dgvTrades.CellClick += DgvTrades_CellClick;
        // 
        // colTradeTime
        // 
        colTradeTime.HeaderText = "Time";
        colTradeTime.Name = "colTradeTime";
        colTradeTime.ReadOnly = true;
        // 
        // colTradeType
        // 
        colTradeType.HeaderText = "Type";
        colTradeType.Name = "colTradeType";
        colTradeType.ReadOnly = true;
        // 
        // colTradeStrike
        // 
        colTradeStrike.HeaderText = "StrikePrice";
        colTradeStrike.Name = "colTradeStrike";
        colTradeStrike.ReadOnly = true;
        // 
        // colTradeBid
        // 
        colTradeBid.HeaderText = "Bid";
        colTradeBid.Name = "colTradeBid";
        colTradeBid.ReadOnly = true;
        // 
        // colTradeAsk
        // 
        colTradeAsk.HeaderText = "Ask";
        colTradeAsk.Name = "colTradeAsk";
        colTradeAsk.ReadOnly = true;
        // 
        // colTradeContracts
        // 
        colTradeContracts.HeaderText = "Contracts";
        colTradeContracts.Name = "colTradeContracts";
        colTradeContracts.ReadOnly = true;
        // 
        // colTradeEntryPrice
        // 
        colTradeEntryPrice.HeaderText = "EntryPrice";
        colTradeEntryPrice.Name = "colTradeEntryPrice";
        colTradeEntryPrice.ReadOnly = true;
        // 
        // colTradeCBid
        // 
        colTradeCBid.HeaderText = "C_Bid";
        colTradeCBid.Name = "colTradeCBid";
        colTradeCBid.ReadOnly = true;
        // 
        // colTradeTBid
        // 
        colTradeTBid.HeaderText = "T_Bid";
        colTradeTBid.Name = "colTradeTBid";
        colTradeTBid.ReadOnly = true;
        // 
        // colTradePnL
        // 
        colTradePnL.HeaderText = "PnL";
        colTradePnL.Name = "colTradePnL";
        colTradePnL.ReadOnly = true;
        // 
        // colTradePnLPercent
        // 
        colTradePnLPercent.HeaderText = "PnL_Percent";
        colTradePnLPercent.Name = "colTradePnLPercent";
        colTradePnLPercent.ReadOnly = true;
        // 
        // colTradePnLTarget
        // 
        colTradePnLTarget.HeaderText = "PnL_Target";
        colTradePnLTarget.Name = "colTradePnLTarget";
        colTradePnLTarget.ReadOnly = true;
        // 
        // colTradeExitTime
        // 
        colTradeExitTime.HeaderText = "ExitTime";
        colTradeExitTime.Name = "colTradeExitTime";
        colTradeExitTime.ReadOnly = true;
        // 
        // colTradeClose
        // 
        colTradeClose.FlatStyle = FlatStyle.Flat;
        colTradeClose.HeaderText = "Close";
        colTradeClose.Name = "colTradeClose";
        // 
        // grpOptionsChain
        // 
        grpOptionsChain.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grpOptionsChain.Controls.Add(dgvQuotes);
        grpOptionsChain.Controls.Add(lblCallHeader);
        grpOptionsChain.Controls.Add(lblPutHeader);
        grpOptionsChain.Controls.Add(chkStopAt11);
        grpOptionsChain.Location = new Point(8, 100);
        grpOptionsChain.Name = "grpOptionsChain";
        grpOptionsChain.Size = new Size(1000, 200);
        grpOptionsChain.TabIndex = 2;
        grpOptionsChain.TabStop = false;
        grpOptionsChain.Text = "OptionsChain";
        // 
        // dgvQuotes
        // 
        dgvQuotes.AllowUserToAddRows = false;
        dgvQuotes.AllowUserToDeleteRows = false;
        dgvQuotes.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        dgvQuotes.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgvQuotes.Columns.AddRange(new DataGridViewColumn[] { colSymbolQ, colRange, colCallSprd, colCallBid, colCallAsk, colSpotPrice, colStrikePrice, colPutBid, colPutAsk, colPutSprd, colContracts, colLevel });
        dgvQuotes.Location = new Point(3, 20);
        dgvQuotes.Name = "dgvQuotes";
        dgvQuotes.RowHeadersVisible = false;
        dgvQuotes.RowTemplate.Height = 22;
        dgvQuotes.Size = new Size(994, 175);
        dgvQuotes.TabIndex = 0;
        dgvQuotes.CellClick += DgvQuotes_CellClick;
        dgvQuotes.CellFormatting += DgvQuotes_CellFormatting;
        // 
        // colSymbolQ
        // 
        colSymbolQ.HeaderText = "Symbol";
        colSymbolQ.Name = "colSymbolQ";
        colSymbolQ.ReadOnly = true;
        colSymbolQ.Width = 70;
        // 
        // colRange
        // 
        colRange.HeaderText = "Range";
        colRange.Name = "colRange";
        colRange.ReadOnly = true;
        colRange.Width = 80;
        // 
        // colCallSprd
        // 
        colCallSprd.HeaderText = "Sprd";
        colCallSprd.Name = "colCallSprd";
        colCallSprd.ReadOnly = true;
        colCallSprd.Width = 55;
        // 
        // colCallBid
        // 
        colCallBid.HeaderText = "Bid";
        colCallBid.Name = "colCallBid";
        colCallBid.ReadOnly = true;
        colCallBid.Width = 65;
        // 
        // colCallAsk
        // 
        colCallAsk.HeaderText = "Ask";
        colCallAsk.Name = "colCallAsk";
        colCallAsk.ReadOnly = true;
        colCallAsk.Width = 65;
        // 
        // colStrikePrice
        // 
        colStrikePrice.FlatStyle = FlatStyle.Flat;
        colSpotPrice.HeaderText = "SpotPrice";
        colSpotPrice.Name = "colSpotPrice";
        colSpotPrice.ReadOnly = true;
        colSpotPrice.Width = 75;
        //
        colStrikePrice.HeaderText = "StrikePrice";
        colStrikePrice.Name = "colStrikePrice";
        colStrikePrice.Width = 80;
        colStrikePrice.FlatStyle = FlatStyle.Standard;
        colStrikePrice.UseColumnTextForButtonValue = false;
        // 
        // colPutBid
        // 
        colPutBid.HeaderText = "Bid";
        colPutBid.Name = "colPutBid";
        colPutBid.ReadOnly = true;
        colPutBid.Width = 65;
        // 
        // colPutAsk
        // 
        colPutAsk.HeaderText = "Ask";
        colPutAsk.Name = "colPutAsk";
        colPutAsk.ReadOnly = true;
        colPutAsk.Width = 65;
        // 
        // colPutSprd
        // 
        colPutSprd.HeaderText = "Sprd";
        colPutSprd.Name = "colPutSprd";
        colPutSprd.ReadOnly = true;
        colPutSprd.Width = 55;
        // 
        // colContracts
        // 
        colContracts.HeaderText = "Contracts";
        colContracts.Name = "colContracts";
        colContracts.ReadOnly = true;
        colContracts.Width = 75;
        // 
        // colLevel
        // 
        colLevel.HeaderText = "Level";
        colLevel.Name = "colLevel";
        colLevel.ReadOnly = true;
        colLevel.Width = 60;
        // 
        // lblCallHeader
        // 
        lblCallHeader.AutoSize = true;
        lblCallHeader.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
        lblCallHeader.ForeColor = Color.Green;
        lblCallHeader.Location = new Point(130, 2);
        lblCallHeader.Name = "lblCallHeader";
        lblCallHeader.Size = new Size(40, 15);
        lblCallHeader.TabIndex = 1;
        lblCallHeader.Text = "CALL";
        // 
        // lblPutHeader
        // 
        lblPutHeader.AutoSize = true;
        lblPutHeader.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
        lblPutHeader.ForeColor = Color.Red;
        lblPutHeader.Location = new Point(620, 2);
        lblPutHeader.Name = "lblPutHeader";
        lblPutHeader.Size = new Size(34, 15);
        lblPutHeader.TabIndex = 2;
        lblPutHeader.Text = "PUT";
        // 
        // chkStopAt11
        // 
        chkStopAt11.AutoSize = true;
        chkStopAt11.Location = new Point(760, 2);
        chkStopAt11.Name = "chkStopAt11";
        chkStopAt11.Size = new Size(115, 19);
        chkStopAt11.TabIndex = 3;
        chkStopAt11.Text = "Stop at 11:00 AM";
        //
        // chkSaveToCsv
        //
        chkSaveToCsv.AutoSize = true;
        chkSaveToCsv.Checked = true;
        chkSaveToCsv.CheckState = CheckState.Checked;
        chkSaveToCsv.Location = new Point(760, 100);
        chkSaveToCsv.Size = new Size(100, 19);
        chkSaveToCsv.Name = "chkSaveToCsv";
        chkSaveToCsv.Text = "Save to CSV";
        //
        // btnFetchQuotes
        // 
        btnFetchQuotes.Location = new Point(630, 22);
        btnFetchQuotes.Name = "btnFetchQuotes";
        btnFetchQuotes.Size = new Size(120, 26);
        btnFetchQuotes.TabIndex = 1;
        btnFetchQuotes.Text = "Fetch Quotes";
        btnFetchQuotes.Click += BtnFetchQuotes_Click;
        // 
        // btnStartPolling
        // 
        btnStartPolling.BackColor = Color.DarkGreen;
        btnStartPolling.FlatStyle = FlatStyle.Flat;
        btnStartPolling.ForeColor = Color.White;
        btnStartPolling.Location = new Point(500, 22);
        btnStartPolling.Name = "btnStartPolling";
        btnStartPolling.Size = new Size(120, 26);
        btnStartPolling.TabIndex = 3;
        btnStartPolling.Text = "Start Polling";
        btnStartPolling.UseVisualStyleBackColor = false;
        btnStartPolling.Click += BtnStartPolling_Click;
        // 
        // lblExpDate
        // 
        lblExpDate.AutoSize = true;
        lblExpDate.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        lblExpDate.ForeColor = Color.DarkGoldenrod;
        lblExpDate.Location = new Point(500, 54);
        lblExpDate.Name = "lblExpDate";
        lblExpDate.Size = new Size(0, 13);
        lblExpDate.TabIndex = 4;
        // 
        // lblLastUpdate
        // 
        lblLastUpdate.AutoSize = true;
        lblLastUpdate.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        lblLastUpdate.ForeColor = Color.DarkGoldenrod;
        lblLastUpdate.Location = new Point(630, 54);
        lblLastUpdate.Name = "lblLastUpdate";
        lblLastUpdate.Size = new Size(0, 13);
        lblLastUpdate.TabIndex = 5;
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
        // grpTickerButtons
        // 
        grpTickerButtons.Controls.Add(flpTickers);
        grpTickerButtons.Location = new Point(128, 4);
        grpTickerButtons.Name = "grpTickerButtons";
        grpTickerButtons.Size = new Size(360, 68);
        grpTickerButtons.TabIndex = 6;
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
        flpTickers.TabIndex = 0;
        // 
        // grpTrade
        // 
        grpTrade.Controls.Add(rbNoTrade);
        grpTrade.Controls.Add(rbTrade);
        grpTrade.Controls.Add(rbTradeTarget);
        grpTrade.Location = new Point(760, 4);
        grpTrade.Name = "grpTrade";
        grpTrade.Size = new Size(130, 90);
        grpTrade.TabIndex = 7;
        grpTrade.TabStop = false;
        grpTrade.Text = "Trade";
        // 
        // rbNoTrade
        // 
        rbNoTrade.Checked = true;
        rbNoTrade.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        rbNoTrade.ForeColor = Color.DarkOrange;
        rbNoTrade.Location = new Point(12, 22);
        rbNoTrade.Name = "rbNoTrade";
        rbNoTrade.Size = new Size(80, 20);
        rbNoTrade.TabIndex = 0;
        rbNoTrade.TabStop = true;
        rbNoTrade.Text = "No Trade";
        rbNoTrade.CheckedChanged += TradeRadioButton_CheckedChanged;
        // 
        // rbTrade
        // 
        rbTrade.Location = new Point(12, 44);
        rbTrade.Name = "rbTrade";
        rbTrade.Size = new Size(80, 20);
        rbTrade.TabIndex = 1;
        rbTrade.Text = "Trade";
        rbTrade.CheckedChanged += TradeRadioButton_CheckedChanged;
        // 
        // rbTradeTarget
        // 
        rbTradeTarget.Location = new Point(12, 66);
        rbTradeTarget.Name = "rbTradeTarget";
        rbTradeTarget.Size = new Size(100, 20);
        rbTradeTarget.TabIndex = 2;
        rbTradeTarget.Text = "Trade-Target";
        rbTradeTarget.CheckedChanged += TradeRadioButton_CheckedChanged;
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
        grpSchwabCredentials.Controls.Add(lblCredentialsSaved);
        grpSchwabCredentials.Location = new Point(8, 180);
        grpSchwabCredentials.Name = "grpSchwabCredentials";
        grpSchwabCredentials.Size = new Size(507, 125);
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
        // lblCredentialsSaved
        // 
        lblCredentialsSaved.AutoSize = true;
        lblCredentialsSaved.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        lblCredentialsSaved.ForeColor = Color.Green;
        lblCredentialsSaved.Location = new Point(12, 102);
        lblCredentialsSaved.Name = "lblCredentialsSaved";
        lblCredentialsSaved.Size = new Size(110, 13);
        lblCredentialsSaved.TabIndex = 5;
        lblCredentialsSaved.Text = "Credentials Saved";
        lblCredentialsSaved.Visible = false;
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
        tabQuotes.PerformLayout();
        grpLogger.ResumeLayout(false);
        grpTrades.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)dgvTrades).EndInit();
        grpOptionsChain.ResumeLayout(false);
        grpOptionsChain.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)dgvQuotes).EndInit();
        grpBalance.ResumeLayout(false);
        grpBalance.PerformLayout();
        grpTickerButtons.ResumeLayout(false);
        grpTrade.ResumeLayout(false);
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
    private DataGridViewTextBoxColumn colSymbolQ;
    private DataGridViewTextBoxColumn colRange;
    private DataGridViewTextBoxColumn colCallSprd;
    private DataGridViewTextBoxColumn colCallBid;
    private DataGridViewTextBoxColumn colCallAsk;
    private DataGridViewTextBoxColumn colSpotPrice;
    private DataGridViewButtonColumn colStrikePrice;
    private DataGridViewTextBoxColumn colPutBid;
    private DataGridViewTextBoxColumn colPutAsk;
    private DataGridViewTextBoxColumn colPutSprd;
    private DataGridViewTextBoxColumn colContracts;
    private DataGridViewTextBoxColumn colLevel;
    private Label lblCallHeader;
    private Label lblPutHeader;
    private CheckBox chkStopAt11;
    private CheckBox chkSaveToCsv;
    private GroupBox grpTrade;
    private RadioButton rbNoTrade;
    private RadioButton rbTrade;
    private RadioButton rbTradeTarget;
    private GroupBox grpOptionsChain;
    private GroupBox grpTrades;
    private GroupBox grpLogger;
    private RichTextBox rtbLogger;
    private DataGridView dgvTrades;
    private DataGridViewTextBoxColumn colTradeTime;
    private DataGridViewTextBoxColumn colTradeType;
    private DataGridViewTextBoxColumn colTradeStrike;
    private DataGridViewTextBoxColumn colTradeBid;
    private DataGridViewTextBoxColumn colTradeAsk;
    private DataGridViewTextBoxColumn colTradeContracts;
    private DataGridViewTextBoxColumn colTradeEntryPrice;
    private DataGridViewTextBoxColumn colTradeCBid;
    private DataGridViewTextBoxColumn colTradeTBid;
    private DataGridViewTextBoxColumn colTradePnL;
    private DataGridViewTextBoxColumn colTradePnLPercent;
    private DataGridViewTextBoxColumn colTradePnLTarget;
    private DataGridViewTextBoxColumn colTradeExitTime;
    private DataGridViewButtonColumn colTradeClose;
    private Button btnFetchQuotes;
    private Button btnStartPolling;
    private Label lblExpDate;
    private Label lblLastUpdate;
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
    private Label lblCredentialsSaved;
}
