using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace GUI
{
    public sealed class ThemeOption
    {
        public ThemeOption(string name, string source, Brush swatch)
        {
            Name = name;
            Source = source;
            Swatch = swatch;
        }

        public string Name { get; }

        public string Source { get; }

        public Brush Swatch { get; }

        public ResourceDictionary Load() => new() { Source = new Uri(Source, UriKind.Relative) };
    }

    public static class AppThemes
    {
        private static readonly (string Name, string Source)[] Definitions =
        {
            ("Dark", "Themes/Dark.xaml"),
            ("Light", "Themes/Light.xaml"),
            ("Brown", "Themes/Brown.xaml"),
            ("Nord", "Themes/Nord.xaml"),
            ("Ocean", "Themes/Ocean.xaml"),
            ("Forest", "Themes/Forest.xaml"),
            ("Sakura", "Themes/Sakura.xaml"),
        };

        private static List<ThemeOption>? cache;

        public static IReadOnlyList<ThemeOption> All => cache ??= Build();

        public static ThemeOption Default => All[0];

        public static ThemeOption Find(string? name) =>
            All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Default;

        private static List<ThemeOption> Build()
        {
            var options = new List<ThemeOption>();

            foreach (var (name, source) in Definitions)
            {
                try
                {
                    var dictionary = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
                    options.Add(new ThemeOption(name, source, BuildSwatch(dictionary)));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not load theme '{name}': {ex.Message}");
                }
            }

            if (options.Count == 0)
                options.Add(new ThemeOption("Dark", "Themes/Dark.xaml", Brushes.Black));

            return options;
        }

        private static Brush BuildSwatch(ResourceDictionary dictionary)
        {
            Color background = ColourOf(dictionary, "WindowBackgroundBrush", "WindowBackgroundColor", Colors.Black);
            Color control = ColourOf(dictionary, "ControlBackgroundBrush", null, background);
            Color accent = ColourOf(dictionary, "ControlHighlightBrush", null, control);

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(background, 0),
                    new GradientStop(control, 0.55),
                    new GradientStop(accent, 1)
                }
            };

            brush.Freeze();
            return brush;
        }

        private static Color ColourOf(ResourceDictionary dictionary, string brushKey, string? colourKey, Color fallback)
        {
            if (dictionary[brushKey] is SolidColorBrush brush)
                return brush.Color;

            if (colourKey != null && dictionary[colourKey] is Color colour)
                return colour;

            return fallback;
        }
    }
}
