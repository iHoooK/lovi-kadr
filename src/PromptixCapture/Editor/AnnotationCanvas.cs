using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PromptixCapture.Models;
using PromptixCapture.Windows;

namespace PromptixCapture.Editor;

public sealed class AnnotationCanvas : FrameworkElement
{
    public AnnotationDocument Document { get; }
    public AnnotationTool Tool { get; set; } = AnnotationTool.Arrow;
    public Color StrokeColor { get; set; } = Colors.OrangeRed;
    public double StrokeWidth { get; set; } = 4;
    public double FontSize { get; set; } = 26;
    public bool FillShapes { get; set; }
    public AnnotationModel? Selected { get; private set; }
    public event Action<AnnotationModel?>? SelectionChanged;
    private AnnotationModel? _draft, _beforeResize;
    private Point _last;
    private bool _moving;
    private readonly Dictionary<Guid,(Rect Rect,int Size,AnnotationTool Tool,BitmapSource Image)> _filters=new();
    public AnnotationCanvas(AnnotationDocument document)
    {
        Document=document; Width=document.BaseImage.PixelWidth; Height=document.BaseImage.PixelHeight;
        Focusable=true; Cursor=Cursors.Cross;
        Document.Changed+=()=>{_filters.Clear();InvalidateVisual();};
        MouseLeftButtonDown+=Down; MouseMove+=Move; MouseLeftButtonUp+=Up;
    }
    public void ChangeStyle(Color color,double thickness,double fontSize,bool filled)
    {
        StrokeColor=color;StrokeWidth=thickness;FontSize=fontSize;FillShapes=filled;
        if(Selected is null)return;
        Document.Checkpoint();Selected.Color=color;Selected.Thickness=thickness;Selected.FontSize=fontSize;Selected.Filled=filled;Document.Notify();
    }
    public void ClearSelection(){Selected=null;SelectionChanged?.Invoke(null);InvalidateVisual();}
    public void DeleteSelected(){if(Selected is null)return;Document.Checkpoint();Document.Items.Remove(Selected);Selected=null;SelectionChanged?.Invoke(null);Document.Notify();}
    private Point Location(MouseEventArgs e){var p=e.GetPosition(this);return new Point(Math.Clamp(p.X,0,Width-1),Math.Clamp(p.Y,0,Height-1));}
    private void Down(object sender,MouseButtonEventArgs e)
    {
        Focus();var p=Location(e);_last=p;
        if(Tool==AnnotationTool.Select)
        {
            if(e.ClickCount==2 && Selected?.Tool==AnnotationTool.Text)
            {
                var updatedText=Ui.Prompt(Window.GetWindow(this),"Изменить подпись","Текст:",Selected.Text);
                if(!string.IsNullOrWhiteSpace(updatedText)){Document.Checkpoint();Selected.Text=updatedText;Document.Notify();}
                return;
            }
            if(Selected is not null && (Selected.Bounds.BottomRight-p).Length<14){Document.Checkpoint();_beforeResize=Selected.Clone();_moving=true;CaptureMouse();return;}
            Selected=Document.Items.LastOrDefault(a=>a.HitTest(p));
            SelectionChanged?.Invoke(Selected);
            if(Selected is not null){Document.Checkpoint();_moving=true;CaptureMouse();}
            InvalidateVisual();return;
        }
        Selected=null;
        string text="";
        if(Tool==AnnotationTool.Text)
        {
            text=Ui.Prompt(Window.GetWindow(this),"Текст аннотации","Введите текст (можно несколько строк):","")??"";
            if(string.IsNullOrWhiteSpace(text))return;
        }
        Document.Checkpoint();
        _draft=new AnnotationModel{Tool=Tool,Start=p,End=p,Color=StrokeColor,Thickness=StrokeWidth,FontSize=FontSize,Filled=FillShapes,Text=text,Number=Document.Items.Where(a=>a.Tool==AnnotationTool.Number).Select(a=>a.Number).DefaultIfEmpty(0).Max()+1};
        if(Tool is AnnotationTool.Pen or AnnotationTool.Highlighter)_draft.Points.Add(p);
        Document.Items.Add(_draft);
        if(Tool is AnnotationTool.Text or AnnotationTool.Number){_draft=null;Document.Notify();return;}
        CaptureMouse();InvalidateVisual();
    }
    private void Move(object sender,MouseEventArgs e)
    {
        if(!IsMouseCaptured)return;var p=Location(e);
        if(_moving && Selected is not null)
        {
            if(_beforeResize is not null){var b=_beforeResize.Bounds;Selected.ResizeFrom(_beforeResize,new Rect(b.TopLeft,new Point(Math.Max(b.Left+4,p.X),Math.Max(b.Top+4,p.Y))));}
            else Selected.Translate(p-_last);
        }
        else if(_draft is not null)
        {
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _draft.Tool is AnnotationTool.Line or AnnotationTool.Arrow)
            {var v=p-_draft.Start;double length=v.Length,angle=Math.Round(Math.Atan2(v.Y,v.X)/(Math.PI/4))*(Math.PI/4);p=_draft.Start+new Vector(Math.Cos(angle)*length,Math.Sin(angle)*length);}
            _draft.End=p;if(_draft.Tool is AnnotationTool.Pen or AnnotationTool.Highlighter)_draft.Points.Add(p);
        }
        _last=p;InvalidateVisual();
    }
    private void Up(object sender,MouseButtonEventArgs e)
    {
        if(!IsMouseCaptured)return;
        if(_draft is not null && _draft.Tool is not (AnnotationTool.Pen or AnnotationTool.Highlighter) && (_draft.End-_draft.Start).Length<2)Document.Items.Remove(_draft);
        _draft=null;_beforeResize=null;_moving=false;ReleaseMouseCapture();Document.Notify();
    }
    protected override void OnRender(DrawingContext dc)
    {
        RenderImage(dc);
        if(Selected is not null)
        {
            var b=Selected.Bounds;b.Inflate(5,5);dc.DrawRectangle(null,new Pen(Brushes.DeepSkyBlue,1){DashStyle=DashStyles.Dash},b);
            dc.DrawRectangle(Brushes.White,new Pen(Brushes.DeepSkyBlue,1),new Rect(Selected.Bounds.Right-5,Selected.Bounds.Bottom-5,10,10));
        }
    }
    public BitmapSource Export()
    {
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())RenderImage(dc);
        var result=new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return result;
    }
    private void RenderImage(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0,0,Width,Height)));
        dc.DrawImage(Document.BaseImage,new Rect(0,0,Width,Height));
        // Filters below annotations, opaque redactions LAST: no later blur can reveal a redacted password.
        foreach(var a in Document.Items.Where(a=>a.Tool is AnnotationTool.Blur or AnnotationTool.Pixelate))DrawFilter(dc,a);
        foreach(var a in Document.Items.Where(a=>a.Tool is not (AnnotationTool.Blur or AnnotationTool.Pixelate or AnnotationTool.Redact)))DrawAnnotation(dc,a);
        foreach(var a in Document.Items.Where(a=>a.Tool==AnnotationTool.Redact))dc.DrawRectangle(new SolidColorBrush(a.Color),null,a.Bounds);
        dc.Pop();
    }
    private void DrawFilter(DrawingContext dc,AnnotationModel a)
    {
        var r=Rect.Intersect(a.Bounds,new Rect(0,0,Width,Height));if(r.IsEmpty || r.Width<1 || r.Height<1)return;
        int size=(int)Math.Clamp(a.Thickness*2,4,32);
        if(!_filters.TryGetValue(a.Id,out var cached)||cached.Rect!=r||cached.Size!=size||cached.Tool!=a.Tool)
        {
            var rect=new Int32Rect((int)r.X,(int)r.Y,Math.Max(1,(int)r.Width),Math.Max(1,(int)r.Height));
            cached=(r,size,a.Tool,BitmapFilters.Apply(Document.BaseImage,rect,a.Tool,size));_filters[a.Id]=cached;
        }
        dc.DrawImage(cached.Image,r);
    }
    private static void DrawAnnotation(DrawingContext dc,AnnotationModel a)
    {
        var brush=new SolidColorBrush(a.Color);
        var pen=new Pen(brush,a.Thickness){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
        var r=a.Bounds;
        switch(a.Tool)
        {
            case AnnotationTool.Pen: case AnnotationTool.Highlighter:
                if(a.Points.Count==0)break;
                if(a.Tool==AnnotationTool.Highlighter){dc.PushOpacity(.35);pen.Thickness=Math.Max(12,a.Thickness*4);}
                var path=new StreamGeometry();using(var ctx=path.Open()){ctx.BeginFigure(a.Points[0],false,false);ctx.PolyLineTo(a.Points.Skip(1).ToArray(),true,false);}
                dc.DrawGeometry(null,pen,path);if(a.Tool==AnnotationTool.Highlighter)dc.Pop();break;
            case AnnotationTool.Line: case AnnotationTool.Arrow:
                dc.DrawLine(pen,a.Start,a.End);
                if(a.Tool==AnnotationTool.Arrow && (a.End-a.Start).Length>1)
                {var v=a.End-a.Start;v.Normalize();var side=new Vector(-v.Y,v.X);double n=Math.Max(12,a.Thickness*4);dc.DrawLine(pen,a.End,a.End-v*n+side*n*.5);dc.DrawLine(pen,a.End,a.End-v*n-side*n*.5);}break;
            case AnnotationTool.Rectangle: dc.DrawRectangle(a.Filled?brush:null,pen,r);break;
            case AnnotationTool.Ellipse: dc.DrawEllipse(a.Filled?brush:null,pen,new Point(r.X+r.Width/2,r.Y+r.Height/2),r.Width/2,r.Height/2);break;
            case AnnotationTool.Text:
                var ft=Text(a.Text,a.FontSize,brush);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(180,13,17,23)),null,new Rect(a.Start.X-4,a.Start.Y-2,ft.Width+8,ft.Height+4),3,3);dc.DrawText(ft,a.Start);break;
            case AnnotationTool.Number:
                dc.DrawEllipse(brush,new Pen(Brushes.White,2),a.Start,a.FontSize,a.FontSize);
                var number=Text(a.Number.ToString(),a.FontSize,Brushes.White);dc.DrawText(number,new Point(a.Start.X-number.Width/2,a.Start.Y-number.Height/2));break;
        }
    }
    private static FormattedText Text(string text,double size,Brush brush)=>new(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,1);
}
