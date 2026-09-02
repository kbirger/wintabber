using iNKORE.UI.WPF.Modern.Common.IconKeys;
using ReactiveUI;
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
        }

        private StartupMode _startupMode;
        private ThumbnailResizeMode _thumbnailResizeMode;
        private bool _enableWindowSuspension;
        private bool _enableMediaControls;
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
    }
}
