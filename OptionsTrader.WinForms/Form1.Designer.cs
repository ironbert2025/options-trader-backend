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

        tabControl.SuspendLayout();
        grpBroker.SuspendLayout();
        grpTickers.SuspendLayout();
        grpPositionSize.SuspendLayout();
        grpTarget.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvTickers).BeginInit();
        SuspendLayout();

        // tabControl
        tabControl.Controls.Add(tabOptions);
        tabControl.Controls.Add(tabSettings);
        tabControl.Dock = DockStyle.Fill;
        tabControl.Name = "tabControl";
        tabControl.SelectedIndex = 0;

        // tabOptions
        tabOptions.Name = "tabOptions";
        tabOptions.Padding = new Padding(8);
        tabOptions.Text = "Options Data";

        // tabSettings
        tabSettings.Controls.Add(grpBroker);
        tabSettings.Controls.Add(grpTickers);
        tabSettings.Controls.Add(grpPositionSize);
        tabSettings.Controls.Add(grpTarget);
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
        grpPositionSize.Location = new Point(8, 122);
        grpPositionSize.Name = "grpPositionSize";
        grpPositionSize.Size = new Size(155, 105);
        grpPositionSize.TabStop = false;
        grpPositionSize.Text = "Position Size (%)";

        // rbPosition25
        rbPosition25.Location = new Point(12, 22);
        rbPosition25.Name = "rbPosition25";
        rbPosition25.Size = new Size(135, 20);
        rbPosition25.Text = "2.5";
        rbPosition25.CheckedChanged += PositionSizeRadioButton_CheckedChanged;

        // rbPosition5
        rbPosition5.Location = new Point(12, 46);
        rbPosition5.Name = "rbPosition5";
        rbPosition5.Size = new Size(135, 20);
        rbPosition5.Text = "5";
        rbPosition5.CheckedChanged += PositionSizeRadioButton_CheckedChanged;

        // rbPosition10
        rbPosition10.Location = new Point(12, 70);
        rbPosition10.Name = "rbPosition10";
        rbPosition10.Size = new Size(135, 20);
        rbPosition10.Text = "10";
        rbPosition10.CheckedChanged += PositionSizeRadioButton_CheckedChanged;

        // grpTarget
        grpTarget.Controls.Add(rbTarget10);
        grpTarget.Controls.Add(rbTarget35);
        grpTarget.Controls.Add(rbTarget100);
        grpTarget.Location = new Point(8, 237);
        grpTarget.Name = "grpTarget";
        grpTarget.Size = new Size(155, 105);
        grpTarget.TabStop = false;
        grpTarget.Text = "Target (%)";

        // rbTarget10
        rbTarget10.Location = new Point(12, 22);
        rbTarget10.Name = "rbTarget10";
        rbTarget10.Size = new Size(135, 20);
        rbTarget10.Text = "10";
        rbTarget10.CheckedChanged += TargetRadioButton_CheckedChanged;

        // rbTarget35
        rbTarget35.Location = new Point(12, 46);
        rbTarget35.Name = "rbTarget35";
        rbTarget35.Size = new Size(135, 20);
        rbTarget35.Text = "35";
        rbTarget35.CheckedChanged += TargetRadioButton_CheckedChanged;

        // rbTarget100
        rbTarget100.Location = new Point(12, 70);
        rbTarget100.Name = "rbTarget100";
        rbTarget100.Size = new Size(135, 20);
        rbTarget100.Text = "100";
        rbTarget100.CheckedChanged += TargetRadioButton_CheckedChanged;

        // Form1
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1024, 600);
        Controls.Add(tabControl);
        Name = "Form1";
        Text = "Options Trader";

        tabControl.ResumeLayout(false);
        grpBroker.ResumeLayout(false);
        grpBroker.PerformLayout();
        grpTickers.ResumeLayout(false);
        grpPositionSize.ResumeLayout(false);
        grpPositionSize.PerformLayout();
        grpTarget.ResumeLayout(false);
        grpTarget.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)dgvTickers).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private TabControl tabControl;
    private TabPage tabOptions;
    private TabPage tabSettings;
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
}
