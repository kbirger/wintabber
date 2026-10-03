using System.ComponentModel;
using System.Diagnostics;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using WinTabber.Events.Shortcuts;
using WinTabber.Infrastructure;
using WinTabber.Interop;
using WinTabberUI.Models.Settings;
using WinTabberUI.Services;

namespace WinTabber.ViewModels.Settings
{
    public record StartupModeItem(string Name, StartupMode Mode);

    public class GeneralSettingsViewModel : SettingsViewModelBase
    {
        public GeneralSettingsViewModel(GeneralSettings settings, GsudoElevationLauncher gsudoElevationLauncher)
            : base("General", IconKey.Settings_32_Filled)
        {
            _settings = settings;
            _gsudoElevationLauncher = gsudoElevationLauncher;
            IsGsudoAvailable = _gsudoElevationLauncher.IsAvailable;
            InstallGsudoCommand = ReactiveCommand.CreateFromTask(InstallGsudoAsync);
            _showGsudoInstallPrompt = this.WhenAnyValue(
                    x => x.ElevationBackend,
                    x => x.IsGsudoAvailable,
                    (backend, available) => backend == ElevationBackend.Gsudo && !available
                )
                .ToProperty(this, x => x.ShowGsudoInstallPrompt);
            StartupMode = settings.StartupMode;
            ThumbnailResizeMode = settings.ThumbnailResizeMode;
            EnableWindowSuspension = settings.EnableWindowSuspension;
            EnableMediaControls = settings.EnableMediaControls;
            EnableCloseApplicationWindows = settings.EnableCloseApplicationWindows;
            EnableFocusSelect = settings.EnableFocusSelect;
            FocusSelectModifier = settings.FocusSelectModifier;
            FocusSelectScope = settings.FocusSelectScope;
            ElevationBackend = settings.ElevationBackend;
        }

        private StartupMode _startupMode;
        private ThumbnailResizeMode _thumbnailResizeMode;
        private bool _enableWindowSuspension;
        private bool _enableMediaControls;
        private bool _enableCloseApplicationWindows;
        private bool _enableFocusSelect;
        private ShortcutModifiers _focusSelectModifier;
        private FocusSelectScope _focusSelectScope;
        private ElevationBackend _elevationBackend;
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
        [ShortcutModifiers.Ctrl, ShortcutModifiers.Alt, ShortcutModifiers.Shift, ShortcutModifiers.Win];

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

        public ElevationBackend ElevationBackend
        {
            get => _elevationBackend;
            set
            {
                _settings.ElevationBackend = value;
                this.RaiseAndSetIfChanged(ref _elevationBackend, value);
            }
        }

        public ElevationBackend[] ElevationBackends => Enum.GetValues<ElevationBackend>();

        private readonly GsudoElevationLauncher _gsudoElevationLauncher;
        private readonly ObservableAsPropertyHelper<bool> _showGsudoInstallPrompt;
        private bool _isGsudoAvailable;

        public bool IsGsudoAvailable
        {
            get => _isGsudoAvailable;
            private set => this.RaiseAndSetIfChanged(ref _isGsudoAvailable, value);
        }

        /// <summary>True only when Gsudo is the selected backend and it isn't actually installed.</summary>
        public bool ShowGsudoInstallPrompt => _showGsudoInstallPrompt.Value;

        public ReactiveCommand<Unit, Unit> InstallGsudoCommand { get; }

        private async Task InstallGsudoAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo { FileName = "winget", UseShellExecute = false };
                startInfo.ArgumentList.Add("install");
                startInfo.ArgumentList.Add("--id");
                startInfo.ArgumentList.Add("gerardog.gsudo");

                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    await process.WaitForExitAsync();
                }
            }
            catch (Win32Exception)
            {
                // winget missing, install failed, user cancelled, etc. — IsGsudoAvailable below
                // simply stays false, and the install card stays visible.
            }

            IsGsudoAvailable = _gsudoElevationLauncher.IsAvailable;
        }
    }
}
