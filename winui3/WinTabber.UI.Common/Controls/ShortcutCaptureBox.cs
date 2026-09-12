using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinTabber.Events.Shortcuts;
using WinTabber.Events.Shortcuts.Detection;

namespace WinTabber.UI.Common.Controls;

/// <summary>
/// Captures a shortcut from live global input.
/// <para>
/// <b>Why not WinUI3 keyboard events:</b> WinUI3 cannot see the Win key reliably and cannot see
/// mouse buttons pressed outside the window, so capture goes through
/// <see cref="IShortcutTriggerSource.BeginCapture" /> (§3.2).
/// </para>
/// <para>
/// <b>The hook is never torn down to enter capture mode.</b> The gate lives inside the trigger
/// source: the hook stays alive, command dispatch is muted, and raw input is both suppressed and
/// forwarded here — so pressing Alt+Tab while capturing doesn't switch windows.
/// </para>
/// <para>
/// <b>CapsLock:</b> <c>HyperKeyState</c> honors the same gate and steps aside while capturing, so
/// CapsLock is captured as CapsLock rather than as its Ctrl+Alt+Shift+Win expansion (§3.4).
/// </para>
/// </summary>
[TemplatePart(Name = PartPresenter, Type = typeof(ShortcutPresenter))]
public class ShortcutCaptureBox : Control
{
    private const string PartPresenter = "PART_Presenter";

    /// <summary>
    /// Chords the OS intercepts before any hook sees them. Accepting one silently would produce a
    /// binding that never fires, so they get an inline message instead (§3.4).
    /// </summary>
    private static readonly (ShortcutModifiers Modifiers, ushort Key, string Name)[] ReservedByWindows =
    [
        (ShortcutModifiers.Win, 0x4C, "Win+L"),
        (ShortcutModifiers.Ctrl | ShortcutModifiers.Alt, VirtualKeys.Delete, "Ctrl+Alt+Del"),
    ];

    /// <summary>Backstop if the user walks away mid-capture (§3.3).</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(10);

    private IDisposable? _session;
    private IDisposable? _rawSubscription;
    private DispatcherQueueTimer? _idleTimer;
    private ShortcutModifiers _pendingModifiers;

    public ShortcutCaptureBox()
    {
        DefaultStyleKey = typeof(ShortcutCaptureBox);

        StartCaptureCommand = new RelayCommand(_ => StartCapture(), _ => TriggerSource is not null && !IsCapturing);
        CancelCaptureCommand = new RelayCommand(_ => CancelCapture(), _ => IsCapturing);
        Unloaded += (_, _) => CancelCapture();
        LosingFocus += (_, _) => CancelCapture();

        // Nothing else invokes StartCaptureCommand: the host template (see ShortcutsSettingsPage.xaml)
        // just toggles this control's Visibility on when the row enters edit mode, it never fires the
        // command itself. Without this, becoming visible showed the idle presenter with no capture
        // session behind it, so keystrokes went nowhere.
        RegisterPropertyChangedCallback(
            VisibilityProperty,
            (_, _) =>
            {
                if (Visibility == Visibility.Visible)
                {
                    StartCapture();
                }
                else
                {
                    CancelCapture();
                }
            }
        );
    }

    public static readonly DependencyProperty TriggerProperty = DependencyProperty.Register(
        nameof(Trigger),
        typeof(ShortcutTrigger),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty TriggerSourceProperty = DependencyProperty.Register(
        nameof(TriggerSource),
        typeof(IShortcutTriggerSource),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty AllowMouseButtonsProperty = DependencyProperty.Register(
        nameof(AllowMouseButtons),
        typeof(bool),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(true)
    );

    public static readonly DependencyProperty IsCapturingProperty = DependencyProperty.Register(
        nameof(IsCapturing),
        typeof(bool),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(false)
    );

    public static readonly DependencyProperty PendingChipsProperty = DependencyProperty.Register(
        nameof(PendingChips),
        typeof(IReadOnlyList<ShortcutChip>),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(Array.Empty<ShortcutChip>())
    );

    public static readonly DependencyProperty ValidationMessageProperty = DependencyProperty.Register(
        nameof(ValidationMessage),
        typeof(string),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(null)
    );

    public ShortcutTrigger? Trigger
    {
        get => (ShortcutTrigger?)GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    /// <summary>Supplied by the hosting view model; capture is unavailable until this is set.</summary>
    public IShortcutTriggerSource? TriggerSource
    {
        get => (IShortcutTriggerSource?)GetValue(TriggerSourceProperty);
        set => SetValue(TriggerSourceProperty, value);
    }

    public bool AllowMouseButtons
    {
        get => (bool)GetValue(AllowMouseButtonsProperty);
        set => SetValue(AllowMouseButtonsProperty, value);
    }

    public bool IsCapturing => (bool)GetValue(IsCapturingProperty);

    /// <summary>Live modifier chips while capturing, rendered by the same presenter.</summary>
    public IReadOnlyList<ShortcutChip> PendingChips => (IReadOnlyList<ShortcutChip>)GetValue(PendingChipsProperty);

    public string? ValidationMessage => (string?)GetValue(ValidationMessageProperty);

    public ICommand StartCaptureCommand { get; }

    public ICommand CancelCaptureCommand { get; }

    public event EventHandler<ShortcutTrigger>? Captured;

    public void StartCapture()
    {
        System.IO.File.AppendAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shortcut-capture-debug.log"),
            $"{DateTime.Now:HH:mm:ss.fff} StartCapture called. IsCapturing={IsCapturing} TriggerSource={TriggerSource}\n"
        );
        if (IsCapturing || TriggerSource is not { } source)
        {
            return;
        }

        _pendingModifiers = ShortcutModifiers.None;
        SetValue(ValidationMessageProperty, null);
        SetValue(PendingChipsProperty, Array.Empty<ShortcutChip>());
        SetValue(IsCapturingProperty, true);

        _session = source.BeginCapture(out var raw);

        // IShortcutTriggerSource.BeginCapture's raw observable (WinTabber.Events/Shortcuts/Detection/
        // ShortcutTriggerSource.cs) is backed by a plain Subject<CapturedInput> pushed to from
        // IShortcutCaptureSink.Push, which the global input hook calls from whatever thread SharpHook
        // delivers events on — not the UI thread, and the source performs no marshaling itself. So
        // this control must marshal onto the UI thread before touching dependency properties.
        //
        // WPF's original used Dispatcher (a DispatcherScheduler) via ObserveOn. WinUI 3's
        // DispatcherQueue has no built-in System.Reactive IScheduler, and adding a
        // SynchronizationContextScheduler here would depend on WinUI 3 having installed a
        // SynchronizationContext on this thread, which is not guaranteed the way WPF's Dispatcher
        // one is. Marshaling explicitly via DispatcherQueue.TryEnqueue inside the subscription
        // callbacks avoids that assumption and needs no extra Rx scheduler machinery.
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _rawSubscription = raw.Subscribe(
            input => dispatcherQueue.TryEnqueue(() => OnCapturedInput(input)),
            _ => dispatcherQueue.TryEnqueue(CancelCapture)
        );

        _idleTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _idleTimer.Interval = IdleTimeout;
        _idleTimer.Tick += (_, _) => CancelCapture();
        _idleTimer.Start();

        Focus(FocusState.Programmatic);
    }

    public void CancelCapture()
    {
        System.IO.File.AppendAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shortcut-capture-debug.log"),
            $"{DateTime.Now:HH:mm:ss.fff} CancelCapture called. IsCapturing={IsCapturing}\n"
        );
        if (!IsCapturing)
        {
            return;
        }

        EndSession();
        SetValue(PendingChipsProperty, Array.Empty<ShortcutChip>());
    }

    private void EndSession()
    {
        _idleTimer?.Stop();
        _idleTimer = null;

        _rawSubscription?.Dispose();
        _rawSubscription = null;

        _session?.Dispose();
        _session = null;

        SetValue(IsCapturingProperty, false);
    }

    private void OnCapturedInput(CapturedInput input)
    {
        System.IO.File.AppendAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shortcut-capture-debug.log"),
            $"{DateTime.Now:HH:mm:ss.fff} OnCapturedInput Kind={input.Kind}\n"
        );
        // Any activity resets the idle countdown.
        _idleTimer?.Stop();
        _idleTimer?.Start();

        switch (input.Kind)
        {
            case CapturedInputKind.ModifierDown:
                _pendingModifiers |= input.ModifierBit;
                UpdatePendingChips();
                return;

            case CapturedInputKind.ModifierUp:
                // No completion on modifier release — the user may be re-pressing.
                _pendingModifiers &= ~input.ModifierBit;
                UpdatePendingChips();
                return;

            case CapturedInputKind.KeyDown:
                OnKeyCaptured(input);
                return;

            case CapturedInputKind.MouseDown:
                OnMouseCaptured(input);
                return;
        }
    }

    private void OnKeyCaptured(CapturedInput input)
    {
        if (input.Key.VirtualKey == VirtualKeys.Escape && _pendingModifiers == ShortcutModifiers.None)
        {
            CancelCapture();
            return;
        }

        if (input.Key.VirtualKey == VirtualKeys.Back && _pendingModifiers != ShortcutModifiers.None)
        {
            _pendingModifiers = ShortcutModifiers.None;
            UpdatePendingChips();
            return;
        }

        if (input.Key.IsModifier)
        {
            // A modifier key that the mask did not classify; treat as a modifier, not a completion.
            return;
        }

        if (FindReserved(_pendingModifiers, input.Key.VirtualKey) is { } reserved)
        {
            SetValue(ValidationMessageProperty, $"{reserved} is reserved by Windows and cannot be captured.");
            return;
        }

        Complete(new ShortcutTrigger.Keyboard { Modifiers = _pendingModifiers, Key = input.Key });
    }

    private void OnMouseCaptured(CapturedInput input)
    {
        if (!AllowMouseButtons)
        {
            return;
        }

        if (_pendingModifiers == ShortcutModifiers.None)
        {
            // Binding a bare mouse button would swallow ordinary clicking.
            SetValue(
                ValidationMessageProperty,
                "A mouse shortcut needs at least one modifier. Hold Ctrl, Alt, Shift or Win first."
            );
            return;
        }

        Complete(new ShortcutTrigger.KeyMouse { Modifiers = _pendingModifiers, Button = input.Button });
    }

    private void Complete(ShortcutTrigger trigger)
    {
        EndSession();
        SetValue(PendingChipsProperty, Array.Empty<ShortcutChip>());
        SetValue(ValidationMessageProperty, null);

        Trigger = trigger;
        Captured?.Invoke(this, trigger);
    }

    private void UpdatePendingChips() =>
        SetValue(PendingChipsProperty, ShortcutChips.BuildInProgress(_pendingModifiers));

    private static string? FindReserved(ShortcutModifiers modifiers, ushort key)
    {
        foreach (var (reservedModifiers, reservedKey, name) in ReservedByWindows)
        {
            if (reservedKey == key && modifiers == reservedModifiers)
            {
                return name;
            }
        }

        return null;
    }

    private sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => execute(parameter);
    }
}
