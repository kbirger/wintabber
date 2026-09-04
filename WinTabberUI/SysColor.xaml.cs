using System.Reflection;
using System.Windows;
using System.Windows.Media;

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
    public required string Name { get; set; }
    public required Brush Color { get; set; }
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
                Brush systemColor = (Brush)property.GetValue(null, null)!;
                //Brush brush = new SolidColorBrush(systemColor);
                SystemColorsList.Add(new SystemColorInfo { Name = property.Name, Color = systemColor });
            }
        }
    }
}
