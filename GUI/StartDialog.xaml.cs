using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Core;
using Dark.Net;

namespace GUI
{
    public partial class StartDialog : Window
    {
        public string SelectedGame { get; private set; }
        public string SelectedServer { get; private set; }
        public string SelectedVersion { get; private set; }
        public string SelectedCategory { get; private set; }
        public string SelectedMode { get; private set; }
        public string PreviousVersion { get; private set; }
        public string STokenBuildData { get; private set; }

        private string customSophonUrl;

        private string currentPackageId;
        private string currentPassword;
        private string preDownloadPassword;

        private bool allowClose = false;

        public StartDialog()
        {
            InitializeComponent();
            DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark);

            ModeCombo.ItemsSource = new[]
            {
                new ComboBoxItem() { Content = "Sophon" },
                new ComboBoxItem() { Content = "Scattered Files" }
            };

            PreviousVersion = null;
            STokenBuildData = "";
            this.Title = "Select Game Options";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!allowClose)
            {
                System.Windows.Application.Current.Shutdown();
                e.Cancel = true;
            }
            base.OnClosing(e);
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
                //new { Name = "Honkai Impact 3rd", Icon = "pack://application:,,,/icons/bh3.png", Id = "bh3" }, // hi3 needs lot of hardcoded stuff cuz it's different
            };

            if (SelectedMode == "Sophon")
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
            SelectedMode = (string)(ModeCombo.SelectedItem as ComboBoxItem).Content.ToString();

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
            System.Windows.MessageBox.Show("Sophon mode is the new method to download files, it is better & faster.\n\nScattered files is the old method, while older it provides content such as full game zip, update zip, and files from versions earlier than when sophon was available, consider it the legacy mode.", "Mode Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region game selection
        private async void GameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameCombo.SelectedItem == null) return;
            SelectedGame = ((dynamic)GameCombo.SelectedItem).Id;

            ServerCombo.ItemsSource = null;
            VersionCombo.ItemsSource = null;

            CustomSophonTitle.Visibility = Visibility.Hidden;
            CustomSophonUrl.Visibility = Visibility.Hidden;
            CustomSophonUrl.Text = "";
            customSophonUrl = "";
            CheckSophonButton.Visibility = Visibility.Hidden;

            ServerTitle.Visibility = Visibility.Visible;
            ServerCombo.Visibility = Visibility.Visible;

            if (SelectedMode == "Sophon")
            {
                // custom sophon
                if (SelectedGame == "custom")
                {
                    CustomSophonTitle.Visibility = Visibility.Visible;
                    CustomSophonUrl.Visibility = Visibility.Visible;
                    CheckSophonButton.Visibility = Visibility.Visible;

                    ServerTitle.Visibility = Visibility.Hidden;
                    ServerCombo.Visibility = Visibility.Hidden;
                } else
                {
                    ServerCombo.ItemsSource = new[]
                    {
                        new ComboBoxItem() { Content = "OS" },
                        new ComboBoxItem() { Content = "CN" }
                    };

                    VersionCombo.IsEnabled = false;
                }
                ServerCombo.IsEnabled = true;
            } else
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                var versions = await Dispatch.GetDispatchVersions(SelectedGame);
                LoadingOverlay.Visibility = Visibility.Collapsed;

                VersionCombo.ItemsSource = versions;
                VersionCombo.IsEnabled = true;
            }

            CategoryCombo.IsEnabled = false;
            CategoryCombo.ItemsSource = null;

            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;

            ConfirmButton.IsEnabled = false;
        }

        private async void CheckSophonButton_Click(object sender, RoutedEventArgs e)
        {
            LoadingOverlay.Visibility = Visibility.Visible;

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
                System.Windows.MessageBox.Show("Failed to fetch sophon build from the provided URL. Make sure it is a /getBuild URL and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        #endregion

        #region server selection
        private async void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerCombo.SelectedItem == null) return;
            SelectedServer = (ServerCombo.SelectedItem as ComboBoxItem)?.Content.ToString();

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

            LoadingOverlay.Visibility = Visibility.Visible;
            dynamic metaData = await Meta.GetVersions(SelectedGame, SelectedServer);
            var versions = (List<string>)metaData.Item1;
            currentPackageId = (string)metaData.Item2;
            currentPassword = (string)metaData.Item3;
            if ((string)metaData.Item4 != "")
                preDownloadPassword = (string)metaData.Item4;
            LoadingOverlay.Visibility = Visibility.Collapsed;

            VersionCombo.ItemsSource = versions;
            VersionCombo.IsEnabled = true;
        }
        #endregion

        #region version selection
        private async void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionCombo.SelectedItem == null) return;
            SelectedVersion = VersionCombo.SelectedItem?.ToString();

            CategoryCombo.IsEnabled = false;
            CategoryCombo.ItemsSource = null;
            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;
            ConfirmButton.IsEnabled = false;

            ComboBoxItem[] packageItems = Array.Empty<ComboBoxItem>();

            LoadingOverlay.Visibility = Visibility.Visible;
            if (SelectedMode == "Sophon")
            {
                var password = currentPassword;
                if (SelectedVersion.EndsWith(" (pre-download)") && preDownloadPassword != null)
                {
                    password = preDownloadPassword;
                }
                var packages = SelectedGame == "custom" ? await Meta.GetCustomPackages(customSophonUrl) : await Meta.GetPackages(SelectedServer, SelectedVersion, currentPackageId, password);

                packageItems = packages.Select(p =>
                    new ComboBoxItem() { Content = $"{p[1]} - {p[2]}", Tag = p[0] }
                ).ToArray();
            } else
            {
                List<string> packages = await Dispatch.GetPackages(SelectedGame, SelectedVersion);

                packageItems = packages.Select(p =>
                    new ComboBoxItem() { Content = p, Tag = p.ToLower() }
                ).ToArray();
            }
            LoadingOverlay.Visibility = Visibility.Collapsed;

            CategoryCombo.ItemsSource = packageItems;
            CategoryCombo.IsEnabled = true;
        }
        #endregion

        #region package selection
        private async void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedCategory = ((dynamic)CategoryCombo.SelectedItem)?.Tag;
            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;
            
            if (SelectedMode == "Sophon" && SelectedVersion != VersionCombo.Items[^1])
            {
                DiffMode.IsEnabled = true;
            }

            ConfirmButton.IsEnabled = true;
        }
        #endregion

        private void Confirm_Click(object? sender = null, RoutedEventArgs? e = null)
        {
            allowClose = true;
            SelectedVersion = SelectedMode == "Sophon" ? $"{SelectedVersion}.0" : SelectedVersion;
            if (SelectedGame == "custom")
            {
                SelectedServer = customSophonUrl;
            }
            if ((bool)DiffMode.IsChecked)
            {
                PreviousVersion = (string)VersionCombo.Items[VersionCombo.SelectedIndex + 1];
            }
            DialogResult = true;
        }

        private void STokenButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "SToken Build|getBuildWithStokenLogin.json|JSON Files (*.json)|*.json",
                Multiselect = false
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string json = File.ReadAllText(dlg.FileName);
                if (json != null)
                {
                    STokenBuildData = json;
                    SelectedMode = "Sophon";
                    Confirm_Click();
                }
            }
        }
    }

}
