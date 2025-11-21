using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.Models.Settings;

namespace WinTabberUI.ViewModels.Settings
{
    public class GeneralSettingsViewModel : SettingsViewModelBase
    {
        public GeneralSettingsViewModel(GeneralSettings settings) : base("General")
        {
            StartWithWindows = settings.StartWithWindows;
        }

        private bool _startWithWindows;
        public bool StartWithWindows
        {
            get => _startWithWindows;
            set => this.RaiseAndSetIfChanged(ref _startWithWindows, value);
        }
    }
}
