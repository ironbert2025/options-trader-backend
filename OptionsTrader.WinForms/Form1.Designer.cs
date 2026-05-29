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

        tabControl.SuspendLayout();
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
        tabSettings.Name = "tabSettings";
        tabSettings.Padding = new Padding(8);
        tabSettings.Text = "Settings";

        // Form1
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1024, 600);
        Controls.Add(tabControl);
        Name = "Form1";
        Text = "Options Trader";

        tabControl.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TabControl tabControl;
    private TabPage tabOptions;
    private TabPage tabSettings;
}
