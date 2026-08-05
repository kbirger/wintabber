using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace WinTabberUI.Models.Settings
{
    public class ApplicationSettings
    {
        public static ApplicationSettings Load()
        {
            try
            {
                using var fileStream = File.OpenRead(Paths.SettingsFilePath);
                return JsonSerializer.Deserialize<ApplicationSettings>(fileStream) ?? GetDefaultSettings();
            }
            catch (IOException ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                Directory.CreateDirectory(Paths.SettingsDirectory);
                var settings = GetDefaultSettings();
                settings.Save();
                return settings;
            }
            catch (JsonException ex)
            {
                // §6.3: the Shortcuts block is hand-editable, so malformed JSON is reachable. One
                // typo must not take down all settings loading — fall back to defaults in memory.
                // Deliberately NOT re-saved: overwriting would destroy the file the user is editing.
                Debug.WriteLine($"Settings file is not valid JSON; using defaults for this session. {ex.Message}");
                return GetDefaultSettings();
            }
        }

        private static ApplicationSettings GetDefaultSettings()
        {
            return new ApplicationSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Paths.SettingsDirectory);
            using var fileStream = File.Open(Paths.SettingsFilePath, FileMode.Create);
            JsonSerializer.Serialize(fileStream, this, new JsonSerializerOptions { WriteIndented = true });
        }

        public AppearanceSettings Appearance { get; set; } = new AppearanceSettings();

        public GeneralSettings General { get; set; } = new GeneralSettings();

        public ShortcutSettings Shortcuts { get; set; } = new ShortcutSettings();
    }
}
