using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dark.Net;

namespace GUI
{
    public partial class FilterDialog : Window
    {
        private static readonly long[] UnitMultipliers = { 1L << 10, 1L << 20, 1L << 30 };

        public SearchFilter Filter { get; private set; } = new();

        public FilterDialog(SearchFilter current)
        {
            InitializeComponent();
            DarkNet.Instance.SetWindowThemeWpf(this, CurrentTheme());
            Load(current);
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

        private void Load(SearchFilter current)
        {
            TypeSelector.SelectedIndex = current.Type switch
            {
                SearchItemType.FilesOnly => 1,
                SearchItemType.FoldersOnly => 2,
                _ => 0
            };

            // Show each limit in the largest unit that keeps it a whole number
            Show(current.MinSize, MinSizeBox, MinSizeUnit);
            Show(current.MaxSize, MaxSizeBox, MaxSizeUnit);
        }

        private static void Show(long? bytes, TextBox box, ComboBox unit)
        {
            if (bytes is not { } value || value <= 0)
            {
                box.Text = "";
                unit.SelectedIndex = 1; // MB
                return;
            }

            int index = 0;
            for (int i = UnitMultipliers.Length - 1; i >= 0; i--)
            {
                if (value % UnitMultipliers[i] == 0)
                {
                    index = i;
                    break;
                }
            }

            unit.SelectedIndex = index;
            box.Text = (value / (double)UnitMultipliers[index]).ToString("0.##", CultureInfo.CurrentCulture);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRead(MinSizeBox, MinSizeUnit, "at least", out long? min)) return;
            if (!TryRead(MaxSizeBox, MaxSizeUnit, "at most", out long? max)) return;

            if (min.HasValue && max.HasValue && min > max)
            {
                ThemedDialog.Show("The 'at least' size is bigger than the 'at most' size, so nothing could match.",
                    "Check the sizes", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            Filter = new SearchFilter
            {
                Type = TypeSelector.SelectedIndex switch
                {
                    1 => SearchItemType.FilesOnly,
                    2 => SearchItemType.FoldersOnly,
                    _ => SearchItemType.All
                },
                MinSize = min,
                MaxSize = max
            };

            DialogResult = true;
        }

        private bool TryRead(TextBox box, ComboBox unit, string label, out long? bytes)
        {
            bytes = null;
            string text = box.Text.Trim();
            if (text.Length == 0) return true;

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) &&
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                ThemedDialog.Show($"'{text}' is not a number, so the '{label}' size cannot be used.",
                    "Check the sizes", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                box.Focus();
                box.SelectAll();
                return false;
            }

            if (value < 0)
            {
                ThemedDialog.Show($"The '{label}' size cannot be negative.",
                    "Check the sizes", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                box.Focus();
                box.SelectAll();
                return false;
            }

            if (value == 0) return true;

            int index = Math.Clamp(unit.SelectedIndex, 0, UnitMultipliers.Length - 1);
            bytes = (long)Math.Round(value * UnitMultipliers[index]);
            return true;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            TypeSelector.SelectedIndex = 0;
            MinSizeBox.Text = "";
            MaxSizeBox.Text = "";
            MinSizeUnit.SelectedIndex = 1;
            MaxSizeUnit.SelectedIndex = 1;
        }
    }
}
