using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dark.Net;

namespace GUI
{
    public partial class ThemedDialog : Window
    {
        private MessageBoxResult result = MessageBoxResult.None;

        private ThemedDialog()
        {
            InitializeComponent();
            DarkNet.Instance.SetWindowThemeWpf(this, CurrentTheme());
        }

        public static MessageBoxResult Show(
            string message,
            string title = "",
            MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None,
            Window? owner = null)
        {
            var dialog = new ThemedDialog
            {
                Title = string.IsNullOrEmpty(title) ? " " : title
            };
            dialog.MessageText.Text = message;
            dialog.ApplyIcon(icon);
            dialog.BuildButtons(buttons);

            owner ??= ActiveOwner();
            if (owner != null && !ReferenceEquals(owner, dialog))
            {
                try { dialog.Owner = owner; }
                catch { dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen; }
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();
            return dialog.result;
        }

        private static Window? ActiveOwner()
        {
            if (Application.Current == null) return null;

            var windows = Application.Current.Windows.OfType<Window>().Where(w => w.IsLoaded).ToList();
            return windows.FirstOrDefault(w => w.IsActive)
                ?? (Application.Current.MainWindow?.IsLoaded == true ? Application.Current.MainWindow : null);
        }

        private static Theme CurrentTheme()
        {
            if (Application.Current?.TryFindResource("WindowBackgroundColor") is Color background)
            {
                double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
                return luminance > 0.5 ? Theme.Light : Theme.Dark;
            }
            return Theme.Dark;
        }

        private void ApplyIcon(MessageBoxImage icon)
        {
            (string glyph, Color colour) = icon switch
            {
                MessageBoxImage.Error => ("!", Color.FromRgb(0xE0, 0x52, 0x52)),
                MessageBoxImage.Warning => ("!", Color.FromRgb(0xE0, 0xA0, 0x50)),
                MessageBoxImage.Question => ("?", Color.FromRgb(0x4C, 0x9A, 0xFF)),
                MessageBoxImage.Information => ("i", Color.FromRgb(0x4C, 0x9A, 0xFF)),
                _ => ("", Colors.Transparent)
            };

            if (glyph.Length == 0)
            {
                IconBadge.Visibility = Visibility.Collapsed;
                return;
            }

            IconBadge.Visibility = Visibility.Visible;
            IconBadge.Background = new SolidColorBrush(colour);
            IconGlyph.Text = glyph;
        }

        private void BuildButtons(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.OKCancel:
                    AddButton("OK", MessageBoxResult.OK, isDefault: true);
                    AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                    break;

                case MessageBoxButton.YesNo:
                    AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                    AddButton("No", MessageBoxResult.No, isCancel: true);
                    break;

                case MessageBoxButton.YesNoCancel:
                    AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                    AddButton("No", MessageBoxResult.No);
                    AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                    break;

                default:
                    AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true);
                    break;
            }
        }

        private void AddButton(string caption, MessageBoxResult value, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button
            {
                Content = caption,
                MinWidth = 88,
                Height = 28,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel
            };

            button.Click += (_, _) =>
            {
                result = value;
                DialogResult = true;
            };

            ButtonPanel.Children.Add(button);

            if (isCancel)
                Closed += (_, _) => { if (result == MessageBoxResult.None) result = value; };
        }
    }
}
