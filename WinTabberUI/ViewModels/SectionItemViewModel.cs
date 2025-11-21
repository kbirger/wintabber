using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI.ViewModels
{
    public class SectionItemViewModel
    {
        public required string Property { get; init; }
        public required string DisplayName { get; init; }
        public required object SectionInstance { get; init; }

        public static SectionItemViewModel Named(string name, object instance) =>
            new SectionItemViewModel
            {
                Property = name,
                DisplayName = name,
                SectionInstance = instance
            };
    }
}
