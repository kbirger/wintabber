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

    /// <summary>
    /// Derives from the shared <see cref="WindowChange" /> stream rather than subscribing to
    /// <c>_interop.ActiveWindowChangedEvents()</c> directly, so it never installs a second
    /// WinEvent hook.
    /// </summary>
    private IObservable<WinTabberEvent<string>> ObserveActiveApplicationChange()
    {
        return WindowChange
            .Select(evt => _interop.GetWindowProcessId(evt.Arg))
            .DistinctUntilChanged()
            .Select(TryGetProcessName)
            // §3: Process.GetProcessById(pid).ProcessName throws if the process has already
            // exited by the time we look it up. Because WindowChange (and therefore this
            // stream) is now shared across every subscriber, an unhandled exception here would
            // OnError the sequence permanently for all of them. TryGetProcessName swallows that
            // race and returns null, which we simply skip - one stale notification is dropped
            // instead of killing the stream.
            .Where(processName => processName is not null)
            .DistinctUntilChanged()
            .Select(processName => new WinTabberEvent<string>(EventType.ActiveApplicatonChanged, processName!));
    }

    private static string? TryGetProcessName(int pid)
    {
        try
        {
            return Process.GetProcessById(pid).ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Process exited between the foreground-change notification and this lookup - skip it.
            return null;
        }
    }

    /// <summary>
    /// Exactly one <c>SetWinEventHook</c> exists no matter how many subscribers
    /// <see cref="WindowChange" /> (or the derived <see cref="ApplicationChange" />) has.
    /// <para>
    /// <c>hookChanges</c> is the shared, multicast tail: the raw hook events, deduped and
    /// filtered once, <c>Publish().RefCount()</c>'d so the hook installs on first subscriber
    /// and unhooks when the last one leaves. <c>DistinctUntilChanged</c> living here means its
    /// state is global rather than per-subscriber - that is intentional and correct, because it
    /// is deduping the one true sequence of real hardware foreground-change notifications that
    /// every subscriber should see identically, not any per-subscriber view state.
    /// </para>
    /// <para>
    /// The <c>StartWith</c> value is a different story and must stay per-subscriber. It is
    /// wrapped in <see cref="Observable.Defer{TResult}(Func{IObservable{TResult}})" /> so
    /// <c>GetForegroundWindowHandle()</c> is (re-)evaluated fresh every time someone subscribes,
    /// including a subscriber that arrives long after the first one connected. Two alternatives
    /// were rejected: <c>Publish().RefCount()</c> alone caches nothing, so a late subscriber
    /// gets no value at all until the next real foreground change; <c>Replay(1).RefCount()</c>
    /// caches the last value that actually flowed through the shared stream, but once the last
    /// subscriber unsubscribes RefCount disconnects (and unhooks), so a late subscriber arriving
    /// after a gap with no listeners would either replay nothing or replay an arbitrarily stale
    /// value. Deferring a fresh live query is cheap (a single GetForegroundWindow call) and is
    /// always correct regardless of how long the stream sat unsubscribed.
    /// </para>
    /// </summary>
    private IObservable<WinTabberEvent<int>> ObserveActiveWindowChange()
    {
        var hookChanges = _interop
            .ActiveWindowChangedEvents()
            .Select(data => data.Handle)
            .DistinctUntilChanged()
            .Where(handle => handle != 0)
            .Select(handle => new WinTabberEvent<int>(EventType.ActiveWindowChanged, handle))
            .Publish()
            .RefCount();

        return Observable
            .Defer(() =>
                hookChanges.StartWith(
                    new WinTabberEvent<int>(EventType.ActiveWindowChanged, _interop.GetForegroundWindowHandle())
                )
            )
            .Where(evt => evt.Arg != 0)
            .DistinctUntilChanged();
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
