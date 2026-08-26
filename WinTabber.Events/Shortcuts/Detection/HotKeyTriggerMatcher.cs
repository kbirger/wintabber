using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using GlobalHotKeys;

namespace WinTabber.Events.Shortcuts.Detection;

/// <summary>A <c>RegisterHotKey</c> registration the OS refused, usually because another app owns it.</summary>
public sealed record ShortcutRegistrationFailure(ShortcutCommand Command, ShortcutTrigger Trigger);

/// <summary>
/// Routes <see cref="ShortcutTrigger.IsHotKeyEligible" /> triggers through
/// <c>RegisterHotKey</c>. Replaces <c>WinTabberEventManager.CreateHotKeyEventsObservable</c>.
/// <para>
/// Why keep <c>RegisterHotKey</c> at all when the SharpHook hook is always running: (a) it takes an
/// exclusive OS-level claim, so a failed registration is free conflict detection against *other*
/// applications, surfaced via <see cref="Failures" />; (b) it keeps working while
/// <c>WinTabberEventManager.Pause()</c> has torn the hook down.
/// </para>
/// <para>
/// Unlike the code this replaces, registrations are owned here rather than pushed into the event
/// manager's <c>_resources</c> list, and there is no <c>??=</c> init-once pattern — so
/// <see cref="Rebind" /> can genuinely re-register, and nothing is orphaned.
/// </para>
/// </summary>
public sealed class HotKeyTriggerMatcher : IDisposable
{
    private readonly HotKeyManager _manager = new();
    private readonly IScheduler _scheduler;
    private readonly List<IDisposable> _registrations = new();
    private readonly Subject<ShortcutRegistrationFailure> _failures = new();
    private readonly object _gate = new();

    private Dictionary<int, ShortcutActivation> _mappings = new();
    private bool _disposed;

    /// <param name="scheduler">
    /// Must be the same scheduler the rest of the event manager uses. <c>RegisterHotKey</c> binds to
    /// the thread that pumps messages for the hidden window, so rebinding from a different
    /// EventLoopScheduler would intermittently fail. It also serializes rebinds against in-flight
    /// presses (§8).
    /// </param>
    public HotKeyTriggerMatcher(IScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>
    /// Fires for each registered hotkey press.
    /// <para>
    /// Unmapped ids are dropped. The code this replaces returned <c>0</c> for an unmapped id, and
    /// because <c>EventType</c> ordinal 0 is <c>CmdNextWindow</c>, a stray hotkey id would silently
    /// switch windows.
    /// </para>
    /// </summary>
    public IObservable<ShortcutActivation> Activations =>
        _manager
            .HotKeyPressed.Select(hotKey =>
            {
                lock (_gate)
                {
                    return _mappings.TryGetValue(hotKey.Id, out var activation) ? activation : (ShortcutActivation?)null;
                }
            })
            .Where(activation => activation.HasValue)
            .Select(activation => activation!.Value);

    /// <summary>Registrations the OS rejected. Surfaced in the settings UI as an inline warning.</summary>
    public IObservable<ShortcutRegistrationFailure> Failures => _failures;

    /// <summary>
    /// Drops every prior registration and re-registers from <paramref name="map" />. Marshalled onto
    /// the event-loop scheduler so it cannot race an in-flight press.
    /// </summary>
    public void Rebind(ShortcutMap map) => _scheduler.Schedule(() => RebindCore(map));

    private void RebindCore(ShortcutMap map)
    {
        if (_disposed)
        {
            return;
        }

        // Old registrations must go first: RegisterHotKey fails a chord that's already registered
        // anywhere in this process, even under a different id. Registering the new set before
        // releasing the old one meant every *unchanged* binding collided with itself and was
        // reported as rejected — which then also tore down its own previously-working registration
        // once the (empty-for-that-slot) new set replaced it below.
        List<IDisposable> previous;
        lock (_gate)
        {
            previous = [.. _registrations];
            _registrations.Clear();
            _mappings = new Dictionary<int, ShortcutActivation>();
        }

        foreach (var registration in previous)
        {
            try
            {
                registration.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to unregister a hotkey: {ex.Message}");
            }
        }

        var mappings = new Dictionary<int, ShortcutActivation>();
        var registrations = new List<IDisposable>();
        var failures = new List<ShortcutRegistrationFailure>();

        foreach (var binding in map.Bindings)
        {
            if (binding.Trigger is not ShortcutTrigger.Keyboard keyboard || !keyboard.IsHotKeyEligible)
            {
                continue;
            }

            IRegistration registration;
            try
            {
                registration = _manager.Register(
                    SharpHookAdapters.ToGlobalVirtualKey(keyboard.Key),
                    SharpHookAdapters.ToGlobalModifiers(keyboard.Modifiers)
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RegisterHotKey threw for {binding.Command} ({keyboard}): {ex.Message}");
                failures.Add(new ShortcutRegistrationFailure(binding.Command, binding.Trigger));
                continue;
            }

            if (!registration.IsSuccessful)
            {
                // Another application already owns this chord at the OS level.
                failures.Add(new ShortcutRegistrationFailure(binding.Command, binding.Trigger));
                if (registration is IDisposable rejected)
                {
                    rejected.Dispose();
                }
                continue;
            }

            mappings[registration.Id] = new ShortcutActivation(binding.Command, binding.Trigger);
            if (registration is IDisposable disposable)
            {
                registrations.Add(disposable);
            }
        }

        lock (_gate)
        {
            _registrations.AddRange(registrations);
            _mappings = mappings;
        }

        foreach (var failure in failures)
        {
            _failures.OnNext(failure);
        }
    }

    public void Dispose()
    {
        _disposed = true;

        lock (_gate)
        {
            foreach (var registration in _registrations)
            {
                try
                {
                    registration.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to unregister a hotkey during dispose: {ex.Message}");
                }
            }

            _registrations.Clear();
            _mappings = new Dictionary<int, ShortcutActivation>();
        }

        _manager.Dispose();
        _failures.Dispose();
    }
}
