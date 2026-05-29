using WinFormsApp = System.Windows.Forms.Application;

namespace OptionsTrader.WinForms;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        WinFormsApp.Run(new Form1());
    }
}