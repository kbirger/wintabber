using ReactiveUI;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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


   
    private void TextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        //_originalText = OriginalText;
    }

   

}
