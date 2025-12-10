using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace WinTabberUI.Hints;
public interface IDecoratedElementProvider
{
    public IEnumerable<DecoratedElementInfo> GetDecoratedElements(FrameworkElement element);
}
