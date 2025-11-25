using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.Services;

namespace WinTabberUI.Models.Settings
{
    public class GeneralSettings
    {
        public StartupMode StartupMode { get; set; } = StartupMode.Disabled;
    }
}
