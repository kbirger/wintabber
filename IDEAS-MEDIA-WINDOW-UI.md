# Ideas: Media Controls Window UI

Layout and presentation items for the media controls window. Reported against
the WinUI 3 app, but check the WPF window for each one: the two XAML files are
separate, so a fix must usually be made twice.

- WinUI 3: `winui3/WinTabberUI/Views/MediaControlsWindow.xaml`
- WPF: `WinTabber.UI.Media/Views/MediaControlsWindow.xaml`

None of these is a correctness bug. None is urgent.

---

## 1. The session dropdown is shorter when nothing is playing

**Symptom.** With no session playing, the Session ComboBox renders shorter than
the Playback and Recording ones.

**Cause.** None of the three ComboBoxes sets a height. Each one sizes itself
from its item template, so the closed-state height depends on having an item to
measure. `SessionItemTemplate` (line 42) holds a 16 px image with
`Margin="4,4,10,4"`; `DeviceItemTemplate` (line 33) holds a TextBlock with
`Margin="4"`. An existing comment at line 41 records that those margins were
matched on purpose so both report the same closed height.

That match only holds while both have items. An empty Session list has nothing
to measure, so it falls back to the control default and the row looks uneven.

**Decided.** Give all three ComboBoxes the same explicit `MinHeight`, so the
populated and empty states agree. That also removes the hidden dependency on two
item templates keeping their margins in step, which the line 41 comment exists
to protect.

**Scope.** The same reasoning applies to the device dropdowns. It shows up on
the session one because having no audio device is rare.

---

## 2. The transport controls are not centred in the window

**Symptom.** The Prev, Play/Pause and Next buttons sit off centre, and the
amount they are off depends on how long the current title, artist and album are.

**Cause.** The row is a three-column grid (lines 227 to 231):

| Column | Width | Content |
|---|---|---|
| 0 | `Auto` | thumbnail and track information |
| 1 | `*` | the transport controls, `HorizontalAlignment="Center"` |
| 2 | `Auto` | empty |

The controls are centred inside **column 1**, not inside the window. Column 0 is
`Auto`, so it grows with the text, column 1 starts further right, and the
controls drift with it.

The track information already has `MaxWidth="260"` (line 246), so the drift is
bounded — but bounded drift is still drift, and the maximum width is doing a job
it was not added for.

**Decided.** Make the outer columns symmetric: column 0 `*`, column 1 `Auto`
for the controls, column 2 `*`. The controls then sit in the middle of the
window regardless of the text, and `MaxWidth` goes back to meaning only "do not
let a long title run away".

Check what column 2 is for. It is currently empty in the WinUI file, which
suggests the symmetric layout was the original intent.

---

## 3. The progress row is wrong for sources that report no position

**Symptom.** With a source that publishes no playback position, the progress bar
is dead and both time labels read `0:00`. Two zeros and an empty bar say nothing
true.

**Cause.** `PlaybackControlsViewModel.Duration` is `TimeSpan.Zero` for such a
source. The slider binds `Maximum` to it (line 291) and the two `TextBlock`s
bind `Position` and `Duration` through `TimeSpanToStringConverter`. Nothing
tests whether a duration exists, so the row renders its empty state instead of
not rendering.

This is not rare. The Nora player never publishes position, and it also accepts
seek commands and silently drops them.

**Decided.** Add a `HasProgress` property to `PlaybackControlsViewModel`
(`Duration > TimeSpan.Zero`). The view model is shared by both apps, so the rule
is written once and only the binding is duplicated.

Note that `CanSeek` already exists and is bound to the slider's `IsEnabled`. It
is a different question — whether seeking is allowed — and should not be reused
as a proxy for whether a position exists.

**Decided: swap the control, do not switch a mode.** `Slider` has no
indeterminate state. Verified against the pinned Windows App SDK 1.8 metadata
(`Microsoft.UI.Xaml.winmd`), not from memory:

```
Slider       Indeterminate: NONE
ProgressBar  Indeterminate: IsIndeterminate, IsIndeterminateProperty
```

`ProgressRing` has it too, and WPF is the same shape. So the three states share
one grid cell and `HasProgress` decides which control is visible:

| State | Control | Times |
|---|---|---|
| Position known | `Slider`, as now | shown |
| Playing, position unknown | `ProgressBar` with `IsIndeterminate` | hidden |
| No session | neither | hidden |

Two details that change the markup:

- **Give the cell a fixed height.** A `Slider` and a `ProgressBar` have
  different natural heights. Without a fixed height the window jumps when the
  source changes, which is what made this visible in the first place.
- **Animate only while playing.** Bind `IsIndeterminate` to
  "playing and no progress", not to "no progress" alone. A bar that keeps
  marching while paused states something false.

Do not reuse `CanSeek` for any of this. It answers whether seeking is permitted,
which is a different question. Nora is the case that separates them: it accepts
seek commands and silently drops them.
