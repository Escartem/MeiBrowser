using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Core;
using Microsoft.Win32;

namespace GUI.Views
{
    public partial class SetupView : UserControl
    {
        public event EventHandler<PackageSelection>? Confirmed;

        private string? selectedGame;
        private string? selectedServer;
        private string? selectedVersion;
        private string? selectedCategory;
        private string? selectedMode;
        private string stokenBuildData = "";

        private string customSophonUrl = "";
        private string? currentPackageId;
        private string? currentPassword;
        private string? preDownloadPassword;

        public SetupView()
        {
            InitializeComponent();

            ModeCombo.ItemsSource = new[]
            {
                new ComboBoxItem() { Content = "Sophon" },
                new ComboBoxItem() { Content = "Scattered Files" }
            };
        }

        private void ShowLoading(bool busy)
        {
            LoadingOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (busy) TaskbarProgress.Indeterminate();
            else TaskbarProgress.Clear();
        }

        private void ResetGameCombo()
        {
            GameCombo.ItemsSource = null;
            var source = new[]
            {
                new { Name = "Genshin Impact", Icon = "pack://application:,,,/icons/hk4e.png", Id = "hk4e" },
                new { Name = "Honkai: Star Rail", Icon = "pack://application:,,,/icons/hkrpg.png", Id = "hkrpg" },
                new { Name = "Zenless Zone Zero", Icon = "pack://application:,,,/icons/nap.png", Id = "nap" },
                // TODO: add hi3 support
            };

            if (selectedMode == "Sophon")
            {
                var list = source.ToList();
                list.Add(new { Name = "Custom Sophon URL", Icon = "pack://application:,,,/icons/custom.png", Id = "custom" });
                source = list.ToArray();
            }

            GameCombo.ItemsSource = source;
        }

        #region mode selection
        private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedMode = (ModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();

            ResetGameCombo();
            GameCombo.IsEnabled = true;

            ServerCombo.IsEnabled = false;
            ServerCombo.ItemsSource = null;

            VersionCombo.IsEnabled = false;
            VersionCombo.ItemsSource = null;

            CategoryCombo.IsEnabled = false;
            CategoryCombo.ItemsSource = null;

            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;

            ConfirmButton.IsEnabled = false;
        }

        private void ModeHelpButton_Click(object sender, RoutedEventArgs e)
        {
            ThemedDialog.Show(
                "Sophon mode is the new method to download files, it is better & faster.\n\nScattered files is the old method, while older it provides content such as full game zip, update zip, and files from versions earlier than when sophon was available, consider it the legacy mode.",
                "Mode Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        #endregion

        #region game selection
        private async void GameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameCombo.SelectedItem == null) return;
            selectedGame = ((dynamic)GameCombo.SelectedItem).Id;

            ServerCombo.ItemsSource = null;
            VersionCombo.ItemsSource = null;

            CustomSophonTitle.Visibility = Visibility.Hidden;
            CustomSophonUrl.Visibility = Visibility.Hidden;
            CustomSophonUrl.Text = "";
            customSophonUrl = "";
            CheckSophonButton.Visibility = Visibility.Hidden;

            ServerTitle.Visibility = Visibility.Visible;
            ServerCombo.Visibility = Visibility.Visible;

            if (selectedMode == "Sophon")
            {
                if (selectedGame == "custom")
                {
                    CustomSophonTitle.Visibility = Visibility.Visible;
                    CustomSophonUrl.Visibility = Visibility.Visible;
                    CheckSophonButton.Visibility = Visibility.Visible;

                    ServerTitle.Visibility = Visibility.Hidden;
                    ServerCombo.Visibility = Visibility.Hidden;
                }
                else
                {
                    ServerCombo.ItemsSource = new[]
                    {
                        new ComboBoxItem() { Content = "OS" },
                        new ComboBoxItem() { Content = "CN" }
                    };

                    VersionCombo.IsEnabled = false;
                }
                ServerCombo.IsEnabled = true;
            }
            else
            {
                ShowLoading(true);
                try
                {
                    var versions = await Dispatch.GetDispatchVersions(selectedGame!);
                    VersionCombo.ItemsSource = versions;
                    VersionCombo.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    Report("Could not load the version list for this game.", ex);
                }
                finally
                {
                    ShowLoading(false);
                }
            }

            CategoryCombo.IsEnabled = false;
            CategoryCombo.ItemsSource = null;

            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;

            ConfirmButton.IsEnabled = false;
        }

        private async void CheckSophonButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLoading(true);

            try
            {
                customSophonUrl = CustomSophonUrl.Text;
                var version = await Sophon.CheckBuild(customSophonUrl);
                VersionCombo.ItemsSource = null;
                VersionCombo.ItemsSource = new[] { version };
                VersionCombo.IsEnabled = true;

                CategoryCombo.ItemsSource = null;
                CategoryCombo.IsEnabled = false;

                ConfirmButton.IsEnabled = false;
            }
            catch
            {
                ThemedDialog.Show("Failed to fetch sophon build from the provided URL. Make sure it is a /getBuild URL and try again.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
            }
        }
        #endregion

        #region server selection
        private async void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerCombo.SelectedItem == null) return;
            selectedServer = (ServerCombo.SelectedItem as ComboBoxItem)?.Content.ToString();

            VersionCombo.IsEnabled = false;
            CategoryCombo.IsEnabled = false;
            VersionCombo.ItemsSource = null;
            CategoryCombo.ItemsSource = null;
            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;
            ConfirmButton.IsEnabled = false;

            currentPackageId = null;
            currentPassword = null;
            preDownloadPassword = null;

            ShowLoading(true);
            try
            {
                dynamic metaData = await Meta.GetVersions(selectedGame!, selectedServer!);
                var versions = (List<string>)metaData.Item1;
                currentPackageId = (string)metaData.Item2;
                currentPassword = (string)metaData.Item3;
                if ((string)metaData.Item4 != "")
                    preDownloadPassword = (string)metaData.Item4;

                VersionCombo.ItemsSource = versions;
                VersionCombo.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Report("Could not load the versions for this server.", ex);
            }
            finally
            {
                ShowLoading(false);
            }
        }
        #endregion

        #region version selection
        private async void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionCombo.SelectedItem == null) return;
            selectedVersion = VersionCombo.SelectedItem?.ToString();

            CategoryCombo.IsEnabled = false;
            CategoryCombo.ItemsSource = null;
            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;
            ConfirmButton.IsEnabled = false;

            ComboBoxItem[] packageItems;

            ShowLoading(true);
            try
            {
                if (selectedMode == "Sophon")
                {
                    var password = currentPassword;
                    if (selectedVersion!.EndsWith(" (pre-download)") && preDownloadPassword != null)
                    {
                        password = preDownloadPassword;
                    }
                    var packages = selectedGame == "custom"
                        ? await Meta.GetCustomPackages(customSophonUrl)
                        : await Meta.GetPackages(selectedServer!, selectedVersion, currentPackageId!, password!);

                    packageItems = packages.Select(p =>
                        new ComboBoxItem() { Content = $"{p[1]} - {p[2]}", Tag = p[0] }
                    ).ToArray();
                }
                else
                {
                    List<string> packages = await Dispatch.GetPackages(selectedGame!, selectedVersion!);

                    packageItems = packages.Select(p =>
                        new ComboBoxItem() { Content = p, Tag = p.ToLower() }
                    ).ToArray();
                }

                CategoryCombo.ItemsSource = packageItems;
                CategoryCombo.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Report("Could not load the packages for this version.", ex);
            }
            finally
            {
                ShowLoading(false);
            }
        }
        #endregion

        #region package selection
        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedCategory = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;

            // Emptying the list raises this event as well, need to handle it
            if (selectedCategory == null)
            {
                ConfirmButton.IsEnabled = false;
                return;
            }

            DiffMode.IsEnabled = selectedMode == "Sophon" && HasOlderVersion();
            ConfirmButton.IsEnabled = true;
        }

        private bool HasOlderVersion()
        {
            int index = VersionCombo.SelectedIndex;
            return index >= 0 && index + 1 < VersionCombo.Items.Count;
        }
        #endregion

        private void Confirm_Click(object? sender = null, RoutedEventArgs? e = null)
        {
            string version = selectedMode == "Sophon" ? $"{selectedVersion}.0" : selectedVersion ?? "";
            string region = selectedGame == "custom" ? customSophonUrl : selectedServer ?? "";

            string? previousVersion = null;
            if (DiffMode.IsChecked == true && HasOlderVersion())
                previousVersion = VersionCombo.Items[VersionCombo.SelectedIndex + 1]?.ToString();

            Confirmed?.Invoke(this, new PackageSelection(
                selectedGame ?? "",
                region,
                version,
                selectedCategory ?? "",
                selectedMode ?? "Sophon",
                previousVersion,
                stokenBuildData));

            stokenBuildData = "";
        }

        private void STokenButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "SToken Build|getBuildWithStokenLogin.json|JSON Files (*.json)|*.json",
                Multiselect = false
            };

            if (dlg.ShowDialog() == true)
            {
                string json = File.ReadAllText(dlg.FileName);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    stokenBuildData = json;
                    selectedMode = "Sophon";
                    Confirm_Click();
                }
            }
        }

        private static void Report(string message, Exception ex)
        {
            Console.WriteLine($"{message}\n{ex}");
            ThemedDialog.Show($"{message}\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
