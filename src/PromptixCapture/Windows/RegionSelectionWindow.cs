using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PromptixCapture.Helpers;
using DRect = System.Drawing.Rectangle;

namespace PromptixCapture.Windows;

public enum SelectionAction { Confirm, Copy, Save, Editor }

// One native window sized in physical pixels; drawing and hit testing use its
// measured client-size ratios, not the primary monitor's DPI.
public sealed class RegionSelectionWindow : Window
{
    private readonly SelectionSurface _surface;
    private readonly DRect _desktop;
    public DRect? Result { get; private set; }
    public SelectionAction Action { get; private set; } = SelectionAction.Confirm;
    public RegionSelectionWindow(BitmapSource screen, DRect desktop, DRect? previous = null)
    {
        _desktop = desktop;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross;
        Title = "ЛовиКадр — выбор области";
        _surface = new SelectionSurface(screen, desktop, null);
        var root=new Grid();root.Children.Add(_surface);
        var toolbar=new Border{Background=new SolidColorBrush(Color.FromArgb(235,21,27,35)),BorderBrush=Ui.Cyan,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(6),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(12)};
        var actions=new StackPanel{Orientation=Orientation.Horizontal};
        actions.Children.Add(Ui.Button("Копировать",()=>Finish(SelectionAction.Copy),"Ctrl+C"));
        actions.Children.Add(Ui.Button("Сохранить",()=>Finish(SelectionAction.Save),"Ctrl+S"));
        actions.Children.Add(Ui.Button("Редактор",()=>Finish(SelectionAction.Editor),"Открыть подробный редактор"));
        actions.Children.Add(Ui.Button("Готово",()=>Finish(SelectionAction.Confirm),"Enter"));
        actions.Children.Add(Ui.Button("Отмена",()=>{DialogResult=false;},"Esc"));toolbar.Child=actions;root.Children.Add(toolbar);Content=root;
        SourceInitialized += (_, _) => NativeMethods.PlacePixels(this, desktop);
        Loaded += (_, _) => { NativeMethods.PlacePixels(this, desktop); Activate(); Focus(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; return; }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key==Key.C) { Finish(SelectionAction.Copy); e.Handled=true; return; }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key==Key.S) { Finish(SelectionAction.Save); e.Handled=true; return; }
            if (e.Key == Key.Enter && _surface.Selection.Width > 2 && _surface.Selection.Height > 2)
            { Finish(SelectionAction.Confirm); e.Handled = true; }
            var delta = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                _surface.Move(e.Key == Key.Left ? -delta : e.Key == Key.Right ? delta : 0, e.Key == Key.Up ? -delta : e.Key == Key.Down ? delta : 0);
                e.Handled = true;
            }
        };
        MouseRightButtonDown += (_, _) => DialogResult = false;
        MouseDoubleClick += (_, _) => Finish(SelectionAction.Confirm);
    }
    private void Finish(SelectionAction action)
    {
        if(_surface.Selection.Width<=2 || _surface.Selection.Height<=2)return;
        Action=action;Result=_surface.PixelRectangle();DialogResult=true;
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
            MouseLeftButtonUp += (_, e) => { ReleaseMouseCapture(); UpdateCursor(PixelPoint(e)); };
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
        private int HitHandle(Point point)
        {
            int i=0;foreach(var handle in Handles()){if((handle-point).Length<12)return i;i++;}return -1;
        }
        private void UpdateCursor(Point point)
        {
            if(Selection.Width<=0){Cursor=Cursors.Cross;return;}
            Cursor=HitHandle(point) switch
            {
                0 or 4=>Cursors.SizeNWSE,
                2 or 6=>Cursors.SizeNESW,
                1 or 5=>Cursors.SizeNS,
                3 or 7=>Cursors.SizeWE,
                _ when Selection.Contains(point)=>Cursors.SizeAll,
                _=>Cursors.Cross
            };
        }
        private void Drag(object sender, MouseEventArgs e)
        {
            if (!IsMouseCaptured){UpdateCursor(PixelPoint(e));return;}
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
            InvalidateVisual();UpdateCursor(p);
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
