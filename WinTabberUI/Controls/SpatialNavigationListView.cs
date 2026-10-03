using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using WinTabber.ViewModels;
using WinTabber.ViewModels.Models;

namespace WinTabberUI.Controls;

/// <summary>
/// Arrow-key spatial navigation between tiles, plus hover-to-select gated on the pointer having
/// actually moved (see <see cref="HoverSelectionEnabled" />). Ported from WinTabberUI's WPF
/// SpatialNavigationListView.cs; see WindowSelectorWindow.xaml.cs's port for why the selector
/// window reuses this control's instance across opens rather than recreating it.
/// <para>
/// Hover-select is a deliberate behavioral deviation from the WPF original, not just a mechanical
/// port: WPF's <c>MultiTrigger</c> was a *state* condition (<c>IsMouseOver AND HoverSelect.IsEnabled</c>),
/// so the instant the flag flipped back to true the tile already under a stationary cursor got
/// selected. This port's <c>PointerEntered</c> handler is *edge*-triggered -- once re-armed, no new
/// <c>PointerEntered</c> fires for a container the pointer is already inside, so the tile under the
/// cursor stays unselected until the pointer actually crosses into a different container. That is
/// arguably a better fix for the selection-jump bug (see MEMORY.md's selector-selection-jump note)
/// than the WPF original's own suppress/re-arm dance was, but it is a real behavior difference this
/// port did not reproduce faithfully, and is worth confirming against user expectations in 4b.4's
/// live verification.
/// </para>
/// </summary>
public class SpatialNavigationListView : ListView
{
    private static readonly VirtualKey[] ArrowKeys =
        [VirtualKey.Down, VirtualKey.Up, VirtualKey.Left, VirtualKey.Right];

    private WindowTileGrid? _tileGrid;
    private Point? _hoverAnchor;

    /// <summary>
    /// Replaces WPF's HoverSelect attached property. WinUI 3 has no property-value inheritance
    /// down the visual tree, so this is a plain property on the list itself rather than an
    /// attached one on each container -- each container's PointerEntered handler (wired below,
    /// via ContainerContentChanging) reads this directly off its owning list.
    /// </summary>
    public bool HoverSelectionEnabled { get; private set; } = true;

    public SpatialNavigationListView()
    {
        ContainerContentChanging += OnContainerContentChanging;

        // Wired as an event, not an OnKeyDown override: the WPF original used the tunneling
        // OnPreviewKeyDown (which runs before ListView's own arrow-key handling) specifically so
        // our spatial nav wins the race against list-order selection movement and Handled=true
        // actually suppresses it. Control in WinUI 3 has no protected OnPreviewKeyDown virtual to
        // override, but UIElement exposes PreviewKeyDown as a public tunneling event, which gives
        // the same ordering. See OnPreviewKeyDown below for the open question this still leaves
        // about Alt+Arrow specifically (this selector is shown while Alt is held).
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// Ignore hover selection until the pointer actually moves. Also drops the cached tile grid:
    /// the WPF original invalidated it on IsVisibleChanged/OnItemsChanged because the selector
    /// window reuses this control's instance across opens (see the class doc comment), so a grid
    /// built on a previous open -- at stale tile coordinates, over a stale window list -- would
    /// otherwise steer arrow-key navigation on every subsequent one. WinUI 3's ListViewBase has no
    /// direct equivalent of either WPF override to hook that invalidation into, but this method is
    /// already called exactly once per open (from WindowSelectorWindow.xaml.cs's ShowWindowSelector),
    /// which makes it the natural place to do the same invalidation instead.
    /// </summary>
    public void SuppressHoverUntilPointerMoves()
    {
        _hoverAnchor = GetCursorPosition();
        HoverSelectionEnabled = false;
        _tileGrid = null;
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer is not ListViewItem container)
        {
            return;
        }

        // Safe to re-add on every call, including recycled containers: WinUI 3 event handler
        // subscription is idempotent only if this container never got this exact delegate
        // instance before -- ContainerContentChanging can fire more than once for the same
        // container across its lifetime, so guard with -= before += to avoid a double-fire.
        container.PointerEntered -= OnContainerPointerEntered;
        container.PointerEntered += OnContainerPointerEntered;

        // Closes the other half of the WPF original's invalidation (see SuppressHoverUntilPointerMoves's
        // doc comment for the per-open half): a container getting new content here means the item list
        // or its on-screen positions changed since the grid was last built (items added/removed, or a
        // scroll bringing different containers into view), so any cached grid is stale. Cheap: this is
        // just a null assignment, and TryInitializeTileGrid only rebuilds lazily, on the next arrow press.
        _tileGrid = null;
    }

    private void OnContainerPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!HoverSelectionEnabled)
        {
            return;
        }

        if (sender is ListViewItem { Content: WindowItem item })
        {
            SelectedItem = item;
        }
    }

    /// <summary>Re-arms hover selection on the first real pointer movement after a suppress.</summary>
    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_hoverAnchor is not { } anchor)
        {
            return;
        }

        var current = GetCursorPosition();
        if (current.X == anchor.X && current.Y == anchor.Y)
        {
            return;
        }

        _hoverAnchor = null;
        HoverSelectionEnabled = true;
    }

    /// <summary>
    /// Ported from the WPF original's OnPreviewKeyDown override (see the ctor comment for why this
    /// is a tunneling event handler rather than an OnKeyDown override). One open item this port
    /// could not verify without a live window (SpatialNavigationListView has no consumer yet --
    /// that is WindowSelectorWindow's port, task 4b.4): the WPF version specifically checked for
    /// <c>Key.System</c>/<c>e.SystemKey</c> because Alt is held for this control's entire lifetime
    /// (it is the Alt-Tab-style selector) and WPF reports Alt+key as a system key with the real key
    /// in SystemKey. WinUI 3's KeyRoutedEventArgs has no SystemKey-equivalent split that this port
    /// found, so e.Key is used directly on the assumption arrow keys arrive normally even with Alt
    /// held; if 4b.4's live verification shows Alt+Arrow does not reach this handler as a plain
    /// VirtualKey.Down/Up/Left/Right, this is the place to fix.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ArrowKeys.Contains(e.Key))
        {
            return;
        }

        if (!TryInitializeTileGrid(out var tileGrid))
        {
            return;
        }

        var next = e.Key switch
        {
            VirtualKey.Down => tileGrid.MoveDown(),
            VirtualKey.Up => tileGrid.MoveUp(),
            VirtualKey.Left => tileGrid.MoveLeft(),
            VirtualKey.Right => tileGrid.MoveRight(),
            _ => null,
        };

        if (next is { })
        {
            SelectedItem = next;
            e.Handled = true;
        }
    }

    /// <summary>Reads the live cursor position without a pointer event in hand, matching the
    /// established approach already proven elsewhere in this migration (WindowSelectorViewModel's
    /// CursorScreen/CenterScreen). System.Windows.Forms.Control is available here because
    /// WinTabber.Interop sets UseWindowsForms and this project references it transitively.</summary>
    private static Point GetCursorPosition()
    {
        var p = System.Windows.Forms.Control.MousePosition;
        return new Point(p.X, p.Y);
    }

    /// <summary>Rebuilds the tile grid unless already built; leaves it unbuilt if containers
    /// are not yet realised, so the next arrow press retries rather than caching a half-built grid.</summary>
    private bool TryInitializeTileGrid(out WindowTileGrid tileGrid)
    {
        if (_tileGrid is { } cached)
        {
            tileGrid = cached;
            return true;
        }

        var infos = new List<WindowTileInfo>(Items.Count);
        for (var i = 0; i < Items.Count; i++)
        {
            if (ContainerFromIndex(i) is not FrameworkElement container)
            {
                tileGrid = null!;
                return false;
            }

            infos.Add(GetTile(i, container));
        }

        if (infos.Count == 0)
        {
            tileGrid = null!;
            return false;
        }

        _tileGrid = WindowTileGrid.Create(infos);
        tileGrid = _tileGrid;
        return true;
    }

    private WindowTileInfo GetTile(int index, FrameworkElement container)
    {
        var item = (WindowItem)Items[index];
        var transform = container.TransformToVisual(this);
        var location = transform.TransformPoint(new Point(0, 0));

        return new WindowTileInfo
        {
            Container = container,
            WindowItem = item,
            Location = new TilePoint(location.X, location.Y),
            IsSelected = index == SelectedIndex,
            Index = index,
        };
    }
}
