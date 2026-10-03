# Hint-Overlay WinUI 3 Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the winui3 `MediaControlsWindow`/`VolumeControls` the same Alt-triggered keyboard hint activation the WPF app has, using WinUI 3's native `AccessKeyManager` instead of a custom overlay.

**Architecture:** Fixed controls (buttons, the play/pause and mute toggles, the two volume sliders) get a plain `AccessKey` in XAML — the framework handles display, matching, and the default action, no code. The three `ComboBox`es also get a fixed `AccessKey` for opening them, plus a new reusable helper, `DynamicAccessKeyScope`, that assigns a sequential digit `AccessKey` to each drop-down item once it is realized and drives selection on activation — the one piece of real glue code this feature needs.

**Tech Stack:** WinUI 3 / Windows App SDK, `Microsoft.UI.Xaml.Input.AccessKeyManager`, C#, XAML.

**Spec:** `docs/superpowers/specs/2026-09-17-hint-overlay-winui3-design.md`

## Global Constraints

- Scope is exactly `MediaControlsWindow` and `VolumeControls` — no other window gets hints in this plan.
- Custom badge rendering (`AccessKeyDisplayRequested`/`Dismissed`) is explicitly out of scope — default framework badges are used.
- Do not port `GeneratedHintsProvider`, `HintAdorner`'s dead highlight code, `HintPosition`, `HintChordState`, `HintActivationScope`, `IHintBehaviorKernel`, or its two kernel implementations — none of these carry forward.
- `DynamicAccessKeyScope` is typed to `ComboBox` specifically, not a generic `ItemsControl` helper — do not generalize it in this plan.
- WinUI 3's `ComboBox` has no `ContainerContentChanging`; item containers are only reachable via `ContainerFromIndex` after `DropDownOpened` fires.

---

## File Structure

- Modify `winui3/WinTabberUI/Views/MediaControlsWindow.xaml` — add `AccessKey` to the prev/play-pause/next controls and each `ComboBox`'s own key; add `x:Name` to the two device `ComboBox`es (only `SessionSelector` currently has one).
- Modify `winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs` — wire `DynamicAccessKeyScope.AttachSequentialKeys` for the three `ComboBox`es.
- Modify `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml` — bind `AccessKey` on the `Slider` and the `MuteToggle` to the existing `VolumeHintText`/`MuteHintText` view-model properties (already ported, unused since the WPF `b:HintBehavior.HintText` binding was dropped when this file was first ported).
- Create `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs` — the reusable glue for dynamic per-item hints.

No test project changes: see "Testing approach" below for why.

## Testing approach

The spec's testing section calls for a desktop-requiring test of `DynamicAccessKeyScope.AttachSequentialKeys`, the way WPF's `HintBehaviorDesktopTests` used a real, shown `Window`. This repository's winui3 test project (`winui3/WinTabber.UI.Common.Tests`) has no precedent for instantiating a real `Window`/`ComboBox`/`XamlRoot` inside a TUnit test — every existing test there (`ShortcutChipsTests`, `ValueConvertersTests`) is pure logic with no live visual tree, and building that harness from scratch is its own yak-shave, out of scope for this plan.

`AttachSequentialKeys` has no logic that is meaningfully separable from the live `ComboBox`/`AccessKeyManager` runtime it drives (`DropDownOpened`, `ContainerFromIndex`, `AccessKeyManager.EnterDisplayMode` all require a real, shown window). Each task below is instead verified live against the running app, the same way the spec's own findings were verified. If a future task adds a real WinUI 3 UI-test harness to this repo, `DynamicAccessKeyScope` would be a good first candidate to backfill with an automated test.

---

### Task 1: Fixed access keys on `MediaControlsWindow`'s buttons and `ComboBox`es

**Files:**
- Modify: `winui3/WinTabberUI/Views/MediaControlsWindow.xaml`

**Interfaces:**
- Produces: `x:Name="PlaybackDeviceSelector"` and `x:Name="RecordingDeviceSelector"` on the two previously-unnamed `ComboBox`es — Task 4 wires these up in code-behind.

- [ ] **Step 1: Add `AccessKey` to the prev/play-pause/next controls**

In `winui3/WinTabberUI/Views/MediaControlsWindow.xaml`, find this block (around line 259):

```xml
<StackPanel Orientation="Horizontal" Grid.Column="1" Grid.Row="0" HorizontalAlignment="Center">
    <Button Style="{StaticResource MediaButton}" Command="{Binding ActiveSession.Playback.Prev}">
        <FontIcon Glyph="&#xE892;" />
    </Button>
    <ToggleButton
        Style="{StaticResource PlayPauseMediaButton}"
        DataContext="{Binding ActiveSession.Playback}"
        Command="{Binding PlayPause}"
        x:Name="PlayPauseButton"
        IsChecked="{Binding IsPlaying, Mode=OneWay}"
    />
    <Button Style="{StaticResource MediaButton}" Command="{Binding ActiveSession.Playback.Next}">
        <FontIcon Glyph="&#xE893;" />
    </Button>
</StackPanel>
```

Replace it with:

```xml
<StackPanel Orientation="Horizontal" Grid.Column="1" Grid.Row="0" HorizontalAlignment="Center">
    <Button Style="{StaticResource MediaButton}" AccessKey="B" Command="{Binding ActiveSession.Playback.Prev}">
        <FontIcon Glyph="&#xE892;" />
    </Button>
    <ToggleButton
        Style="{StaticResource PlayPauseMediaButton}"
        DataContext="{Binding ActiveSession.Playback}"
        Command="{Binding PlayPause}"
        x:Name="PlayPauseButton"
        AccessKey="P"
        IsChecked="{Binding IsPlaying, Mode=OneWay}"
    />
    <Button Style="{StaticResource MediaButton}" AccessKey="F" Command="{Binding ActiveSession.Playback.Next}">
        <FontIcon Glyph="&#xE893;" />
    </Button>
</StackPanel>
```

- [ ] **Step 2: Add `AccessKey` and `x:Name` to the three `ComboBox`es**

In the same file, find this block (around line 176):

```xml
<ComboBox
    Grid.Row="1"
    Grid.ColumnSpan="2"
    ItemsSource="{Binding Sessions}"
    HorizontalAlignment="Stretch"
    x:Name="SessionSelector"
    ItemTemplate="{StaticResource SessionItemTemplate}"
    SelectedItem="{Binding SelectedSessionListItem, Mode=TwoWay}"
/>
```

Add `AccessKey="A"`:

```xml
<ComboBox
    Grid.Row="1"
    Grid.ColumnSpan="2"
    ItemsSource="{Binding Sessions}"
    HorizontalAlignment="Stretch"
    x:Name="SessionSelector"
    AccessKey="A"
    ItemTemplate="{StaticResource SessionItemTemplate}"
    SelectedItem="{Binding SelectedSessionListItem, Mode=TwoWay}"
/>
```

Find the playback-device `ComboBox` (around line 188):

```xml
<ComboBox
    Grid.Column="2"
    Grid.ColumnSpan="2"
    Grid.Row="1"
    Margin="7,0,0,0"
    HorizontalAlignment="Stretch"
    ItemTemplate="{StaticResource DeviceItemTemplate}"
    ItemsSource="{Binding Playback.Devices}"
    SelectedItem="{Binding Playback.SelectedDevice, Mode=TwoWay}"
/>
```

Add `x:Name="PlaybackDeviceSelector"` and `AccessKey="S"`:

```xml
<ComboBox
    Grid.Column="2"
    Grid.ColumnSpan="2"
    Grid.Row="1"
    Margin="7,0,0,0"
    HorizontalAlignment="Stretch"
    x:Name="PlaybackDeviceSelector"
    AccessKey="S"
    ItemTemplate="{StaticResource DeviceItemTemplate}"
    ItemsSource="{Binding Playback.Devices}"
    SelectedItem="{Binding Playback.SelectedDevice, Mode=TwoWay}"
/>
```

Find the recording-device `ComboBox` (around line 200):

```xml
<ComboBox
    Margin="7,0,0,0"
    Grid.Column="4"
    Grid.ColumnSpan="2"
    Grid.Row="1"
    HorizontalAlignment="Stretch"
    ItemTemplate="{StaticResource DeviceItemTemplate}"
    ItemsSource="{Binding Recording.Devices}"
    SelectedItem="{Binding Recording.SelectedDevice, Mode=TwoWay}"
/>
```

Add `x:Name="RecordingDeviceSelector"` and `AccessKey="R"`:

```xml
<ComboBox
    Margin="7,0,0,0"
    Grid.Column="4"
    Grid.ColumnSpan="2"
    Grid.Row="1"
    HorizontalAlignment="Stretch"
    x:Name="RecordingDeviceSelector"
    AccessKey="R"
    ItemTemplate="{StaticResource DeviceItemTemplate}"
    ItemsSource="{Binding Recording.Devices}"
    SelectedItem="{Binding Recording.SelectedDevice, Mode=TwoWay}"
/>
```

- [ ] **Step 3: Build**

Run: `dotnet build winui3/WinTabberUI/WinTabberUI.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Manual verification**

Kill any running instance (`taskkill //IM WinTabberUI.exe //F`), rebuild, launch the app, and open the media controls window. Press Alt and confirm badges `B`, `P`, `F`, `A`, `S`, `R` appear over the prev button, play/pause toggle, next button, and the three `ComboBox`es. Press `P` and confirm play/pause toggles. Press `B` then `F` (separately) and confirm prev/next fire.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabberUI/Views/MediaControlsWindow.xaml
git commit -m "feat: add fixed access keys to media controls window"
```

---

### Task 2: Access keys on `VolumeControls`'s slider and mute toggle

**Files:**
- Modify: `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml`

**Interfaces:**
- Consumes: `VolumeControlsViewModel.VolumeHintText`/`MuteHintText` (`WinTabber.ViewModels/VolumeControlsViewModel.cs:182-183`, already ported, currently unbound in the winui3 XAML). Set per instance in `winui3/WinTabber.UI.Media/ViewModels/MediaSessionViewModel.cs:138,140`: `DeviceVolumeControls` uses `"DV"`/`"DM"`, `SessionVolumeControls` uses `"PM"`/`"PM"` (both the slider and the mute toggle share the same hint text for that one instance — a pre-existing value from the WPF original, not a new decision; flag this during manual verification in Step 3 below rather than silently changing it).

- [ ] **Step 1: Bind `AccessKey` on the `Slider` and `MuteToggle`**

In `winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml`, find:

```xml
<Slider
    Minimum="0"
    Maximum="100"
    StepFrequency="5"
    IsEnabled="{Binding CanSetVolume}"
    Value="{Binding Volume, Converter={StaticResource FloatToPercentageConverter}, Mode=TwoWay}"
/>
<ToggleButton
    Style="{StaticResource MuteToggle}"
    Margin="7 0 0 0"
    Grid.Column="1"
    HorizontalAlignment="Center"
    VerticalAlignment="Stretch"
    IsChecked="{Binding IsMuted, Mode=OneWay}"
    Command="{Binding SetMuted}"
    CommandParameter="{Binding IsMuted, Converter={StaticResource InverseBoolConverter}}"
/>
```

Replace with:

```xml
<Slider
    Minimum="0"
    Maximum="100"
    StepFrequency="5"
    IsEnabled="{Binding CanSetVolume}"
    AccessKey="{Binding VolumeHintText}"
    Value="{Binding Volume, Converter={StaticResource FloatToPercentageConverter}, Mode=TwoWay}"
/>
<ToggleButton
    Style="{StaticResource MuteToggle}"
    Margin="7 0 0 0"
    Grid.Column="1"
    HorizontalAlignment="Center"
    VerticalAlignment="Stretch"
    AccessKey="{Binding MuteHintText}"
    IsChecked="{Binding IsMuted, Mode=OneWay}"
    Command="{Binding SetMuted}"
    CommandParameter="{Binding IsMuted, Converter={StaticResource InverseBoolConverter}}"
/>
```

- [ ] **Step 2: Build**

Run: `dotnet build winui3/WinTabberUI/WinTabberUI.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual verification**

Rebuild and launch. Press Alt and confirm a badge appears over the "Playback" volume slider and mute toggle (`DV`/`DM`). Press `D` then `V` and confirm the slider gets focus/keyboard control; press Alt, `D`, `M` and confirm mute toggles. Then check the "Session" volume slider and mute toggle (`PM`/`PM`) — note whether both badges show distinctly or collide (same hint text on two controls is a carried-over quirk from the WPF original, not something to fix here; if they collide, note it for a future task rather than changing it now).

- [ ] **Step 4: Commit**

```bash
git add winui3/WinTabber.UI.Media/UserControls/VolumeControls.xaml
git commit -m "feat: bind volume controls access keys to existing hint-text properties"
```

---

### Task 3: `DynamicAccessKeyScope` helper

**Files:**
- Create: `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`

**Interfaces:**
- Produces: `DynamicAccessKeyScope.AttachSequentialKeys(ComboBox owner, Action<ComboBoxItem, int> onActivated)` — Task 4 calls this once per `ComboBox`.

- [ ] **Step 1: Create the helper**

Create `winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// Assigns a sequential digit AccessKey ("1", "2", "3", ...) to each item container of a
/// ComboBox's drop-down as it is realized, and routes activation through onActivated. ComboBox
/// has no ContainerContentChanging (confirmed unavailable on this type in the Windows App SDK),
/// so containers are only reachable via ContainerFromIndex once DropDownOpened fires.
/// </summary>
public static class DynamicAccessKeyScope
{
    /// <summary>
    /// ExitDisplayModeOnAccessKeyInvoked=false plus the explicit EnterDisplayMode call keep the
    /// access-key badge/chord session continuous across the transition from the ComboBox's own
    /// key into its items' keys -- without both, the user must press Alt a second time after the
    /// drop-down opens. The HashSet dedup is required because DropDownOpened fires on every open
    /// and containers can be reused; without it, AccessKeyInvoked handlers stack across repeated
    /// opens.
    /// </summary>
    public static void AttachSequentialKeys(ComboBox owner, Action<ComboBoxItem, int> onActivated)
    {
        owner.IsAccessKeyScope = true;
        owner.ExitDisplayModeOnAccessKeyInvoked = false;

        var wired = new HashSet<ComboBoxItem>();
        owner.DropDownOpened += (_, _) =>
        {
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                for (int i = 0; i < owner.Items.Count; i++)
                {
                    if (owner.ContainerFromIndex(i) is ComboBoxItem container)
                    {
                        container.AccessKey = (i + 1).ToString();
                        if (wired.Add(container))
                        {
                            var index = i;
                            container.AccessKeyInvoked += (_, args) =>
                            {
                                onActivated(container, index);
                                args.Handled = true;
                            };
                        }
                    }
                }

                AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
            });
        };
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabber.UI.Common/AccessKeys/DynamicAccessKeyScope.cs
git commit -m "feat: add DynamicAccessKeyScope for dynamic per-item access keys"
```

---

### Task 4: Wire `DynamicAccessKeyScope` into `MediaControlsWindow`'s three `ComboBox`es

**Files:**
- Modify: `winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs`

**Interfaces:**
- Consumes: `DynamicAccessKeyScope.AttachSequentialKeys(ComboBox, Action<ComboBoxItem, int>)` (Task 3); `SessionSelector`, `PlaybackDeviceSelector`, `RecordingDeviceSelector` (Task 1's `x:Name`s, generated as fields by `InitializeComponent()`).

- [ ] **Step 1: Add the `using` and wire the three `ComboBox`es**

In `winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs`, add this to the top of the file, alongside the existing `using` statements:

```csharp
using WinTabber.UI.Common.AccessKeys;
```

Then, in the constructor, immediately after `ResizeHeightToContent();` and before the closing brace of the constructor:

```csharp
        DynamicAccessKeyScope.AttachSequentialKeys(SessionSelector, (_, index) =>
        {
            SessionSelector.SelectedIndex = index;
            SessionSelector.IsDropDownOpen = false;
        });
        DynamicAccessKeyScope.AttachSequentialKeys(PlaybackDeviceSelector, (_, index) =>
        {
            PlaybackDeviceSelector.SelectedIndex = index;
            PlaybackDeviceSelector.IsDropDownOpen = false;
        });
        DynamicAccessKeyScope.AttachSequentialKeys(RecordingDeviceSelector, (_, index) =>
        {
            RecordingDeviceSelector.SelectedIndex = index;
            RecordingDeviceSelector.IsDropDownOpen = false;
        });
```

- [ ] **Step 2: Build**

Run: `dotnet build winui3/WinTabberUI/WinTabberUI.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual verification**

Kill any running instance, rebuild, launch, open the media controls window. For each of the three `ComboBox`es: press Alt, then its key (`A`/`S`/`R`), confirm the drop-down opens and stays in one continuous hint session (no second Alt press needed), confirm digit badges appear over the items, press a digit, and confirm that item gets selected and the drop-down closes. Repeat the open/select cycle two or three times on the same `ComboBox` and confirm activation keeps working every time (per the spec's accepted gap, the digit badges may stop visually redrawing after the first open — that is expected and not a regression; only silent activation failure would be).

- [ ] **Step 4: Commit**

```bash
git add winui3/WinTabberUI/Views/MediaControlsWindow.xaml.cs
git commit -m "feat: wire dynamic access keys into media controls window combo boxes"
```

---

### Task 5: Update the migration progress ledger

**Files:**
- Modify: `.superpowers/sdd/2026-09-12-wpf-to-winui3-migration/progress.md` (gitignored; not committed)

- [ ] **Step 1: Append an entry**

Add an entry to the ledger noting: hint-overlay system ported for `MediaControlsWindow`/`VolumeControls` using WinUI 3's native `AccessKeyManager` instead of a custom `AdornerLayer`-based overlay; `DynamicAccessKeyScope` added as the one piece of new glue code, for the `ComboBox` dynamic-item case; custom badge rendering deferred as a follow-up task; note the accepted default-badge-redraw gap on repeated `ComboBox` opens (activation unaffected).

- [ ] **Step 2: No commit** (ledger is gitignored).

---

## Self-Review

**Spec coverage:**
- Fixed controls (buttons, toggles, `ComboBox` top-level keys) → Task 1.
- Volume slider/mute chords → Task 2.
- Dynamic per-item hints (`DynamicAccessKeyScope`) → Task 3, wired in Task 4.
- Explicit exclusions (`GeneratedHintsProvider`, dead highlight code, `HintPosition`/`HintChordState`/`HintActivationScope`/`HintAdorner`/kernels) → nothing in this plan touches or references them; covered by omission, called out in Global Constraints.
- Deferred custom-rendering follow-up → explicitly out of scope, noted in Global Constraints and Task 5's ledger entry.
- Testing section → addressed in "Testing approach" above, with the reasoning for why no automated test is added.

**Placeholder scan:** No TBD/TODO markers. Every step has literal code or a literal verification procedure.

**Type consistency:** `DynamicAccessKeyScope.AttachSequentialKeys(ComboBox owner, Action<ComboBoxItem, int> onActivated)` is defined once in Task 3 and called with that exact signature in Task 4, three times, once per `ComboBox`.
