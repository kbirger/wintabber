using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI;

public static class Paths
{
    public static readonly string RoamingDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinTabber");
    public static readonly string SettingsDirectory = Path.Combine(RoamingDataPath, "Settings");

    public static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

}
