using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Dark.Net;

namespace GUI
{
    public partial class App : Application
    {
        // in case of big oopsies
        private static readonly string CrashLog =
            Path.Combine(AppContext.BaseDirectory, "meibrowser-error.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DarkNet.Instance.SetCurrentProcessTheme(Theme.Dark);

            ConsoleLog.Install();

            DispatcherUnhandledException += (_, args) =>
            {
                Record("UI", args.Exception);
                args.Handled = true;
                ThemedDialog.Show(
                    $"Something went wrong.\n\n{args.Exception.Message}\n\nDetails were written to:\n{CrashLog}",
                    "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Record("fatal", args.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Record("background task", args.Exception);
                args.SetObserved();
            };
        }

        private static void Record(string source, Exception? ex)
        {
            if (ex == null) return;

            Console.WriteLine($"Unhandled {source} exception: {ex}");
            try
            {
                File.AppendAllText(CrashLog, $"[{DateTime.Now:u}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // what do we do now
            }
        }
    }

}
