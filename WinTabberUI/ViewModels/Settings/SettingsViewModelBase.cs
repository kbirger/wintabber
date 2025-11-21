using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI.ViewModels.Settings
{
    public abstract class SettingsViewModelBase : ReactiveObject
    {
        public string Name { get; }

        protected SettingsViewModelBase(string name)
        {
            Name = name;
        }
    }
}
