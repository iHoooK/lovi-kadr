using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using System.Windows.Threading;
using PromptixCapture.Editor;
using PromptixCapture.Models;
using PromptixCapture.Services;

namespace PromptixCapture.Windows;

public sealed class EditorWindow : Window
{
    private readonly AnnotationCanvas _canvas;
    private readonly AnnotationDocument _document;
    private readonly ScreenshotSettings _settings;
    private readonly HistoryService _history;
    private readonly HistoryMediaType _kind;
    private readonly TextBlock _status=Ui.Text("Ничего не сохранено",12,true);
    private readonly ScrollViewer _scroll;
    private int _exportedRevision=-1;
    private string? _lastFile;
    private string _lastDirectory;
    private double _zoom=1;
    private bool _autoFit=true;
    internal AnnotationDocument Document=>_document;

    public EditorWindow(AnnotationDocument document,ScreenshotSettings settings,HistoryService history,HistoryMediaType kind=HistoryMediaType.Screenshot,bool alreadyExported=false)
        : this(document.BaseImage,settings,history,kind,document,alreadyExported) { }

    public EditorWindow(BitmapSource image,ScreenshotSettings settings,HistoryService history,HistoryMediaType kind=HistoryMediaType.Screenshot,AnnotationDocument? existing=null,bool alreadyExported=false)
    {
        Ui.ThemeWindow(this);
        _settings=settings;_history=history;_kind=kind;_lastDirectory=settings.Folder;
        _autoFit=kind!=HistoryMediaType.ScrollingScreenshot;
        Title=$"ЛовиКадр • {image.PixelWidth} × {image.PixelHeight}";
        Width=1160;Height=800;MinWidth=800;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        _document=existing??new AnnotationDocument(image);_canvas=new AnnotationCanvas(_document){Tool=settings.DefaultTool};
        if(alreadyExported)_exportedRevision=_document.Revision;
        var root=new DockPanel{LastChildFill=true};Content=root;
        var top=new DockPanel{Margin=new Thickness(14,8,14,8)};
        var topShell=new Border{Child=top,BorderThickness=new Thickness(0,0,0,1)};
        topShell.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");topShell.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        DockPanel.SetDock(topShell,Dock.Top);root.Children.Add(topShell);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Right);top.Children.Add(actions);
        actions.Children.Add(Ui.Button("↶",()=>{_canvas.ClearSelection();_document.Undo();},"Отменить · Ctrl+Z"));
        actions.Children.Add(Ui.Button("↷",()=>{_canvas.ClearSelection();_document.Redo();},"Вернуть · Ctrl+Y"));
        actions.Children.Add(Ui.Button("−",()=>Zoom(_zoom/1.25),"Уменьшить"));actions.Children.Add(Ui.Button("+",()=>Zoom(_zoom*1.25),"Увеличить"));
        actions.Children.Add(Ui.Button("100%",()=>Zoom(1)));actions.Children.Add(Ui.Button("Вписать",Fit));
        top.Children.Add(Ui.Text($"РЕДАКТОР   /   {image.PixelWidth} × {image.PixelHeight}",16));
        var bottom=new DockPanel{Margin=new Thickness(14,8,14,8)};
        var bottomShell=new Border{Child=bottom,BorderThickness=new Thickness(0,1,0,0)};
        bottomShell.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");bottomShell.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        DockPanel.SetDock(bottomShell,Dock.Bottom);root.Children.Add(bottomShell);
        var exports=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(exports,Dock.Right);bottom.Children.Add(exports);
        exports.Children.Add(Ui.AsyncButton("Копировать",Copy,"Ctrl+C — итоговое изображение"));
        exports.Children.Add(Ui.AsyncButton("Сохранить…",()=>Save(false),"Ctrl+S"));
        exports.Children.Add(Ui.AsyncButton("Быстро сохранить",()=>Save(true),"Ctrl+Shift+S"));
        exports.Children.Add(Ui.Button("Папка",()=>{if(_lastFile is not null)Ui.Reveal(_lastFile);else if(Directory.Exists(_lastDirectory))Ui.Open(_lastDirectory);}));bottom.Children.Add(_status);
        var left=new StackPanel{Width=190,Margin=new Thickness(10,8,10,8)};
        var toolsScroll=new ScrollViewer{Content=left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var toolsShell=new Border{Child=toolsScroll,BorderThickness=new Thickness(0,0,1,0)};
        toolsShell.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");toolsShell.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        DockPanel.SetDock(toolsShell,Dock.Left);root.Children.Add(toolsShell);
        var labels=new[]{"Выбор / перемещение","Ручка","Маркер","Линия","Стрелка","Прямоугольник","Эллипс","Текст","Номер","Размытие","Пикселизация","Закрасить"};
        var toolButtons=new List<Button>();
        void MarkTool(AnnotationTool selected)
        {
            foreach(var (button,index) in toolButtons.Select((button,index)=>(button,index)))
            {
                bool active=index==(int)selected;
                button.SetResourceReference(Button.BorderBrushProperty,active?"AccentBrush":"BorderBrush");
                button.SetResourceReference(Button.BackgroundProperty,active?"SurfaceHoverBrush":"ButtonBrush");
                button.SetResourceReference(Button.ForegroundProperty,active?"TextPrimaryBrush":"ButtonTextBrush");
                button.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;
            }
        }
        foreach(var tool in Enum.GetValues<AnnotationTool>())
        {
            var t=tool;var button=Ui.Button(labels[(int)tool],()=>{_canvas.Tool=t;_canvas.ClearSelection();if(t==AnnotationTool.Redact)_canvas.StrokeColor=Colors.Black;MarkTool(t);_canvas.Focus();},labels[(int)tool]);
            button.Padding=new Thickness(8,6,8,6);button.HorizontalContentAlignment=HorizontalAlignment.Left;
            toolButtons.Add(button);left.Children.Add(button);
        }
        MarkTool(_canvas.Tool);
        left.Children.Add(Ui.Text("Цвет",12,true));
        var palette=new WrapPanel();left.Children.Add(palette);
        var colors=new[]{Colors.OrangeRed,Colors.Orange,Colors.Gold,Colors.LimeGreen,Colors.DeepSkyBlue,Colors.RoyalBlue,Colors.MediumPurple,Colors.White,Colors.Black};
        var thickness=new ComboBox{ItemsSource=new double[]{1,2,3,4,6,8,12,16,24},SelectedItem=4d,Margin=new Thickness(3)};
        var fonts=new ComboBox{ItemsSource=new double[]{12,16,20,26,32,40,56,72},SelectedItem=26d,Margin=new Thickness(3)};
        var filled=new CheckBox{Content="Заливка фигур",Margin=new Thickness(3,8,3,8)};
        bool syncing=false;
        void Style(){if(!syncing)_canvas.ChangeStyle(_canvas.StrokeColor,(double)(thickness.SelectedItem??_canvas.StrokeWidth),(double)(fonts.SelectedItem??_canvas.FontSize),filled.IsChecked==true);}
        _canvas.SelectionChanged+=selected=>
        {
            if(selected is null)return;syncing=true;
            _canvas.StrokeColor=selected.Color;_canvas.StrokeWidth=selected.Thickness;_canvas.FontSize=selected.FontSize;
            thickness.SelectedItem=selected.Thickness;fonts.SelectedItem=selected.FontSize;filled.IsChecked=selected.Filled;syncing=false;
        };
        foreach(var color in colors)
        {
            var c=color;var b=Ui.Button("",()=>{_canvas.StrokeColor=c;Style();});b.Background=new SolidColorBrush(c);b.Width=31;b.Height=28;b.MinHeight=28;b.Padding=new Thickness(0);palette.Children.Add(b);
        }
        left.Children.Add(Ui.Button("Другой цвет…",()=>
        {
            using var dialog=new System.Windows.Forms.ColorDialog{FullOpen=true};
            if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK){var c=dialog.Color;_canvas.StrokeColor=Color.FromRgb(c.R,c.G,c.B);Style();}
        }));
        left.Children.Add(Ui.Text("Толщина / сила эффекта",12,true));left.Children.Add(thickness);
        left.Children.Add(Ui.Text("Размер текста / номера",12,true));left.Children.Add(fonts);left.Children.Add(filled);
        thickness.SelectionChanged+=(_,_)=>Style();fonts.SelectionChanged+=(_,_)=>Style();filled.Checked+=(_,_)=>Style();filled.Unchecked+=(_,_)=>Style();
        left.Children.Add(Ui.Button("Удалить объект",()=>_canvas.DeleteSelected(),"Delete"));
        var holder=new Border{Child=_canvas,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,BorderThickness=new Thickness(1)};
        holder.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");holder.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        var stage=new Grid{Margin=new Thickness(12)};stage.Children.Add(holder);stage.SetResourceReference(Panel.BackgroundProperty,"EditorCanvasBrush");
        _scroll=new ScrollViewer{Content=stage,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};root.Children.Add(_scroll);
        BindingOperations.SetBinding(stage,FrameworkElement.MinWidthProperty,new Binding(nameof(ScrollViewer.ViewportWidth)){Source=_scroll});
        BindingOperations.SetBinding(stage,FrameworkElement.MinHeightProperty,new Binding(nameof(ScrollViewer.ViewportHeight)){Source=_scroll});
        Loaded+=(_,_)=>{if(_autoFit)Dispatcher.BeginInvoke(Fit,DispatcherPriority.ContextIdle);else SetZoom(1);_canvas.Focus();};
        _scroll.SizeChanged+=(_,_)=>{if(_autoFit)Dispatcher.BeginInvoke(Fit,DispatcherPriority.ContextIdle);};
        PreviewKeyDown+=async(_,e)=>
        {
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                try
                {
                    switch(e.Key)
                    {
                        case Key.C:e.Handled=true;await Copy();break;
                        case Key.S:e.Handled=true;await Save(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));break;
                        case Key.Z:e.Handled=true;_canvas.ClearSelection();_document.Undo();break;
                        case Key.Y:e.Handled=true;_canvas.ClearSelection();_document.Redo();break;
                    }
                }
                catch(Exception ex){Ui.Error(ex);}
            }
            else if(e.Key==Key.Delete){_canvas.DeleteSelected();e.Handled=true;}
            else if(e.Key==Key.Escape){Close();e.Handled=true;}
        };
        Closing+=ConfirmClose;
    }
    internal static double FitZoom(double viewportWidth,double viewportHeight,double imageWidth,double imageHeight)=>
        Math.Min(1,Math.Min(Math.Max(100,viewportWidth-24)/imageWidth,Math.Max(100,viewportHeight-24)/imageHeight));
    private void Fit(){_autoFit=true;SetZoom(FitZoom(_scroll.ViewportWidth,_scroll.ViewportHeight,_canvas.Width,_canvas.Height));}
    private void Zoom(double value){_autoFit=false;SetZoom(value);}
    private void SetZoom(double value){_zoom=Math.Clamp(value,.02,4);_canvas.LayoutTransform=new ScaleTransform(_zoom,_zoom);}
    private async Task Copy()
    {
        await ImageExportService.CopyAsync(_canvas.Export());_exportedRevision=_document.Revision;_status.Text="Скопировано в буфер обмена";
        AppNotifications.Show(_kind==HistoryMediaType.ScrollingScreenshot?"Длинный снимок скопирован в буфер обмена":"Снимок скопирован в буфер обмена");
        if(_settings.AutoSave)await Save(true);
    }
    private Task Save(bool quick)
    {
        var image=_canvas.Export();
        if(quick&&string.IsNullOrWhiteSpace(_lastDirectory))return Save(false);
        var extension=_settings.Format==ImageFileFormat.Png?".png":".jpg";
        var path=quick?FileNames.Unique(_lastDirectory,_settings.FileNameTemplate,extension,image.PixelWidth,image.PixelHeight):"";
        if(!quick)
        {
            var initial=FileNames.Format(_settings.FileNameTemplate,DateTime.Now,image.PixelWidth,image.PixelHeight)+extension;
            var dialog=new SaveFileDialog{Title="Сохранить снимок",Filter="PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg",FilterIndex=_settings.Format==ImageFileFormat.Png?1:2,FileName=initial,InitialDirectory=_lastDirectory,AddExtension=true,OverwritePrompt=true};
            if(dialog.ShowDialog(this)!=true)return Task.CompletedTask;path=dialog.FileName;
        }
        ImageExportService.Save(image,path,_settings.JpegQuality);_lastFile=path;_exportedRevision=_document.Revision;
        _lastDirectory=Path.GetDirectoryName(path)??_lastDirectory;
        _history.Add(new HistoryItem{Path=path,Type=_kind,Width=image.PixelWidth,Height=image.PixelHeight});_status.Text="Сохранено: "+Path.GetFileName(path);
        AppNotifications.Show((_kind==HistoryMediaType.ScrollingScreenshot?"Длинный снимок сохранён: ":"Снимок сохранён: ")+Path.GetFileName(path),path);return Task.CompletedTask;
    }
    private void ConfirmClose(object? sender,CancelEventArgs e)
    {
        if(IsVisible && _exportedRevision!=_document.Revision && MessageBox.Show(this,"Закрыть снимок без сохранения последних изменений?","ЛовиКадр",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;
    }
}
