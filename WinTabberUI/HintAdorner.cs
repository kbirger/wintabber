using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace WinTabberUI
{
    public class HintAdorner : Adorner
    {
        public HintAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            // Get the size of the adorned element
            Rect adornedElementRect = new Rect(this.AdornedElement.RenderSize);

            // Draw a red rectangle around the adorned element
            Pen renderPen = new Pen(Brushes.Red, 2);
            drawingContext.DrawRectangle(Brushes.Gray, renderPen, adornedElementRect);

            // Optionally, draw a circle at the top-left corner
            drawingContext.DrawEllipse(Brushes.Blue, null, adornedElementRect.TopLeft, 5, 5);
        }
    }
}
