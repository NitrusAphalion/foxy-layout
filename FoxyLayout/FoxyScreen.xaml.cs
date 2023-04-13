using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WpfScreenHelper;
using static FoxyLayout.FoxyUtilities;

namespace FoxyLayout
{
    /// <summary>
    /// Interaction logic for FoxyScreen.xaml
    /// </summary>
    internal sealed partial class FoxyScreen : Window
    {
        public FoxyScreen() { InitializeComponent(); }

        public FoxyScreen(Screen screen) : this()
        {
            Screen = screen;
            SetWindowSize(Screen.WorkingArea);
        }

        public void ClearCanvas() { FoxyUtilities.InvokeIfNecessary(Dispatcher, () => foxyCanvas.Children.Clear()); }

        public void DrawIntersection(Rect rect, Rect rect2, GeometryCombineMode combineMode)
        {
            FoxyUtilities.InvokeIfNecessary(Dispatcher, () =>
            {
                Topmost = true;
                Topmost = false;
                Path pathFill = new Path()
                {
                    Data    = new CombinedGeometry(combineMode, new RectangleGeometry(rect), new RectangleGeometry(rect2)),
                    Fill    = FoxyUtilities.Options.CancelStroke.Brush,
                    Opacity = FoxyUtilities.Options.CancelStroke.Opacity / (double)100
                };
                if (foxyCanvas.Children.Contains(pathFill))
                    return;
                else
                    foxyCanvas.Children.Clear();
                Path pathStroke = new Path()
                {
                    Data            = pathFill.Data,
                    Stroke          = FoxyUtilities.Options.CancelStroke.Brush,
                    StrokeThickness = FoxyUtilities.Options.CancelStroke.Width,
                    StrokeDashArray = GetDashStyle(FoxyUtilities.Options.CancelStroke.DashStyleHelper).Dashes
                };
                foxyCanvas.Children.Add(pathFill);
                foxyCanvas.Children.Add(pathStroke);
            });
        }

        public void DrawRectangle(Rect bounds)
        {
            FoxyUtilities.InvokeIfNecessary(Dispatcher, () =>
            {
                Topmost = true;
                Topmost = false;
                Rectangle rectangleFill = new Rectangle()
                {
                    Width   = bounds.Width,
                    Height  = bounds.Height,
                    Fill    = FoxyUtilities.Options.SplitStroke.Brush,
                    Opacity = FoxyUtilities.Options.SplitStroke.Opacity
                };
                Canvas.SetTop(rectangleFill, bounds.Top);
                Canvas.SetLeft(rectangleFill, bounds.Left);
                if (foxyCanvas.Children.Contains(rectangleFill))
                    return;
                else
                    foxyCanvas.Children.Clear();
                Rectangle rectangleStroke = new Rectangle()
                {
                    Width           = bounds.Width,
                    Height          = bounds.Height,
                    Stroke          = FoxyUtilities.Options.SplitStroke.Brush,
                    StrokeThickness = FoxyUtilities.Options.SplitStroke.Width,
                    StrokeDashArray = GetDashStyle(FoxyUtilities.Options.SplitStroke.DashStyleHelper).Dashes
                };
                Canvas.SetTop(rectangleStroke, bounds.Top);
                Canvas.SetLeft(rectangleStroke, bounds.Left);
                foxyCanvas.Children.Add(rectangleFill);
                foxyCanvas.Children.Add(rectangleStroke);
            });
        }

        private void SetWindowSize(Rect bounds)
        {
            Top     = bounds.Top;
            Left    = bounds.Left;
            Width   = bounds.Width;
            Height  = bounds.Height;
        }

        public Rect GetBounds()
        {
            Rect rect = new Rect();
            FoxyUtilities.InvokeIfNecessary(Dispatcher, () => rect = new Rect(Left, Top, Width, Height));
            return rect;
        }

        public void UpdateBounds(double? left = null, double? top = null, double? width = null, double? height = null)
        {
            FoxyUtilities.InvokeIfNecessary(Dispatcher, () => SetWindowSize(new Rect(left ?? Left, top ?? Top, width ?? Width, height ?? Height)));
        }

        public Screen Screen { get; private set; }
    }
}
