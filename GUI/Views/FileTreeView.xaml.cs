using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Core;

namespace GUI.Views
{
    public partial class FileTreeView : UserControl
    {
        public event EventHandler<DownloadRequest>? DownloadRequested;

        public event EventHandler? LoadFailed;

        public ObservableCollection<FileItem> RootItems { get; } = new();
        public FileItem RootItem { get; } = new("root", 0);

        public PackageSelection Selection { get; }

        private readonly List<SophonManifestAssetProperty> toDownload = new();
        private long downloadSize;
        private string downloadUrl = "";

        private readonly HashSet<FileItem> selectedItems = new();
        private readonly HashSet<FileItem> explicitlyDeselected = new();
        private FileItem? lastClickedItem;

        private SearchFilter activeFilter = new();
        private bool isSearching;
        private readonly DispatcherTimer searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };

        public FileTreeView(PackageSelection selection)
        {
            InitializeComponent();
            Selection = selection;
            DataContext = this;

            searchDebounce.Tick += SearchDebounce_Tick;
            FilterButton.ToolTip = activeFilter.Describe();

            string diff = selection.PreviousVersion == null ? "" : $"  -  changes since {selection.PreviousVersion}";
            PackageSummary.Text = $"{selection.Game} - {selection.Mode}  -  {selection.Region}  -  version {selection.Version}{diff}";
        }

        #region loading
        private void ShowLoading(bool busy, string message = "Loading...")
        {
            LoadingText.Text = message;
            LoadingOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (busy) TaskbarProgress.Indeterminate();
            else TaskbarProgress.Clear();
        }

        public async Task LoadAsync()
        {
            ShowLoading(true);

            try
            {
                var (manifest, buildDownloadUrl) = Selection.Mode == "Sophon"
                    ? await Sophon.GetManifest(Selection.Game, Selection.Version, Selection.Region, Selection.CategoryId, Selection.StokenData)
                    : await Dispatch.GetFiles(Selection.Game, Selection.Version, Selection.CategoryId);

                if (manifest.Assets.Count == 0)
                {
                    ThemedDialog.Show("No files found in this package.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    LoadFailed?.Invoke(this, EventArgs.Empty);
                    return;
                }

                downloadUrl = buildDownloadUrl;

                var assets = new List<SophonManifestAssetProperty>();
                if (Selection.PreviousVersion != null)
                {
                    // TODO: add scattered support for diff version
                    var (prevManifest, _) = await Sophon.GetManifest(Selection.Game, $"{Selection.PreviousVersion}.0", Selection.Region, Selection.CategoryId);
                    var prevMap = new Dictionary<string, string>();
                    foreach (var asset in prevManifest.Assets)
                        prevMap[asset.AssetName] = asset.AssetHashMd5;

                    foreach (var asset in manifest.Assets)
                    {
                        if (!prevMap.TryGetValue(asset.AssetName, out var hash) || hash != asset.AssetHashMd5)
                            assets.Add(asset);
                    }
                }
                else
                {
                    assets.AddRange(manifest.Assets);
                }

                // reverse tree to not count sizes twice
                foreach (var asset in assets)
                    AddFileToRoot(asset);

                SortTree(RootItem);
                RootItems.Add(RootItem);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load the package: {ex}");
                ThemedDialog.Show($"Could not load this package.\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadFailed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private void SortTree(FileItem node)
        {
            if (node.Children.Count == 0) return;

            var sorted = node.Children
                .OrderByDescending(f => !f.IsFile)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            node.ClearChildren();
            foreach (var c in sorted)
                node.AddChild(c);

            foreach (var c in node.Children)
                SortTree(c);
        }

        private void AddFileToRoot(SophonManifestAssetProperty asset)
        {
            var parts = asset.AssetName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            var folder = RootItem;
            for (int i = 0; i < parts.Length - 1; i++)
                folder = folder.GetOrAddFolder(parts[i]);

            string fileName = parts[^1];
            if (folder.FindChild(fileName) != null)
                return; // same asset listed twice in the manifest

            folder.AddChild(new FileItem(fileName, asset.AssetSize, folder, asset, isFile: true));

            for (FileItem? node = folder; node != null; node = node.Parent)
            {
                node.SizeInBytes += asset.AssetSize;
                node.ElementsCount += 1;
            }
        }
        #endregion

        #region search

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchDebounce.Stop();
            if (SearchBox.Text.Trim().Length == 0 && !activeFilter.IsActive)
                searchDebounce.Start();
        }

        private async void SearchDebounce_Tick(object? sender, EventArgs e)
        {
            searchDebounce.Stop();
            if (SearchBox.Text.Trim().Length == 0 && !activeFilter.IsActive)
                await RunSearchAsync();
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await RunSearchAsync();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FilterDialog(activeFilter) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true)
                return;

            activeFilter = dialog.Filter;
            FilterButton.Content = activeFilter.IsActive ? "Filter *" : "Filter";
            FilterButton.ToolTip = activeFilter.Describe();
        }

        private async Task RunSearchAsync()
        {
            if (isSearching) return;

            searchDebounce.Stop();
            ClearSelection();

            string text = SearchBox.Text.Trim();
            Regex? regex = null;

            if (text.Length > 0 && RegexToggle.IsChecked == true)
            {
                try
                {
                    regex = new Regex(text, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
                }
                catch (ArgumentException ex)
                {
                    ThemedDialog.Show($"That is not a valid regular expression.\n\n{ex.Message}",
                        "Invalid pattern", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (text.Length == 0 && !activeFilter.IsActive)
            {
                RootItems.Clear();
                if (RootItem.Children.Count > 0)
                    RootItems.Add(RootItem);
                return;
            }

            var query = new SearchQuery(text, regex, activeFilter);

            isSearching = true;
            SearchButton.IsEnabled = false;
            ShowLoading(true, "Searching...");

            try
            {
                var filtered = await Task.Run(() => FilterTree(RootItem, query, isRoot: true));

                RootItems.Clear();
                if (filtered != null)
                    RootItems.Add(filtered);

                // wait for finishing before dismissing the loading screen
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Search failed: {ex}");
                ThemedDialog.Show($"The search could not be completed.\n\n{ex.Message}",
                    "Search failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                isSearching = false;
                SearchButton.IsEnabled = true;
                ShowLoading(false);
            }
        }

        /// The text and filter conditions a single search run is working from
        private sealed record SearchQuery(string Text, Regex? Pattern, SearchFilter Filter)
        {
            public bool MatchesName(string name)
            {
                if (Text.Length == 0) return true;

                if (Pattern == null)
                    return name.Contains(Text, StringComparison.OrdinalIgnoreCase);

                try { return Pattern.IsMatch(name); }
                catch (RegexMatchTimeoutException) { return false; }
            }
        }

        private FileItem? FilterTree(FileItem node, SearchQuery query, bool isRoot = false)
        {
            if (node.IsFile)
            {
                bool hit = query.Filter.AllowsFiles
                           && query.Filter.SizeInRange(node.SizeInBytes)
                           && query.MatchesName(node.Name);
                return hit ? node : null;
            }

            if (!isRoot
                && query.Filter.AllowsFolders
                && query.Filter.SizeInRange(node.SizeInBytes)
                && query.MatchesName(node.Name))
                return node;

            var matchingChildren = new List<FileItem>();
            foreach (var child in node.Children)
            {
                var result = FilterTree(child, query);
                if (result != null)
                    matchingChildren.Add(result);
            }

            if (matchingChildren.Count == 0)
                return null;

            var copy = new FileItem(node.Name, 0, node.Parent) { IsExpanded = true };
            foreach (var child in matchingChildren)
            {
                copy.AddChild(child);
                copy.SizeInBytes += child.SizeInBytes;
                copy.ElementsCount += child.IsFile ? 1 : child.ElementsCount;
            }

            return copy;
        }
        #endregion

        #region multi-select
        private void ClearSelection()
        {
            foreach (var item in selectedItems)
                item.IsSelected = false;
            selectedItems.Clear();
            explicitlyDeselected.Clear();
            lastClickedItem = null;
        }

        private void FileTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Let the expand/collapse toggle button work without touching selection
            if (IsExpandToggleClick(e.GetPosition(FileTree)))
                return;

            var clickedItem = GetItemAtPoint(e.GetPosition(FileTree));

            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if (clickedItem == null)
            {
                if (!ctrl && !shift)
                    ClearSelection();
                return;
            }

            if (ctrl && !shift)
            {
                clickedItem.IsSelected = !clickedItem.IsSelected;
                if (clickedItem.IsSelected)
                {
                    selectedItems.Add(clickedItem);
                    explicitlyDeselected.Remove(clickedItem);
                }
                else
                {
                    selectedItems.Remove(clickedItem);
                    explicitlyDeselected.Add(clickedItem);
                }
                lastClickedItem = clickedItem;
            }
            else if (shift && lastClickedItem != null)
            {
                var allItems = GetAllItemsInDisplayOrder();
                int startIdx = allItems.IndexOf(lastClickedItem);
                int endIdx = allItems.IndexOf(clickedItem);

                if (startIdx >= 0 && endIdx >= 0)
                {
                    foreach (var item in selectedItems)
                        item.IsSelected = false;
                    selectedItems.Clear();

                    int min = Math.Min(startIdx, endIdx);
                    int max = Math.Max(startIdx, endIdx);
                    for (int i = min; i <= max; i++)
                    {
                        allItems[i].IsSelected = true;
                        selectedItems.Add(allItems[i]);
                    }
                }
                // anchor (lastClickedItem) stays unchanged for shift+click
            }
            else
            {
                foreach (var item in selectedItems)
                    item.IsSelected = false;
                selectedItems.Clear();

                clickedItem.IsSelected = true;
                selectedItems.Add(clickedItem);
                lastClickedItem = clickedItem;
            }
        }

        private bool IsExpandToggleClick(Point point)
        {
            var element = FileTree.InputHitTest(point) as DependencyObject;
            while (element != null && element != FileTree)
            {
                if (element is ToggleButton)
                    return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private FileItem? GetItemAtPoint(Point point)
        {
            var element = FileTree.InputHitTest(point) as DependencyObject;
            bool inChildrenArea = false;
            while (element != null && element != FileTree)
            {
                if (element is Panel panel && panel.IsItemsHost)
                    inChildrenArea = true;
                if (element is TreeViewItem tvi)
                    return inChildrenArea ? null : tvi.DataContext as FileItem;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private List<FileItem> GetAllItemsInDisplayOrder()
        {
            var result = new List<FileItem>();
            foreach (var root in RootItems)
                CollectItems(root, result);
            return result;
        }

        private void CollectItems(FileItem node, List<FileItem> result)
        {
            result.Add(node);
            foreach (var child in node.Children)
                CollectItems(child, result);
        }
        #endregion

        #region download hand-off
        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            toDownload.Clear();
            downloadSize = 0;

            if (selectedItems.Count == 0)
            {
                ThemedDialog.Show("Select at least one file or folder first.", "Nothing selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var seen = new HashSet<string>();
            foreach (var item in selectedItems)
                CollectForDownload(item, seen);

            if (toDownload.Count == 0)
            {
                ThemedDialog.Show("The selection contains no downloadable files.", "Nothing to download",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DownloadRequested?.Invoke(this, new DownloadRequest(
                toDownload.ToList(), downloadSize, downloadUrl, Selection.Title));
        }

        private void CollectForDownload(FileItem node, HashSet<string> seen)
        {
            if (node.IsFile)
            {
                if (node.SourceFile != null && seen.Add(node.SourceFile.AssetName))
                {
                    toDownload.Add(node.SourceFile);
                    downloadSize += node.SizeInBytes;
                }
                return;
            }

            // Don't recurse the whole folder or we'd add unselected siblings.
            if (node.Children.Any(c => selectedItems.Contains(c)))
                return;

            // No selected direct children
            foreach (var child in node.Children)
                CollectAllFiles(child, seen);
        }

        private void CollectAllFiles(FileItem node, HashSet<string> seen)
        {
            if (explicitlyDeselected.Contains(node))
                return;

            if (node.IsFile)
            {
                if (node.SourceFile != null && seen.Add(node.SourceFile.AssetName))
                {
                    toDownload.Add(node.SourceFile);
                    downloadSize += node.SizeInBytes;
                }
                return;
            }
            foreach (var child in node.Children)
                CollectAllFiles(child, seen);
        }
        #endregion
    }
}
