using ReactiveUI;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using WinTabber.Events;
using WinTabber.Interop;
namespace WinTabber.UI.Media.Services;
public partial class MediaControlsStateService(WinTabberEventManager eventManager, Func<bool> isFeatureEnabled)
    : IMediaControlsStateService
{
    private readonly WinTabberEventManager _eventManager = eventManager;
    private readonly Func<bool> _isFeatureEnabled = isFeatureEnabled;
    private readonly BehaviorSubject<bool> _visibilityEvents = new BehaviorSubject<bool>(false);
    private readonly CompositeDisposable _cleanUp = new CompositeDisposable();

    // Three threads push into _visibilityEvents: the taskpool thread of the hotkey, the user
    // interface thread of Window.OnDeactivated, and the callback thread of the foreground hook.
    // A subject does not serialize notifications, and ToggleView reads the value before it
    // writes it. The gate covers both.
    private readonly object _gate = new object();

    private int _windowHandle;

    public void RegisterWindow(int handle) => _windowHandle = handle;

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
            .Where(evt => evt.Type == EventType.CmdMediaWindow && _isFeatureEnabled())
            .Subscribe(
                _ => ToggleView(),
                ex => Debug.WriteLine($"media controls: hotkey stream failed: {ex}")
            )
            .DisposeWith(_cleanUp);

        // While the window is visible, watch the system foreground window. This is the only thing
        // that hides the view when the user clicks away. The window deactivation event cannot do
        // it: ForceForeground can raise one while the window opens.
        _visibilityEvents
            .DistinctUntilChanged()
            .Select(isVisible => isVisible ? ObserveFirstOtherForegroundWindow() : Observable.Empty<int>())
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
                    Debug.WriteLine($"media controls: foreground moved to {handle} - hiding");
                    HideView();
                },
                ex => Debug.WriteLine($"media controls: foreground watch failed: {ex}")
            )
            .DisposeWith(_cleanUp);

        _visibilityEvents.DisposeWith(_cleanUp);
        return _visibilityEvents;
    }

    /// <summary>
    /// Emits the handle of the first foreground window that is not the media controls window.
    /// This includes other windows of this process, such as the settings window. A dropdown of
    /// the media window does not take the foreground, so it does not appear here.
    /// <para>
    /// This reads ForegroundWindowChanges, not WindowChange. Two properties of WindowChange break
    /// this use. It starts with the window that holds the foreground now, which is the window the
    /// user came from. It also removes a handle that repeats the previous one, and this process
    /// can take the foreground without the hook reporting it, so a click back on the window the
    /// user came from looks like a repeat and disappears.
    /// </para>
    /// <para>
    /// The media window itself can report a foreground change when it opens, so its own handle
    /// is skipped.
    /// </para>
    /// </summary>
    private IObservable<int> ObserveFirstOtherForegroundWindow()
    {
        return Observable.Defer(() =>
        {
            Debug.WriteLine("media controls: foreground watch starts");
            return _eventManager
                .ForegroundWindowChanges.Do(handle => Debug.WriteLine($"media controls: foreground {handle}"))
                .Where(handle => handle != _windowHandle)
                .Take(1)
                .Finally(() => Debug.WriteLine("media controls: foreground watch ends"));
        });
    }
}
