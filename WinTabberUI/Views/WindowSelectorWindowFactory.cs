using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Views;
public class WindowSelectorWindowFactory(IServiceProvider serviceProvider)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public WindowSelectorWindow CreateWindowSelectorWindow()
    {
        var instance = new WindowSelectorWindow();
        instance.DataContext = _serviceProvider.GetRequiredService<WindowSelectorViewModel>();

        return instance;
    }
}
