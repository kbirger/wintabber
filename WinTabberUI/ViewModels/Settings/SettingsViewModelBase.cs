using iNKORE.UI.WPF.Modern.Common.IconKeys;
using ReactiveUI;

namespace WinTabberUI.ViewModels.Settings
{
    public abstract class SettingsViewModelBase : ReactiveObject
    {
        public string Name { get; }

        public FontIconData Icon { get; }

        protected SettingsViewModelBase(string name, FontIconData icon)
        {
            Name = name;
            Icon = icon;

        }


    }
}
