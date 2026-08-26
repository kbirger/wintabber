using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WinTabber.Events.Shortcuts;
using WinTabber.Events.Shortcuts.Detection;

namespace WinTabber.UI.Common.Controls;

/// <summary>
/// Captures a shortcut from live global input.
/// <para>
/// <b>Why not WPF keyboard events:</b> WPF cannot see the Win key reliably and cannot see mouse
/// buttons pressed outside the window, so capture goes through
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
    private DispatcherTimer? _idleTimer;
    private ShortcutModifiers _pendingModifiers;

    static ShortcutCaptureBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ShortcutCaptureBox),
            new FrameworkPropertyMetadata(typeof(ShortcutCaptureBox))
        );
    }

    public ShortcutCaptureBox()
    {
        StartCaptureCommand = new RelayCommand(_ => StartCapture(), _ => TriggerSource is not null && !IsCapturing);
        CancelCaptureCommand = new RelayCommand(_ => CancelCapture(), _ => IsCapturing);
        Unloaded += (_, _) => CancelCapture();
        LostKeyboardFocus += (_, _) => CancelCapture();

        // Nothing else invokes StartCaptureCommand: the host template (see ShortcutsSettingsPage.xaml)
        // just toggles this control's Visibility on when the row enters edit mode, it never fires the
        // command itself. Without this, becoming visible showed the idle presenter with no capture
        // session behind it, so keystrokes went nowhere.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                StartCapture();
            }
            else
            {
                CancelCapture();
            }
        };
    }

    public static readonly DependencyProperty TriggerProperty = DependencyProperty.Register(
        nameof(Trigger),
        typeof(ShortcutTrigger),
        typeof(ShortcutCaptureBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault)
    );

    public static readonly DependencyProperty TriggerSourceProperty = DependencyProperty.Register(
        nameof(TriggerSource),
        typeof(IShortcutTriggerSource),
        typeof(ShortcutCaptureBox),
        new FrameworkPropertyMetadata(null)
    );

    public static readonly DependencyProperty AllowMouseButtonsProperty = DependencyProperty.Register(
        nameof(AllowMouseButtons),
        typeof(bool),
        typeof(ShortcutCaptureBox),
        new FrameworkPropertyMetadata(true)
    );

    private static readonly DependencyPropertyKey IsCapturingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCapturing),
        typeof(bool),
        typeof(ShortcutCaptureBox),
        new FrameworkPropertyMetadata(false)
    );

    public static readonly DependencyProperty IsCapturingProperty = IsCapturingPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey PendingChipsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PendingChips),
        typeof(IReadOnlyList<ShortcutChip>),
        typeof(ShortcutCaptureBox),
        new FrameworkPropertyMetadata(Array.Empty<ShortcutChip>())
    );

    public static readonly DependencyProperty PendingChipsProperty = PendingChipsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ValidationMessagePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ValidationMessage),
        typeof(string),
        typeof(ShortcutCaptureBox),
        new FrameworkPropertyMetadata(null)
    );

    public static readonly DependencyProperty ValidationMessageProperty =
        ValidationMessagePropertyKey.DependencyProperty;

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
        if (IsCapturing || TriggerSource is not { } source)
        {
            return;
        }

        _pendingModifiers = ShortcutModifiers.None;
        SetValue(ValidationMessagePropertyKey, null);
        SetValue(PendingChipsPropertyKey, Array.Empty<ShortcutChip>());
        SetValue(IsCapturingPropertyKey, true);

        _session = source.BeginCapture(out var raw);
        _rawSubscription = raw.ObserveOn(Dispatcher).Subscribe(OnCapturedInput, _ => CancelCapture());

        _idleTimer = new DispatcherTimer(IdleTimeout, DispatcherPriority.Normal, (_, _) => CancelCapture(), Dispatcher);
        _idleTimer.Start();

        Keyboard.Focus(this);
    }

    public void CancelCapture()
    {
        if (!IsCapturing)
        {
            return;
        }

        EndSession();
        SetValue(PendingChipsPropertyKey, Array.Empty<ShortcutChip>());
    }

    private void EndSession()
    {
        _idleTimer?.Stop();
        _idleTimer = null;

        _rawSubscription?.Dispose();
        _rawSubscription = null;

        _session?.Dispose();
        _session = null;

        SetValue(IsCapturingPropertyKey, false);
    }

    private void OnCapturedInput(CapturedInput input)
    {
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
            SetValue(ValidationMessagePropertyKey, $"{reserved} is reserved by Windows and cannot be captured.");
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
                ValidationMessagePropertyKey,
                "A mouse shortcut needs at least one modifier. Hold Ctrl, Alt, Shift or Win first."
            );
            return;
        }

        Complete(new ShortcutTrigger.KeyMouse { Modifiers = _pendingModifiers, Button = input.Button });
    }

    private void Complete(ShortcutTrigger trigger)
    {
        EndSession();
        SetValue(PendingChipsPropertyKey, Array.Empty<ShortcutChip>());
        SetValue(ValidationMessagePropertyKey, null);

        Trigger = trigger;
        Captured?.Invoke(this, trigger);
    }

    private void UpdatePendingChips() =>
        SetValue(PendingChipsPropertyKey, ShortcutChips.BuildInProgress(_pendingModifiers));

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
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => execute(parameter);
    }
}
