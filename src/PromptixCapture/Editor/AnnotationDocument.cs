using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PromptixCapture.Models;

namespace PromptixCapture.Editor;

public sealed class AnnotationModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AnnotationTool Tool { get; set; }
    public Point Start { get; set; }
    public Point End { get; set; }
    public List<Point> Points { get; set; } = new();
    public Color Color { get; set; } = Colors.OrangeRed;
    public double Thickness { get; set; } = 4;
    public double FontSize { get; set; } = 26;
    public bool Filled { get; set; }
    public string Text { get; set; } = "";
    public int Number { get; set; }
    public AnnotationModel Clone() => new() { Id=Id, Tool=Tool, Start=Start, End=End, Points=Points.ToList(), Color=Color, Thickness=Thickness, FontSize=FontSize, Filled=Filled, Text=Text, Number=Number };
    public Rect Bounds
    {
        get
        {
            if (Tool == AnnotationTool.Text) return new Rect(Start, new Size(Math.Max(FontSize, Text.Split('\n').Max(t=>t.Length)*FontSize*.67), Math.Max(FontSize*1.4, Text.Split('\n').Length*FontSize*1.4)));
            if (Tool == AnnotationTool.Number) return new Rect(Start.X-FontSize,Start.Y-FontSize,FontSize*2,FontSize*2);
            if (Points.Count > 0) return new Rect(new Point(Points.Min(p=>p.X),Points.Min(p=>p.Y)),new Point(Points.Max(p=>p.X),Points.Max(p=>p.Y)));
            return new Rect(Start,End);
        }
    }
    public void Translate(Vector delta) { Start += delta; End += delta; for(int i=0;i<Points.Count;i++) Points[i] += delta; }
    public void ResizeFrom(AnnotationModel before, Rect target)
    {
        var source=before.Bounds;
        double sx=target.Width/Math.Max(1,source.Width), sy=target.Height/Math.Max(1,source.Height);
        Point Map(Point p)=>new(target.X+(p.X-source.X)*sx,target.Y+(p.Y-source.Y)*sy);
        Start=Map(before.Start); End=Map(before.End); Points=before.Points.Select(Map).ToList();
        if(Tool is AnnotationTool.Text or AnnotationTool.Number) FontSize=Math.Clamp(before.FontSize*Math.Max(sx,sy),8,160);
    }
}

public sealed class AnnotationDocument
{
    public BitmapSource BaseImage { get; }
    public List<AnnotationModel> Items { get; private set; } = new();
    private readonly Stack<List<AnnotationModel>> _undo = new();
    private readonly Stack<List<AnnotationModel>> _redo = new();
    public event Action? Changed;
    public int Revision { get; private set; }
    public AnnotationDocument(BitmapSource image) => BaseImage=image;
    public void Checkpoint()
    {
        _undo.Push(Items.Select(a=>a.Clone()).ToList()); _redo.Clear();
        if(_undo.Count>100) { var recent=_undo.Take(100).Reverse().ToArray(); _undo.Clear(); foreach(var state in recent) _undo.Push(state); }
    }
    public void Notify() { Revision++; Changed?.Invoke(); }
    public void Undo() { if(_undo.Count==0)return; _redo.Push(Items.Select(a=>a.Clone()).ToList()); Items=_undo.Pop(); Notify(); }
    public void Redo() { if(_redo.Count==0)return; _undo.Push(Items.Select(a=>a.Clone()).ToList()); Items=_redo.Pop(); Notify(); }
}
