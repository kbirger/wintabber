using Gma.System.MouseKeyHook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace WinTabber
{
    public class Main
    {
        //private IKeyboardMouseEvents m_GlobalHook;

        public void Subscribe()
        {
            var exitVim = Sequence.FromString("Shift+Z");
            var rename = Sequence.FromString("Control+R,R");
            var exitReally = Sequence.FromString("Escape,Escape,Escape");
            var assignment = new Dictionary<Sequence, Action>
{
    {exitVim, ()=>Console.WriteLine("No!")},
    {rename, ()=>Console.WriteLine("rename2")},
    {exitReally, ()=>Console.WriteLine("Ok.")},
};

            Hook.GlobalEvents().OnSequence(assignment);
        }

        }
    }
