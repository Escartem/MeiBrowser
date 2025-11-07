using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using Dark.Net;

namespace GUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DarkNet.Instance.SetCurrentProcessTheme(Theme.Dark);

            // TODO: add support for logging to file + console
            //var logFile = "download.log";
            //var fileStream = new FileStream(logFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            //var streamWriter = new StreamWriter(fileStream) { AutoFlush = true };
            //Console.SetOut(streamWriter);
            //Console.SetError(streamWriter);
        }
    }

}
