using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace WinTabberUI.Services;

public class WindowService(IServiceProvider serviceProvider)
{
    protected Dictionary<Type, Window> _windows = new Dictionary<Type, Window>();
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public void ShowWindow<T>() where T : Window
    {
        var key = typeof(T);
        if (!_windows.TryGetValue(key, out var window))
        {
            window = _serviceProvider.GetRequiredService<T>();
            _windows.Add(key, window);
        }

        window.Show();
    }

    public void HideWindow<T>() where T : Window
    {
        var key = typeof(T);
        if(_windows.TryGetValue(key, out var window))
        {
            window.Hide();
        }
    }

    public void CloseWindow<T>() where T: Window
    {
        var key = typeof(T);
        if (_windows.TryGetValue(key, out var window))
        {
            window.Close();
            _windows.Remove(key);
        }
    }
}
