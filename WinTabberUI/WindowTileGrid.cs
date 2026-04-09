using System.Diagnostics;
using WinTabberUI.ViewModels;

namespace WinTabberUI;

public class WindowTileGrid
{
    public static WindowTileGrid Create(IEnumerable<WindowTileInfo> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        // Collect unique X and Y coordinates, sorted for consistent order
        var xs = items.Select(i => i.Location.X).Distinct().OrderBy(x => x).ToList();
        var ys = items.Select(i => i.Location.Y).Distinct().OrderBy(y => y).ToList();

        int cols = xs.Count;
        int rows = ys.Count;
        int selectedX = 0;
        int selectedY = 0;
        // Create a 2D array with [row, column] indexing
        var grid = new WindowTileInfo[rows][];
        for (int i = 0; i < rows; i++)
        {
            grid[i] = new WindowTileInfo[cols];
        }
        // Build lookup for coordinate to index
        var xIndex = xs.Select((x, i) => (x, i)).ToDictionary(t => t.x, t => t.i);
        var yIndex = ys.Select((y, i) => (y, i)).ToDictionary(t => t.y, t => t.i);

        // Place items into their appropriate cells
        foreach (var item in items)
        {
            int col = xIndex[item.Location.X];
            int row = yIndex[item.Location.Y];
            grid[row][col] = item;
            if (item.IsSelected)
            {
                selectedX = col;
                selectedY = row;
            }
        }

        for (int i = 0; i < rows; i++)
        {
            var realLength = Array.IndexOf(grid[i], null);
            if (realLength >= 0)
            {
                Array.Resize(ref grid[i], realLength);
                Debug.WriteLine($"row {i} length {grid[i].Length}");
            }
        }
        return new WindowTileGrid
        {
            Items = grid,
            SelectedX = selectedX,
            SelectedY = selectedY
        };
    }

    public WindowTileInfo[][] Items { get; init; }
    public int SelectedX { get; private set; }
    public int SelectedY { get; private set; }
    public WindowItem? SelectedItem => 
        Items.ElementAtOrDefault(SelectedY)
        ?.ElementAtOrDefault(SelectedX)
        ?.WindowItem;

    private int SelectedRowLength => Items[SelectedY].Length;
    private int SelectedColumnLength => Items.TakeWhile(row => row.Length > SelectedX).Count();

    public WindowItem? MoveRight(bool wrap = true)
    {
        
        if(SelectedX < SelectedRowLength -1)
        {
            SelectedX++;
        }
        else if(wrap)
        {
            SelectedX = 0;
            MoveDown(false);
            MoveToRowStart();
        }

        return SelectedItem;
    }

    public WindowItem? MoveLeft(bool wrap = true)
    {
        if (SelectedX > 0)
        {
            SelectedX--;
        }
        else if(wrap)
        {
            MoveUp(true);
            MoveToRowEnd();                
        }

        return SelectedItem;
    }

    public WindowItem? MoveDown(bool wrap = true)
    {
        if(SelectedY < SelectedColumnLength - 1)
        {
            SelectedY++;
        }
        else
        {
            SelectedY = 0;
        }

        return SelectedItem;
    }

    public WindowItem? MoveUp(bool wrap = true)
    {
        if(SelectedY > 0)
        {
            SelectedY--;
        }
        else if(wrap)
        {
            MoveToColumnEnd();
        }

        return SelectedItem;
    }

    public WindowItem? MoveToRowStart()
    {
        SelectedX = 0;

        return SelectedItem;
    }

    public WindowItem? MoveToRowEnd()
    {
        SelectedX = SelectedRowLength - 1;

        return SelectedItem;
    }

    public WindowItem? MoveToColumnStart()
    {
        SelectedY = 0;
        return SelectedItem;
    }

    public WindowItem? MoveToColumnEnd()
    {
        SelectedY = SelectedColumnLength - 1;
        return SelectedItem;
    }
}
