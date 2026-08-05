# Configurable Shortcuts — Design & Implementation Plan

Status: proposed. Target: make every global shortcut in WinTabber user-configurable, with a
settings page, a capture control, a read-only renderer, and persisted configuration.

Audience: implementing agents. Sections 0–1 are inventory/facts. Sections 2–6 are the design.
Section 7 is the phased file-by-file work breakdown. Read section 2 before writing any code —
the data model constrains everything downstream.

---

## 0. Current shortcut inventory

### 0.1 Global shortcuts (fire regardless of focus)

| # | Command | Current trigger | Mechanism | Defined at |
|---|---------|-----------------|-----------|-----------|
| 1 | `CmdNextWindow` | `Alt + \`` (VK_OEM_3) | `RegisterHotKey` (GlobalHotKeys pkg) | `WinTabber.Events/WinTabberEventManager.cs:113` |
| 2 | `CmdPreviousWindow` | `Alt + Shift + \`` | `RegisterHotKey` | `WinTabberEventManager.cs:114` |
| 3 | `CmdMediaWindow` | `Alt + Ctrl + G` | `RegisterHotKey` | `WinTabberEventManager.cs:115` |
| 4 | `CmdDockWindow` | `Win + Ctrl + Left` | SharpHook key-down chord | `WinTabberEventManager.cs:ObserveKeyChords` |
| 5 | `CmdMinimizeWindow` | `Ctrl + Alt + LeftClick` **or** `Ctrl + Mouse5 (XButton2)` | SharpHook mouse-down | `_hkMinPlain` / `_hkMin`, `WinTabberEventManager.cs:35,37` |
| 6 | `CmdMaximizeWindow` | `Ctrl + Alt + RightClick` **or** `Ctrl + Mouse4 (XButton1)` | SharpHook mouse-down | `_hkMaxPlain` / `_hkMax`, `WinTabberEventManager.cs:36,38` |
| 7 | `CmdAppHide` (= commit selection) | **release** of `LeftAlt` | SharpHook key-up | `WinTabberEventManager.cs:ObserveKeyCommands` |
| 8 | *(hyperkey)* CapsLock → Ctrl+Alt+Shift+Win | hold CapsLock; tap = CapsLock passthrough | SharpHook, suppressing | `WinTabber.Events/HyperKeyState.cs` |
| 9 | `CmdShowSettings` | no keyboard binding (tray menu only) | — | `NotifyIconViewModel.cs:60` |

### 0.2 Requested new global shortcuts

| # | Command | Today | Notes |
|---|---------|-------|-------|
| 10 | `CmdThumbnailWindow` | no binding — thumbnails only open when `IWindowThumbnailService` starts tracking | Bind to "thumbnail the active window" (toggle). `ThumbnailWindowCoordinator` already opens a window per tracked entry, so the command calls `StartThumbnail/StopThumbnail` for the foreground handle. |
| 11 | `CmdSuspendedWindows` ("sleep window") | no binding — `SuspendedWindowsViewCoordinator` shows it only while the switcher is active *and* something is suspended | Bind to toggle the sleep window independently of the switcher. Requires the coordinator's `GetChangeEvents()` to merge a command-driven toggle with its existing condition. |

### 0.3 In-window shortcuts (focus-scoped — **out of scope for v1**, see §6.4)

| Surface | Keys | Location |
|---------|------|----------|
| Window selector grid | Arrow keys — spatial navigation | `WinTabberUI/SpatialNavigationListView.cs:13,38-41` |
| Hint mode trigger | `LeftAlt` (key-down inside a hinted window) | `WinTabber.UI.Common/Behaviors/HintBehavior.cs:310` |
| Hint chord entry | `A–Z`, `0–9`, `NumPad0–9`; `Backspace` = undo last char | `WinTabber.UI.Common/Hints/HintChordState.cs:30,80-89` |

### 0.4 Facts discovered during inventory (do not "fix" silently)

- **Bindings are many-to-one.** Commands 5 and 6 each have *two* distinct triggers today. Any
  schema that stores one binding per command loses half of the existing behavior.
- **Modifiers are side-specific by accident.** `GetMods()` reads only `EventMask.LeftCtrl /
  LeftAlt / LeftShift / LeftMeta`; `ObserveKeyChords` requires `LeftMeta|LeftCtrl`;
  `ObserveKeyCommands` fires on `VcLeftAlt` only. **Right-side modifiers do not work today.**
  This is a bug, not a feature — see decision D2.
- **`CreateHotKeyEventsObservable` is init-once.** It uses `??=` and pushes registrations into
  `_resources`, so hotkeys can never be re-registered. Runtime rebinding requires restructuring
  it (§4.3).
- **`EventType` mixes commands with notifications.** `ActiveWindowChanged`,
  `ActiveApplicatonChanged`, `WindowSelected`, `EditingStateChanged` are not bindable. Only the
  `Cmd*` subset is.
- **`EventType` has no explicit ordinals.** Persisting numeric enum values would silently remap
  every saved binding when a member is inserted. Persist stable string ids (decision D4).
- **The SharpHook hook is already always running** (HyperKey needs it). So `RegisterHotKey` is
  not about avoiding the hook — see §3.

---

## 1. Design decisions (settle these first)

- **D1 — Binding cardinality.** Config is `command → List<Binding>`. Every command may have zero
  or more bindings. UI renders a list per command with add/remove.
- **D2 — Modifier sidedness.** `ModifierSet` is **side-agnostic** (`Ctrl`, `Alt`, `Shift`, `Win`),
  matching `RegisterHotKey` semantics. Hook matching accepts either side. This fixes the existing
  right-modifier bug. Do not preserve left-only matching.
- **D3 — Commit signal.** The "release of Alt ends switcher mode" behavior is generalized into a
  derived `CmdCommitSelection` event (§5), **not** by overloading `CmdAppHide`.
  `CmdAppHide` remains as-is because `App.xaml.cs:24` sends it programmatically and
  `WindowSelectorViewModel.cs:107` maps it through `IsEditing`.
- **D4 — Persistence keys.** Commands persist as stable strings (`"NextWindow"`,
  `"PreviousWindow"`, …), decoupled from `EventType` ordinals and from `EventType` member names.
- **D5 — Bindable command set.** Introduce a distinct `ShortcutCommand` enum in
  `WinTabber.Events` covering only bindable commands, with a mapping to `EventType`. The settings
  UI enumerates `ShortcutCommand`, never `EventType`.
- **D6 — Scope.** v1 covers global shortcuts (§0.1 + §0.2). In-window shortcuts (§0.3) are
  designed for but not implemented (§6.4).

---

## 2. Trigger abstraction (task 1)

New namespace: `WinTabber.Events.Shortcuts`. **No dependency on WPF or SharpHook types in the
model** — the model must be usable from `WinTabber.UI.Common` (WPF) and from the event layer.

### 2.1 Core types

```csharp
namespace WinTabber.Events.Shortcuts;

[Flags]
public enum ShortcutModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// Side-agnostic, hardware-independent key identity. Backed by Win32 virtual-key codes so it
/// round-trips to both GlobalHotKeys' VirtualKeyCode and SharpHook's KeyCode.
public readonly record struct ShortcutKey(ushort VirtualKey)
{
    public static readonly ShortcutKey None = new(0);
    public bool IsModifier { get; }     // VK_CONTROL/MENU/SHIFT/LWIN/RWIN + sided variants
}

public enum ShortcutMouseButton { None, Left, Right, Middle, X1, X2 }

/// When the binding fires.
public enum TriggerEdge { Press, Release }

/// A single trigger. Exactly one of Key / MouseButton is set (KeyOnly vs KeyMouse), or neither
/// (ModifierRelease).
public abstract record ShortcutTrigger
{
    public required ShortcutModifiers Modifiers { get; init; }

    /// Keyboard-only: modifiers + one non-modifier key. RegisterHotKey-eligible when Edge==Press.
    public sealed record Keyboard : ShortcutTrigger
    {
        public required ShortcutKey Key { get; init; }
        public TriggerEdge Edge { get; init; } = TriggerEdge.Press;
        public bool Suppress { get; init; }      // swallow the input from downstream apps
    }

    /// Modifiers + exactly one mouse button. Always hook-based.
    public sealed record KeyMouse : ShortcutTrigger
    {
        public required ShortcutMouseButton Button { get; init; }
        public bool Suppress { get; init; }
    }

}
```

There is deliberately **no `ModifierRelease` trigger shape.** Commit-on-modifier-release is derived
per-activation (§5), never user-bound, so nothing would ever construct one; and `Keyboard { Edge =
Release }` already covers "fire when this specific key is released." Do not add one — and do not
add a `"Type": "ModifierRelease"` branch to the JSON converter.

Notes:

- `ShortcutTrigger` is a value record → free equality, which the settings UI needs for conflict
  detection and the matcher needs for dictionary lookup.
- `Suppress` exists because command 4 (`Win+Ctrl+Left`) must not reach the OS. Presence of
  `Suppress` forces hook routing regardless of shape.
- `ShortcutTrigger.Keyboard` with `Edge == Release` is the only release-edge shape. Commit
  behavior does **not** use it (§5).
- `ShortcutKey` wraps a `ushort` VK rather than SharpHook's `KeyCode` or WPF's `Key` so the model
  stays dependency-free. Conversions live in adapter classes (§2.3).

### 2.2 Bindings and the binding set

```csharp
public enum ShortcutCommand      // D5 — bindable commands only
{
    NextWindow, PreviousWindow, CommitSelection, DockWindow,
    MinimizeWindow, MaximizeWindow, MediaWindow, ShowSettings,
    ThumbnailWindow, SuspendedWindows,
}

public sealed record ShortcutBinding(ShortcutCommand Command, ShortcutTrigger Trigger);

/// Immutable snapshot of the whole keymap. Replaced wholesale on save.
public sealed class ShortcutMap
{
    public IReadOnlyList<ShortcutBinding> Bindings { get; }
    public IReadOnlyList<ShortcutTrigger> For(ShortcutCommand command);
    public static ShortcutMap Default { get; }         // §6.2 default table
    public IReadOnlyList<ShortcutConflict> FindConflicts();
}
```

`ShortcutCommand → EventType` mapping lives in one switch (`ShortcutCommandExtensions.ToEventType`).
`CommitSelection` maps to a *new* `EventType.CmdCommitSelection` (D3).

### 2.3 Detection routing

A trigger is **RegisterHotKey-eligible** iff all hold:

1. shape is `Keyboard`
2. `Edge == Press`
3. `!Suppress`
4. `Key != None` and `!Key.IsModifier`

Everything else → hook. That means `KeyMouse` (5, 6), `ModifierRelease` (7), the suppressing dock
chord (4), and any bare-modifier binding route through SharpHook.

**Why keep `RegisterHotKey` at all**, given the hook is always running: (a) it takes an exclusive
OS-level claim, so a failed registration is free conflict detection against *other* applications
— surface that in the settings UI; (b) it keeps working while `WinTabberEventManager.Pause()` has
torn the hook down.

### 2.4 The trigger source

```csharp
/// Which command fired, and *which of its triggers* fired. The trigger is required, not
/// informational: §5 captures its modifier set to decide when the switcher commits.
public readonly record struct ShortcutActivation(ShortcutCommand Command, ShortcutTrigger Trigger);

public interface IShortcutTriggerSource
{
    IObservable<ShortcutActivation> Activations { get; }
    /// Live view of currently-held modifiers; drives the per-activation hold set (§5).
    IObservable<ShortcutModifiers> HeldModifiers { get; }
    /// Exclusive gate. While a capture session is open, Commands emits nothing and
    /// (optionally) raw input is suppressed. Disposing the session restores dispatch.
    IDisposable BeginCapture(out IObservable<CapturedInput> raw);
}
```

Implementation `ShortcutTriggerSource` composes two matchers over the existing plumbing:

- `HotKeyTriggerMatcher` — wraps `GlobalHotKeys.HotKeyManager`; owns
  `Dictionary<int, ShortcutCommand>`; supports `Rebind(ShortcutMap)` which disposes prior
  `IRegistration`s and re-registers (§4.3).
- `HookTriggerMatcher` — subscribes to `InputListenerEvents.KeyDownEvents / KeyUpEvents /
  MouseChords`; maintains a held-modifier bitmask from `UioHookEvent.Mask` (side-agnostic, D2);
  matches `Keyboard`, `KeyMouse`, `ModifierRelease` triggers; sets `SuppressEvent = true` for
  triggers with `Suppress`.

The held-modifier bitmask is the single source of truth for both matchers and for §5.

---

## 3. Capture control (task 2)

`ShortcutCaptureBox` — custom control in `WinTabber.UI.Common/Controls/`, styled in
`Themes/Generic.xaml` (that file already exists and is the project's custom-control convention).

### 3.1 API

```csharp
public class ShortcutCaptureBox : Control
{
    public ShortcutTrigger? Trigger { get; set; }        // DP, TwoWay by default
    public bool IsCapturing { get; }                     // read-only DP
    public bool AllowMouseButtons { get; set; }          // DP, default true
    public ICommand StartCaptureCommand { get; }
    public event EventHandler<ShortcutTrigger>? Captured;
}
```

Visually it hosts a `ShortcutPresenter` (§4) when idle, and a "Press a shortcut… (Esc to cancel)"
prompt with live in-progress chips while capturing. **The presenter is reused, not duplicated**,
so chip rendering exists in exactly one place.

### 3.2 Capture must be hook-based

WPF keyboard events cannot see the Win key reliably and cannot see mouse buttons pressed outside
the window. Capture therefore goes through `IShortcutTriggerSource.BeginCapture`.

Critical: **do not tear down and recreate the hook to enter capture mode.** The gate lives inside
the trigger source — the hook stays alive, command dispatch is muted, and raw input is forwarded
to the capture session. While capturing, set `SuppressEvent = true` (the same mechanism
`HyperKeyState.Connect` already uses at `HyperKeyState.cs:90`) so pressing Alt+Tab during capture
doesn't switch windows.

### 3.3 State machine

```
Idle --StartCapture--> Capturing(mods=∅)
  modifier down            -> mods |= m
  modifier up              -> mods &= ~m         (no completion; allows re-press)
  non-modifier key down    -> COMPLETE Keyboard{mods, key}
  mouse button down        -> COMPLETE KeyMouse{mods, button}   (if AllowMouseButtons)
  Esc                      -> CANCEL (Trigger unchanged)
  Backspace                -> clear mods, stay capturing
  lost focus / 10s idle    -> CANCEL
```

`KeyMouse` requires ≥1 modifier (per the user's stated constraint); completing with zero
modifiers on a mouse button is rejected with an inline validation message.

### 3.4 Known traps — state these in code comments

- **CapsLock is intercepted by `HyperKeyState`** and rewritten to "all four modifiers down."
  Capture must bypass the hyperkey transform (subscribe upstream of it, or have `HyperKeyState`
  honor the same capture gate) — otherwise CapsLock captures as `Ctrl+Alt+Shift+Win`. Decision:
  bypass, so CapsLock captures as CapsLock.
- **`Win+L` and `Ctrl+Alt+Del` are not hookable** by design. Maintain an unbindable list and show
  an inline "this shortcut is reserved by Windows" message rather than silently accepting.
- Ignore events where `IsEventSimulated` is true (the app injects modifiers via
  `IInteropProxy.SendInput`).

---

## 4. Read-only renderer (task 3)

`ShortcutPresenter` — custom control in `WinTabber.UI.Common/Controls/`.

```csharp
public class ShortcutPresenter : Control
{
    public ShortcutTrigger? Trigger { get; set; }   // DP
    public Orientation Orientation { get; set; }    // DP, default Horizontal
    public bool ShowEdgeHint { get; set; }          // DP — renders "(release)" for Release/ModifierRelease
}
```

Template: `ItemsControl` over a computed `IReadOnlyList<ShortcutChip>` where
`ShortcutChip(string Text, ChipKind Kind)` and `ChipKind ∈ { Modifier, Key, Mouse, Hint }`.

- **Canonical modifier order**: `Ctrl`, `Alt`, `Shift`, `Win` — always, regardless of input order.
- **Display-name map** (`ShortcutDisplayNames`, static, in `WinTabber.Events.Shortcuts` so both
  the control and any logging can use it):
  - `VK_OEM_3` → `` ` `` ; `VK_OEM_MINUS` → `-` ; punctuation VKs → their glyph
  - `VK_LEFT/UP/RIGHT/DOWN` → `←` `↑` `→` `↓`
  - `X1` → `Mouse 4`, `X2` → `Mouse 5`, `Left` → `Left Click`, `Right` → `Right Click`
  - fallback: `KeyInterop.KeyFromVirtualKey(vk).ToString()` — but that lives in a WPF-side
    partial so the model stays WPF-free.
- `ModifierRelease` renders as e.g. `Alt ⏏` / "Release Alt".
- Empty/`null` trigger renders a muted "Not set".

---

## 5. Generalizing "release of Alt ends switcher mode"

Today: `ObserveKeyCommands` fires `CmdAppHide` on `VcLeftAlt` key-up;
`WindowSelectorViewModel.cs:71-76` commits when the switcher is active.

**Replacement — derived per activation, not configured and not per-map.** The user never binds
"commit"; it is computed from *the trigger that actually fired*:

1. On the activation that opens the switcher (`NextWindow` / `PreviousWindow`), capture
   `ActiveHoldSet = activation.Trigger.Modifiers`. This is why `Activations` carries the trigger
   (§2.4).
2. Emit `EventType.CmdCommitSelection` when `HeldModifiers & ActiveHoldSet` transitions from
   non-zero to zero, while the switcher is active. Clear `ActiveHoldSet` when the switcher closes.
3. `WindowSelectorViewModel` subscribes to `CmdCommitSelection`, replacing its `CmdAppHide`
   subscription at `WindowSelectorViewModel.cs:71-76`.
4. `IsSwitcherActiveChanges` (`:96`) gains `CmdCommitSelection` in its `IsOneOf` filter, mapping
   to `false`. **`CmdAppHide` stays in that filter** with its existing `=> isEditing` mapping
   (`:107`): once `ObserveKeyCommands` is deleted its only producer is `App.xaml.cs:24`
   (app-exit/hide), and that path must keep dismissing the switcher.

**Do not derive `ActiveHoldSet` from the union or intersection of all switcher bindings.** Any
map-wide set mixes in modifiers from bindings that weren't used. Concrete failure: with a second
`NextWindow` binding of `Ctrl+Tab`, a map-wide set contains `{Alt, Ctrl}`; the user activates with
`Alt+\``, presses Ctrl mid-cycle, releases Alt but keeps Ctrl held — `{Ctrl} & {Alt,Ctrl} ≠ 0`, so
the switcher never commits and is stuck open. Per-activation capture is also what Alt-Tab itself
does, and it means nothing needs recomputing when the `ShortcutMap` changes.

**Edge case — no modifiers.** If the activating trigger is modifier-less (e.g. `F13`),
`ActiveHoldSet` is empty and there is no release to commit on. Fallback: while the switcher is
active with an empty hold set, the switcher window itself handles `Enter` (commit) and `Esc`
(cancel). Without this the switcher becomes unclosable — do not skip it.

---

## 6. Settings page (task 4) and configuration (task 5)

### 6.1 Settings page

Follows the existing pattern exactly:

- `WinTabberUI/Models/Settings/ShortcutSettings.cs`
- `WinTabberUI/ViewModels/Settings/ShortcutsSettingsViewModel.cs : SettingsViewModelBase`
  (ctor: `base("Shortcuts", FluentSystemIcons.Keyboard_24_Filled)`)
- `WinTabberUI/Views/ShortcutsSettingsPage.xaml`
- Register in `SettingsViewModel` ctor: construct alongside `General`/`Appearance` and add to
  `Sections` (`SettingsWindowViewModel.cs:41-44`). Save already flows automatically via
  `SubscribeToSettingsChanges()` merging `section.Changed`.
- Add a `DataTemplate` for `ShortcutsSettingsViewModel` in `SettingsWindow.xaml`'s
  `ui:Frame.Resources`.
- Note: the hardcoded `Home`/`Apps`/`Games` items in `NavigationView.MenuItems` are placeholder
  cruft; the new section goes through `Sections`, not there.

Page layout: grouped list (Window Switching / Window Management / Panels), one row per
`ShortcutCommand`:

```
┌───────────────────────────────────────────────────────────────┐
│ Next window                    [Ctrl][`]  ✎ ✕                 │
│                                [+ Add shortcut]               │
├───────────────────────────────────────────────────────────────┤
│ Minimize window                [Ctrl][Alt][Left Click] ✎ ✕     │
│                                [Ctrl][Mouse 5]        ✎ ✕     │
│                                [+ Add shortcut]               │
└───────────────────────────────────────────────────────────────┘
```

Each row: `ShortcutPresenter` when idle, swapped for `ShortcutCaptureBox` on ✎. Per-command
"Reset to default"; page-level "Reset all". Conflicts (same trigger on two commands, or a
`RegisterHotKey` registration rejected by the OS) render an inline warning icon + tooltip; they
do not block saving.

### 6.2 Default map

`ShortcutMap.Default` reproduces §0.1 exactly (with D2's side-agnostic modifiers) plus:
`ThumbnailWindow` = `Alt+Ctrl+T`, `SuspendedWindows` = `Alt+Ctrl+S`, `ShowSettings` =
`Alt+Ctrl+,`. Verify these three don't collide before finalizing.

### 6.3 Config file

Extends the existing `%AppData%\WinTabber\Settings\settings.json`
(`WinTabberUI/Paths.cs:10`) with a `Shortcuts` block:

```jsonc
{
  "Appearance": { ... },
  "General": { ... },
  "Shortcuts": {
    "Version": 1,
    "Bindings": {
      "NextWindow":      [ { "Type": "Keyboard", "Modifiers": "Alt",        "Key": "OemTilde" } ],
      "PreviousWindow":  [ { "Type": "Keyboard", "Modifiers": "Alt, Shift", "Key": "OemTilde" } ],
      "DockWindow":      [ { "Type": "Keyboard", "Modifiers": "Win, Ctrl",  "Key": "Left", "Suppress": true } ],
      "MinimizeWindow":  [ { "Type": "KeyMouse", "Modifiers": "Ctrl, Alt",  "Button": "Left" },
                           { "Type": "KeyMouse", "Modifiers": "Ctrl",       "Button": "X2"   } ],
      "MaximizeWindow":  [ { "Type": "KeyMouse", "Modifiers": "Ctrl, Alt",  "Button": "Right" },
                           { "Type": "KeyMouse", "Modifiers": "Ctrl",       "Button": "X1"   } ],
      "MediaWindow":     [ { "Type": "Keyboard", "Modifiers": "Alt, Ctrl",  "Key": "G" } ],
      "ThumbnailWindow": [ { "Type": "Keyboard", "Modifiers": "Alt, Ctrl",  "Key": "T" } ],
      "SuspendedWindows":[ { "Type": "Keyboard", "Modifiers": "Alt, Ctrl",  "Key": "S" } ]
    }
  }
}
```

Format rules:

- Command keys are the D4 stable strings. **Unknown command keys are ignored, not fatal**
  (forward compat).
- `Modifiers` is the flags enum as a comma-joined string — human-editable and stable.
- `Key` is a name, not a number: serialize via a `ShortcutKey` ↔ name table (the same table
  backing `ShortcutDisplayNames`, keyed by canonical name rather than glyph). Round-trip must be
  lossless; add a unit test asserting `Parse(Format(k)) == k` for every VK in the table.
- `Version` gates future migrations.
- A missing `Shortcuts` block or a missing command entry falls back to `ShortcutMap.Default` for
  that command.
- **Robustness:** `ApplicationSettings.Load()` currently catches only `IOException`
  (`ApplicationSettings.cs:15`). A hand-editable block of key-name strings makes `JsonException`
  reachable — one typo would take down *all* settings loading. Add `JsonException` to the catch,
  and make per-binding parse failures fall back to that command's default rather than throwing.

### 6.4 Out of scope for v1 (design only)

In-window shortcuts (§0.3) stay hardcoded. When they are made configurable, the same
`ShortcutTrigger` model applies; the difference is the detection layer — WPF `InputBinding`s /
`OnPreviewKeyDown` instead of hook/RegisterHotKey. `ShortcutPresenter` and `ShortcutCaptureBox`
are reusable as-is. `HintChordState`'s A–Z/0–9 alphabet is a *hint alphabet* setting, not a
shortcut, and should get its own General-settings field.

---

## 7. Work breakdown

Phase order matters: the model and settings schema must land before either control, since both
controls bind to `ShortcutTrigger`.

### Phase 1 — Model (`WinTabber.Events/Shortcuts/`)
- `ShortcutModifiers.cs`, `ShortcutKey.cs`, `ShortcutMouseButton.cs`, `TriggerEdge.cs`
- `ShortcutTrigger.cs` (abstract + `Keyboard` / `KeyMouse` records — no `ModifierRelease`)
- `ShortcutActivation.cs`
- `ShortcutCommand.cs` + `ShortcutCommandExtensions.ToEventType()`
- `ShortcutBinding.cs`, `ShortcutMap.cs` (incl. `Default`, `For`, `FindConflicts`)
- `ShortcutDisplayNames.cs` (canonical name ↔ VK table; WPF-free)
- Add `EventType.CmdCommitSelection`, `EventType.CmdThumbnailWindow`,
  `EventType.CmdSuspendedWindows` (`WinTabber.Events/EventType.cs`)
- Tests in `WinTabber.Events.Tests`: key-name round-trip, conflict detection, per-activation hold
  capture (incl. the `Ctrl+Tab` second-binding case and the empty-hold-set fallback from §5).

### Phase 2 — Persistence
- `WinTabberUI/Models/Settings/ShortcutSettings.cs` + JSON converters for `ShortcutTrigger`
  polymorphism (`Type` discriminator) and `ShortcutKey`
- Wire `Shortcuts` into `ApplicationSettings`; harden `Load()` (§6.3)
- `IShortcutMapProvider` (`BehaviorSubject<ShortcutMap>`) registered in `Bootstrapper.cs` near
  line 73; settings save pushes a new map.

### Phase 3 — Detection
- `HotKeyTriggerMatcher` — replaces `CreateHotKeyEventsObservable`
  (`WinTabberEventManager.cs:109-133`). Must dispose + re-register + rebuild `_mappings` on every
  map change; drop the `??=` init-once pattern; report registration failures.
- `HookTriggerMatcher` — replaces `ObserveKeyChords`, `ObserveMouseHook`, `ObserveKeyCommands`;
  side-agnostic modifier mask (D2); honors `Suppress`.
- `ShortcutTriggerSource` — merges both, exposes `HeldModifiers` + `BeginCapture`.
- Commit derivation (§5); update `WindowSelectorViewModel.cs:71-76` and `:96-110`.
- `HyperKeyState` — honor the capture gate (§3.4).

### Phase 4 — Controls (`WinTabber.UI.Common/Controls/`)
- `ShortcutPresenter` + `Themes/Generic.xaml` style + WPF-side display-name partial
- `ShortcutCaptureBox` (reuses the presenter)

### Phase 5 — Settings UI
- `ShortcutsSettingsViewModel`, `ShortcutsSettingsPage.xaml`
- `SettingsViewModel` wiring + `SettingsWindow.xaml` `DataTemplate`

### Phase 6 — New commands
- `CmdThumbnailWindow` → toggle tracking on the foreground window via `IWindowThumbnailService`
  (`WinTabber.API/Thumbnails/IWindowThumbnailService.cs`): `IsThumbnailed(handle)` ?
  `StopThumbnail(handle)` : `CanThumbnail(window) && StartThumbnail(window)`. Note
  `StartThumbnail` takes a `WindowRef`, `StopThumbnail`/`IsThumbnailed` take an `int` handle — so
  the command handler needs the `WindowManager`/`ApplicationState` lookup, not just the HWND.
- `CmdSuspendedWindows` → merge a command toggle into
  `SuspendedWindowsViewCoordinator.GetChangeEvents()`
- Optional keyboard binding for `CmdShowSettings`

---

## 8. Risks

| Risk | Mitigation |
|------|-----------|
| Re-registration races with an in-flight hotkey press | Rebind on the event-loop scheduler already used by `WinTabberEventManager.GetScheduler()` |
| User binds a shortcut that makes the app unreachable | Tray menu always exposes Settings (`NotifyIconViewModel.cs:60`); "Reset all" on the page |
| Suppressing input traps the user | Only honor `Suppress` for triggers with ≥1 modifier; never suppress bare keys |
| Capture gate leaks (dispatch stays muted) | `BeginCapture` returns `IDisposable`; add a watchdog timeout that force-closes the session |
| Hyperkey/capture interaction | Explicit bypass (§3.4), covered by a manual test in `Wintabber.SessionsTest` |
