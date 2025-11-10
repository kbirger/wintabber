using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinTabberUI
{
    public class SpatialNavigationListView : ListView
    {

        private static readonly Key[] _arrowKeys = [Key.Down, Key.Up, Key.Left, Key.Right];

        private WindowTileGrid? _tileGrid;

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
            InitializeTileGrid();
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

        [MemberNotNull(nameof(_tileGrid))]
        private void InitializeTileGrid()
        {
            if (_tileGrid is not null)
            {
                return;
            }
            var infos = new List<WindowTileInfo>(Items.Count);
            for (int i = 0; i < Items.Count; i++)
            {
                var tile = GetTile(i);
                infos.Add(tile);
            }
            _tileGrid = WindowTileGrid.Create(infos);
        }

        private WindowTileInfo GetTile(int index)
        {
            var container = (Visual)ItemContainerGenerator.ContainerFromIndex(index);
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
}
