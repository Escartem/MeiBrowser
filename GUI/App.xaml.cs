using System.Configuration;
using System.Data;
using System.IO;
using System.Text;
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

            var logFile = "latest.log";
            var fileStream = new FileStream(logFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            var streamWriter = new StreamWriter(fileStream) { AutoFlush = true };

            var multiWriter = new System.IO.TextWriter[2] { Console.Out, streamWriter };
            Console.SetOut(TextWriter.Synchronized(new MultiTextWriter(multiWriter)));
            Console.SetError(TextWriter.Synchronized(new MultiTextWriter(multiWriter)));


        }
    }
    class MultiTextWriter : TextWriter
    {
        private readonly TextWriter[] writers;
        public MultiTextWriter(TextWriter[] writers) => this.writers = writers;
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            foreach (var w in writers) w.Write(value);
        }
        public override void Write(string value)
        {
            foreach (var w in writers) w.Write(value);
        }
        public override void WriteLine(string value)
        {
            foreach (var w in writers) w.WriteLine(value);
        }
    }
}
