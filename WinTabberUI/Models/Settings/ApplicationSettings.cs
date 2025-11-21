using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI.Models.Settings
{
    public class ApplicationSettings
    {
        public static ApplicationSettings Load()
        {
            return new ApplicationSettings();
        }
        public void Save()
        {

        }

        public AppearanceSettings Appearance { get; set; } = new AppearanceSettings();

        public GeneralSettings General { get; set; } = new GeneralSettings();
    }
}
