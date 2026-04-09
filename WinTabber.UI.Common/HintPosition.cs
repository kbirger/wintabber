
//using System.Drawing;
using System.Windows;

namespace WinTabber.UI.Common;

public class HintPosition
{
    public HintPosition()
    {

    }

    public double? OffsetLeft { get; init; }
    public double? OffsetTop { get; init; }
    public double? OffsetRight { get; init; }
    public double? OffsetBottom { get; init; }

    public static readonly HintPosition TopLeft = new HintPosition
    {
        OffsetLeft = -4,
        OffsetTop = -4
    };

    public static readonly HintPosition Left = new HintPosition
    {
        OffsetLeft = -4,
    };

    public static readonly HintPosition BottomLeft = new HintPosition
    {
        OffsetBottom = -4,
    };

    public static readonly HintPosition TopLeftInset = new HintPosition
    {
        OffsetLeft = 0,
        OffsetTop = 0
    };

    public static readonly HintPosition LeftInset = new HintPosition
    {
        OffsetLeft = 0,
    };

    public static readonly HintPosition BottomLeftInset = new HintPosition
    {
        OffsetBottom = 0,
    };

    public static readonly HintPosition RightInset = new HintPosition
    {
        OffsetRight = 0
    };

    public Point GetPoint(Rect bounds, double width, double height, Thickness padding)
    {
        return new Point(GetX(width, bounds, padding), GetY(height, bounds, padding));
    }

    private double GetX(double width, Rect bounds, Thickness padding)
    {
        if (OffsetLeft is not null)
        {
            return bounds.Left + OffsetLeft.Value;
        }
        else if (OffsetRight is not null)
        {
            return bounds.Right - OffsetRight.Value - width * 2 - padding.Right - padding.Left;
        }

        else
        {
            return bounds.Right - bounds.Width / 2 - width / 2 - padding.Right;
        }
    }

    private double GetY(double height, Rect bounds, Thickness padding)
    {
        if (OffsetTop is not null)
        {
            return bounds.Top + OffsetTop.Value;
        }

        else if (OffsetBottom is not null)
        {
            return bounds.Bottom - OffsetBottom.Value - height;
        }

        else
        {
            return bounds.Bottom - bounds.Height / 2 - height / 2 - padding.Bottom;
        }
    }
}
