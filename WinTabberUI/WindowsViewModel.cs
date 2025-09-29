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

                SetValue(_selectedIndex, value);
            }
        }
    }
}
