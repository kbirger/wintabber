using iNKORE.UI.WPF.Modern.Common.IconKeys;
using ReactiveUI;
using WinTabber.Events.Shortcuts;
using WinTabberUI.Models.Settings;
using WinTabberUI.Services;

namespace WinTabberUI.ViewModels.Settings
{
    public record StartupModeItem(string Name, StartupMode Mode);
    public class GeneralSettingsViewModel : SettingsViewModelBase
    {
        public GeneralSettingsViewModel(GeneralSettings settings)
            : base("General", FluentSystemIcons.Settings_32_Filled)
        {
            _settings = settings;
            StartupMode = settings.StartupMode;
            ThumbnailResizeMode = settings.ThumbnailResizeMode;
            EnableWindowSuspension = settings.EnableWindowSuspension;
            EnableMediaControls = settings.EnableMediaControls;
            EnableCloseApplicationWindows = settings.EnableCloseApplicationWindows;
            EnableFocusSelect = settings.EnableFocusSelect;
            FocusSelectModifier = settings.FocusSelectModifier;
            FocusSelectScope = settings.FocusSelectScope;
        }

        private StartupMode _startupMode;
        private ThumbnailResizeMode _thumbnailResizeMode;
        private bool _enableWindowSuspension;
        private bool _enableMediaControls;
        private bool _enableCloseApplicationWindows;
        private bool _enableFocusSelect;
        private ShortcutModifiers _focusSelectModifier;
        private FocusSelectScope _focusSelectScope;
        private GeneralSettings _settings;

        public StartupMode StartupMode
        {
            get => _startupMode;
            set
            {
                _settings.StartupMode = value;
                this.RaiseAndSetIfChanged(ref _startupMode, value);
            }
        }

        public StartupMode[] StartupModes => Enum.GetValues<StartupMode>();

        public ThumbnailResizeMode ThumbnailResizeMode
        {
            get => _thumbnailResizeMode;
            set
            {
                _settings.ThumbnailResizeMode = value;
                this.RaiseAndSetIfChanged(ref _thumbnailResizeMode, value);
            }
        }

        public ThumbnailResizeMode[] ThumbnailResizeModes => Enum.GetValues<ThumbnailResizeMode>();

        public bool EnableWindowSuspension
        {
            get => _enableWindowSuspension;
            set
            {
                _settings.EnableWindowSuspension = value;
                this.RaiseAndSetIfChanged(ref _enableWindowSuspension, value);
            }
        }

        public bool EnableMediaControls
        {
            get => _enableMediaControls;
            set
            {
                _settings.EnableMediaControls = value;
                this.RaiseAndSetIfChanged(ref _enableMediaControls, value);
            }
        }

        public bool EnableCloseApplicationWindows
        {
            get => _enableCloseApplicationWindows;
            set
            {
                _settings.EnableCloseApplicationWindows = value;
                this.RaiseAndSetIfChanged(ref _enableCloseApplicationWindows, value);
            }
        }

        public bool EnableFocusSelect
        {
            get => _enableFocusSelect;
            set
            {
                _settings.EnableFocusSelect = value;
                this.RaiseAndSetIfChanged(ref _enableFocusSelect, value);
            }
        }

        public ShortcutModifiers FocusSelectModifier
        {
            get => _focusSelectModifier;
            set
            {
                _settings.FocusSelectModifier = value;
                this.RaiseAndSetIfChanged(ref _focusSelectModifier, value);
            }
        }

        /// <summary>Single-modifier choices only — Focus Select checks one flag, not a combination.</summary>
        public ShortcutModifiers[] FocusSelectModifiers { get; } =
        [
            ShortcutModifiers.Ctrl,
            ShortcutModifiers.Alt,
            ShortcutModifiers.Shift,
            ShortcutModifiers.Win,
        ];

        public FocusSelectScope FocusSelectScope
        {
            get => _focusSelectScope;
            set
            {
                _settings.FocusSelectScope = value;
                this.RaiseAndSetIfChanged(ref _focusSelectScope, value);
            }
        }

        public FocusSelectScope[] FocusSelectScopes => Enum.GetValues<FocusSelectScope>();
    }
}
