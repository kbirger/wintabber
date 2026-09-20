// winui3/WinTabberUI/Views/WindowSelectorWindow.xaml.cs
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using WinTabber.ViewModels;
using WinTabberUI.Models.Settings;
using WinTabberUI.Windowing;
using WinUIEx;

namespace WinTabberUI.Views;

// Two real findings from this port worth flagging for whoever touches this file (or the next
// Window-rooted XAML in this migration) next:
//   1. `{x:Bind Foo, Converter={StaticResource Bar}}` cannot be used anywhere in this file's XAML --
//      DataTemplate-scoped or top-level -- because the generated binding code always ends up calling
//      SetConverterLookupRoot(this), and `this` here is WindowSelectorWindow : WinUIEx.WindowEx, which
//      is not a Microsoft.UI.Xaml.FrameworkElement (confirmed via real compiler output: CS1503 "cannot
//      convert from 'WinTabberUI.Views.WindowSelectorWindow' to 'Microsoft.UI.Xaml.FrameworkElement'").
//      Every converter-carrying binding in WindowSelectorWindow.xaml therefore uses classic {Binding}
//      instead (DataContext resolved via RootGrid.DataContext set below). x:Bind without a converter
//      is unaffected and used throughout. NOTE: a classic {Binding ElementName=..., Path=(attached
//      property)} was also tried for per-tile MaxWidth/MaxHeight and found to crash the process
//      natively (see ScaleTiles' doc comment) -- that path is no longer used in this file at all.
//   1b. WindowSelectorWindow.xaml's "WindowTileItemStyle" ListViewItem style replicates the WPF
//      original's WindowSelectorResources.xaml "WindowItemList" ItemContainerStyle: a full rounded
//      Border (BorderThickness 4, CornerRadius 10, Padding 10) instead of the default ListViewItem's
//      left accent bar (ListViewItemPresenter's own SelectionIndicatorMode), with the same selected
//      look (#aa444444 background, accent-color border). Per this migration's own WinUI3
//      ToggleButton CheckStates finding: combined state names (PointerOverSelected, PressedSelected)
//      belong in the single CommonStates group, not a separate group -- a second group is simply
//      never looked at by ListViewItem's own state-transition logic. REAL BUG found via live
//      verification: WinUI 3's automatic system focus visual (a Windows 11 accent-colored outline,
//      drawn on top of ANY control's template regardless of what that template itself renders) was
//      still showing around the selected/focused tile alongside the custom Border above -- the WPF
//      original's own equivalent (`FocusVisualStyle="{StaticResource FocusVisual}"`, a blank
//      Rectangle) suppressed the same thing there. Fixed with `UseSystemFocusVisuals="False"` on
//      the style, WinUI 3's own suppression switch for this.
//   2. A large multi-line XML comment placed directly before the root Grid element in this file's XAML
//      reproducibly made XamlCompiler.exe exit 1 with zero output on both stdout/stderr and in
//      output.json's MSBuildLogEntries -- the same "no diagnostic at all" pass2 crash class
//      SettingsWindow.xaml.cs's SystemBackdrop finding already documented, but triggered by comment
//      content this time, confirmed by bisection (removing just that comment, with no other change,
//      made the build succeed). Keep XAML comments in this file short; put detailed rationale in this
//      .cs file instead, as this comment block does.
public sealed partial class WindowSelectorWindow : WindowEx
{
    private readonly nint _hwnd;
    private readonly ApplicationSettings _settings;
    private Rect? _screenBounds;
    private bool _parked;
    private int _framesBeforeReveal;
    private int? _lastRequestedClientWidth;
    private int? _lastRequestedClientHeight;

    public WindowSelectorViewModel ViewModel { get; }

    // DEVIATION from the brief's stated "Produces" constructor shape
    // (WindowSelectorWindow(WindowSelectorViewModel viewModel)): a second parameter, ApplicationSettings,
    // was added to resolve TODO(verify) item 3 (ScaleTiles' settings property path) against a real,
    // already-DI-registered singleton (Bootstrapper.AddSettingsGraph) rather than threading the whole
    // SettingsViewModel through (the WPF original's approach) or reaching for a service locator. DI
    // resolves the extra parameter with no further wiring since ApplicationSettings is already
    // registered as a singleton.
    public WindowSelectorWindow(WindowSelectorViewModel viewModel, ApplicationSettings settings)
    {
        ViewModel = viewModel;
        _settings = settings;
        InitializeComponent();

        SystemBackdrop = new DesktopAcrylicBackdrop();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Only needed for the one binding that could not stay {x:Bind} -- see
        // OnRootGridPreviewKeyDown's neighboring CloseApplicationButton.Visibility doc note in the
        // .xaml file for why (CS1503 on SetConverterLookupRoot(this): WindowEx is not a
        // FrameworkElement). Classic {Binding} resolves this via inherited DataContext down the
        // visual tree from RootGrid, exactly like it does in WPF/UWP.
        RootGrid.DataContext = ViewModel;

        // REAL BUG found via live verification (Task 4b.4, and this follow-up): without wiring each
        // tile's WindowThumbnail.TargetWindow, InitialiseThumbnail's `TargetWindow is { } window`
        // guard is always false and DwmRegisterThumbnail never runs -- thumbnails silently never
        // appear. The brief's original approach (Ported from DockWindow.xaml.cs) used
        // ListView.ContainerContentChanging for this. CONFIRMED VIA LIVE VERIFICATION THAT DOES NOT
        // WORK HERE: ContainerContentChanging simply never fires at all when the ItemsPanel is
        // CommunityToolkit.WinUI.Controls.WrapPanel -- that event is only raised by the built-in
        // virtualizing panels (ItemsStackPanel/ItemsWrapGrid); a third-party non-virtualizing panel
        // never generates the notification. Fixed by walking TabListView's realized visual tree
        // directly instead, from RootGrid.SizeChanged. A non-virtualizing panel realizes every item's
        // container eagerly, so a full-tree walk finds every tile in one pass; the per-tile calls are
        // idempotent (safe to repeat on every layout).
        //
        // NOTE: RootGrid.SizeChanged only fires when RootGrid's own outer bounds change, NOT on every
        // ItemsSource content swap (a WindowItems reassignment with the same overall tile count/layout
        // does not necessarily resize RootGrid) -- confirmed live, see the ViewModel.PropertyChanged
        // subscription below, which is what actually catches that case.
        RootGrid.SizeChanged += (_, _) =>
        {
            WireRealizedTiles();
            CenterWindow();
            TryFocusTabList();
        };
        // REAL BUG found via live verification (this task's own fourth follow-up): thumbnails
        // flashed once on a real focus switch and never came back (a regression from the earlier
        // flash-then-reappear symptom). Root cause: this handler ignored
        // WindowActivatedEventArgs.WindowActivationState, so it ran on DEACTIVATION too (WinUI 3's
        // Window.Activated fires for both gaining and losing activation, unlike WPF's separate
        // OnActivated/OnDeactivated overrides -- the WPF original's OnDeactivated is empty, which is
        // exactly why this was never a problem there). CenterWindow() calls AppWindow.Move, so the
        // instant the user clicked another window, the selector silently moved -- but nothing then
        // forced a fresh LayoutUpdated pass for each WindowThumbnail, so DWM kept drawing each
        // thumbnail's rcDestination at the pre-move screen position, which is exactly where the
        // now-foreground window used to be. Fixed by ignoring the event when the window is losing
        // (not gaining) activation.
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                return;
            }
            ScaleTiles();
            CenterWindow();
        };
        Closed += (_, _) => DisarmReveal();

        // REAL BUG found via live verification (this task's own follow-up): thumbnails went blank
        // after switching focus to another window and only came back after clicking back into the
        // selector. Root cause: WindowSelectorViewModel.WindowItems gets reassigned (a whole new
        // array, new WindowItem/WindowThumbnail instances) whenever the foreground window changes
        // while the selector is open, but nothing was re-running WireRealizedTiles for the newly
        // realized containers -- RootGrid.SizeChanged only fires when RootGrid's own outer bounds
        // change, not on every ItemsSource content swap.
        // <para>
        // REAL BUG found via a second round of live verification, under REAL (not locked-session)
        // window-activation events: a single deferred WireRealizedTiles call could still run before
        // the ListView had actually regenerated containers for the new array, finding nothing to
        // wire -- confirmed live via a call-count log showing a 4+ second gap between a real
        // WindowItems change and the eventual successful wiring, with nothing retrying in between.
        // An earlier attempt to paper over this with an unconditional 200ms-forever timer was wrong
        // for a different reason (that testing session's session was locked, and the churn driving
        // it was lock-screen UI noise, not real activity -- verified via a live GetForegroundWindow
        // poll before and after unlocking). The real, remaining gap is retry timing, not event
        // frequency: RetryUntil below is bounded and stops as soon as every currently known
        // WindowItem's tile is wired, rather than running forever.
        // </para>
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WindowSelectorViewModel.WindowItems))
            {
                // REAL BUG found via live verification (this task's own third follow-up): 100ms
                // steps converged (every WindowItems change eventually got wired, confirmed live),
                // but slowly enough -- up to 1.5s across attempts -- to be visibly seen as the
                // thumbnail flashing away and back on every real focus switch. 30ms is roughly two
                // display frames; still bounded, just fast enough that the gap stops being visible.
                RetryUntil(WireRealizedTiles, maxAttempts: 40, TimeSpan.FromMilliseconds(30));
            }
        };
    }

    // REAL BUG found via live verification (this task's own follow-up): thumbnails only appeared
    // after clicking a tile, never on initial open. Root cause: WindowThumbnail's first
    // LayoutUpdated pass (from its own Loaded handler) runs BEFORE this method gets a chance to set
    // the tile's Width/Height, so it computes and commits a degenerate rcDestination against the
    // still-unsized tile. Width/Height then get set correctly here, but that alone does not
    // guarantee another LayoutUpdated notification fires for WindowThumbnail specifically -- a click
    // happened to trigger one indirectly (selection visual-state change invalidates layout nearby).
    // Fixed by explicitly invalidating each WindowThumbnail's own measure/arrange after its tile's
    // size is set, forcing a fresh LayoutUpdated pass with the now-correct geometry, deterministically
    // instead of by accident.
    // <para>
    // REAL BUG found via a second round of live verification: invalidating unconditionally on every
    // call created a feedback loop -- RootGrid.SizeChanged fires this method, which invalidated every
    // thumbnail regardless of whether anything about it had actually changed, which forced a new
    // layout pass, which (via a WrapPanel relayout that transiently unloads/reloads the container)
    // toggled WindowThumbnail's own Unloaded/Loaded, releasing and re-registering the DWM thumbnail
    // over and over, tens of times per second -- confirmed live via a call-count/stack-trace log
    // showing continuous ReleaseThumbnail/DwmRegisterThumbnail churn with no user action driving it.
    // Fixed by only touching a thumbnail (setting TargetWindow and invalidating) the first time it is
    // seen -- every later WireRealizedTiles call is now a no-op for tiles already wired, so the
    // feedback loop cannot sustain itself. Same guard applied to ApplyTileSize so re-setting the
    // identical Width/Height doesn't itself re-trigger a layout pass.
    // </para>
    /// <returns>
    /// True once every realized tile currently in the tree has a wired thumbnail. Used by
    /// <see cref="RetryUntil"/> to know when to stop retrying; a mismatch between this count and
    /// <see cref="WindowSelectorViewModel.WindowItems"/>'s length just means containers for the
    /// latest array have not been realized yet, not that anything is wrong.
    /// </returns>
    private bool WireRealizedTiles()
    {
        var realizedCount = 0;
        var wiredCount = 0;

        foreach (var tileGrid in FindDescendants<FrameworkElement>(TabListView, e => e.Name == "TileRootGrid"))
        {
            ApplyTileSize(tileGrid);

            foreach (var thumbnail in FindDescendants<WinTabberUI.Controls.WindowThumbnail>(tileGrid))
            {
                realizedCount++;

                if (thumbnail.TargetWindow is not null)
                {
                    wiredCount++;
                    continue;
                }

                thumbnail.TargetWindow = this;
                thumbnail.InvalidateMeasure();
                thumbnail.InvalidateArrange();
                wiredCount++;
            }
        }

        ResizeToContent();

        return realizedCount > 0 && realizedCount == wiredCount && realizedCount == ViewModel.WindowItems.Length;
    }

    /// <summary>
    /// WPF's original relied on <c>SizeToContent="WidthAndHeight"</c> (set on the Window itself via
    /// a Style in <c>WindowSelectorResources.xaml</c>) to auto-fit the window to its wrapped tiles,
    /// clamped by <c>MaxWidth</c>/<c>MaxHeight</c>. WinUI 3's Window has no SizeToContent equivalent,
    /// and <see cref="WinUIEx.WindowEx.MaxWidth"/>/<c>MaxHeight</c> (set in <see cref="ApplyScreenBounds"/>)
    /// only clamp interactive resize -- they never shrink the window to fit content (confirmed by
    /// decompiling WinUIEx.dll: both delegate straight to WindowManager with no resize side effect).
    /// Without this, the window keeps whatever oversized default AppWindow client size it started
    /// with -- the same gap <see cref="SuspendedWindowsWindow"/>'s port already found and fixed the
    /// same way: measure the content root with an unbounded constraint (RootGrid's own MaxWidth/
    /// MaxHeight, set in <see cref="ApplyScreenBounds"/>, clamp that measurement exactly like the WPF
    /// Window's MaxWidth/MaxHeight clamped its auto-size) and resize the real AppWindow client area to
    /// match. Called from <see cref="WireRealizedTiles"/> so it re-runs every time tile sizing or
    /// wiring changes, the same choke point already used for that.
    /// </summary>
    private void ResizeToContent()
    {
        RootGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = RootGrid.DesiredSize;
        if (desired.Width <= 0 || desired.Height <= 0)
        {
            return;
        }

        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        var targetWidth = (int)Math.Ceiling(desired.Width * scale);
        var targetHeight = (int)Math.Ceiling(desired.Height * scale);

        // Guard against reassigning the identical size -- see WireRealizedTiles' feedback-loop doc
        // comment for why: ResizeClient changes RootGrid's own outer bounds, which re-fires
        // RootGrid.SizeChanged, which calls back into WireRealizedTiles/ResizeToContent again.
        // Compared against the size THIS METHOD last requested, not AppWindow.Size/ClientSize: the
        // OS-granted client size can differ from what was asked for by a DPI-rounding pixel or two,
        // which would make an AppWindow.Size comparison never match and loop every layout pass.
        if (_lastRequestedClientWidth == targetWidth && _lastRequestedClientHeight == targetHeight)
        {
            return;
        }

        _lastRequestedClientWidth = targetWidth;
        _lastRequestedClientHeight = targetHeight;
        AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(targetWidth, targetHeight));
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root, System.Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && (predicate is null || predicate(match)))
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child, predicate))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Frames still to be composed before revealing. Two rather than one because
    /// CompositionTarget.Rendering is raised while a frame is still being built, not after it has
    /// been presented -- ported from the WPF original's identical comment.
    /// <para>
    /// RESOLVED (TODO(verify) item 1/2): confirmed via real compiler output that
    /// Microsoft.UI.Xaml.Media.CompositionTarget.Rendering is EventHandler&lt;object&gt; (not plain
    /// EventHandler like WPF's), so RevealWhenComposed's signature below is `object? sender, object e`.
    /// The Dispatcher.BeginInvoke(DispatcherPriority.Render, ...) fallback is NOT ported: WinUI 3's
    /// DispatcherQueue has no Render-specific priority (only High/Normal/Low -- confirmed, no
    /// intermediate "after layout, before input" priority exists).
    /// <para>
    /// UPDATE: this window is now a reused DI singleton (a later change in this same migration plan
    /// moved it off `.AddTransient&lt;Views.WindowSelectorWindow&gt;()`), so the stale-surface-reuse
    /// problem the fallback above guarded against in WPF is back, structurally, not just
    /// probabilistically -- a reused window can already be showing the previous open's rendered
    /// surface (and previous tile order) when <see cref="ShowWindowSelector"/> runs again. That is
    /// exactly why <see cref="ShowWindowSelector"/> now calls `Show()` followed by
    /// `RootGrid.UpdateLayout()` before `Activate()`: forcing a fresh layout pass against the current
    /// ViewModel state before the window is activated, rather than activating (and revealing) whatever
    /// was last composed.
    /// </para>
    /// </summary>
    private void ArmReveal()
    {
        _parked = true;
        _framesBeforeReveal = 2;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= RevealWhenComposed;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += RevealWhenComposed;
    }

    private void DisarmReveal()
    {
        _parked = false;
        _framesBeforeReveal = 0;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= RevealWhenComposed;
    }

    private void RevealWhenComposed(object? sender, object e)
    {
        if (--_framesBeforeReveal > 0)
        {
            return;
        }

        RevealNow();
    }

    private void RevealNow()
    {
        if (!_parked)
        {
            return;
        }

        DisarmReveal();
        CenterWindow();
    }

    private const double FillPercent = 0.8;

    /// <summary>
    /// RESOLVED (TODO(verify) item 3): WPF's WrapPanel had ItemWidth/ItemHeight for uniform tile
    /// sizing; CommunityToolkit.WinUI.Controls.WrapPanel has no such properties (confirmed -- its
    /// public surface is only Orientation/HorizontalSpacing/VerticalSpacing), so uniform sizing is
    /// reproduced on the tile Grid itself, using the same
    /// `_settings.Appearance.WindowTileWidth * _settings.Appearance.ScaleFactor` formula and
    /// height-from-aspect-ratio calculation as the WPF original's ScaleTiles/MaxItemWidth/MaxItemHeight.
    /// <para>
    /// REAL BUG found via live verification (this task's own follow-up, not the brief's draft): the
    /// brief's original design pushed this size onto RootGrid via a pair of TileSize attached
    /// DependencyProperties, read by each tile's classic {Binding ElementName=RootGrid,
    /// Path=(views:TileSize.MaxItemWidth)}. That binding crashes the process outright on the very
    /// first layout pass -- not a catchable .NET exception, a native STATUS_STOWED_EXCEPTION
    /// (0xC000027B) fast-fail inside Microsoft.UI.Xaml.dll, confirmed by bisection (removing just
    /// those two MaxWidth/MaxHeight bindings, with no other change, made the crash stop). Classic
    /// Binding's reflection-based resolution of a parenthesized attached-property Path segment is
    /// evidently not a safe pattern against a WinUI 3 attached DependencyProperty in this SDK version.
    /// Fixed by dropping TileSize/the attached-property indirection entirely and setting the size
    /// directly on each realized tile's root Grid instead, via <see cref="WireRealizedTiles"/>'s
    /// visual-tree walk below.
    /// </para>
    /// <para>
    /// REAL BUG found via a further round of live verification: MaxWidth/MaxHeight alone do not force
    /// a size, they only cap one -- an Auto+*-row Grid with no explicit Width/Height still sizes to
    /// its Auto row's content only, so the tile's own MaxWidth/MaxHeight had no visible effect and the
    /// title text overlapped the tile. Fixed by setting Width/Height directly (see
    /// <see cref="ApplyTileSize"/>) instead of MaxWidth/MaxHeight.
    /// </para>
    /// </summary>
    private void ScaleTiles()
    {
        var bounds = GetScreenBounds();
        var ratio = bounds.Width > 0 ? bounds.Height / bounds.Width : 1.0;
        var width = _settings.Appearance.WindowTileWidth * _settings.Appearance.ScaleFactor;

        _tileMaxWidth = width;
        _tileMaxHeight = 55 + width * ratio;

        // Pushes the freshly computed size onto every already-realized tile (e.g. a settings
        // change while the selector is open); newly realized tiles pick it up from
        // RootGrid.SizeChanged's own WireRealizedTiles call above.
        WireRealizedTiles();
    }

    private double _tileMaxWidth = 400.0;
    private double _tileMaxHeight = 400.0;

    // REAL BUG found via live verification (this task's own follow-up): MaxWidth/MaxHeight alone
    // only cap a Grid's size, they do not force it -- with no explicit Width/Height, an
    // Auto+*-row Grid still sizes itself to its Auto row's content only, so the *-row (the
    // Viewbox/WindowThumbnail) collapsed to zero height and the title (the Auto row) rendered
    // with no reserved space below it, which is what "thumbnails don't show, title overlaps the
    // tile" looks like. WPF's WrapPanel.ItemWidth/ItemHeight forced every cell to an exact size;
    // the equivalent here is setting Width/Height (not just MaxWidth/MaxHeight) on each tile.
    private void ApplyTileSize(FrameworkElement tileGrid)
    {
        // Guard against reassigning the identical value -- see WireRealizedTiles' feedback-loop
        // doc comment for why an unconditional set here matters, not just for saved cycles.
        if (tileGrid.Width != _tileMaxWidth)
        {
            tileGrid.Width = _tileMaxWidth;
        }

        if (tileGrid.Height != _tileMaxHeight)
        {
            tileGrid.Height = _tileMaxHeight;
        }
    }

    private Rect GetScreenBounds()
    {
        if (_screenBounds is { } cached)
        {
            return cached;
        }

        var cursorScreenBounds = ViewModel.CursorScreen.Bounds;
        var rect = DesktopHelper.ToLogicalBounds(_hwnd, new System.Drawing.Rectangle(
            cursorScreenBounds.X, cursorScreenBounds.Y, cursorScreenBounds.Width, cursorScreenBounds.Height));
        var logical = new Rect(rect.X, rect.Y, rect.Width, rect.Height);

        _screenBounds = logical;
        return logical;
    }

    private void ApplyScreenBounds()
    {
        var bounds = GetScreenBounds();
        MaxHeight = bounds.Height * FillPercent;
        MaxWidth = bounds.Width * FillPercent;

        // The Window-level MaxWidth/MaxHeight above only clamp interactive resize (see
        // ResizeToContent's doc comment) -- Microsoft.UI.Xaml.Window is not a FrameworkElement, so it
        // never participates in layout. RootGrid is what actually gets measured, so the WPF original's
        // Window-level MaxWidth/MaxHeight clamp (which the WrapPanel's measure pass saw directly) has
        // to be reproduced here on RootGrid instead, for ResizeToContent's RootGrid.Measure call to
        // respect the same 80%-of-screen cap.
        RootGrid.MaxHeight = MaxHeight;
        RootGrid.MaxWidth = MaxWidth;
    }

    private void CenterWindow()
    {
        if (_parked)
        {
            return;
        }

        var bounds = GetScreenBounds();
        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        var x = bounds.Left + (bounds.Width - Bounds.Width) / 2;
        var y = bounds.Top + (bounds.Height - Bounds.Height) / 2;

        AppWindow.Move(new Windows.Graphics.PointInt32((int)(x * scale), (int)(y * scale)));
    }

    public void ShowWindowSelector()
    {
        _screenBounds = null;
        _lastRequestedClientWidth = null;
        _lastRequestedClientHeight = null;
        _focusAcquired = false;
        var bounds = GetScreenBounds();

        ScaleTiles();
        ApplyScreenBounds();

        TabListView.SuppressHoverUntilPointerMoves();

        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        AppWindow.Move(new Windows.Graphics.PointInt32(
            (int)(bounds.Left * scale), (int)((bounds.Top - bounds.Height) * scale)));

        ArmReveal();

        // This window is now a reused DI singleton (see ArmReveal's doc comment below), so it may
        // already be Hide()-den from a previous close. Show() must run before Activate() so a
        // previously hidden window is actually un-hidden, not just re-activated in place.
        this.Show();
        RootGrid.UpdateLayout();

        // Best-effort resize while still parked off-screen, so the first frame the user actually
        // sees (once RevealNow's CenterWindow runs) is already the right size, matching the WPF
        // original's own "a MaxWidth arriving after Show() rearranges every tile in front of the
        // user" concern -- see ResizeToContent's doc comment. Not guaranteed to catch every tile:
        // WrapPanel containers may not be realized yet this early (the same reason TryFocusTabList
        // below needs a retry). The RootGrid.SizeChanged/WindowItems-changed handlers still correct
        // the size afterward if this pass finds nothing.
        WireRealizedTiles();

        Activate();

        // REAL BUG found via live verification (this task's own follow-up): Alt+Arrow (and every
        // other key) never reached any PreviewKeyDown handler at all, even though this window WAS
        // the real Win32 foreground window (confirmed via GetForegroundWindow -- ruling out the
        // "Alt+Arrow delivered as WM_SYSKEYDOWN, bypassing XAML input" theory this file's doc
        // comments had flagged as the leading suspect). Root cause: TabListView.Focus(...) here
        // returns false -- the container isn't focusable yet this early in Activate()'s call chain,
        // most likely because its items/containers haven't been realized yet (a non-virtualizing
        // WrapPanel still needs a layout pass to produce them). With no element in this XamlRoot
        // ever gaining keyboard focus, WinUI 3 has nothing to route key input to, so PreviewKeyDown
        // never tunnels through RootGrid at all. A single retry from RootGrid.SizeChanged still
        // wasn't reliably enough (confirmed live: Alt+Arrow kept requiring a manual click even with
        // that retry in place) -- SizeChanged's *first* firing can itself still be too early, before
        // the WrapPanel has produced a genuinely focusable container. Retried instead on a short,
        // explicitly bounded schedule (see RetryUntil) so it converges within a few frames of
        // whenever the container actually becomes focusable, without polling indefinitely.
        RetryUntil(TryFocusTabList, maxAttempts: 40, TimeSpan.FromMilliseconds(30));
    }

    /// <summary>
    /// Retries <paramref name="attempt"/> on <paramref name="interval"/> until it returns true or
    /// <paramref name="maxAttempts"/> is reached, then stops -- a bounded, self-terminating retry
    /// for a known one-shot operation (this window's own initial realization, or reacting to one
    /// WindowItems change), not an indefinite poll. See ShowWindowSelector's and the
    /// WindowItems-changed handler's doc comments for why a single deferred attempt is not enough:
    /// the WrapPanel's container realization after either this window's own first layout pass or an
    /// ItemsSource content swap does not land on any one predictable event, confirmed live via call-
    /// count logs showing single-attempt retries succeeding anywhere from immediately to several
    /// seconds later.
    /// </summary>
    private void RetryUntil(Func<bool> attempt, int maxAttempts, TimeSpan interval)
    {
        if (attempt() || maxAttempts <= 0)
        {
            return;
        }

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = interval;
        var remaining = maxAttempts;
        timer.Tick += (_, _) =>
        {
            remaining--;
            if (attempt() || remaining <= 0)
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    private bool _focusAcquired;

    private bool TryFocusTabList()
    {
        if (_focusAcquired)
        {
            return true;
        }

        _focusAcquired = TabListView.Focus(FocusState.Programmatic);
        return _focusAcquired;
    }

    public void SwitchWindowAndClose()
    {
        if (ViewModel.SelectedIndex >= 0 && ViewModel.SelectedIndex < ViewModel.WindowItems.Length)
        {
            ViewModel.SelectedItem?.Activate();
        }

        // RESTORED: the brief's draft dropped this call (present in the WPF original's
        // SwitchWindowAndClose). Without it, a mouse-click close leaves ViewModel.SelectedIndex at
        // whatever the clicked tile's index was instead of resetting to -1, which would make the next
        // open's SelectNext/SelectPrevious skip RefreshOnActivation's RefreshFromForeground call (it
        // only refreshes when SelectedIndex < 0) -- the switcher would then open showing a stale tile
        // list from before the close, not the freshly-focused application's windows.
        ViewModel.Deactivate();
        ViewModel.EndPreview();
        ViewModel.NotifySwitcherClosed();
        this.Hide();
    }

    private void OnTileGridPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsUnderEditableTextBlock(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: WindowItem clicked })
        {
            ViewModel.SelectedItem = clicked;
        }

        SwitchWindowAndClose();
    }

    private static bool IsUnderEditableTextBlock(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is EditableTextBlock)
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    /// <summary>
    /// §5 fallback: Enter commits, Esc cancels. Ported from the WPF original's
    /// OnPreviewKeyDown(KeyEventArgs) Window override.
    /// <para>
    /// RESOLVED (TODO(verify) item 4, and the reason this is a RootGrid.PreviewKeyDown event handler
    /// rather than an OnKeyDown override): Microsoft.UI.Xaml.Window / WinUIEx.WindowEx is not a
    /// DependencyObject/UIElement (confirmed by DockWindow.xaml.cs's "WinUI 3's Window has no
    /// overridable OnClosed" precedent, and independently confirmed here: `protected override void
    /// OnKeyDown` as drafted in the brief fails with CS0115 "no suitable method found to override"
    /// against the real compiler -- WinUIEx.WindowEx's public surface has no OnKeyDown/OnPreviewKeyDown
    /// virtual at all), so there is no OnKeyDown to override -- WindowSelectorWindow.xaml wires this
    /// to RootGrid's tunneling PreviewKeyDown event instead, the same event-vs-override substitution
    /// SpatialNavigationListView already established for its own arrow-key handling. RootGrid is the
    /// root of the content tree, so its PreviewKeyDown fires before any descendant's (including
    /// TabListView's own PreviewKeyDown, which only acts on arrow keys and leaves Enter/Escape alone).
    /// </para>
    /// <para>
    /// The WPF original's `Keyboard.FocusedElement is TextBox` guard is ported as
    /// `FocusManager.GetFocusedElement(...) is TextBox`: a rename-in-progress's Enter/Escape belong to
    /// EditableTextBlock's own TextBox KeyDown handler (Task 4b.2), not to this window-level handler --
    /// without this guard, RootGrid's tunneling PreviewKeyDown would set e.Handled=true and stop the
    /// event before it ever reached the TextBox's own (bubbling) KeyDown, silently breaking rename
    /// commit/cancel.
    /// </para>
    /// </summary>
    private void OnRootGridPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Content?.XamlRoot is { } xamlRoot && FocusManager.GetFocusedElement(xamlRoot) is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                ViewModel.CommitSelection();
                this.Hide();
                return;
            case VirtualKey.Escape:
                e.Handled = true;
                ViewModel.CancelSelection();
                this.Hide();
                return;
        }
    }
}
