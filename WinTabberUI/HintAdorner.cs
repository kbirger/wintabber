//using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace WinTabberUI;
public class HintAdorner : Adorner
{
    private const int PaddingX = 5;
    private const int PaddingY = 4;
    //private const int OffsetX = -4;
    //private const int OffsetY = -4;
    private const int CornerRadius = 4;
    private const double FontSize = 10;

    private static readonly Typeface _typeface = new Typeface(SystemFonts.IconFontFamily, SystemFonts.IconFontStyle, SystemFonts.IconFontWeight, FontStretch.FromOpenTypeStretch(1));
    private static readonly Brush _textBrush = SystemColors.HighlightTextBrush;
    private static readonly Brush _fillBrush = SystemColors.AccentColorLight2Brush;
    private static readonly Brush _borderBrush = SystemColors.AccentColorDark1Brush;

    

    private static readonly double _wWidth;

        static HintAdorner()
    {
        _wWidth = new FormattedText("W", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, FontSize, _textBrush, 1).WidthIncludingTrailingWhitespace;
    }
    public HintAdorner(UIElement adornedElement)
        : base(adornedElement)
    {
    }

    public required string HintText { get; set; }

    public HintPosition Position { get; set; } = HintPosition.TopLeft;

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Get the size of the adorned element
        Rect adornedElementRect = new Rect(this.AdornedElement.RenderSize);
        
        var text = new FormattedText(
            HintText, 
            CultureInfo.CurrentCulture, 
            FlowDirection.LeftToRight, 
            _typeface,
            10, 
            _textBrush, 
            1);

        Point borderPosition = Position.GetPoint(adornedElementRect, text.Width, text.Height, new Thickness { Left = PaddingX, Right = PaddingX, Top = PaddingY, Bottom = PaddingY });
        Rect borderRect = new Rect(
            borderPosition.X, 
            borderPosition.Y, 
            _wWidth + PaddingX * 2,
            text.Height + PaddingY * 2);

        // Draw a red rectangle around the adorned element
        //Pen renderPen = new Pen(Brushes.Red, 2);
        drawingContext.DrawRoundedRectangle(
            _fillBrush, 
            new Pen(_borderBrush, 0.5), 
            borderRect, CornerRadius, CornerRadius);

        drawingContext.DrawText(
            text,
            new Point(
                (borderRect.Left) + (borderRect.Width / 2) - (text.Width / 2),
                (borderRect.Top ) + (borderRect.Height / 2) - (text.Height / 2)
            )
        );
    }
}
