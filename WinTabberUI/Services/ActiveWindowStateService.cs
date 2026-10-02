using System.Reactive.Linq;
using ReactiveUI;
using WinTabber.Api.Windowing;
using WinTabber.Events;

namespace WinTabberUI.Services;

/// <summary>
/// WinUI 3 port of <c>WinTabberUI/Services/ActiveWindowStateService.cs</c> (the WPF project's
/// implementation of the same <see cref="IActiveWindowStateService"/> shared interface). Not
/// registered by the original migration trace in the Task 4b.1 brief — this project had no
/// implementation of the interface at all yet, only the WPF one, which this DI graph cannot
/// reference. The WPF version ends both pipelines with <c>.ObserveOnDispatcher()</c>, a
/// System.Windows.Threading.Dispatcher-based Rx extension unavailable here; this port uses
/// <c>.ObserveOn(RxApp.MainThreadScheduler)</c> instead, the same platform-agnostic idiom
/// <c>WinTabber.ViewModels/SuspendedWindowsViewModel.cs</c> already uses and that
/// <c>WinTabber.UI.Common/Controls/ShortcutCaptureBox.cs</c> documents as the reason a raw
/// DispatcherQueue-backed Rx scheduler isn't used instead.
/// <para>
/// Deferred via hand-rolled <see cref="Lazy{T}"/> fields rather than the WPF version's [Lazy]
/// source generator (WinTabber.Generators, not referenced by this project): touching
/// <c>RxApp.MainThreadScheduler</c> triggers ReactiveUI's static initializer, which calls
/// <c>Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()</c> and throws outside a real
/// WinUI 3 <see cref="Microsoft.UI.Xaml.Application"/> (see
/// <c>WinTabberUI.Tests/BootstrapperDiResolutionTests.cs</c>'s documented spikes). Building the
/// pipelines eagerly in the constructor — as this file's first version did — would move that
/// dispatcher-dependent side effect from first property access (this service is a singleton,
/// constructed once) to DI-resolution time, a real timing departure from the WPF version this is
/// meant to mirror exactly, even though it caused no failure with today's callers.
/// </para>
/// </summary>
public sealed class ActiveWindowStateService : IActiveWindowStateService
{
    private readonly Lazy<IObservable<ApplicationRef?>> _applicationChanges;
    private readonly Lazy<IObservable<WindowRef?>> _windowChanges;

    public IObservable<ApplicationRef?> ApplicationChanges => _applicationChanges.Value;
    public IObservable<WindowRef?> WindowChanges => _windowChanges.Value;

    public ActiveWindowStateService(WinTabberEventManager eventManager, WindowManager windowManager)
    {
        _applicationChanges = new Lazy<IObservable<ApplicationRef?>>(() =>
            eventManager
                .ApplicationChange.Select(data => windowManager.GetApplication(data.Arg))
                .Where(applicationRef => applicationRef is null || (applicationRef.IsValidProcess && applicationRef.CurrentWindow() is { }))
                .Replay(1)
                .RefCount()
                .ObserveOn(RxApp.MainThreadScheduler));

        _windowChanges = new Lazy<IObservable<WindowRef?>>(() =>
            eventManager
                .WindowChange.Select(data => windowManager.GetWindow(data.Arg))
                .Where(windowRef => windowRef is null || (windowRef.IsValidUserWindow && windowRef.Process.IsValid))
                // Activation history is recorded here, upstream of the Replay, so that every subscriber
                // observes a history which already includes this change. See the WPF version's identical
                // comment for the ordering rationale.
                .Do(windowRef =>
                {
                    if (windowRef is not null)
                    {
                        windowManager.RegisterForegroundWindowChanged(windowRef.Handle);
                    }
                })
                .Replay(1)
                .RefCount()
                .ObserveOn(RxApp.MainThreadScheduler));
    }
}
