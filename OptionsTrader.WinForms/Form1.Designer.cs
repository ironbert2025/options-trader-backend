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
        statusStrip1 = new StatusStrip();
        lblStatusUser = new ToolStripStatusLabel();
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
        colTradePnLMin = new DataGridViewTextBoxColumn();
        colTradePnLMax = new DataGridViewTextBoxColumn();
        colTradeMoneyness = new DataGridViewTextBoxColumn();
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
        chkCallFilter = new CheckBox();
        chkPutFilter = new CheckBox();
        grpOptionsChainNext = new GroupBox();
        dgvQuotesNext = new DataGridView();
        colSymbolQNext = new DataGridViewTextBoxColumn();
        colRangeNext = new DataGridViewTextBoxColumn();
        colCallSprdNext = new DataGridViewTextBoxColumn();
        colCallBidNext = new DataGridViewTextBoxColumn();
        colCallAskNext = new DataGridViewTextBoxColumn();
        colSpotPriceNext = new DataGridViewTextBoxColumn();
        colStrikePriceNext = new DataGridViewTextBoxColumn();
        colPutBidNext = new DataGridViewTextBoxColumn();
        colPutAskNext = new DataGridViewTextBoxColumn();
        colPutSprdNext = new DataGridViewTextBoxColumn();
        colContractsNext = new DataGridViewTextBoxColumn();
        colLevelNext = new DataGridViewTextBoxColumn();
        lblCallHeaderNext = new Label();
        lblPutHeaderNext = new Label();
        lblExpDateNext = new Label();
        chkStopAt11 = new CheckBox();
        chkSaveToCsv = new CheckBox();
        chkHideNextExpDate = new CheckBox();
        btnFetchQuotes = new Button();
        btnLiveChart = new Button();
        btnFourEtfCharts = new Button();
        btnHubHost = new Button();
        btnSimulator = new Button();
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
        rbNoTradeTarget = new RadioButton();
        grpContracts = new GroupBox();
        rbContracts1 = new RadioButton();
        rbContracts2 = new RadioButton();
        rbContracts3 = new RadioButton();
        rbContracts4 = new RadioButton();
        rbContracts5 = new RadioButton();
        rbContracts6 = new RadioButton();
        rbContractsPositionSize = new RadioButton();
        grpCounts = new GroupBox();
        rbCounts3 = new RadioButton();
        rbCounts4 = new RadioButton();
        rbCounts5 = new RadioButton();
        rbCounts6 = new RadioButton();
        rbCounts7 = new RadioButton();
        rbCounts8 = new RadioButton();
        rbCounts9 = new RadioButton();
        rbCounts10 = new RadioButton();
        rbCountsInRange = new RadioButton();
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
        grpScreenCoords = new GroupBox();
        btnSaveCoords = new Button();
        btnResetCoords = new Button();
        pnlCoordsRows = new Panel();
        grpPositionSize = new GroupBox();
        rbPosition25 = new RadioButton();
        rbPosition5 = new RadioButton();
        rbPosition10 = new RadioButton();
        chkSaveDumps = new CheckBox();
        chkShowOrderConfirmation = new CheckBox();
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
        grpRefreshToken = new GroupBox();
        btnLogin = new Button();
        txtResponse = new TextBox();
        lblResponseHint = new Label();
        lblTokenStatus = new Label();
        grpAccounts = new GroupBox();
        dgvAccounts = new DataGridView();
        colAccountDefault = new DataGridViewCheckBoxColumn();
        colAccountNumber = new DataGridViewTextBoxColumn();
        colAccountHash = new DataGridViewTextBoxColumn();
        btnRefreshAccounts = new Button();
        grpAwsSettings = new GroupBox();
        lblAwsAccessKey = new Label();
        txtAwsAccessKey = new TextBox();
        lblAwsSecretKey = new Label();
        txtAwsSecretKey = new TextBox();
        lblAwsBucket = new Label();
        txtAwsBucket = new TextBox();
        lblAwsRegion = new Label();
        txtAwsRegion = new TextBox();
        btnSaveAwsSettings = new Button();
        lblAwsSaved = new Label();
        tabControl.SuspendLayout();
        tabQuotes.SuspendLayout();
        grpLogger.SuspendLayout();
        grpTrades.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvTrades).BeginInit();
        grpOptionsChain.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvQuotes).BeginInit();
        grpOptionsChainNext.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvQuotesNext).BeginInit();
        grpBalance.SuspendLayout();
        grpTickerButtons.SuspendLayout();
        grpTrade.SuspendLayout();
        grpContracts.SuspendLayout();
        grpCounts.SuspendLayout();
        tabSettings.SuspendLayout();
        grpBroker.SuspendLayout();
        grpTickers.SuspendLayout();
        grpScreenCoords.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvTickers).BeginInit();
        grpPositionSize.SuspendLayout();
        grpTarget.SuspendLayout();
        grpSchwabCredentials.SuspendLayout();
        grpRefreshToken.SuspendLayout();
        grpAccounts.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvAccounts).BeginInit();
        grpAwsSettings.SuspendLayout();
        statusStrip1.SuspendLayout();
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
        tabControl.Size = new Size(1181, 600);
        tabControl.TabIndex = 0;
        // 
        // tabQuotes
        // 
        tabQuotes.Controls.Add(grpLogger);
        tabQuotes.Controls.Add(grpTrades);
        tabQuotes.Controls.Add(grpOptionsChain);
        tabQuotes.Controls.Add(grpOptionsChainNext);
        tabQuotes.Controls.Add(btnFetchQuotes);
        tabQuotes.Controls.Add(btnLiveChart);
        tabQuotes.Controls.Add(btnFourEtfCharts);
        tabQuotes.Controls.Add(btnHubHost);
        tabQuotes.Controls.Add(btnSimulator);
        tabQuotes.Controls.Add(btnStartPolling);
        tabQuotes.Controls.Add(lblLastUpdate);
        tabQuotes.Controls.Add(grpBalance);
        tabQuotes.Controls.Add(grpTickerButtons);
        tabQuotes.Controls.Add(grpTrade);
        tabQuotes.Controls.Add(grpContracts);
        tabQuotes.Controls.Add(grpCounts);
        tabQuotes.Controls.Add(chkStopAt11);
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
        grpLogger.Size = new Size(1150, 149);
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
        rtbLogger.Size = new Size(1054, 127);
        rtbLogger.TabIndex = 0;
        rtbLogger.Text = "";
        rtbLogger.WordWrap = false;
        // 
        // grpTrades
        // 
        grpTrades.Controls.Add(dgvTrades);
        grpTrades.Location = new Point(11, 306);
        grpTrades.Name = "grpTrades";
        grpTrades.Size = new Size(1147, 90);
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
        dgvTrades.Columns.AddRange(new DataGridViewColumn[] { colTradeTime, colTradeType, colTradeStrike, colTradeBid, colTradeAsk, colTradeContracts, colTradeEntryPrice, colTradeCBid, colTradeTBid, colTradePnL, colTradePnLPercent, colTradePnLTarget, colTradeExitTime, colTradeClose, colTradePnLMin, colTradePnLMax, colTradeMoneyness });
        dgvTrades.Dock = DockStyle.Fill;
        dgvTrades.Location = new Point(3, 19);
        dgvTrades.Name = "dgvTrades";
        dgvTrades.RowHeadersVisible = false;
        dgvTrades.RowTemplate.Height = 22;
        dgvTrades.Size = new Size(1054, 68);
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
        colTradePnLPercent.DefaultCellStyle.Font = new Font(dgvTrades.Font, FontStyle.Bold);
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
        // colTradePnLMin
        //
        colTradePnLMin.HeaderText = "Min PnL%";
        colTradePnLMin.Name = "colTradePnLMin";
        colTradePnLMin.ReadOnly = true;
        //
        // colTradePnLMax
        //
        colTradePnLMax.HeaderText = "Max PnL%";
        colTradePnLMax.Name = "colTradePnLMax";
        colTradePnLMax.ReadOnly = true;
        //
        // colTradeMoneyness
        //
        colTradeMoneyness.HeaderText = "OTM/ITM";
        colTradeMoneyness.Name = "colTradeMoneyness";
        colTradeMoneyness.ReadOnly = true;
        //
        // grpOptionsChain
        //
        grpOptionsChain.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        grpOptionsChain.Controls.Add(dgvQuotes);
        grpOptionsChain.Controls.Add(lblCallHeader);
        grpOptionsChain.Controls.Add(lblPutHeader);
        grpOptionsChain.Controls.Add(chkCallFilter);
        grpOptionsChain.Controls.Add(chkPutFilter);
        grpOptionsChain.Controls.Add(lblExpDate);
        grpOptionsChain.Location = new Point(8, 100);
        grpOptionsChain.Name = "grpOptionsChain";
        grpOptionsChain.Size = new Size(572, 200);
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
        dgvQuotes.Size = new Size(566, 175);
        dgvQuotes.TabIndex = 0;
        dgvQuotes.CellClick += DgvQuotes_CellClick;
        dgvQuotes.CellFormatting += DgvQuotes_CellFormatting;
        dgvQuotes.CellPainting += DgvQuotes_CellPainting;
        // 
        // colSymbolQ
        // 
        colSymbolQ.HeaderText = "Symbol";
        colSymbolQ.Name = "colSymbolQ";
        colSymbolQ.ReadOnly = true;
        colSymbolQ.Width = 57;
        // 
        // colRange
        // 
        colRange.HeaderText = "Range";
        colRange.Name = "colRange";
        colRange.ReadOnly = true;
        colRange.Width = 62;
        // 
        // colCallSprd
        // 
        colCallSprd.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colCallSprd.HeaderText = "Sprd";
        colCallSprd.Name = "colCallSprd";
        colCallSprd.ReadOnly = true;
        colCallSprd.Width = 33;
        // 
        // colCallBid
        // 
        colCallBid.HeaderText = "Bid";
        colCallBid.Name = "colCallBid";
        colCallBid.ReadOnly = true;
        colCallBid.Width = 33;
        // 
        // colCallAsk
        // 
        colCallAsk.HeaderText = "Ask";
        colCallAsk.Name = "colCallAsk";
        colCallAsk.ReadOnly = true;
        colCallAsk.Width = 33;
        // 
        // colStrikePrice
        // 
        colStrikePrice.FlatStyle = FlatStyle.Flat;
        colSpotPrice.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colSpotPrice.HeaderText = "Spot";
        colSpotPrice.Name = "colSpotPrice";
        colSpotPrice.ReadOnly = true;
        colSpotPrice.Width = 50;
        //
        colStrikePrice.HeaderText = "Strike";
        colStrikePrice.Name = "colStrikePrice";
        colStrikePrice.Width = 60;
        colStrikePrice.FlatStyle = FlatStyle.Standard;
        colStrikePrice.UseColumnTextForButtonValue = false;
        // 
        // colPutBid
        // 
        colPutBid.HeaderText = "Bid";
        colPutBid.Name = "colPutBid";
        colPutBid.ReadOnly = true;
        colPutBid.Width = 33;
        // 
        // colPutAsk
        // 
        colPutAsk.HeaderText = "Ask";
        colPutAsk.Name = "colPutAsk";
        colPutAsk.ReadOnly = true;
        colPutAsk.Width = 33;
        // 
        // colPutSprd
        // 
        colPutSprd.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colPutSprd.HeaderText = "Sprd";
        colPutSprd.Name = "colPutSprd";
        colPutSprd.ReadOnly = true;
        colPutSprd.Width = 33;
        // 
        // colContracts
        // 
        colContracts.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colContracts.HeaderText = "Conts";
        colContracts.Name = "colContracts";
        colContracts.ReadOnly = true;
        colContracts.Width = 48;
        // 
        // colLevel
        // 
        colLevel.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colLevel.HeaderText = "Level";
        colLevel.Name = "colLevel";
        colLevel.ReadOnly = true;
        colLevel.Width = 45;
        // 
        // lblCallHeader
        // 
        lblCallHeader.AutoSize = true;
        lblCallHeader.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
        lblCallHeader.ForeColor = Color.Green;
        lblCallHeader.Location = new Point(124, 2);
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
        lblPutHeader.Location = new Point(403, 2);
        lblPutHeader.Name = "lblPutHeader";
        lblPutHeader.Size = new Size(34, 15);
        lblPutHeader.TabIndex = 2;
        lblPutHeader.Text = "PUT";
        //
        // chkCallFilter
        //
        chkCallFilter.AutoSize = true;
        chkCallFilter.Location = new Point(106, 3);
        chkCallFilter.Name = "chkCallFilter";
        chkCallFilter.Size = new Size(15, 14);
        chkCallFilter.TabIndex = 9;
        chkCallFilter.CheckedChanged += CallPutFilter_CheckedChanged;
        //
        // chkPutFilter
        //
        chkPutFilter.AutoSize = true;
        chkPutFilter.Location = new Point(385, 3);
        chkPutFilter.Name = "chkPutFilter";
        chkPutFilter.Size = new Size(15, 14);
        chkPutFilter.TabIndex = 10;
        chkPutFilter.CheckedChanged += CallPutFilter_CheckedChanged;
        //
        // lblExpDate
        //
        lblExpDate.AutoSize = true;
        lblExpDate.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        lblExpDate.ForeColor = Color.DarkGoldenrod;
        lblExpDate.Location = new Point(208, 3);
        lblExpDate.Name = "lblExpDate";
        lblExpDate.Size = new Size(0, 13);
        lblExpDate.TabIndex = 4;
        //
        // grpOptionsChainNext
        //
        grpOptionsChainNext.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        grpOptionsChainNext.Controls.Add(dgvQuotesNext);
        grpOptionsChainNext.Controls.Add(lblCallHeaderNext);
        grpOptionsChainNext.Controls.Add(lblPutHeaderNext);
        grpOptionsChainNext.Controls.Add(lblExpDateNext);
        grpOptionsChainNext.Location = new Point(586, 100);
        grpOptionsChainNext.Name = "grpOptionsChainNext";
        grpOptionsChainNext.Size = new Size(572, 200);
        grpOptionsChainNext.TabIndex = 7;
        grpOptionsChainNext.TabStop = false;
        grpOptionsChainNext.Text = "OptionsChain (Next ExpDate)";
        //
        // dgvQuotesNext
        //
        dgvQuotesNext.AllowUserToAddRows = false;
        dgvQuotesNext.AllowUserToDeleteRows = false;
        dgvQuotesNext.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        dgvQuotesNext.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgvQuotesNext.Columns.AddRange(new DataGridViewColumn[] { colSymbolQNext, colRangeNext, colCallSprdNext, colCallBidNext, colCallAskNext, colSpotPriceNext, colStrikePriceNext, colPutBidNext, colPutAskNext, colPutSprdNext, colContractsNext, colLevelNext });
        dgvQuotesNext.Location = new Point(3, 20);
        dgvQuotesNext.Name = "dgvQuotesNext";
        dgvQuotesNext.ReadOnly = true;
        dgvQuotesNext.RowHeadersVisible = false;
        dgvQuotesNext.RowTemplate.Height = 22;
        dgvQuotesNext.Size = new Size(566, 175);
        dgvQuotesNext.TabIndex = 0;
        dgvQuotesNext.CellFormatting += DgvQuotesNext_CellFormatting;
        dgvQuotesNext.CellPainting += DgvQuotesNext_CellPainting;
        //
        // colSymbolQNext
        //
        colSymbolQNext.HeaderText = "Symbol";
        colSymbolQNext.Name = "colSymbolQNext";
        colSymbolQNext.ReadOnly = true;
        colSymbolQNext.Width = 57;
        //
        // colRangeNext
        //
        colRangeNext.HeaderText = "Range";
        colRangeNext.Name = "colRangeNext";
        colRangeNext.ReadOnly = true;
        colRangeNext.Width = 62;
        //
        // colCallSprdNext
        //
        colCallSprdNext.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colCallSprdNext.HeaderText = "Sprd";
        colCallSprdNext.Name = "colCallSprdNext";
        colCallSprdNext.ReadOnly = true;
        colCallSprdNext.Width = 33;
        //
        // colCallBidNext
        //
        colCallBidNext.HeaderText = "Bid";
        colCallBidNext.Name = "colCallBidNext";
        colCallBidNext.ReadOnly = true;
        colCallBidNext.Width = 33;
        //
        // colCallAskNext
        //
        colCallAskNext.HeaderText = "Ask";
        colCallAskNext.Name = "colCallAskNext";
        colCallAskNext.ReadOnly = true;
        colCallAskNext.Width = 33;
        //
        // colSpotPriceNext
        //
        colSpotPriceNext.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colSpotPriceNext.HeaderText = "Spot";
        colSpotPriceNext.Name = "colSpotPriceNext";
        colSpotPriceNext.ReadOnly = true;
        colSpotPriceNext.Width = 50;
        //
        // colStrikePriceNext
        //
        colStrikePriceNext.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colStrikePriceNext.HeaderText = "Strike";
        colStrikePriceNext.Name = "colStrikePriceNext";
        colStrikePriceNext.ReadOnly = true;
        colStrikePriceNext.Width = 60;
        //
        // colPutBidNext
        //
        colPutBidNext.HeaderText = "Bid";
        colPutBidNext.Name = "colPutBidNext";
        colPutBidNext.ReadOnly = true;
        colPutBidNext.Width = 33;
        //
        // colPutAskNext
        //
        colPutAskNext.HeaderText = "Ask";
        colPutAskNext.Name = "colPutAskNext";
        colPutAskNext.ReadOnly = true;
        colPutAskNext.Width = 33;
        //
        // colPutSprdNext
        //
        colPutSprdNext.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colPutSprdNext.HeaderText = "Sprd";
        colPutSprdNext.Name = "colPutSprdNext";
        colPutSprdNext.ReadOnly = true;
        colPutSprdNext.Width = 33;
        //
        // colContractsNext
        //
        colContractsNext.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colContractsNext.HeaderText = "Conts";
        colContractsNext.Name = "colContractsNext";
        colContractsNext.ReadOnly = true;
        colContractsNext.Width = 48;
        //
        // colLevelNext
        //
        colLevelNext.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colLevelNext.HeaderText = "Level";
        colLevelNext.Name = "colLevelNext";
        colLevelNext.ReadOnly = true;
        colLevelNext.Width = 45;
        //
        // lblCallHeaderNext
        //
        lblCallHeaderNext.AutoSize = true;
        lblCallHeaderNext.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
        lblCallHeaderNext.ForeColor = Color.Green;
        lblCallHeaderNext.Location = new Point(124, 2);
        lblCallHeaderNext.Name = "lblCallHeaderNext";
        lblCallHeaderNext.Size = new Size(40, 15);
        lblCallHeaderNext.TabIndex = 1;
        lblCallHeaderNext.Text = "CALL";
        //
        // lblPutHeaderNext
        //
        lblPutHeaderNext.AutoSize = true;
        lblPutHeaderNext.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
        lblPutHeaderNext.ForeColor = Color.Red;
        lblPutHeaderNext.Location = new Point(403, 2);
        lblPutHeaderNext.Name = "lblPutHeaderNext";
        lblPutHeaderNext.Size = new Size(34, 15);
        lblPutHeaderNext.TabIndex = 2;
        lblPutHeaderNext.Text = "PUT";
        //
        // lblExpDateNext
        //
        lblExpDateNext.AutoSize = true;
        lblExpDateNext.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        lblExpDateNext.ForeColor = Color.DarkGoldenrod;
        lblExpDateNext.Location = new Point(208, 3);
        lblExpDateNext.Name = "lblExpDateNext";
        lblExpDateNext.Size = new Size(0, 13);
        lblExpDateNext.TabIndex = 8;
        //
        // chkStopAt11
        // 
        chkStopAt11.AutoSize = true;
        chkStopAt11.Location = new Point(420, 84);
        chkStopAt11.Name = "chkStopAt11";
        chkStopAt11.Size = new Size(115, 19);
        chkStopAt11.TabIndex = 3;
        chkStopAt11.Text = "Stop at 11:00 AM";
        chkStopAt11.Visible = false;
        //
        // chkSaveToCsv
        //
        chkSaveToCsv.AutoSize = true;
        chkSaveToCsv.Checked = true;
        chkSaveToCsv.CheckState = CheckState.Checked;
        chkSaveToCsv.Location = new Point(800, 142);
        chkSaveToCsv.Size = new Size(110, 19);
        chkSaveToCsv.Name = "chkSaveToCsv";
        chkSaveToCsv.Text = "Quotes to CSV";
        //
        // chkHideNextExpDate
        //
        chkHideNextExpDate.AutoSize = true;
        chkHideNextExpDate.Checked = true;
        chkHideNextExpDate.CheckState = CheckState.Checked;
        chkHideNextExpDate.Location = new Point(800, 166);
        chkHideNextExpDate.Name = "chkHideNextExpDate";
        chkHideNextExpDate.Size = new Size(115, 19);
        chkHideNextExpDate.TabIndex = 4;
        chkHideNextExpDate.Text = "Hide Next ExpDate";
        chkHideNextExpDate.CheckedChanged += ChkHideNextExpDate_CheckedChanged;
        //
        // btnFetchQuotes
        // 
        btnFetchQuotes.Location = new Point(420, 57);
        btnFetchQuotes.Name = "btnFetchQuotes";
        btnFetchQuotes.Size = new Size(95, 25);
        btnFetchQuotes.TabIndex = 1;
        btnFetchQuotes.Text = "Fetch Quotes";
        btnFetchQuotes.Click += BtnFetchQuotes_Click;
        //
        // btnLiveChart
        //
        btnLiveChart.Location = new Point(1020, 4);
        btnLiveChart.Name = "btnLiveChart";
        btnLiveChart.Size = new Size(95, 25);
        btnLiveChart.TabIndex = 2;
        btnLiveChart.Text = "Live Chart";
        btnLiveChart.Click += BtnLiveChart_Click;
        //
        // btnFourEtfCharts
        //
        btnFourEtfCharts.Location = new Point(1020, 34);
        btnFourEtfCharts.Name = "btnFourEtfCharts";
        btnFourEtfCharts.Size = new Size(120, 25);
        btnFourEtfCharts.TabIndex = 3;
        btnFourEtfCharts.Text = "Block Mov";
        btnFourEtfCharts.Click += BtnFourEtfCharts_Click;
        //
        // btnHubHost
        //
        btnHubHost.Location = new Point(1020, 64);
        btnHubHost.Name = "btnHubHost";
        btnHubHost.Size = new Size(90, 25);
        btnHubHost.TabIndex = 4;
        btnHubHost.Text = "Hub Host";
        btnHubHost.Click += BtnHubHost_Click;
        //
        // btnSimulator
        //
        btnSimulator.Location = new Point(1020, 94);
        btnSimulator.Name = "btnSimulator";
        btnSimulator.Size = new Size(90, 25);
        btnSimulator.TabIndex = 5;
        btnSimulator.Text = "Simulador";
        btnSimulator.Click += BtnSimulator_Click;
        //
        // btnStartPolling
        //
        btnStartPolling.BackColor = Color.DarkGreen;
        btnStartPolling.FlatStyle = FlatStyle.Flat;
        btnStartPolling.ForeColor = Color.White;
        btnStartPolling.Location = new Point(420, 7);
        btnStartPolling.Name = "btnStartPolling";
        btnStartPolling.Size = new Size(95, 25);
        btnStartPolling.TabIndex = 3;
        btnStartPolling.Text = "Start Polling";
        btnStartPolling.UseVisualStyleBackColor = false;
        btnStartPolling.Click += BtnStartPolling_Click;
        //
        // lblLastUpdate
        // 
        lblLastUpdate.AutoSize = true;
        lblLastUpdate.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        lblLastUpdate.ForeColor = Color.DarkGoldenrod;
        lblLastUpdate.Location = new Point(435, 39);
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
        grpTickerButtons.Size = new Size(285, 96);
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
        flpTickers.Size = new Size(279, 73);
        flpTickers.TabIndex = 0;
        // 
        // grpTrade
        // 
        grpTrade.Controls.Add(rbNoTrade);
        grpTrade.Controls.Add(rbNoTradeTarget);
        grpTrade.Controls.Add(rbTrade);
        grpTrade.Controls.Add(rbTradeTarget);
        grpTrade.Location = new Point(740, 4);
        grpTrade.Name = "grpTrade";
        grpTrade.Size = new Size(155, 116);
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
        // rbNoTradeTarget
        //
        rbNoTradeTarget.AutoSize = false;
        rbNoTradeTarget.Location = new Point(12, 44);
        rbNoTradeTarget.Name = "rbNoTradeTarget";
        rbNoTradeTarget.Size = new Size(130, 20);
        rbNoTradeTarget.TabIndex = 1;
        rbNoTradeTarget.Text = "No Trade-Target";
        rbNoTradeTarget.CheckedChanged += TradeRadioButton_CheckedChanged;
        //
        // rbTrade
        //
        rbTrade.Location = new Point(12, 66);
        rbTrade.Name = "rbTrade";
        rbTrade.Size = new Size(80, 20);
        rbTrade.TabIndex = 2;
        rbTrade.Text = "Trade";
        rbTrade.CheckedChanged += TradeRadioButton_CheckedChanged;
        //
        // rbTradeTarget
        //
        rbTradeTarget.AutoSize = true;
        rbTradeTarget.Location = new Point(12, 88);
        rbTradeTarget.Name = "rbTradeTarget";
        rbTradeTarget.TabIndex = 3;
        rbTradeTarget.Text = "Trade-Target";
        rbTradeTarget.CheckedChanged += TradeRadioButton_CheckedChanged;
        //
        // grpContracts
        //
        grpContracts.Controls.Add(rbContracts1);
        grpContracts.Controls.Add(rbContracts2);
        grpContracts.Controls.Add(rbContracts3);
        grpContracts.Controls.Add(rbContracts4);
        grpContracts.Controls.Add(rbContracts5);
        grpContracts.Controls.Add(rbContracts6);
        grpContracts.Controls.Add(rbContractsPositionSize);
        grpContracts.Location = new Point(905, 4);
        grpContracts.Name = "grpContracts";
        grpContracts.Size = new Size(105, 96);
        grpContracts.TabIndex = 8;
        grpContracts.TabStop = false;
        grpContracts.Text = "Contracts";
        //
        // rbContracts1
        //
        rbContracts1.Checked = true;
        rbContracts1.Location = new Point(6, 20);
        rbContracts1.Name = "rbContracts1";
        rbContracts1.Size = new Size(28, 18);
        rbContracts1.TabIndex = 0;
        rbContracts1.TabStop = true;
        rbContracts1.Text = "1";
        rbContracts1.CheckedChanged += ContractsRadioButton_CheckedChanged;
        //
        // rbContracts2
        //
        rbContracts2.Location = new Point(36, 20);
        rbContracts2.Name = "rbContracts2";
        rbContracts2.Size = new Size(28, 18);
        rbContracts2.TabIndex = 1;
        rbContracts2.Text = "2";
        rbContracts2.CheckedChanged += ContractsRadioButton_CheckedChanged;
        //
        // rbContracts3
        //
        rbContracts3.Location = new Point(66, 20);
        rbContracts3.Name = "rbContracts3";
        rbContracts3.Size = new Size(28, 18);
        rbContracts3.TabIndex = 2;
        rbContracts3.Text = "3";
        rbContracts3.CheckedChanged += ContractsRadioButton_CheckedChanged;
        //
        // rbContracts4
        //
        rbContracts4.Location = new Point(6, 42);
        rbContracts4.Name = "rbContracts4";
        rbContracts4.Size = new Size(28, 18);
        rbContracts4.TabIndex = 3;
        rbContracts4.Text = "4";
        rbContracts4.CheckedChanged += ContractsRadioButton_CheckedChanged;
        //
        // rbContracts5
        //
        rbContracts5.Location = new Point(36, 42);
        rbContracts5.Name = "rbContracts5";
        rbContracts5.Size = new Size(28, 18);
        rbContracts5.TabIndex = 4;
        rbContracts5.Text = "5";
        rbContracts5.CheckedChanged += ContractsRadioButton_CheckedChanged;
        //
        // rbContracts6
        //
        rbContracts6.Location = new Point(66, 42);
        rbContracts6.Name = "rbContracts6";
        rbContracts6.Size = new Size(28, 18);
        rbContracts6.TabIndex = 5;
        rbContracts6.Text = "6";
        rbContracts6.CheckedChanged += ContractsRadioButton_CheckedChanged;
        //
        // rbContractsPositionSize
        //
        rbContractsPositionSize.Location = new Point(6, 64);
        rbContractsPositionSize.Name = "rbContractsPositionSize";
        rbContractsPositionSize.Size = new Size(93, 18);
        rbContractsPositionSize.TabIndex = 6;
        rbContractsPositionSize.Text = "PositionSize";
        rbContractsPositionSize.CheckedChanged += ContractsRadioButton_CheckedChanged;
        //
        // grpCounts
        //
        grpCounts.Controls.Add(rbCounts3);
        grpCounts.Controls.Add(rbCounts4);
        grpCounts.Controls.Add(rbCounts5);
        grpCounts.Controls.Add(rbCounts6);
        grpCounts.Controls.Add(rbCounts7);
        grpCounts.Controls.Add(rbCounts8);
        grpCounts.Controls.Add(rbCounts9);
        grpCounts.Controls.Add(rbCounts10);
        grpCounts.Controls.Add(rbCountsInRange);
        grpCounts.Location = new Point(545, 4);
        grpCounts.Name = "grpCounts";
        grpCounts.Size = new Size(185, 96);
        grpCounts.TabIndex = 9;
        grpCounts.TabStop = false;
        grpCounts.Text = "Counts";
        //
        // rbCounts3
        //
        rbCounts3.Location = new Point(6, 20);
        rbCounts3.Name = "rbCounts3";
        rbCounts3.Size = new Size(36, 18);
        rbCounts3.TabIndex = 0;
        rbCounts3.Text = "3";
        rbCounts3.CheckedChanged += CountsRadioButton_CheckedChanged;
        //
        // rbCounts4
        //
        rbCounts4.Location = new Point(48, 20);
        rbCounts4.Name = "rbCounts4";
        rbCounts4.Size = new Size(36, 18);
        rbCounts4.TabIndex = 1;
        rbCounts4.Text = "4";
        rbCounts4.CheckedChanged += CountsRadioButton_CheckedChanged;
        //
        // rbCounts5
        //
        rbCounts5.Location = new Point(90, 20);
        rbCounts5.Name = "rbCounts5";
        rbCounts5.Size = new Size(36, 18);
        rbCounts5.TabIndex = 2;
        rbCounts5.Text = "5";
        rbCounts5.CheckedChanged += CountsRadioButton_CheckedChanged;
        //
        // rbCounts6
        //
        rbCounts6.Checked = true;
        rbCounts6.Location = new Point(132, 20);
        rbCounts6.Name = "rbCounts6";
        rbCounts6.Size = new Size(36, 18);
        rbCounts6.TabIndex = 3;
        rbCounts6.TabStop = true;
        rbCounts6.Text = "6";
        rbCounts6.CheckedChanged += CountsRadioButton_CheckedChanged;
        //
        // rbCounts7
        //
        rbCounts7.Location = new Point(6, 42);
        rbCounts7.Name = "rbCounts7";
        rbCounts7.Size = new Size(36, 18);
        rbCounts7.TabIndex = 4;
        rbCounts7.Text = "7";
        rbCounts7.CheckedChanged += CountsRadioButton_CheckedChanged;
        //
        // rbCounts8
        //
        rbCounts8.Location = new Point(48, 42);
        rbCounts8.Name = "rbCounts8";
        rbCounts8.Size = new Size(36, 18);
        rbCounts8.TabIndex = 5;
        rbCounts8.Text = "8";
        rbCounts8.CheckedChanged += CountsRadioButton_CheckedChanged;
        //
        // rbCounts9
        //
        rbCounts9.Location = new Point(90, 42);
        rbCounts9.Name = "rbCounts9";
        rbCounts9.Size = new Size(42, 18);
        rbCounts9.TabIndex = 6;
        rbCounts9.Text = "9";
        rbCounts9.CheckedChanged += CountsRadioButton_CheckedChanged;
        //
        // rbCounts10
        //
        rbCounts10.Location = new Point(132, 42);
        rbCounts10.Name = "rbCounts10";
        rbCounts10.Size = new Size(42, 18);
        rbCounts10.TabIndex = 7;
        rbCounts10.Text = "10";
        rbCounts10.CheckedChanged += CountsRadioButton_CheckedChanged;
        //
        // rbCountsInRange
        //
        rbCountsInRange.Location = new Point(6, 64);
        rbCountsInRange.Name = "rbCountsInRange";
        rbCountsInRange.Size = new Size(90, 18);
        rbCountsInRange.TabIndex = 8;
        rbCountsInRange.TabStop = true;
        rbCountsInRange.Text = "In Range";
        rbCountsInRange.CheckedChanged += CountsRadioButton_CheckedChanged;
        //
        // tabSettings
        // 
        tabSettings.Controls.Add(grpBroker);
        tabSettings.Controls.Add(grpTickers);
        tabSettings.Controls.Add(grpScreenCoords);
        tabSettings.Controls.Add(grpPositionSize);
        tabSettings.Controls.Add(chkSaveDumps);
        tabSettings.Controls.Add(chkSaveToCsv);
        tabSettings.Controls.Add(chkHideNextExpDate);
        tabSettings.Controls.Add(chkShowOrderConfirmation);
        tabSettings.Controls.Add(grpTarget);
        tabSettings.Controls.Add(grpSchwabCredentials);
        tabSettings.Controls.Add(grpRefreshToken);
        tabSettings.Controls.Add(grpAccounts);
        tabSettings.Controls.Add(grpAwsSettings);
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
        rbIBKR.Enabled = false;
        rbIBKR.Location = new Point(12, 46);
        rbIBKR.Name = "rbIBKR";
        rbIBKR.Size = new Size(135, 20);
        rbIBKR.TabIndex = 1;
        rbIBKR.Text = "Interactive Broker";
        rbIBKR.CheckedChanged += BrokerRadioButton_CheckedChanged;
        //
        // rbETrade
        //
        rbETrade.Enabled = false;
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
        grpTickers.Size = new Size(340, 210);
        grpTickers.TabIndex = 1;
        grpTickers.TabStop = false;
        grpTickers.Text = "Tickers";
        //
        // grpScreenCoords
        //
        grpScreenCoords.Controls.Add(btnSaveCoords);
        grpScreenCoords.Controls.Add(btnResetCoords);
        grpScreenCoords.Controls.Add(pnlCoordsRows);
        grpScreenCoords.Location = new Point(525, 8);
        grpScreenCoords.Name = "grpScreenCoords";
        grpScreenCoords.Size = new Size(265, 200);
        grpScreenCoords.TabIndex = 6;
        grpScreenCoords.TabStop = false;
        grpScreenCoords.Text = "Screen Coordinates";
        // pnlCoordsRows — one row (button + 2 coord textboxes) generated per Ticker symbol at
        // runtime by LoadCoordsButtons(); AutoScroll so it never overflows the GroupBox if more
        // than ~4 tickers are configured.
        pnlCoordsRows.Location = new Point(5, 22);
        pnlCoordsRows.Name = "pnlCoordsRows";
        pnlCoordsRows.Size = new Size(255, 128);
        pnlCoordsRows.AutoScroll = true;
        pnlCoordsRows.TabIndex = 0;
        // btnSaveCoords
        btnSaveCoords.Location = new Point(78, 155);
        btnSaveCoords.Name = "btnSaveCoords";
        btnSaveCoords.Size = new Size(80, 23);
        btnSaveCoords.TabIndex = 12;
        btnSaveCoords.Text = "Save";
        btnSaveCoords.Click += BtnSaveCoords_Click;
        // btnResetCoords
        btnResetCoords.Location = new Point(166, 155);
        btnResetCoords.Name = "btnResetCoords";
        btnResetCoords.Size = new Size(80, 23);
        btnResetCoords.TabIndex = 13;
        btnResetCoords.Text = "Reset";
        btnResetCoords.Click += BtnResetCoords_Click;
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
        dgvTickers.Size = new Size(318, 154);
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
        btnSaveTickers.Location = new Point(250, 180);
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
        grpPositionSize.Location = new Point(800, 8);
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
        // chkSaveDumps
        //
        chkSaveDumps.Location = new Point(800, 118);
        chkSaveDumps.Name = "chkSaveDumps";
        chkSaveDumps.Size = new Size(110, 20);
        chkSaveDumps.TabIndex = 3;
        chkSaveDumps.Text = "Save Dumps";
        chkSaveDumps.CheckedChanged += ChkSaveDumps_CheckedChanged;
        //
        // chkShowOrderConfirmation
        //
        chkShowOrderConfirmation.AutoSize = true;
        chkShowOrderConfirmation.Checked = false;
        chkShowOrderConfirmation.CheckState = CheckState.Unchecked;
        chkShowOrderConfirmation.Location = new Point(800, 190);
        chkShowOrderConfirmation.Name = "chkShowOrderConfirmation";
        chkShowOrderConfirmation.Size = new Size(160, 19);
        chkShowOrderConfirmation.TabIndex = 5;
        chkShowOrderConfirmation.Text = "Show Order Confirmation";
        chkShowOrderConfirmation.CheckedChanged += ChkShowOrderConfirmation_CheckedChanged;
        //
        // grpTarget
        // 
        grpTarget.Controls.Add(rbTarget10);
        grpTarget.Controls.Add(rbTarget35);
        grpTarget.Controls.Add(rbTarget100);
        grpTarget.Location = new Point(918, 8);
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
        // grpRefreshToken
        //
        grpRefreshToken.Controls.Add(btnLogin);
        grpRefreshToken.Controls.Add(lblResponseHint);
        grpRefreshToken.Controls.Add(txtResponse);
        grpRefreshToken.Controls.Add(lblTokenStatus);
        grpRefreshToken.Location = new Point(525, 215);
        grpRefreshToken.Name = "grpRefreshToken";
        grpRefreshToken.Size = new Size(265, 120);
        grpRefreshToken.TabIndex = 7;
        grpRefreshToken.TabStop = false;
        grpRefreshToken.Text = "Refresh Token";
        //
        // btnLogin
        //
        btnLogin.Location = new Point(10, 22);
        btnLogin.Name = "btnLogin";
        btnLogin.Size = new Size(75, 25);
        btnLogin.TabIndex = 0;
        btnLogin.Text = "Login";
        btnLogin.Click += BtnLogin_Click;
        //
        // lblResponseHint
        //
        lblResponseHint.Location = new Point(10, 55);
        lblResponseHint.Name = "lblResponseHint";
        lblResponseHint.Size = new Size(245, 15);
        lblResponseHint.Text = "Response:";
        lblResponseHint.Font = new Font("Segoe UI", 8F);
        //
        // txtResponse
        //
        txtResponse.Location = new Point(10, 70);
        txtResponse.Name = "txtResponse";
        txtResponse.Size = new Size(245, 23);
        txtResponse.TabIndex = 1;
        txtResponse.KeyDown += TxtResponse_KeyDown;
        //
        // lblTokenStatus
        //
        lblTokenStatus.AutoSize = true;
        lblTokenStatus.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        lblTokenStatus.ForeColor = Color.Green;
        lblTokenStatus.Location = new Point(10, 98);
        lblTokenStatus.Name = "lblTokenStatus";
        lblTokenStatus.TabIndex = 2;
        lblTokenStatus.Text = string.Empty;
        //
        // grpAccounts
        //
        grpAccounts.Controls.Add(dgvAccounts);
        grpAccounts.Controls.Add(btnRefreshAccounts);
        grpAccounts.Location = new Point(800, 215);
        grpAccounts.Name = "grpAccounts";
        grpAccounts.Size = new Size(310, 165);
        grpAccounts.TabIndex = 8;
        grpAccounts.TabStop = false;
        grpAccounts.Text = "Broker Accounts (Schwab)";
        //
        // dgvAccounts
        //
        dgvAccounts.AllowUserToAddRows = false;
        dgvAccounts.AllowUserToDeleteRows = false;
        dgvAccounts.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgvAccounts.Columns.AddRange(new DataGridViewColumn[] { colAccountDefault, colAccountNumber, colAccountHash });
        dgvAccounts.Location = new Point(10, 22);
        dgvAccounts.Name = "dgvAccounts";
        dgvAccounts.RowHeadersVisible = false;
        dgvAccounts.RowTemplate.Height = 22;
        dgvAccounts.Size = new Size(290, 100);
        dgvAccounts.TabIndex = 0;
        dgvAccounts.CellContentClick += DgvAccounts_CellContentClick;
        //
        // colAccountDefault
        //
        colAccountDefault.HeaderText = "Default";
        colAccountDefault.Name = "colAccountDefault";
        colAccountDefault.Width = 55;
        //
        // colAccountNumber
        //
        colAccountNumber.HeaderText = "Account";
        colAccountNumber.Name = "colAccountNumber";
        colAccountNumber.ReadOnly = true;
        colAccountNumber.Width = 215;
        //
        // colAccountHash
        //
        colAccountHash.HeaderText = "Hash";
        colAccountHash.Name = "colAccountHash";
        colAccountHash.ReadOnly = true;
        colAccountHash.Visible = false;
        //
        // btnRefreshAccounts
        //
        btnRefreshAccounts.Location = new Point(10, 128);
        btnRefreshAccounts.Name = "btnRefreshAccounts";
        btnRefreshAccounts.Size = new Size(130, 25);
        btnRefreshAccounts.TabIndex = 1;
        btnRefreshAccounts.Text = "Refresh Accounts";
        btnRefreshAccounts.Click += BtnRefreshAccounts_Click;
        //
        // grpAwsSettings
        //
        grpAwsSettings.Controls.Add(lblAwsAccessKey);
        grpAwsSettings.Controls.Add(txtAwsAccessKey);
        grpAwsSettings.Controls.Add(lblAwsSecretKey);
        grpAwsSettings.Controls.Add(txtAwsSecretKey);
        grpAwsSettings.Controls.Add(lblAwsBucket);
        grpAwsSettings.Controls.Add(txtAwsBucket);
        grpAwsSettings.Controls.Add(lblAwsRegion);
        grpAwsSettings.Controls.Add(txtAwsRegion);
        grpAwsSettings.Controls.Add(btnSaveAwsSettings);
        grpAwsSettings.Controls.Add(lblAwsSaved);
        grpAwsSettings.Location = new Point(8, 315);
        grpAwsSettings.Name = "grpAwsSettings";
        grpAwsSettings.Size = new Size(507, 155);
        grpAwsSettings.TabIndex = 5;
        grpAwsSettings.TabStop = false;
        grpAwsSettings.Text = "AWS S3 Settings";
        //
        // lblAwsAccessKey
        //
        lblAwsAccessKey.Location = new Point(12, 24);
        lblAwsAccessKey.Name = "lblAwsAccessKey";
        lblAwsAccessKey.Size = new Size(75, 20);
        lblAwsAccessKey.TabIndex = 0;
        lblAwsAccessKey.Text = "Access Key:";
        //
        // txtAwsAccessKey
        //
        txtAwsAccessKey.Location = new Point(90, 22);
        txtAwsAccessKey.Name = "txtAwsAccessKey";
        txtAwsAccessKey.Size = new Size(400, 23);
        txtAwsAccessKey.TabIndex = 1;
        //
        // lblAwsSecretKey
        //
        lblAwsSecretKey.Location = new Point(12, 54);
        lblAwsSecretKey.Name = "lblAwsSecretKey";
        lblAwsSecretKey.Size = new Size(75, 20);
        lblAwsSecretKey.TabIndex = 2;
        lblAwsSecretKey.Text = "Secret Key:";
        //
        // txtAwsSecretKey
        //
        txtAwsSecretKey.Location = new Point(90, 52);
        txtAwsSecretKey.Name = "txtAwsSecretKey";
        txtAwsSecretKey.Size = new Size(400, 23);
        txtAwsSecretKey.TabIndex = 3;
        txtAwsSecretKey.UseSystemPasswordChar = true;
        //
        // lblAwsBucket
        //
        lblAwsBucket.Location = new Point(12, 84);
        lblAwsBucket.Name = "lblAwsBucket";
        lblAwsBucket.Size = new Size(75, 20);
        lblAwsBucket.TabIndex = 4;
        lblAwsBucket.Text = "Bucket:";
        //
        // txtAwsBucket
        //
        txtAwsBucket.Location = new Point(90, 82);
        txtAwsBucket.Name = "txtAwsBucket";
        txtAwsBucket.Size = new Size(190, 23);
        txtAwsBucket.TabIndex = 5;
        //
        // lblAwsRegion
        //
        lblAwsRegion.Location = new Point(290, 84);
        lblAwsRegion.Name = "lblAwsRegion";
        lblAwsRegion.Size = new Size(50, 20);
        lblAwsRegion.TabIndex = 6;
        lblAwsRegion.Text = "Region:";
        //
        // txtAwsRegion
        //
        txtAwsRegion.Location = new Point(345, 82);
        txtAwsRegion.Name = "txtAwsRegion";
        txtAwsRegion.Size = new Size(145, 23);
        txtAwsRegion.TabIndex = 7;
        //
        // btnSaveAwsSettings
        //
        btnSaveAwsSettings.Location = new Point(415, 115);
        btnSaveAwsSettings.Name = "btnSaveAwsSettings";
        btnSaveAwsSettings.Size = new Size(80, 23);
        btnSaveAwsSettings.TabIndex = 8;
        btnSaveAwsSettings.Text = "Save";
        btnSaveAwsSettings.Click += BtnSaveAwsSettings_Click;
        //
        // lblAwsSaved
        //
        lblAwsSaved.AutoSize = true;
        lblAwsSaved.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        lblAwsSaved.ForeColor = Color.Green;
        lblAwsSaved.Location = new Point(12, 119);
        lblAwsSaved.Name = "lblAwsSaved";
        lblAwsSaved.Size = new Size(110, 13);
        lblAwsSaved.TabIndex = 9;
        lblAwsSaved.Text = "Settings Saved";
        lblAwsSaved.Visible = false;
        //
        // statusStrip1
        //
        statusStrip1.Items.AddRange(new ToolStripItem[] { lblStatusUser });
        statusStrip1.Location = new Point(0, 578);
        statusStrip1.Name = "statusStrip1";
        statusStrip1.Size = new Size(1181, 22);
        statusStrip1.TabIndex = 1;
        //
        // lblStatusUser
        //
        lblStatusUser.Name = "lblStatusUser";
        lblStatusUser.Size = new Size(0, 17);
        //
        // Form1
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1181, 600);
        Controls.Add(statusStrip1);
        Controls.Add(tabControl);
        Name = "Form1";
        Text = "Options Trader";
        statusStrip1.ResumeLayout(false);
        statusStrip1.PerformLayout();
        tabControl.ResumeLayout(false);
        tabQuotes.ResumeLayout(false);
        tabQuotes.PerformLayout();
        grpLogger.ResumeLayout(false);
        grpTrades.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)dgvTrades).EndInit();
        grpOptionsChain.ResumeLayout(false);
        grpOptionsChain.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)dgvQuotes).EndInit();
        grpOptionsChainNext.ResumeLayout(false);
        grpOptionsChainNext.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)dgvQuotesNext).EndInit();
        grpBalance.ResumeLayout(false);
        grpBalance.PerformLayout();
        grpTickerButtons.ResumeLayout(false);
        grpTrade.ResumeLayout(false);
        grpContracts.ResumeLayout(false);
        grpCounts.ResumeLayout(false);
        tabSettings.ResumeLayout(false);
        grpBroker.ResumeLayout(false);
        grpTickers.ResumeLayout(false);
        grpScreenCoords.ResumeLayout(false);
        grpScreenCoords.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)dgvTickers).EndInit();
        grpPositionSize.ResumeLayout(false);
        grpTarget.ResumeLayout(false);
        grpSchwabCredentials.ResumeLayout(false);
        grpSchwabCredentials.PerformLayout();
        grpRefreshToken.ResumeLayout(false);
        grpRefreshToken.PerformLayout();
        grpAccounts.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)dgvAccounts).EndInit();
        grpAwsSettings.ResumeLayout(false);
        grpAwsSettings.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private StatusStrip statusStrip1;
    private ToolStripStatusLabel lblStatusUser;
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
    private CheckBox chkCallFilter;
    private CheckBox chkPutFilter;
    private CheckBox chkStopAt11;
    private CheckBox chkSaveToCsv;
    private CheckBox chkHideNextExpDate;
    private GroupBox grpTrade;
    private RadioButton rbNoTrade;
    private RadioButton rbTrade;
    private RadioButton rbTradeTarget;
    private RadioButton rbNoTradeTarget;
    private GroupBox grpContracts;
    private RadioButton rbContracts1;
    private RadioButton rbContracts2;
    private RadioButton rbContracts3;
    private RadioButton rbContracts4;
    private RadioButton rbContracts5;
    private RadioButton rbContracts6;
    private RadioButton rbContractsPositionSize;
    private GroupBox grpCounts;
    private RadioButton rbCounts3;
    private RadioButton rbCounts4;
    private RadioButton rbCounts5;
    private RadioButton rbCounts6;
    private RadioButton rbCounts7;
    private RadioButton rbCounts8;
    private RadioButton rbCounts9;
    private RadioButton rbCounts10;
    private RadioButton rbCountsInRange;
    private GroupBox grpOptionsChain;
    private GroupBox grpOptionsChainNext;
    private DataGridView dgvQuotesNext;
    private DataGridViewTextBoxColumn colSymbolQNext;
    private DataGridViewTextBoxColumn colRangeNext;
    private DataGridViewTextBoxColumn colCallSprdNext;
    private DataGridViewTextBoxColumn colCallBidNext;
    private DataGridViewTextBoxColumn colCallAskNext;
    private DataGridViewTextBoxColumn colSpotPriceNext;
    private DataGridViewTextBoxColumn colStrikePriceNext;
    private DataGridViewTextBoxColumn colPutBidNext;
    private DataGridViewTextBoxColumn colPutAskNext;
    private DataGridViewTextBoxColumn colPutSprdNext;
    private DataGridViewTextBoxColumn colContractsNext;
    private DataGridViewTextBoxColumn colLevelNext;
    private Label lblCallHeaderNext;
    private Label lblPutHeaderNext;
    private Label lblExpDateNext;
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
    private DataGridViewTextBoxColumn colTradePnLMin;
    private DataGridViewTextBoxColumn colTradePnLMax;
    private DataGridViewTextBoxColumn colTradeMoneyness;
    private Button btnFetchQuotes;
    private Button btnLiveChart;
    private Button btnFourEtfCharts;
    private Button btnHubHost;
    private Button btnSimulator;
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
    private CheckBox chkSaveDumps;
    private CheckBox chkShowOrderConfirmation;
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
    private GroupBox grpRefreshToken;
    private GroupBox grpAccounts;
    private DataGridView dgvAccounts;
    private DataGridViewCheckBoxColumn colAccountDefault;
    private DataGridViewTextBoxColumn colAccountNumber;
    private DataGridViewTextBoxColumn colAccountHash;
    private Button btnRefreshAccounts;
    private Button btnLogin;
    private TextBox txtResponse;
    private Label lblResponseHint;
    private Label lblTokenStatus;
    private GroupBox grpAwsSettings;
    private Label lblAwsAccessKey;
    private TextBox txtAwsAccessKey;
    private Label lblAwsSecretKey;
    private TextBox txtAwsSecretKey;
    private Label lblAwsBucket;
    private TextBox txtAwsBucket;
    private Label lblAwsRegion;
    private TextBox txtAwsRegion;
    private Button btnSaveAwsSettings;
    private Label lblAwsSaved;
    private GroupBox grpScreenCoords;
    private Panel pnlCoordsRows;
    private Button btnSaveCoords;
    private Button btnResetCoords;
}
