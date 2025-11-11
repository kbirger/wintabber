using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media;

using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for SysColor.xaml
/// </summary>
public partial class SysColor : Window
{
    public SysColor()
    {
        InitializeComponent();
    }
}


public class SystemColorInfo
{
    public string Name { get; set; }
    public Brush Color { get; set; }
}

public class SystemColorsViewModel
{
    public List<SystemColorInfo> SystemColorsList { get; set; }

    public SystemColorsViewModel()
    {
        SystemColorsList = new List<SystemColorInfo>();
        LoadSystemColors();
    }

    private void LoadSystemColors()
    {
        PropertyInfo[] properties = typeof(SystemColors).GetProperties(BindingFlags.Public | BindingFlags.Static);

        foreach (PropertyInfo property in properties)
        {
            if (property.Name.EndsWith("Brush"))
            {
                Brush systemColor = (Brush)property.GetValue(null, null);
                //Brush brush = new SolidColorBrush(systemColor);
                SystemColorsList.Add(new SystemColorInfo { Name = property.Name, Color = systemColor });
            }
        }
    }
}
