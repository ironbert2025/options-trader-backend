namespace OptionsTrader.WinForms;

public partial class Form1 : Form
{
    public Form1()
    {
        InitializeComponent();
        LoadBrokerSelection();
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
        }
    }

    private void BrokerRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        foreach (var rb in grpBroker.Controls.OfType<RadioButton>())
            rb.ForeColor = rb.Checked ? Color.Green : SystemColors.ControlText;

        if (sender is RadioButton { Checked: true } selected)
            BrokerSettingsStore.Save(selected.Text);
    }
}
