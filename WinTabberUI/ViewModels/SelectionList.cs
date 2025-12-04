using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI.ViewModels
{
    public class SelectionList<T> : ObservableCollection<T> where T : ISelectable
    {
        public SelectionList()
        {
        }
        public SelectionList(IEnumerable<T> collection) : base(collection)
        {
        }


        public void SelectItem(T? item)
        {
            if (item is not null && !Contains(item))
            {
                return;
            }

            foreach(var listItem in this)
            {
                listItem.IsSelected = listItem!.Equals(listItem);
            }
        }

        public T? SelectedItem
        {
            get
            {
                return this.SingleOrDefault(item => item.IsSelected);
            }
            set
            {
                OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectedItem)));
                SelectItem(value);
            }
        }
    }
}
