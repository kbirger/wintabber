using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace WinTabberUI.Infrastructure
{
    public class ViewLocator(IServiceProvider serviceProvider) : IViewLocator
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;

        public IViewFor? ResolveView<T>(T? viewModel, string? contract = null)
        {
            var view = _serviceProvider.GetRequiredService(typeof(T)) as IViewFor;
            if(view is not null)
            {
                view.ViewModel = viewModel;
            }

            return view;
        }
    }
}
