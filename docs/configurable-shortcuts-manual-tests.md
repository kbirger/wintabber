# Configurable Shortcuts — manual verification checklist

The implementation of `configurable-shortcuts-plan.md` is complete and the automated suite is green
(130 tests). Global hook behavior is essentially not unit-testable, so the items below were **never
verified against a running app**. They are the known-unknowns, not a general regression list.

## Unverified assumptions (highest risk first)

| # | Assumption | Failure signature if wrong |
|---|-----------|---------------------------|
| 1 | `KeyboardEventData.RawCode` equals the Win32 VK on Windows. Documented libuiohook behavior; it is what lets the code skip a KeyCode↔VK table. | **No hook-routed keyboard trigger matches at all.** |
| 2 | `RegisterHotKey` rebinding works live. Registrations moved out of `_resources` into `HotKeyTriggerMatcher` (the old `??=` could never re-register). `Rebind` is marshalled onto the single `EventLoopScheduler` from `Init()` — note `GetScheduler()` mints a *new* scheduler per call and `RegisterHotKey` binds to the message-pumping thread. | Changing a shortcut in settings does nothing, or works intermittently. |
| 3 | libuiohook clears a modifier's own mask bit on its key-up event. The code compensates with an explicit set/clear, which should be correct either way. | Held-modifier state drifts; commit-on-release misfires. |
| 4 | `IRegistration.IsSuccessful` is the OS-conflict signal (inferred via reflection — the library reports failure by flag, not exception). | Conflicts with other apps' hotkeys are not surfaced in the settings UI. |
| 5 | `Suppress` actually swallows `Win+Ctrl+Left` from the OS. | Docking also triggers Windows' virtual-desktop switch. |
| 6 | `Pause()`/`Start()` cycling re-attaches the hyperkey correctly. | CapsLock hyperkey dies after a pause/resume. |
| 7 | The capture gate's 60s watchdog fires. | A leaked capture session mutes all shortcut dispatch. |

**Resolved, no longer needs testing:** whether `IInteropProxy.SendInput` output is flagged
`IsEventSimulated`. Confirmed — SharpHook 7.1.1 derives the flag from the OS-level "injected" flag,
so it is set for any `SendInput` event regardless of origin
([SharpHook discussion #122](https://github.com/TolikPylypchuk/SharpHook/discussions/122)).

## Behavior changes to confirm feel right

- **Right-hand modifiers now work** (plan decision D2). `GetMods()` previously read only
  `LeftCtrl/LeftAlt/LeftShift/LeftMeta`. Try RightAlt+`` ` ``.
- **Modifier matching is exact, not superset.** `Win+Ctrl+Shift+Left` no longer docks; only
  `Win+Ctrl+Left` does. Required to keep `Alt+`` ` ``` and `Alt+Shift+`` ` ``` distinct.
  **Consequence:** the hyperkey holds all four modifiers, so `Win+Ctrl+Left` no longer docks while
  CapsLock is held. **Decided (2026-08-04): exact matching is the intended behavior** — the hyperkey
  exists to feed *other* apps' shortcuts, so losing hyperkey-held WinTabber chords is acceptable. Do
  not re-introduce superset matching.
- **Commit-on-Alt-release** is now `CmdCommitSelection`, derived per-activation from the trigger that
  fired, not `CmdAppHide`. Test: activate with `Alt+`` ` ```, press Ctrl mid-cycle, release Alt —
  should commit.
- **Switcher Enter/Esc** now always work as an escape hatch (guarded so an in-progress rename keeps
  its own Enter/Esc).

## Pre-existing bugs fixed along the way

- Unmapped hotkey ids fired `CmdNextWindow` (`MapHotKeyToEvent` returned `: 0`, and ordinal 0 is
  `CmdNextWindow`).
- `IsRunning` always reported `false` — it read a field never assigned anywhere (CS0649).
- `HyperKeyState.Connect()` returned only one of its two subscriptions, leaking a handler per resume.
- `ApplicationSettings.Load()` was called twice, producing two divergent instances, so settings edits
  would never have reached the keymap.
- Mouse-click and Esc close paths did not emit `WindowSelected`, which would have left the commit
  tracker armed.

## Not reviewed

Phases 4 (controls: `ShortcutPresenter`, `ShortcutCaptureBox`) and 5 (settings page) landed without a
checkpoint report — no inventory exists of the decisions made there. Worth a read-through before
trusting them, particularly the CapsLock bypass in capture (§3.4) and the capture gate's watchdog.
