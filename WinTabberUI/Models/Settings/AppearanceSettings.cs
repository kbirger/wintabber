using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI.Models.Settings
{
    public class AppearanceSettings
    {
        public float ScaleFactor { get; set; } = 1.0f;
        public bool ScaleToDpi { get; set; } = true;

        public double WindowTileWidth { get; set; } = 250;
    }
}
