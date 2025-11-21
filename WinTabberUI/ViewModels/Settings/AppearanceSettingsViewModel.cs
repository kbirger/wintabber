using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.Models;
using WinTabberUI.Models.Settings;

namespace WinTabberUI.ViewModels.Settings
{
    public class AppearanceSettingsViewModel : SettingsViewModelBase
    {
        private float _scaleFactor;
        private bool _scaleTodpi;
        public AppearanceSettingsViewModel(AppearanceSettings settings) : base("Appearance")
        {
            ScaleFactor = settings.ScaleFactor;
            ScaleToDpi = settings.ScaleToDpi;
        }

        public float ScaleFactor
        {
            get => _scaleFactor;
            set => this.RaiseAndSetIfChanged(ref _scaleFactor, value);
        }

        public bool ScaleToDpi
        {
            get => _scaleTodpi;
            set => this.RaiseAndSetIfChanged(ref _scaleTodpi, value);
        }
    }
}
