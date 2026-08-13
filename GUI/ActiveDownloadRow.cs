using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using Core;

namespace GUI
{
    /// View model for a single file in the live download list
    public class ActiveDownloadRow : INotifyPropertyChanged
    {
        private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));
        private static readonly Brush VerifyBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x8A, 0xFF));
        private static readonly Brush RetryBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB2, 0x4C));
        private static readonly Brush FailedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

        public event PropertyChangedEventHandler? PropertyChanged;

        public ActiveDownloadRow(string key)
        {
            Key = key;
            Name = System.IO.Path.GetFileName(key.Replace('\\', '/').TrimEnd('/'));
            if (string.IsNullOrEmpty(Name)) Name = key;
        }

        public string Key { get; }
        public string Name { get; }

        private double _percent;
        public double Percent
        {
            get => _percent;
            private set => Set(ref _percent, value, nameof(Percent));
        }

        private string _status = "";
        public string Status
        {
            get => _status;
            private set => Set(ref _status, value, nameof(Status));
        }

        private Brush _statusBrush = RunningBrush;
        public Brush StatusBrush
        {
            get => _statusBrush;
            private set => Set(ref _statusBrush, value, nameof(StatusBrush));
        }

        public void Update(FileProgressSnapshot snapshot)
        {
            Percent = (snapshot.State == DownloadFileState.Verifying
                ? snapshot.VerifyFraction
                : snapshot.Fraction) * 100;

            string retry = snapshot.Attempt > 1 ? $"  (try {snapshot.Attempt}/{snapshot.MaxAttempts})" : "";

            switch (snapshot.State)
            {
                case DownloadFileState.Verifying:
                    StatusBrush = VerifyBrush;
                    Status = snapshot.Detail ?? "checking";
                    break;

                case DownloadFileState.Retrying:
                    StatusBrush = RetryBrush;
                    Status = $"retrying{retry}";
                    break;

                case DownloadFileState.Failed:
                    StatusBrush = FailedBrush;
                    Status = "failed";
                    break;

                default:
                    StatusBrush = RunningBrush;
                    string size = snapshot.TotalBytes > 0
                        ? $"{Utils.FormatSize(snapshot.BytesCompleted)} / {Utils.FormatSize(snapshot.TotalBytes)}"
                        : Utils.FormatSize(snapshot.BytesCompleted);
                    string detail = string.IsNullOrEmpty(snapshot.Detail) ? "" : $"  {snapshot.Detail}";
                    Status = $"{size}{detail}{retry}";
                    break;
            }
        }

        private void Set<T>(ref T field, T value, string property)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}
