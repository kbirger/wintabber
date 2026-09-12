using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinTabberUI.Behaviors;
using WinTabberUI.Models;
using WinTabber.ViewModels;

namespace WinTabberUI.Controls;

public class SpatialNavigationListView : ListView
{

    private static readonly Key[] _arrowKeys = [Key.Down, Key.Up, Key.Left, Key.Right];

    private WindowTileGrid? _tileGrid;

    /// <summary>Cursor position when hover selection was suppressed; null once it is re-armed.</summary>
    private System.Drawing.Point? _hoverAnchor;

    /// <summary>
    /// The selector window is reused across opens (see <c>ReuseInstances</c>), so this control -- and
    /// any tile grid it has already built -- outlives a single open. Both the item list and the tile
    /// positions change from one open to the next, so a grid built on the first open would steer
    /// arrow-key navigation on every later one, off a stale window list at stale coordinates.
    /// Dropping it here lets the next arrow press rebuild it against what is actually on screen.
    /// </summary>
    public SpatialNavigationListView()
    {
        IsVisibleChanged += (_, _) => _tileGrid = null;
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        _tileGrid = null;
        base.OnItemsChanged(e);
    }

    /// <summary>
    /// Ignore hover selection until the pointer actually moves. Called as the selector is shown;
    /// see <see cref="HoverSelect" /> for why the reveal alone must not select anything.
    /// </summary>
    public void SuppressHoverUntilPointerMoves()
    {
        _hoverAnchor = System.Windows.Forms.Control.MousePosition;
        HoverSelect.SetIsEnabled(this, false);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        // Field test first: this runs for every mouse move over the list, and in the armed state
        // (the overwhelming majority of them) it must not cost a cursor query or a property read.
        if (_hoverAnchor is not { } anchor || System.Windows.Forms.Control.MousePosition == anchor)
        {
            return;
        }

        _hoverAnchor = null;
        HoverSelect.SetIsEnabled(this, true);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is WindowItem windowItem)
        {
            SelectedItem = windowItem;
            ScrollIntoView(windowItem);
        }
        base.OnSelectionChanged(e);
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (!_arrowKeys.Contains(key))
        {
            return;
        }

        if (!TryInitializeTileGrid())
        {
            return;
        }

        var next = key switch
        {
            Key.Down => _tileGrid.MoveDown(),
            Key.Up => _tileGrid.MoveUp(),
            Key.Left => _tileGrid.MoveLeft(),
            Key.Right => _tileGrid.MoveRight(),
            _ => null
        };

        if (next is { })
        {
            SelectedItem = next;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Builds the tile grid unless it is already built. Returns false when the containers have not
    /// been realised yet: the grid is then left unbuilt so the next press retries, rather than being
    /// cached in a half-built state. This matters now that the grid is invalidated on every open --
    /// an arrow press can arrive before the regenerated containers exist.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_tileGrid))]
    private bool TryInitializeTileGrid()
    {
        if (_tileGrid is not null)
        {
            return true;
        }

        var infos = new List<WindowTileInfo>(Items.Count);
        for (int i = 0; i < Items.Count; i++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(i) is not Visual container)
            {
                return false;
            }

            infos.Add(GetTile(i, container));
        }

        if (infos.Count == 0)
        {
            return false;
        }

        _tileGrid = WindowTileGrid.Create(infos);
        return true;
    }

    private WindowTileInfo GetTile(int index, Visual container)
    {
        var item = (WindowItem)Items[index];
        var location = container.TransformToVisual(this).Transform(new Point(0, 0));

        return new WindowTileInfo
        {
            Container = container,
            WindowItem = item,
            Location = location,
            IsSelected = index == SelectedIndex,
            Index = index
        };
    }
}