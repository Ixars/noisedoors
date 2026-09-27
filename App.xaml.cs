using System.Configuration;
using System.Data;
using System.Windows;

namespace Noise
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show(args.Exception.ToString(), "Unhandled Error");
                args.Handled = true;
            };
            base.OnStartup(e);
        }
    }

}
