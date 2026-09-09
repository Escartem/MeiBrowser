using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Themes;
using Keys = AvalonDock.Themes.VS2013.Themes.ResourceKeys;

namespace GUI
{
    internal static class DockTheme
    {
        public static void Apply(DockingManager dock)
        {
            if (dock == null) return;

            Color background = Resolve("WindowBackgroundBrush", "WindowBackgroundColor", Color.FromRgb(0x1E, 0x1E, 0x1E));
            bool light = Luminance(background) > 0.5;

            Color foreground = Resolve("WindowForegroundBrush", "WindowForegroundColor",
                light ? Colors.Black : Color.FromRgb(0xE0, 0xE0, 0xE0));
            Color control = Resolve("ControlBackgroundBrush", null, Shade(background, 0.06, light));
            Color border = Resolve("ControlBorderBrush", null, Shade(background, 0.14, light));
            Color highlight = Resolve("ControlHighlightBrush", null, Shade(background, 0.22, light));
            Color dim = Resolve("ControlDisabledForegroundBrush", null, Blend(foreground, background, 0.45));

            dock.Theme = light ? new Vs2013LightTheme() : new Vs2013DarkTheme();

            // panes and the well behind the documents
            Set(dock, Keys.Background, background);
            Set(dock, Keys.TabBackground, background);
            Set(dock, Keys.PanelBorderBrush, border);

            // document tabs
            Set(dock, Keys.DocumentWellTabSelectedActiveBackground, highlight);
            Set(dock, Keys.DocumentWellTabSelectedActiveText, foreground);
            Set(dock, Keys.DocumentWellTabSelectedInactiveBackground, border);
            Set(dock, Keys.DocumentWellTabSelectedInactiveText, foreground);
            Set(dock, Keys.DocumentWellTabUnselectedBackground, control);
            Set(dock, Keys.DocumentWellTabUnselectedText, dim);
            Set(dock, Keys.DocumentWellTabUnselectedHoveredBackground, border);
            Set(dock, Keys.DocumentWellTabUnselectedHoveredText, foreground);
            Set(dock, Keys.DocumentWellTabButtonSelectedActiveGlyph, foreground);
            Set(dock, Keys.DocumentWellTabButtonSelectedInactiveGlyph, foreground);
            Set(dock, Keys.DocumentWellTabButtonUnselectedTabHoveredGlyph, foreground);
            Set(dock, Keys.DocumentWellOverflowButtonDefaultGlyph, foreground);

            // the console pane's caption and tab
            Set(dock, Keys.ToolWindowCaptionActiveBackground, control);
            Set(dock, Keys.ToolWindowCaptionActiveText, foreground);
            Set(dock, Keys.ToolWindowCaptionActiveGrip, border);
            Set(dock, Keys.ToolWindowCaptionInactiveBackground, control);
            Set(dock, Keys.ToolWindowCaptionInactiveText, dim);
            Set(dock, Keys.ToolWindowCaptionInactiveGrip, border);
            Set(dock, Keys.ToolWindowCaptionButtonActiveGlyph, foreground);
            Set(dock, Keys.ToolWindowCaptionButtonInactiveGlyph, dim);
            Set(dock, Keys.ToolWindowTabSelectedActiveBackground, highlight);
            Set(dock, Keys.ToolWindowTabSelectedActiveText, foreground);
            Set(dock, Keys.ToolWindowTabSelectedInactiveBackground, border);
            Set(dock, Keys.ToolWindowTabSelectedInactiveText, foreground);
            Set(dock, Keys.ToolWindowTabUnselectedBackground, control);
            Set(dock, Keys.ToolWindowTabUnselectedText, dim);
            Set(dock, Keys.ToolWindowTabUnselectedHoveredBackground, border);
            Set(dock, Keys.ToolWindowTabUnselectedHoveredText, foreground);

            // panes torn off into their own windows
            Set(dock, Keys.FloatingDocumentWindowBackground, background);
            Set(dock, Keys.FloatingDocumentWindowBorder, border);
            Set(dock, Keys.FloatingToolWindowBackground, background);
            Set(dock, Keys.FloatingToolWindowBorder, border);

            // the docking hints shown while dragging a pane
            Set(dock, Keys.DockingButtonBackgroundBrushKey, control);
            Set(dock, Keys.DockingButtonForegroundBrushKey, border);
            Set(dock, Keys.DockingButtonForegroundArrowBrushKey, foreground);
            Set(dock, Keys.PreviewBoxBackgroundBrushKey, Transparentize(highlight, 0.35));
            Set(dock, Keys.PreviewBoxBorderBrushKey, foreground);

            ApplyContextMenus(dock, control, foreground, border);
            dock.Background = new SolidColorBrush(background);
        }

        private static void ApplyContextMenus(DockingManager dock, Color background, Color foreground, Color border)
        {
            if (Application.Current.TryFindResource("DockContextMenuItemTemplate") is not ControlTemplate itemTemplate)
                return;

            foreach (string resourceKey in new[]
            {
                "AvalonDockThemeVs2013DocumentContextMenu",
                "AvalonDockThemeVs2013AnchorableContextMenu"
            })
            {
                if (dock.TryFindResource(resourceKey) is not ContextMenu menu)
                    continue;

                menu.Background = Brush(background);
                menu.Foreground = Brush(foreground);
                menu.BorderBrush = Brush(border);
                menu.BorderThickness = new Thickness(1);
                menu.Padding = new Thickness(0);

                ApplyMenuItems(menu.Items, itemTemplate, background, foreground, border);
            }
        }

        private static void ApplyMenuItems(ItemCollection items, ControlTemplate itemTemplate,
            Color background, Color foreground, Color border)
        {
            foreach (object entry in items)
            {
                if (entry is not MenuItem item)
                    continue;

                item.Template = itemTemplate;
                item.Background = Brush(background);
                item.Foreground = Brush(foreground);
                item.BorderBrush = Brush(border);
                item.BorderThickness = new Thickness(0);
                item.Padding = new Thickness(8, 4, 8, 4);

                ApplyMenuItems(item.Items, itemTemplate, background, foreground, border);
            }
        }

        private static void Set(DockingManager dock, object key, Color colour)
        {
            dock.Resources[key] = Brush(colour);
        }

        private static SolidColorBrush Brush(Color colour)
        {
            var brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }

        private static Color Resolve(string brushKey, string? colourKey, Color fallback)
        {
            var app = Application.Current;
            if (app == null) return fallback;

            if (app.TryFindResource(brushKey) is SolidColorBrush brush)
                return brush.Color;

            if (colourKey != null && app.TryFindResource(colourKey) is Color colour)
                return colour;

            return fallback;
        }

        private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

        private static Color Shade(Color c, double amount, bool light)
        {
            double direction = light ? -1 : 1;
            byte Channel(byte value) => (byte)Math.Clamp(value + direction * amount * 255, 0, 255);
            return Color.FromRgb(Channel(c.R), Channel(c.G), Channel(c.B));
        }

        private static Color Blend(Color a, Color b, double weightOfB)
        {
            byte Channel(byte x, byte y) => (byte)Math.Clamp(x * (1 - weightOfB) + y * weightOfB, 0, 255);
            return Color.FromRgb(Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B));
        }

        private static Color Transparentize(Color c, double alpha) =>
            Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);
    }
}
