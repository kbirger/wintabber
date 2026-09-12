using ReactiveUI;
using WinTabber.Infrastructure;

namespace WinTabber.ViewModels.Settings
{
    public abstract class SettingsViewModelBase : ReactiveObject
    {
        public string Name { get; }

        public IconKey Icon { get; }

        protected SettingsViewModelBase(string name, IconKey icon)
        {
            Name = name;
            Icon = icon;
        }
    }
}
