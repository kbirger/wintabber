using ReactiveUI;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinTabberUI.ViewModels;

namespace WinTabberUI;

public partial class EditableTextBlock : ReactiveUserControl<WindowItem>
{

    public EditableTextBlock()
    {
        InitializeComponent();
        //PreviewMouseDown += BorderContainer_MouseDown;
        BorderContainer.PreviewMouseDown += BorderContainer_MouseDown;
        BorderContainer.MouseEnter += BorderContainer_MouseEnter;
        PreviewMouseDown += BorderContainer_MouseDown;
        TextBox.PreviewMouseDown += BorderContainer_MouseDown;
    }


    private void BorderContainer_MouseEnter(object sender, MouseEventArgs e)
    {
    }

    private void BorderContainer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsUnderButton(e.OriginalSource))
        {
            // Let the button handle the click (e.g. the suspend button) instead of entering edit mode.
            return;
        }

        if(ViewModel is null)
        {
            return;
        }
        if(ViewModel.CanEdit)
        {
            using (ViewModel.StartEditCommand.Execute().Subscribe())
            { }
            TextBox.Focus();
            TextBox.CaretIndex = TextBox.Text.Length;
            //e.Handled = true;
        }
    }

    /// <summary>
    /// Walks up the visual tree from <paramref name="originalSource"/> looking for a <see cref="ButtonBase"/>.
    /// </summary>
    private static bool IsUnderButton(object? originalSource)
    {
        if (originalSource is not DependencyObject node)
        {
            return false;
        }

        while (node is not null)
        {
            if (node is ButtonBase)
            {
                return true;
            }
            // OriginalSource can be a content element (e.g. a Run); VisualTreeHelper throws on those.
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }


   
    private void TextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        //_originalText = OriginalText;
    }

   

}
