using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace WinTabberUI
{
    public class WindowsViewModel : DependencyObject, INotifyPropertyChanged
    {

        //public event PropertyChangedEventHandler? PropertyChanged;

        public WindowItem[] WindowItems
        {
            get
            {
                return (WindowItem[])GetValue(_windowItems);
            }

            set
            {
                SetValue(_windowItems, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowItems)));
            }
        }

        private DependencyProperty _selectedItem = DependencyProperty.Register(
            "SelectedItem",
            typeof(WindowItem),
            typeof(WindowsViewModel),
            new PropertyMetadata(null));
        private DependencyProperty _selectedIndex = DependencyProperty.Register(
            "SelectedIndex",
            typeof(int),
            typeof(WindowsViewModel),
            new PropertyMetadata(-1));

        private DependencyProperty _windowItems = DependencyProperty.Register(
            "WindowItems",
            typeof(WindowItem[]),
            typeof(WindowsViewModel),
            new PropertyMetadata(Array.Empty<WindowItem>()));

        public event PropertyChangedEventHandler? PropertyChanged;

        public WindowItem SelectedItem
        {
            get { return (WindowItem)GetValue(_selectedItem); }
            set
            {
                if (value != SelectedItem)
                {
                    SetValue(_selectedItem, value);
                    SetValue(_selectedIndex, Array.IndexOf(WindowItems, value));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedItem)));
                }
            }
        }
        public int SelectedIndex
        {
            get { return (int)GetValue(_selectedIndex); }
            set
            {
                if (value >= WindowItems.Length)
                {
                    value = 0;
                }
                else if (value < 0)
                {
                    value = WindowItems.Length - 1;
                }

                if(value != SelectedIndex)
                {
                    SetValue(_selectedIndex, value);
                    SetValue(_selectedItem, WindowItems[value]);
                }

            }
        }
    }
}
