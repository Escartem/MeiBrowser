using System;
using System.IO;
using System.Text.Json;

namespace GUI
{
    public static class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MeiBrowser", "settings.json");

        public static string SelectedTheme { get; set; } = "Dark";

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var data = JsonSerializer.Deserialize<SettingsData>(json);
                    if (data != null)
                        SelectedTheme = data.SelectedTheme ?? "Dark";
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir!);

                var data = new SettingsData { SelectedTheme = SelectedTheme };
                var json = JsonSerializer.Serialize(data);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        private class SettingsData
        {
            public string? SelectedTheme { get; set; }
        }
    }
}
