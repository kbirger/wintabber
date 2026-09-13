using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace WinTabberUI.Tests;

/// <summary>
/// Guards against the class of bug found three separate times during this migration (Tasks 3.5,
/// 4a.4, 4a.5): a <c>Window</c>-derived type (or one of its constructor dependencies) missing from
/// <see cref="Bootstrapper"/>'s registrations, caught previously only when a real window was
/// constructed at runtime — <c>dotnet build</c> succeeding proves nothing about whether the DI
/// graph actually resolves.
/// </summary>
/// <remarks>
/// <para>
/// This does NOT construct the <c>Window</c> instances, or even the ReactiveUI-based ViewModels
/// they depend on. Verified experimentally (temporary spikes, not kept): both
/// <c>sp.GetRequiredService&lt;SuspendedWindowsWindow&gt;()</c> and, more fundamentally,
/// resolving <c>SuspendedWindowsViewModel</c> on its own trigger ReactiveUI's <c>RxApp</c> static
/// initializer, which calls <c>Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()</c>
/// and throws a bare <see cref="System.Runtime.InteropServices.COMException"/>
/// ("ClassFactory cannot supply requested class") outside a real WinUI 3 <see cref="Application"/>.
/// A second spike confirmed even <c>DispatcherQueueController.CreateOnCurrentThread()</c> alone
/// throws the same way in this headless TUnit process — the Windows App SDK runtime here is only
/// ever bootstrapped by <c>App.OnLaunched</c>/<c>Application.Start</c>, which a test host does not
/// run. So actual construction of these types is not feasible in this test framework.
/// </para>
/// <para>
/// Instead this resolves down to — but not including — invoking any constructor at all: for every
/// <c>Window</c>/<c>WindowEx</c> type registered in the container, it walks that type's own
/// constructor parameters and asserts each parameter type has a matching DI registration via
/// <see cref="IServiceProviderIsService.IsService"/>, which answers "would this resolve?" purely
/// from the registration table, without invoking any factory or constructor. That is a pure static
/// check unaffected by the ReactiveUI/dispatcher constraint above, and it still catches exactly the
/// failure mode all three prior bugs shared: a constructor parameter (a `Window` type itself, or a
/// ViewModel it needs) that nobody registered in <see cref="Bootstrapper"/>.
/// </para>
/// </remarks>
public class BootstrapperDiResolutionTests
{
    [Test]
    public async Task AllRegisteredWindowTypes_HaveResolvableConstructorDependencies()
    {
        using var sp = Bootstrapper.Init();
        var isService = sp.GetRequiredService<IServiceProviderIsService>();

        var windowTypes = typeof(Bootstrapper).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => typeof(Window).IsAssignableFrom(t) || IsWindowEx(t))
            .Where(isService.IsService)
            .ToList();

        // Sanity check on the test itself: if nothing matches, the discovery logic (or the
        // registrations it depends on) silently stopped working, and the loop below would pass
        // vacuously by checking zero types. DockWindow and SuspendedWindowsWindow are both
        // registered Transient in Bootstrapper as of this migration's Phase 4a.
        await Assert.That(windowTypes).IsNotEmpty();

        var failures = new List<string>();

        foreach (var windowType in windowTypes)
        {
            var ctor = windowType
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (ctor is null)
            {
                failures.Add($"{windowType.Name}: no public constructor found");
                continue;
            }

            foreach (var parameter in ctor.GetParameters())
            {
                if (!isService.IsService(parameter.ParameterType))
                {
                    failures.Add(
                        $"{windowType.Name}: constructor parameter '{parameter.Name}' " +
                        $"({parameter.ParameterType.Name}) is not registered in Bootstrapper");
                }
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    private static bool IsWindowEx(Type t)
    {
        for (var current = t; current is not null; current = current.BaseType)
        {
            if (current.FullName == "WinUIEx.WindowEx")
            {
                return true;
            }
        }

        return false;
    }
}
