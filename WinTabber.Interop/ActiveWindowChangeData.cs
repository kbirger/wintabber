using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.Interop
{
    public record ActiveWindowChangeData(int Handle, int IdChild, uint ThreadId, uint Time);
}
