using SharpHook;
using SharpHook.Data;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Forms;
using System.Windows.Threading;
using WinTabber.Events.Shortcuts;
using WinTabber.Events.Shortcuts.Detection;
using WinTabber.Interop;
using static WinTabber.Events.InputListenerService;

namespace WinTabber.Events;

public class WinTabberEventManager : IDisposable, IWinTabberEventManager, INotifyPropertyChanged
{
    private IInteropProxy _interop;
    private readonly InputListenerService _inputListener;
    private readonly IShortcutMapProvider _mapProvider;

    public WinTabberEventManager(
        IInteropProxy interop,
        InputListenerService inputListener,
        IShortcutMapProvider mapProvider
    )
    {
        _interop = interop;
        _inputListener = inputListener;
        _mapProvider = mapProvider;
        Init();
    }

    private List<IDisposable> _resources = new();
    private readonly Subject<WinTabberEvent> _subject = new Subject<WinTabberEvent>();
    private readonly Subject<WinTabberEvent> _commitSubject = new Subject<WinTabberEvent>();
    private readonly SwitcherCommitTracker _commitTracker = new();
    private ShortcutTriggerSource _triggerSource = null!;

    private BehaviorSubject<bool> _enabled = new BehaviorSubject<bool>(false);

    [MemberNotNull(nameof(CommandEvents), nameof(ApplicationChange), nameof(WindowChange))]
    internal WinTabberEventManager Init()
    {
        // One scheduler instance, threaded everywhere. GetScheduler() creates a *new* EventLoopScheduler
        // on each call, and RegisterHotKey binds to the thread that pumps messages for its hidden
        // window — so rebinding from a second scheduler would intermittently fail.
        var scheduler = GetScheduler();
        var connection = GetConnection();

        _triggerSource = new ShortcutTriggerSource(_mapProvider, connection, scheduler);
        _resources.Add(_triggerSource);
        _resources.Add(ConnectHyperKey(connection));

        var activations = _triggerSource
            .Activations.Do(activation => _commitTracker.OnActivation(activation))
            .Select(activation => new WinTabberEvent(activation.Command.ToEventType()))
            .Publish()
            .RefCount();

        // §5: commit is derived from the trigger that actually fired, never bound and never
        // recomputed from the map. The tracker closes itself on commit, so the only external
        // closes to report are the other ways a switcher ends.
        _resources.Add(
            _triggerSource
                .HeldModifiers.Where(held => _commitTracker.OnHeldModifiersChanged(held))
                .Subscribe(_ => _commitSubject.OnNext(new WinTabberEvent(EventType.CmdCommitSelection)))
        );

        CommandEvents = Observable.Merge(activations, _commitSubject, _subject).Publish().RefCount();

        _resources.Add(
            CommandEvents
                .Where(evt => evt.Type.IsOneOf(EventType.WindowSelected, EventType.CmdAppHide))
                .Subscribe(_ => _commitTracker.OnSwitcherClosed())
        );

        WindowChange = CreateWindowChangeObservable(scheduler);
        ApplicationChange = CreateApplicaionChangeObservable(scheduler);

        return this;
    }

    /// <summary>
    /// Whether the SharpHook hook is currently attached. Backed by <c>_enabled</c>: it used to read
    /// <c>_hooksConnection != null</c>, a field that was never assigned, so this always reported
    /// false and the tray toggle could never show the running state.
    /// </summary>
    public bool IsRunning
    {
        get => _enabled.Value;
        private set
        {
            if (value != IsRunning)
            {
                if (value)
                {
                    Start();
                }
                else
                {
                    Pause();
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
            }
        }
    }

    private IObservable<InputListenerEvents> GetConnection()
    {
        //var scheduler = GetScheduler();

        return _enabled
            .Select(state =>
            {
                if (!state)
                {
                    return Observable.Empty<InputListenerEvents>();
                }

                return Observable.Defer(() =>
                    _inputListener.GetEvents(new()
                    {
                        //KeyChords = []
                    })).Publish().RefCount() ;
            })
            .Switch()
            .Replay(1)
            .RefCount();       
    }

    public void Pause()
    {
        _enabled.OnNext(false);        
    }

    public void Start()
    {
        _enabled.OnNext(true);
    }
    private IObservable<WinTabberEvent<string>> CreateApplicaionChangeObservable(EventLoopScheduler scheduler)
    {
        return ObserveActiveApplicationChange()
            .SubscribeOn(scheduler);
    }

    private IObservable<WinTabberEvent<int>> CreateWindowChangeObservable(EventLoopScheduler scheduler)
    {
        return ObserveActiveWindowChange()
            .SubscribeOn(scheduler);
    }


    /// <summary>
    /// Attaches the CapsLock hyperkey to each live hook connection.
    /// <para>
    /// Shortcut detection no longer lives here — it moved to <see cref="ShortcutTriggerSource" />.
    /// The hyperkey stays separate because it is an input <i>transform</i>, not a bindable shortcut.
    /// It is handed the capture gate so CapsLock captures as CapsLock instead of as its
    /// Ctrl+Alt+Shift+Win expansion (§3.4).
    /// </para>
    /// </summary>
    private IDisposable ConnectHyperKey(IObservable<InputListenerEvents> connection)
    {
        return connection
            .Select(events =>
                new HyperKeyState(KeyCode.VcCapsLock, events.KeyDownEvents, events.KeyUpEvents, _interop, _triggerSource)
                    .Connect()
            )
            .Switch()
            .Subscribe(_ => { }, _ => { });
    }

    private IObservable<WinTabberEvent<string>> ObserveActiveApplicationChange()
    {
        return WindowChange.Select(evt => _interop.GetWindowProcessId(evt.Arg))
        .DistinctUntilChanged()
        .Select(pid => Process.GetProcessById(pid).ProcessName)
        .DistinctUntilChanged()
        .Select(processName => new WinTabberEvent<string>(EventType.ActiveApplicatonChanged, processName));
    }

    private IObservable<WinTabberEvent<int>> ObserveActiveWindowChange()
    {
        return _interop.ActiveWindowChangedEvents()
            .Select(data => data.Handle)
            .StartWith(_interop.GetForegroundWindowHandle())
            .DistinctUntilChanged()
            .Where(evt => evt != 0)
            .Select(evt => new WinTabberEvent<int>(EventType.ActiveWindowChanged, evt));
    }

    public void SendEvent(WinTabberEvent evt)
    {
        _subject.OnNext(evt);
    }


    public event PropertyChangedEventHandler? PropertyChanged;

    public IObservable<WinTabberEvent> CommandEvents { get; private set; }
    public IObservable<WinTabberEvent<int>> WindowChange { get; private set; }
    public IObservable<WinTabberEvent<string>> ApplicationChange { get; private set; }
    /// <summary>
    /// The live trigger source. The shortcuts settings page needs it for
    /// <see cref="IShortcutTriggerSource.BeginCapture" /> and for
    /// <see cref="IShortcutTriggerSource.RegistrationFailures" />.
    /// </summary>
    public IShortcutTriggerSource TriggerSource => _triggerSource;

    public void Dispose()
    {
        _resources.ForEach(r => r.Dispose());
        _resources.Clear();
        _subject.Dispose();
        _commitSubject.Dispose();
    }

    private EventLoopScheduler GetScheduler()
    {
        return new EventLoopScheduler(ts =>
        {
            var thread = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                ts();
                Application.Run();
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            return thread;
        });
    }

}
