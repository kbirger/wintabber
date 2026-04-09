using System.Windows;
using WinTabberUI.ViewModels;

namespace WinTabberUI;

/// <summary>
/// Interaction logic for RenameWindow.xaml
/// </summary>
public partial class RenameWindow : Window
{
    public RenameWindow()
    {
        InitializeComponent();
    }

    public static RenameWindow ShowFor(WindowItem item)
    {
        var window = new RenameWindow
        {
            DataContext = new WindowRenameViewModel
            {
                WindowItem = item
            }
        };

        window.Show();

        return window;
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        ((WindowRenameViewModel)DataContext).Apply();
        Close();
    }

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
