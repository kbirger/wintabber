using WinTabber.Events.Shortcuts;
using WinTabber.Interop;
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

        /// <summary>
        /// Turns the close-application feature on or off: the close button on the window selector's
        /// chrome and the close-application-windows shortcut.
        /// </summary>
        public bool EnableCloseApplicationWindows { get; set; } = true;

        /// <summary>
        /// Turns Focus Select on or off: minimizing other windows when a selection is committed
        /// while <see cref="FocusSelectModifier" /> is held.
        /// </summary>
        public bool EnableFocusSelect { get; set; } = false;

        /// <summary>The modifier that, held while committing a selection, triggers Focus Select.</summary>
        public ShortcutModifiers FocusSelectModifier { get; set; } = ShortcutModifiers.Ctrl;

        /// <summary>Which windows Focus Select minimizes.</summary>
        public FocusSelectScope FocusSelectScope { get; set; } = FocusSelectScope.SwitcherWindows;

        /// <summary>Which backend closes/minimizes windows belonging to an elevated process.</summary>
        public ElevationBackend ElevationBackend { get; set; } = ElevationBackend.BuiltIn;
    }
}
