using WinTabberUI.Services;

namespace WinTabberUI.Models.Settings
{
    public class GeneralSettings
    {
        public StartupMode StartupMode { get; set; } = StartupMode.Disabled;

        public ThumbnailResizeMode ThumbnailResizeMode { get; set; } = ThumbnailResizeMode.ResizeSource;

        /// <summary>
        /// Turns the window suspend feature on or off: the sleep button on a window tile, the
        /// suspend shortcut, the shortcut that shows the suspended-windows bar, and that bar's
        /// display inside the window selector.
        /// </summary>
        public bool EnableWindowSuspension { get; set; } = true;

        /// <summary>
        /// Turns the media controls feature on or off: the shortcut that opens the media controls
        /// window, and the preload of data that feature uses (installed apps, audio devices).
        /// </summary>
        public bool EnableMediaControls { get; set; } = true;
    }
}
