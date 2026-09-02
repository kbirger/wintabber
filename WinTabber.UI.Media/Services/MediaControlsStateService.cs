using ReactiveUI;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using WinTabber.Events;
using WinTabber.Interop;
using WinTabber.UI.Media.Services;

namespace WinTabberUI.Services;
public partial class MediaControlsStateService(WinTabberEventManager eventManager, IInteropProxy interop)
    : IMediaControlsStateService
{
    private readonly WinTabberEventManager _eventManager = eventManager;
    private readonly IInteropProxy _interop = interop;
    private readonly BehaviorSubject<bool> _visibilityEvents = new BehaviorSubject<bool>(false);
    private readonly CompositeDisposable _cleanUp = new CompositeDisposable();

    // Three threads push into _visibilityEvents: the taskpool thread of the hotkey, the user
    // interface thread of Window.OnDeactivated, and the callback thread of the foreground hook.
    // A subject does not serialize notifications, and ToggleView reads the value before it
    // writes it. The gate covers both.
    private readonly object _gate = new object();

    public void HideView()
    {
        lock (_gate)
        {
            _visibilityEvents.OnNext(false);
        }
    }

    public void ToggleView()
    {
        lock (_gate)
        {
            _visibilityEvents.OnNext(!_visibilityEvents.Value);
        }
    }

    [Lazy]
    private IObservable<bool> GetIsMediaControlsVisibleChanges()
    {
        _eventManager
            .CommandEvents.SubscribeOn(RxSchedulers.TaskpoolScheduler)
            .Where(evt => evt.Type == EventType.CmdMediaWindow)
            .Subscribe(
                _ => ToggleView(),
                ex => Debug.WriteLine($"media controls: hotkey stream failed: {ex}")
            )
            .DisposeWith(_cleanUp);

        // While the window is visible, watch the system foreground window as well.
        // MediaControlsWindow.OnDeactivated covers the plain case, but it is not enough: a
        // dropdown is a separate window of this process, and after the user opens one the main
        // window no longer holds the activation. No further deactivation reaches it.
        _visibilityEvents
            .DistinctUntilChanged()
            .Select(isVisible => isVisible ? ObserveFirstForeignForegroundWindow() : Observable.Empty<int>())
            .Switch()
            // The handler calls HideView, which pushes into the same subject that feeds this
            // pipeline. Without this the push happens inside the delivery of the inner sequence,
            // which breaks the rule that a source must not raise a notification from a re-entrant
            // call path. Switch then loses track of the inner sequence, and the watcher stops
            // working until a hide arrives from another thread. ObserveOn queues the handler
            // instead, so the call stack unwinds first.
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(
                handle =>
                {
                    Debug.WriteLine($"media controls: foreground moved to {handle} outside this process - hiding");
                    HideView();
                },
                ex => Debug.WriteLine($"media controls: foreground watch failed: {ex}")
            )
            .DisposeWith(_cleanUp);

        _visibilityEvents.DisposeWith(_cleanUp);
        return _visibilityEvents;
    }

    /// <summary>
    /// Emits the handle of the first foreground window that belongs to another process.
    /// <para>
    /// This reads ForegroundWindowChanges, not WindowChange. Two properties of WindowChange break
    /// this use. It starts with the window that holds the foreground now, which is the window the
    /// user came from. It also removes a handle that repeats the previous one, and this process
    /// can take the foreground without the hook reporting it, so a click back on the window the
    /// user came from looks like a repeat and disappears.
    /// </para>
    /// <para>
    /// An earlier version waited for a window of this process to take the foreground first. That
    /// test fails on every show after the first one, for the same reason: the system reports no
    /// change of the foreground window when ForceForeground moves it to the media window.
    /// </para>
    /// </summary>
    private IObservable<int> ObserveFirstForeignForegroundWindow()
    {
        return Observable.Defer(() =>
        {
            Debug.WriteLine("media controls: foreground watch starts");
            return _eventManager
                .ForegroundWindowChanges.Select(handle => (Handle: handle, IsOwn: IsOwnProcess(handle)))
                .Do(change => Debug.WriteLine($"media controls: foreground {change.Handle} own={change.IsOwn}"))
                // A dropdown is a separate window of this process. It must not hide the view.
                .Where(change => !change.IsOwn)
                .Select(change => change.Handle)
                .Take(1)
                .Finally(() => Debug.WriteLine("media controls: foreground watch ends"));
        });
    }

    private bool IsOwnProcess(int handle)
    {
        try
        {
            return _interop.GetWindowProcessId(handle) == Environment.ProcessId;
        }
        catch (Exception ex)
        {
            // The window can disappear between the notification and the lookup. Treat an unknown
            // window as foreign: the media window then hides, which is the safe outcome.
            Debug.WriteLine($"media controls: cannot read the process of window {handle}: {ex.Message}");
            return false;
        }
    }
}
