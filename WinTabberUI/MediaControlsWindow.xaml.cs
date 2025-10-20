using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinTabber.Interop;

namespace WinTabberUI
{
    /// <summary>
    /// Interaction logic for MediaControlsWindow.xaml
    /// </summary>
    public partial class MediaControlsWindow : Window
    {
        public MediaControlsWindow()
        {
            InitializeComponent();
        }


        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            MediaKeySender.Prev();
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            MediaKeySender.PlayPause();

        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            MediaKeySender.Next();
        }

        

        protected override void OnActivated(EventArgs e)
        {
            Focus();
            base.OnActivated(e);
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            Close();
            base.OnLostFocus(e);
        }
    }
}
