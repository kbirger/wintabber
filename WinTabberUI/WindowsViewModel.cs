using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI
{
    public class WindowsViewModel : INotifyPropertyChanged
    {
        private WindowItem[] _windowItems;

        public event PropertyChangedEventHandler? PropertyChanged;

        public WindowItem[] WindowItems
        {
            get
            {
                return _windowItems;
            }

            set
            {
                if (_windowItems != value)
                {
                    _windowItems = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowItems)));
                }
            }
        }
    }
}
