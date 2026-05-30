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

        tabControl.SuspendLayout();
        grpBroker.SuspendLayout();
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
        tabSettings.Name = "tabSettings";
        tabSettings.Padding = new Padding(8);
        tabSettings.Text = "Settings";

        // grpBroker
        grpBroker.Controls.Add(rbSchwab);
        grpBroker.Controls.Add(rbIBKR);
        grpBroker.Controls.Add(rbETrade);
        grpBroker.Location = new Point(8, 8);
        grpBroker.Name = "grpBroker";
        grpBroker.Size = new Size(175, 105);
        grpBroker.TabStop = false;
        grpBroker.Text = "Broker";

        // rbSchwab
        rbSchwab.Location = new Point(12, 22);
        rbSchwab.Name = "rbSchwab";
        rbSchwab.Size = new Size(155, 20);
        rbSchwab.Text = "Charles Schwab";
        rbSchwab.CheckedChanged += BrokerRadioButton_CheckedChanged;

        // rbIBKR
        rbIBKR.Location = new Point(12, 46);
        rbIBKR.Name = "rbIBKR";
        rbIBKR.Size = new Size(155, 20);
        rbIBKR.Text = "Interactive Broker";
        rbIBKR.CheckedChanged += BrokerRadioButton_CheckedChanged;

        // rbETrade
        rbETrade.Location = new Point(12, 70);
        rbETrade.Name = "rbETrade";
        rbETrade.Size = new Size(155, 20);
        rbETrade.Text = "ETrade";
        rbETrade.CheckedChanged += BrokerRadioButton_CheckedChanged;

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
}
