using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Core;
using Dark.Net;
using Microsoft.Win32;
using System.IO;

namespace GUI
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<FileItem> RootItems { get; set; }
        public FileItem RootItem { get; set; }

        //
        private string game;
        private string version;
        private string region;
        private string categoryId;
        private string mode;
        private string previousVersion;
        private string stokenData;
        //
        private List<SophonManifestAssetProperty> toDownload = new();
        private long downloadSize = 0;
        private string downloadUrl = "";

        private string appVersion = "1.1";
        private bool isInitializing = true;

        public MainWindow()
        {
            InitializeComponent();
            DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark);

            ConsoleHelper.AllocConsole();
            var handle = ConsoleHelper.GetConsoleWindow();
            ConsoleHelper.ShowWindow(handle, ConsoleHelper.SW_SHOW);
            ConsoleHelper.SetConsoleTitle("Console");

            Console.WriteLine($"Hello World !");

            RootItems = new ObservableCollection<FileItem> {};
            RootItem = new FileItem("root", 0);
            DataContext = this;

            this.WindowState = WindowState.Maximized;
            this.Title = $"MeiBrowser v{appVersion} - @Escartem <3";
        }

        #region package selection
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AppSettings.Load();
            ApplyTheme(AppSettings.SelectedTheme);
            
            for (int i = 0; i < ThemeSelector.Items.Count; i++)
            {
                if (ThemeSelector.Items[i] is ComboBoxItem item && item.Content.ToString() == AppSettings.SelectedTheme)
                {
                    ThemeSelector.SelectedIndex = i;
                    break;
                }
            }
            
            isInitializing = false;
            await ShowPopup();
        }

        private async Task ShowPopup()
        {
            RootItems.Clear();
            RootItem.Children.Clear();
            RootItem.SizeInBytes = 0;
            RootItem.ElementsCount = 0;
            downloadUrl = "";
            toDownload.Clear();
            downloadSize = 0;

            var popup = new StartDialog();
            popup.Owner = this;
            popup.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (popup.ShowDialog() == true)
            {
                game = popup.SelectedGame;
                region = popup.SelectedServer;
                version = popup.SelectedVersion;
                categoryId = popup.SelectedCategory;
                mode = popup.SelectedMode;
                previousVersion = popup.PreviousVersion;
                stokenData = popup.STokenBuildData;
                Console.WriteLine($"Selected: {game}, {region}, {version}, {categoryId} as {mode} (using prev as {previousVersion})");
                await UpdateFiles();
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPopup();
        }
        #endregion

        #region display files
        private async Task UpdateFiles()
        {
            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                var (manifest, buildDownloadUrl) = mode == "Sophon" ? await Sophon.GetManifest(game, version, region, categoryId, stokenData) : await Dispatch.GetFiles(game, version, categoryId);
                if (manifest.Assets.Count == 0)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    MessageBox.Show("No files found in this package.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    await ShowPopup();
                    return;
                }
                downloadUrl = buildDownloadUrl;

                SophonManifestProto diffedManifest = new SophonManifestProto();
                if (previousVersion != null)
                {
                    // TODO: add scattered support
                    var (prevManifest, prevDownloadUrl) = await Sophon.GetManifest(game, $"{previousVersion}.0", region, categoryId);
                    var prevMap = new Dictionary<string, string>();
                    foreach (var asset in prevManifest.Assets)
                    {
                        prevMap[asset.AssetName] = asset.AssetHashMd5;
                    }

                    foreach (var asset in manifest.Assets)
                    {
                        if (!prevMap.ContainsKey(asset.AssetName))
                        {
                            diffedManifest.Assets.Add(asset);
                            continue;
                        }

                        if (prevMap[asset.AssetName] != asset.AssetHashMd5)
                        {
                            diffedManifest.Assets.Add(asset);
                            continue;
                        }
                    }
                } else
                {
                    foreach (var asset in manifest.Assets)
                    {
                        diffedManifest.Assets.Add(asset);
                    }
                }

                foreach (var asset in diffedManifest.Assets)
                    AddFileToRoot(asset);

                
                foreach (var item in RootItem.Children)
                    RootItem.SizeInBytes += item.SizeInBytes;

                RootItems.Add(RootItem);
            }
            finally
            {
                foreach (var root in RootItems)
                    SortTree(root);
                
                foreach (var item in RootItem.Children)
                {
                    if (item.Type == "Folder")
                        RootItem.ElementsCount += item.ElementsCount;
                    else
                        RootItem.ElementsCount += 1;
                }

                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void SortTree(FileItem node)
        {
            if (node.Children.Count == 0) return;

            var sorted = node.Children
                .OrderByDescending(f => f.Type == "Folder")
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            node.Children.Clear();
            foreach (var c in sorted)
                node.Children.Add(c);

            foreach (var c in node.Children)
                SortTree(c);
        }

        private void AddFileToRoot(SophonManifestAssetProperty asset)
        {
            var path = asset.AssetName;
            var size = asset.AssetSize;
            var parts = path.Split('/', '\\');
            var currentList = RootItem.Children;
            FileItem? parent = null;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                bool isFile = i == parts.Length - 1;

                var existing = currentList.FirstOrDefault(x => x.Name == part);
                if (existing == null)
                {
                    existing = new FileItem(part, size, parent, asset);
                    currentList.Add(existing);
                }

                FileItem? node = existing;
                node = node.Parent;
                while (node != null)
                {
                    node.SizeInBytes += isFile ? size : 0;
                    if (isFile)
                        node.ElementsCount += 1;
                    node = node.Parent;
                }

                currentList = existing.Children;
                parent = existing;
            }
        }
        #endregion

        #region download files
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileTree.SelectedItem as FileItem;
            toDownload.Clear();
            downloadSize = 0;

            if (selected == null)
                return;

            addFolderToDownloads(selected);

            var result = MessageBox.Show($"You are about to download {Utils.FormatSize(downloadSize)}, continue ?", "Continue?", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string folderPath = dialog.SelectedPath;
                    await StartDownload(folderPath);
                }
            }
        }

        private void addFolderToDownloads(FileItem node)
        {
            if (node.Type == "File")
            {
                toDownload.Add(node.SourceFile);
                downloadSize += node.SizeInBytes;
                return;
            }

            foreach (var item in node.Children)
            {
                if (item.Type == "File")
                {
                    toDownload.Add(item.SourceFile);
                    downloadSize += item.SizeInBytes;
                } else
                {
                    addFolderToDownloads(item);
                }
            }
        }

        private async Task StartDownload(string savePath)
        {
            DownloadingOverlay.Visibility = Visibility.Visible;
            var downloader = new Download();
            DownloadBar.Value = 0;

            var progress = new Progress<double>(v =>
            {
                double percent = (double)(v / downloadSize) * 100;
                DownloadBar.Value = Math.Min(percent, 100);
                DownloadText.Text = $"{percent:F1}% ({v / 1048576.0:F2} / {downloadSize / 1048576.0:F2} MB)";
            });

            await downloader.DownloadFilesAsync(toDownload, downloadUrl, progress, savePath);
            DownloadingOverlay.Visibility = Visibility.Collapsed;
        }
        #endregion

        private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitializing) return;
            
            if (ThemeSelector.SelectedItem is ComboBoxItem item)
            {
                string choice = item.Content.ToString();
                
                if (choice == "Custom...")
                {
                    var dialog = new OpenFileDialog();
                    dialog.Filter = "XAML Theme Files (*.xaml)|*.xaml";
                    if (dialog.ShowDialog() == true)
                    {
                        try
                        {
                            var customDict = new ResourceDictionary { Source = new Uri(dialog.FileName) };
                            if (!customDict.Contains("WindowBackgroundColor"))
                            {
                                MessageBox.Show("Invalid theme file: Missing 'WindowBackgroundColor'.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }
                            
                            Application.Current.Resources.MergedDictionaries.Clear();
                            Application.Current.Resources.MergedDictionaries.Add(customDict);

                            if (customDict["WindowBackgroundColor"] is Color bgColor)
                            {
                                double luminance = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) / 255;
                                DarkNet.Instance.SetWindowThemeWpf(this, luminance > 0.5 ? Theme.Light : Theme.Dark);
                            }
                            
                            AppSettings.SelectedTheme = dialog.FileName;
                            AppSettings.Save();
                            return;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Failed to load theme: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }
                
                ApplyTheme(choice);
                AppSettings.SelectedTheme = choice;
                AppSettings.Save();
            }
        }
        
        private void ApplyTheme(string themeName)
        {
            Uri themeUri = null;
            Theme darkNetTheme = Theme.Dark;
            
            if (themeName == "Dark")
            {
                themeUri = new Uri("Themes/Dark.xaml", UriKind.Relative);
                darkNetTheme = Theme.Dark;
            }
            else if (themeName == "Light")
            {
                themeUri = new Uri("Themes/Light.xaml", UriKind.Relative);
                darkNetTheme = Theme.Light;
            }
            else if (themeName == "Brown")
            {
                themeUri = new Uri("Themes/Brown.xaml", UriKind.Relative);
                darkNetTheme = Theme.Dark;
            }
            else if (File.Exists(themeName))
            {
                try
                {
                    var customDict = new ResourceDictionary { Source = new Uri(themeName) };
                    Application.Current.Resources.MergedDictionaries.Clear();
                    Application.Current.Resources.MergedDictionaries.Add(customDict);
                    if (customDict["WindowBackgroundColor"] is Color bgColor)
                    {
                        double luminance = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) / 255;
                        DarkNet.Instance.SetWindowThemeWpf(this, luminance > 0.5 ? Theme.Light : Theme.Dark);
                    }
                    return;
                }
                catch { }
            }
            
            if (themeUri != null)
            {
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeUri });
                DarkNet.Instance.SetWindowThemeWpf(this, darkNetTheme);
            }
        }
    }

    public class FileItem
    {
        public string Name { get; set; }
        public string Type => Children.Count == 0 ? "File" : "Folder";
        public ObservableCollection<FileItem> Children { get; set; } = new();
        public long SizeInBytes { get; set; }
        public string Size => Utils.FormatSize(SizeInBytes);
        public string Icon => Type == "File" ? "pack://application:,,,/icons/file2.png" : "pack://application:,,,/icons/folder.png";
        public SophonManifestAssetProperty SourceFile { get; set; }
        public string Elements => ElementsCount == 0 ? "" : $"{ElementsCount.ToString("# ##0")} files";
        public long ElementsCount { get; set; }

        public FileItem? Parent { get; set; }

        public FileItem(string name, long sizeInBytes, FileItem? parent = null, SophonManifestAssetProperty? sourceFile = null)
        {
            Name = name;
            SizeInBytes = sizeInBytes;
            Parent = parent;
            SourceFile = sourceFile;
            ElementsCount = 0;
        }
    }

}
