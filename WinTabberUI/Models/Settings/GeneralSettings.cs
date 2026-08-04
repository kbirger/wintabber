using WinTabberUI.Services;

namespace WinTabberUI.Models.Settings
{
    public class GeneralSettings
    {
        public StartupMode StartupMode { get; set; } = StartupMode.Disabled;

        public ThumbnailResizeMode ThumbnailResizeMode { get; set; } = ThumbnailResizeMode.ResizeSource;
    }
}
