using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WinTabberUI;

public partial class EditableTextBlock : UserControl
{
    public class TextUpdatedEventArgs(RoutedEvent routedEvent, string newValue) : RoutedEventArgs(routedEvent)
    {
        public string NewValue { get; } = newValue;
    }

    public delegate void TextUpdatedEventHandler(object sender, TextUpdatedEventArgs e);



    public EditableTextBlock()
    {
        InitializeComponent();
        BorderContainer.MouseDown += BorderContainer_MouseDown;
        BorderContainer.MouseEnter += BorderContainer_MouseEnter;
        PreviewMouseDown += BorderContainer_MouseDown;
    }
    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(EditableTextBlock), new PropertyMetadata(false));

    public static readonly DependencyProperty CurrentTextProperty =
        DependencyProperty.Register(nameof(CurrentText), typeof(string), typeof(EditableTextBlock), new PropertyMetadata(string.Empty));


    public static readonly DependencyProperty EditCommandProperty =
        DependencyProperty.Register(nameof(EditCommand), typeof(ICommand), typeof(EditableTextBlock), new PropertyMetadata(null));

    private void BorderContainer_MouseEnter(object sender, MouseEventArgs e)
    {
    }

    private void BorderContainer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditing)
        {
            IsEditing = true;
            TextBox.Focus();
            TextBox.CaretIndex = TextBox.Text.Length;
        }
    }

    public static readonly DependencyProperty OriginalTextProperty =
        DependencyProperty.Register(
            nameof(OriginalText),
            typeof(string),
            typeof(EditableTextBlock),
            new PropertyMetadata(string.Empty, OnOriginalTextChanged));

    public string OriginalText
    {
        get => (string)GetValue(OriginalTextProperty);
        set => SetValue(OriginalTextProperty, value);
    }


    public string CurrentText
    {
        get => (string)GetValue(CurrentTextProperty);
        private set => SetValue(CurrentTextProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    private static void OnOriginalTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditableTextBlock instance)
        {
            instance.CurrentText = e.NewValue as string ?? string.Empty;
        }
    }

    private void TextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        //_originalText = OriginalText;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        OnTextChanged(CurrentText);
        IsEditing = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CurrentText = OriginalText;
        IsEditing = false;
    }

    public event RoutedEventHandler TextChanged
    {
        add { AddHandler(TextChangedEvent, value); }
        remove { RemoveHandler(TextChangedEvent, value); }
    }
    public event RoutedEventHandler EditCanceled
    {
        add { AddHandler(EditCanceledEvent, value); }
        remove { RemoveHandler(EditCanceledEvent, value); }
    }


    private void OnTextChanged(string newValue)
    {
        if (EditCommand?.CanExecute(newValue) ?? false)
        {
            EditCommand.Execute(newValue);
        }
        RaiseEvent(new TextUpdatedEventArgs(TextChangedEvent, newValue));
    }
    public ICommand? EditCommand 
    { 
        get => (ICommand?)GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    private void OnEditCanceled()
    {
        RaiseEvent(new RoutedEventArgs(EditCanceledEvent));
    }

    public static readonly RoutedEvent TextChangedEvent = EventManager.RegisterRoutedEvent(
        name: "TextChanged",
        routingStrategy: RoutingStrategy.Bubble,
        handlerType: typeof(TextUpdatedEventHandler),
        ownerType: typeof(EditableTextBlock));


    public static readonly RoutedEvent EditCanceledEvent = EventManager.RegisterRoutedEvent(
    name: "EditCanceled",
    routingStrategy: RoutingStrategy.Bubble,
    handlerType: typeof(RoutedEventHandler),
    ownerType: typeof(EditableTextBlock));

}
