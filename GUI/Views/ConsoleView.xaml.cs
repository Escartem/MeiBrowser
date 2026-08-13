using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GUI.Views
{
    public partial class ConsoleView : UserControl
    {
        private const int MaxChars = 400_000;

        private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(200) };

        public ConsoleView()
        {
            InitializeComponent();
            refresh.Tick += Refresh_Tick;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Output.Text = ConsoleLog.Instance.TakeSnapshot();
            ScrollIfFollowing();

            ConsoleLog.Instance.Cleared += OnLogCleared;
            refresh.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            refresh.Stop();
            ConsoleLog.Instance.Cleared -= OnLogCleared;
        }

        private void OnLogCleared() => Dispatcher.Invoke(() => Output.Clear());

        private void Refresh_Tick(object? sender, EventArgs e)
        {
            string text = ConsoleLog.Instance.DrainPending();
            if (text.Length == 0) return;

            Output.AppendText(text);

            // Keep the box itself bounded
            if (Output.Text.Length > MaxChars)
                Output.Text = Output.Text[^(MaxChars / 2)..];

            ScrollIfFollowing();
        }

        private void ScrollIfFollowing()
        {
            if (AutoScroll.IsChecked == true)
                Output.ScrollToEnd();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) => ConsoleLog.Instance.Clear();

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(Output.Text);
            }
            catch (Exception ex)
            {
                // The clipboard can be locked by another process
                Console.WriteLine($"Could not copy the log: {ex.Message}");
            }
        }
    }
}
