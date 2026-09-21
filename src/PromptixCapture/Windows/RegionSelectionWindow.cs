using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PromptixCapture.Helpers;
using DRect = System.Drawing.Rectangle;

namespace PromptixCapture.Windows;

// One native window sized in physical pixels; drawing and hit testing use its
// measured client-size ratios, not the primary monitor's DPI.
public sealed class RegionSelectionWindow : Window
{
    private readonly SelectionSurface _surface;
    private readonly DRect _desktop;
    public DRect? Result { get; private set; }
    public RegionSelectionWindow(BitmapSource screen, DRect desktop, DRect? previous = null)
    {
        _desktop = desktop;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross;
        Title = "ЛовиКадр — выбор области";
        _surface = new SelectionSurface(screen, desktop, previous);
        Content = _surface;
        SourceInitialized += (_, _) => NativeMethods.PlacePixels(this, desktop);
        Loaded += (_, _) => { NativeMethods.PlacePixels(this, desktop); Activate(); Focus(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            if (e.Key == Key.Enter && _surface.Selection.Width > 2 && _surface.Selection.Height > 2)
            { Result = _surface.PixelRectangle(); DialogResult = true; e.Handled = true; }
            var delta = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                _surface.Move(e.Key == Key.Left ? -delta : e.Key == Key.Right ? delta : 0, e.Key == Key.Up ? -delta : e.Key == Key.Down ? delta : 0);
                e.Handled = true;
            }
        };
        MouseRightButtonDown += (_, _) => DialogResult = false;
        MouseDoubleClick += (_, _) => { if (_surface.Selection.Width > 2 && _surface.Selection.Height > 2) { Result = _surface.PixelRectangle(); DialogResult = true; } };
    }

    private sealed class SelectionSurface : FrameworkElement
    {
        private readonly BitmapSource _image;
        private readonly DRect _desktop;
        public Rect Selection { get; private set; }
        private Point _start;
        private Rect _before;
        private int _drag; // 0 new, 1 move, 2..9 handles
        public SelectionSurface(BitmapSource image, DRect desktop, DRect? previous)
        {
            _image = image; _desktop = desktop;
            if (previous is { } p && desktop.Contains(p)) Selection = new Rect(p.X - desktop.X, p.Y - desktop.Y, p.Width, p.Height);
            MouseLeftButtonDown += Down;
            MouseMove += Drag;
            MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        }
        private Point PixelPoint(MouseEventArgs e) { var p = e.GetPosition(this); return new Point(p.X * _desktop.Width / ActualWidth, p.Y * _desktop.Height / ActualHeight); }
        private IEnumerable<Point> Handles()
        {
            var r = Selection;
            yield return r.TopLeft; yield return new Point(r.Left + r.Width/2,r.Top); yield return r.TopRight;
            yield return new Point(r.Right,r.Top+r.Height/2); yield return r.BottomRight; yield return new Point(r.Left+r.Width/2,r.Bottom);
            yield return r.BottomLeft; yield return new Point(r.Left,r.Top+r.Height/2);
        }
        private void Down(object sender, MouseButtonEventArgs e)
        {
            _start = PixelPoint(e); _before = Selection; _drag = 0;
            if (Selection.Width > 0)
            {
                int i = 0; foreach (var p in Handles()) { if ((p - _start).Length < 12) { _drag = i + 2; break; } i++; }
                if (_drag == 0 && Selection.Contains(_start)) _drag = 1;
            }
            if (_drag == 0) Selection = new Rect(_start, _start);
            CaptureMouse(); InvalidateVisual();
        }
        private void Drag(object sender, MouseEventArgs e)
        {
            if (!IsMouseCaptured) return;
            var p = PixelPoint(e); p.X = Math.Clamp(p.X, 0, _desktop.Width); p.Y = Math.Clamp(p.Y, 0, _desktop.Height);
            var d = p - _start;
            if (_drag == 0) Selection = new Rect(_start, p);
            else if (_drag == 1)
            {
                Selection = new Rect(Math.Clamp(_before.X + d.X, 0, Math.Max(0, _desktop.Width - _before.Width)), Math.Clamp(_before.Y + d.Y, 0, Math.Max(0,_desktop.Height - _before.Height)), _before.Width, _before.Height);
            }
            else
            {
                var a = _before.TopLeft; var b = _before.BottomRight; int h = _drag - 2;
                if (h is 0 or 6 or 7) a.X = p.X;
                if (h is 0 or 1 or 2) a.Y = p.Y;
                if (h is 2 or 3 or 4) b.X = p.X;
                if (h is 4 or 5 or 6) b.Y = p.Y;
                Selection = new Rect(a, b);
            }
            InvalidateVisual();
        }
        public void Move(int x, int y)
        {
            var r = Selection;
            Selection = new Rect(Math.Clamp(r.X+x,0,Math.Max(0,_desktop.Width-r.Width)),Math.Clamp(r.Y+y,0,Math.Max(0,_desktop.Height-r.Height)),r.Width,r.Height);
            InvalidateVisual();
        }
        public DRect PixelRectangle() => new(_desktop.X+(int)Selection.X,_desktop.Y+(int)Selection.Y,Math.Max(1,(int)Selection.Width),Math.Max(1,(int)Selection.Height));
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            double sx = ActualWidth / _desktop.Width, sy = ActualHeight / _desktop.Height;
            dc.PushTransform(new ScaleTransform(sx, sy));
            dc.DrawImage(_image, new Rect(0,0,_desktop.Width,_desktop.Height));
            var mask = new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(new Rect(0,0,_desktop.Width,_desktop.Height)),new RectangleGeometry(Selection));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(155,0,0,0)),null,mask);
            dc.DrawRectangle(null,new Pen(Brushes.DeepSkyBlue,2),Selection);
            if (Selection.Width > 0) foreach (var p in Handles()) dc.DrawRectangle(Brushes.White,new Pen(Brushes.DeepSkyBlue,1),new Rect(p.X-4,p.Y-4,8,8));
            var label = $"{(int)Selection.Width} × {(int)Selection.Height}  •  Enter / двойной щелчок — готово  •  Esc — отмена";
            var ft = new FormattedText(label,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),16,Brushes.White,1);
            var pos = new Point(Math.Clamp(Selection.X, 10, Math.Max(10,_desktop.Width-ft.Width-20)),Math.Max(12,Selection.Y-40));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(13,17,23)),null,new Rect(pos.X-8,pos.Y-6,ft.Width+16,ft.Height+12),8,8);
            dc.DrawText(ft,pos); dc.Pop();
        }
    }
}
