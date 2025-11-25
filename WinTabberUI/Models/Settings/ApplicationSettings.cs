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
        }

        private static ApplicationSettings GetDefaultSettings()
        {
            return new ApplicationSettings();
        }
        public void Save()
        {
            using var fileStream = File.Open(Paths.SettingsFilePath, FileMode.Create);
            JsonSerializer.Serialize(fileStream, this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public AppearanceSettings Appearance { get; set; } = new AppearanceSettings();

        public GeneralSettings General { get; set; } = new GeneralSettings();
    }
}
