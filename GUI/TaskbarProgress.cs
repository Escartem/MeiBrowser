using System;
using System.Windows;
using System.Windows.Shell;

namespace GUI
{
    internal static class TaskbarProgress
    {
        private static TaskbarItemInfo? Info
        {
            get
            {
                var main = Application.Current?.MainWindow;
                if (main == null) return null;

                // A window built without one in XAML still needs somewhere to put the state
                return main.TaskbarItemInfo ??= new TaskbarItemInfo();
            }
        }

        public static bool DownloadActive { get; set; }

        public static void Indeterminate()
        {
            if (DownloadActive) return;
            Apply(TaskbarItemProgressState.Indeterminate, 0);
        }

        public static void Set(double fraction) =>
            Apply(TaskbarItemProgressState.Normal, Math.Clamp(fraction, 0, 1));

        public static void Paused(double fraction) =>
            Apply(TaskbarItemProgressState.Paused, Math.Clamp(fraction, 0, 1));

        public static void Error(double fraction) =>
            Apply(TaskbarItemProgressState.Error, Math.Clamp(fraction, 0, 1));

        public static void Clear()
        {
            if (DownloadActive) return;
            Apply(TaskbarItemProgressState.None, 0);
        }

        private static void Apply(TaskbarItemProgressState state, double value)
        {
            var info = Info;
            if (info == null) return;

            void Assign()
            {
                info.ProgressState = state;
                info.ProgressValue = value;
            }

            // Callers may be on a worker thread but the taskbar item lives on the UI thread
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                Assign();
            else
                dispatcher.Invoke(Assign);
        }
    }
}
