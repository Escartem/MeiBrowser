using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
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

        private string currentPackageId;
        private string currentPassword;

        private bool allowClose = false;

        public StartDialog()
        {
            InitializeComponent();
            DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark);

            GameCombo.ItemsSource = new[]
            {
                new { Name = "Genshin Impact", Icon = "pack://application:,,,/icons/hk4e.png", Id = "hk4e" },
                new { Name = "Honkai: Star Rail", Icon = "pack://application:,,,/icons/hkrpg.png", Id = "hkrpg" },
                new { Name = "Zenless Zone Zero", Icon = "pack://application:,,,/icons/nap.png", Id = "nap" },
                // TODO: add hi3 support
                //new { Name = "Honkai Impact 3rd", Icon = "pack://application:,,,/icons/bh3.png", Id = "bh3" }, // hi3 needs lot of hardcoded stuff cuz it's different
            };

            this.Title = "Select Game Options";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!allowClose)
            {
                Application.Current.Shutdown();
                e.Cancel = true;
            }
            base.OnClosing(e);
        }

        private async void GameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameCombo.SelectedItem == null) return;
            ServerCombo.IsEnabled = true;
            ServerCombo.ItemsSource = null;
            VersionCombo.IsEnabled = false;
            CategoryCombo.IsEnabled = false;
            VersionCombo.ItemsSource = null;
            CategoryCombo.ItemsSource = null;
            ConfirmButton.IsEnabled = false;

            ServerCombo.ItemsSource = new[]
            {
                new ComboBoxItem() { Content = "OS" },
                new ComboBoxItem() { Content = "CN" }
            };
        }

        private async void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerCombo.SelectedItem == null) return;
            VersionCombo.IsEnabled = false;
            CategoryCombo.IsEnabled = false;
            VersionCombo.ItemsSource = null;
            CategoryCombo.ItemsSource = null;
            ConfirmButton.IsEnabled = false;

            currentPackageId = null;
            currentPassword = null;

            LoadingOverlay.Visibility = Visibility.Visible;
            dynamic metaData = await Meta.GetVersions(((dynamic)GameCombo.SelectedItem).Id, (string)(ServerCombo.SelectedItem as ComboBoxItem).Content);
            var versions = (List<string>)metaData.Item1;
            currentPackageId = (string)metaData.Item2;
            currentPassword = (string)metaData.Item3;
            LoadingOverlay.Visibility = Visibility.Collapsed;

            VersionCombo.ItemsSource = versions;
            VersionCombo.IsEnabled = true;
        }

        private async void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionCombo.SelectedItem == null) return;
            CategoryCombo.IsEnabled = false;
            CategoryCombo.ItemsSource = null;
            ConfirmButton.IsEnabled = false;

            LoadingOverlay.Visibility = Visibility.Visible;
            var packages = await Meta.GetPackages((string)(ServerCombo.SelectedItem as ComboBoxItem).Content, (string)VersionCombo.SelectedItem, currentPackageId, currentPassword);

            ComboBoxItem[] packageItems = packages.Select(p =>
                new ComboBoxItem() { Content = $"{p[1]} - {p[2]}", Tag = p[0] }
            ).ToArray();

            LoadingOverlay.Visibility = Visibility.Collapsed;
            CategoryCombo.ItemsSource = packageItems;
            CategoryCombo.IsEnabled = true;
        }

        private async void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ConfirmButton.IsEnabled = true;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            allowClose = true;
            SelectedGame = ((dynamic)GameCombo.SelectedItem)?.Id;
            SelectedServer = (ServerCombo.SelectedItem as ComboBoxItem)?.Content.ToString();
            SelectedVersion = $"{VersionCombo.SelectedItem?.ToString()}.0";
            SelectedCategory = ((dynamic)CategoryCombo.SelectedItem)?.Tag;
            DialogResult = true;
        }
    }

}
