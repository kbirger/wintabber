using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using static WinTabber.Events.InputListenerService;

namespace WinTabber.Events.Shortcuts.Detection;

public interface IShortcutTriggerSource
{
    IObservable<ShortcutActivation> Activations { get; }

    /// <summary>Live view of currently-held modifiers; drives the per-activation hold set (§5).</summary>
    IObservable<ShortcutModifiers> HeldModifiers { get; }

    /// <summary>Registrations the OS refused, for the settings UI's inline warnings.</summary>
    IObservable<ShortcutRegistrationFailure> RegistrationFailures { get; }

    /// <summary>
    /// Exclusive gate. While a capture session is open, <see cref="Activations" /> emits nothing and
    /// raw input is both suppressed and forwarded to <paramref name="raw" />. Disposing the returned
    /// session restores dispatch.
    /// </summary>
    IDisposable BeginCapture(out IObservable<CapturedInput> raw);
}

/// <summary>
/// Merges the <c>RegisterHotKey</c> and hook matchers into one activation stream, and owns the
/// capture gate.
/// </summary>
public sealed class ShortcutTriggerSource : IShortcutTriggerSource, IInputCaptureGate, IShortcutCaptureSink, IDisposable
{
    /// <summary>
    /// Backstop so a leaked session cannot mute command dispatch forever (§8). The capture *control*
    /// enforces its own much shorter idle timeout; this only catches the case where the control is
    /// destroyed without disposing the session.
    /// </summary>
    private static readonly TimeSpan CaptureWatchdog = TimeSpan.FromSeconds(60);

    private readonly IShortcutMapProvider _mapProvider;
    private readonly HotKeyTriggerMatcher _hotKeys;
    private readonly HookTriggerMatcher _hook;
    private readonly BehaviorSubject<ShortcutModifiers> _heldModifiers = new(ShortcutModifiers.None);
    private readonly Subject<ShortcutActivation> _hookActivations = new();
    private readonly CompositeDisposable _cleanUp = new();
    private readonly object _captureGate = new();

    private CaptureSession? _session;

    public ShortcutTriggerSource(
        IShortcutMapProvider mapProvider,
        IObservable<InputListenerEvents> connection,
        IScheduler scheduler
    )
    {
        _mapProvider = mapProvider;
        _hotKeys = new HotKeyTriggerMatcher(scheduler);
        _hook = new HookTriggerMatcher(() => _mapProvider.Current, OnHeldModifiersChanged, this);

        // BehaviorSubject replays immediately, so this doubles as the initial registration. That
        // replaces the old `??=` init-once pattern rather than layering on top of it.
        _cleanUp.Add(_mapProvider.Maps.Subscribe(_hotKeys.Rebind));

        _cleanUp.Add(
            connection.SelectMany(events => _hook.Connect(events)).Subscribe(_hookActivations.OnNext, _ => { })
        );

        Activations = Observable
            .Merge(_hotKeys.Activations, _hookActivations)
            .Where(_ => !IsCapturing)
            .Publish()
            .RefCount();

        _cleanUp.Add(_hotKeys);
        _cleanUp.Add(_hookActivations);
        _cleanUp.Add(_heldModifiers);
    }

    public IObservable<ShortcutActivation> Activations { get; }

    public IObservable<ShortcutModifiers> HeldModifiers => _heldModifiers.DistinctUntilChanged();

    public IObservable<ShortcutRegistrationFailure> RegistrationFailures => _hotKeys.Failures;

    public bool IsCapturing
    {
        get
        {
            lock (_captureGate)
            {
                return _session is not null;
            }
        }
    }

    public IDisposable BeginCapture(out IObservable<CapturedInput> raw)
    {
        var session = new CaptureSession(this);

        lock (_captureGate)
        {
            // A second BeginCapture supersedes the first rather than stacking; two live sessions
            // would both suppress input and only one could ever be disposed by its owner.
            _session?.Dispose();
            _session = session;
        }

        raw = session.Input;
        return session;
    }

    void IShortcutCaptureSink.Push(CapturedInput input)
    {
        CaptureSession? session;
        lock (_captureGate)
        {
            session = _session;
        }

        session?.Push(input);
    }

    private void OnHeldModifiersChanged(ShortcutModifiers modifiers) => _heldModifiers.OnNext(modifiers);

    private void EndCapture(CaptureSession session)
    {
        lock (_captureGate)
        {
            if (ReferenceEquals(_session, session))
            {
                _session = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_captureGate)
        {
            _session?.Dispose();
            _session = null;
        }

        _cleanUp.Dispose();
    }

    private sealed class CaptureSession : IDisposable
    {
        private readonly ShortcutTriggerSource _owner;
        private readonly Subject<CapturedInput> _input = new();
        private readonly IDisposable _watchdog;
        private int _disposed;

        public CaptureSession(ShortcutTriggerSource owner)
        {
            _owner = owner;
            _watchdog = Observable.Timer(CaptureWatchdog).Subscribe(_ => Dispose());
        }

        public IObservable<CapturedInput> Input => _input;

        public void Push(CapturedInput input)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _input.OnNext(input);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _watchdog.Dispose();
            _owner.EndCapture(this);
            _input.OnCompleted();
            _input.Dispose();
        }
    }
}
