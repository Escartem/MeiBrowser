using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using AvalonDock.Layout;
using Core;
using Dark.Net;
using GUI.Views;
using Microsoft.Win32;

namespace GUI
{
    public partial class MainWindow : Window
    {
        private readonly string appVersion = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion;
        private bool isInitializing = true;

        private SetupView setupView = null!;
        private LayoutDocument? downloadDocument;
        private DownloadView? downloadView;

        /// Set when the user chose to quit while a download was still unwinding
        private bool quitAfterDownload;

        private ResourceDictionary? currentThemeDictionary;

        public MainWindow()
        {
            InitializeComponent();
            DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark);

            Console.WriteLine($"MeiBrowser v{appVersion} starting, Hello World !");

            this.WindowState = WindowState.Maximized;
            this.Title = $"MeiBrowser v{appVersion} - @Escartem <3";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AppSettings.Load();

            var saved = AppThemes.Find(AppSettings.SelectedTheme);
            ApplyTheme(saved);

            ThemeSelector.ItemsSource = AppThemes.All;
            ThemeSelector.SelectedItem = saved;

            isInitializing = false;
            BuildFixedTabs();
        }

        #region tabs

        private void BuildFixedTabs()
        {
            setupView = new SetupView();
            setupView.Confirmed += Setup_Confirmed;

            DocumentPane.Children.Add(new LayoutDocument
            {
                Title = "Setup",
                Content = setupView,
                CanClose = false,
                CanFloat = true
            });

            BottomPane.Children.Add(new LayoutAnchorable
            {
                Title = "Console",
                Content = new ConsoleView(),
                CanClose = false,
                CanHide = false,
                CanAutoHide = true,
                CanFloat = true
            });
        }

        private async void Setup_Confirmed(object? sender, PackageSelection selection)
        {
            var view = new FileTreeView(selection);

            var document = new LayoutDocument
            {
                Title = selection.Title,
                ToolTip = $"{selection.Mode} - {selection.Region} - {selection.Version}",
                Content = view,
                CanClose = true
            };

            view.DownloadRequested += FileTree_DownloadRequested;
            view.LoadFailed += (_, _) => CloseDocument(document);

            DocumentPane.Children.Add(document);
            document.IsActive = true;

            await view.LoadAsync();
        }

        private static void CloseDocument(LayoutDocument document)
        {
            document.CanClose = true;
            document.Close();
        }
        #endregion

        #region downloads
        private async void FileTree_DownloadRequested(object? sender, DownloadRequest request)
        {
            // TODO: implement multi download
            if (downloadView is { IsRunning: true })
            {
                ThemedDialog.Show(
                    "A download is already running. Wait for it to finish, or cancel it from the Download tab.",
                    "One at a time", MessageBoxButton.OK, MessageBoxImage.Information, this);

                if (downloadDocument != null)
                    downloadDocument.IsActive = true;
                return;
            }

            var confirm = ThemedDialog.Show(
                $"You are about to download {request.Assets.Count} file(s), {Utils.FormatSize(request.TotalSize)}, continue ?",
                "Continue?", MessageBoxButton.YesNo, MessageBoxImage.Question, this);
            if (confirm != MessageBoxResult.Yes)
                return;

            var folder = new System.Windows.Forms.FolderBrowserDialog();
            if (folder.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            // A finished run may still be sitting there
            if (downloadDocument != null)
                CloseDocument(downloadDocument);

            downloadView = new DownloadView();
            downloadDocument = new LayoutDocument
            {
                Title = "Download",
                Content = downloadView,
                CanClose = false
            };

            var document = downloadDocument;
            downloadView.Finished += (_, outcome) =>
            {
                if (quitAfterDownload)
                {
                    quitAfterDownload = false;
                    Close();
                    return;
                }

                if (outcome == DownloadOutcome.Cancelled || outcome == DownloadOutcome.Dismissed)
                {
                    CloseDocument(document);
                    if (ReferenceEquals(downloadDocument, document))
                    {
                        downloadDocument = null;
                        downloadView = null;
                    }
                    return;
                }

                document.CanClose = true;
                document.Title = outcome == DownloadOutcome.Completed ? "Download - done" : "Download - errors";
            };

            DocumentPane.Children.Add(downloadDocument);
            downloadDocument.IsActive = true;

            await downloadView.RunAsync(request, folder.SelectedPath);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (downloadView is not { IsRunning: true })
                return;

            var answer = ThemedDialog.Show("A download is still running. Stop it and quit?", "Download in progress",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, this);

            e.Cancel = true;

            if (answer != MessageBoxResult.Yes)
                return;

            if (downloadView.RequestCancel(confirm: false))
                quitAfterDownload = true;
        }
        #endregion

        #region theming
        private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitializing) return;
            if (ThemeSelector.SelectedItem is not ThemeOption option) return;

            ApplyTheme(option);
            AppSettings.SelectedTheme = option.Name;
            AppSettings.Save();
        }

        private void ApplyTheme(ThemeOption option)
        {
            try
            {
                SwapThemeDictionary(option.Load());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not apply theme '{option.Name}': {ex.Message}");
            }
        }

        private void SwapThemeDictionary(ResourceDictionary theme)
        {
            var merged = Application.Current.Resources.MergedDictionaries;

            if (currentThemeDictionary != null)
                merged.Remove(currentThemeDictionary);
            else if (merged.Count > 0)
                merged.RemoveAt(0); // the one App.xaml merged at startup

            merged.Add(theme);
            currentThemeDictionary = theme;

            var chrome = Theme.Dark;
            if (theme["WindowBackgroundColor"] is Color background)
            {
                double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
                chrome = luminance > 0.5 ? Theme.Light : Theme.Dark;
            }

            DarkNet.Instance.SetWindowThemeWpf(this, chrome);
            DockTheme.Apply(Dock);
        }
        #endregion
    }
}
