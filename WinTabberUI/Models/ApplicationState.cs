using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabber.API;

namespace WinTabberUI.Models
{
    public partial class ApplicationState : ObservableObject
    {

        [ObservableProperty]
        private bool _isWindowSelectorActive;

        [ObservableProperty]
        private bool _isDockWindowActive;

        [ObservableProperty]
        private bool _isMediaControlWindowActive;

        [ObservableProperty]
        private WindowRef? _activeWindow;

        [ObservableProperty]
        private ApplicationRef? _activeApplication;

    }
}
