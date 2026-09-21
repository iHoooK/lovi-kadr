using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private double _zoom=1;

    public EditorWindow(BitmapSource image,ScreenshotSettings settings,HistoryService history,HistoryMediaType kind=HistoryMediaType.Screenshot)
    {
        _settings=settings;_history=history;_kind=kind;
        Title=$"ЛовиКадр • {image.PixelWidth} × {image.PixelHeight}";
        Width=1160;Height=800;MinWidth=800;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        _document=new AnnotationDocument(image);_canvas=new AnnotationCanvas(_document){Tool=settings.DefaultTool};
        var root=new DockPanel{LastChildFill=true};Content=root;
        var top=new DockPanel{Margin=new Thickness(14,10,14,6)};DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Right);top.Children.Add(actions);
        actions.Children.Add(Ui.Button("↶",()=>{_canvas.ClearSelection();_document.Undo();},"Отменить · Ctrl+Z"));
        actions.Children.Add(Ui.Button("↷",()=>{_canvas.ClearSelection();_document.Redo();},"Вернуть · Ctrl+Y"));
        actions.Children.Add(Ui.Button("−",()=>Zoom(_zoom/1.25),"Уменьшить"));actions.Children.Add(Ui.Button("+",()=>Zoom(_zoom*1.25),"Увеличить"));
        actions.Children.Add(Ui.Button("100%",()=>Zoom(1)));actions.Children.Add(Ui.Button("Вписать",Fit));
        top.Children.Add(Ui.Text($"РЕДАКТОР   /   {image.PixelWidth} × {image.PixelHeight}",16));
        var bottom=new DockPanel{Margin=new Thickness(14,8,14,14)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var exports=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(exports,Dock.Right);bottom.Children.Add(exports);
        exports.Children.Add(Ui.AsyncButton("Копировать",Copy,"Ctrl+C — итоговое изображение"));
        exports.Children.Add(Ui.AsyncButton("Сохранить…",()=>Save(false),"Ctrl+S"));
        exports.Children.Add(Ui.AsyncButton("Быстро сохранить",()=>Save(true),"Ctrl+Shift+S"));
        exports.Children.Add(Ui.Button("Папка",()=>{if(_lastFile is not null)Ui.Reveal(_lastFile);else Ui.Open(settings.Folder);}));bottom.Children.Add(_status);
        var left=new StackPanel{Width=175,Margin=new Thickness(12,4,10,4)};DockPanel.SetDock(left,Dock.Left);root.Children.Add(new ScrollViewer{Content=left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        // Set dock on the actual direct child.
        DockPanel.SetDock(root.Children[^1],Dock.Left);
        var labels=new[]{"Выбор / перемещение","Ручка","Маркер","Линия","Стрелка","Прямоугольник","Эллипс","Текст","Номер","Размытие","Пикселизация","Закрасить"};
        var toolButtons=new List<Button>();
        foreach(var tool in Enum.GetValues<AnnotationTool>())
        {
            var t=tool;var button=Ui.Button(labels[(int)tool],()=>{_canvas.Tool=t;_canvas.ClearSelection();if(t==AnnotationTool.Redact)_canvas.StrokeColor=Colors.Black;foreach(var b in toolButtons)b.BorderBrush=new SolidColorBrush(Color.FromRgb(52,65,84));toolButtons[(int)t].BorderBrush=Ui.Cyan;_canvas.Focus();});
            button.Padding=new Thickness(8,6,8,6);button.HorizontalContentAlignment=HorizontalAlignment.Left;
            if(tool==_canvas.Tool)button.BorderBrush=Ui.Cyan;
            toolButtons.Add(button);left.Children.Add(button);
        }
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
        var holder=new Border{Background=new SolidColorBrush(Color.FromRgb(28,35,45)),Child=_canvas,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
        _scroll=new ScrollViewer{Content=holder,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=new SolidColorBrush(Color.FromRgb(8,12,17)),Margin=new Thickness(0,4,14,0)};root.Children.Add(_scroll);
        Loaded+=(_,_)=>{Fit();_canvas.Focus();};
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
    private void Fit()=>Zoom(Math.Min(1,Math.Min(Math.Max(100,_scroll.ViewportWidth)/_canvas.Width,Math.Max(100,_scroll.ViewportHeight)/_canvas.Height)));
    private void Zoom(double value){_zoom=Math.Clamp(value,.02,4);_canvas.LayoutTransform=new ScaleTransform(_zoom,_zoom);}
    private async Task Copy()
    {
        await ImageExportService.CopyAsync(_canvas.Export());_exportedRevision=_document.Revision;_status.Text="Скопировано в буфер обмена";
        if(_settings.AutoSave)await Save(true);
    }
    private Task Save(bool quick)
    {
        var image=_canvas.Export();var path=ImageExportService.DefaultPath(image,_settings);
        if(!quick)
        {
            var dialog=new SaveFileDialog{Title="Сохранить снимок",Filter="PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg",FilterIndex=_settings.Format==ImageFileFormat.Png?1:2,FileName=Path.GetFileName(path),InitialDirectory=_settings.Folder,AddExtension=true,OverwritePrompt=true};
            if(dialog.ShowDialog(this)!=true)return Task.CompletedTask;path=dialog.FileName;
        }
        ImageExportService.Save(image,path,_settings.JpegQuality);_lastFile=path;_exportedRevision=_document.Revision;
        _history.Add(new HistoryItem{Path=path,Type=_kind,Width=image.PixelWidth,Height=image.PixelHeight});_status.Text="Сохранено: "+Path.GetFileName(path);return Task.CompletedTask;
    }
    private void ConfirmClose(object? sender,CancelEventArgs e)
    {
        if(_exportedRevision!=_document.Revision && MessageBox.Show(this,"Закрыть снимок без сохранения последних изменений?","ЛовиКадр",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;
    }
}
