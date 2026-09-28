using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PromptixCapture.Helpers;
using PromptixCapture.Models;
using PromptixCapture.Services;
using DRect=System.Drawing.Rectangle;
using DPoint=System.Drawing.Point;

namespace PromptixCapture.Windows;

// The selection frame passes input through to the browser during capture.
// Keep its action buttons in a small, separately clickable surface at the
// original toolbar position, without showing a second information dialog.
public sealed class ScrollingWindow : Window
{
    private readonly ScrollingCaptureService _service;
    private readonly CancellationTokenSource _cancel=new();
    private bool _running,_finished;
    private DRect _controlBounds;
    public ScrollResult? Result { get; private set; }

    public ScrollingWindow(CaptureService capture,DRect area,ScrollingSettings settings,DPoint toolbarPosition)
    {
        Ui.ThemeWindow(this);
        Title="ЛовиКадр — управление захватом";
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;
        SizeToContent=SizeToContent.WidthAndHeight;
        WindowStartupLocation=WindowStartupLocation.Manual;
        ShowInTaskbar=false;Topmost=true;
        _service=new ScrollingCaptureService(capture);

        var row=new WrapPanel();
        var panel=new Border
        {
            Background=new SolidColorBrush(Color.FromArgb(240,21,27,35)),
            BorderBrush=new SolidColorBrush(Color.FromRgb(92,113,137)),
            BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),
            Padding=new Thickness(5),Child=row
        };
        Content=panel;
        row.Children.Add(Ui.Button("Стоп",Stop,"Остановить и скопировать длинный снимок"));
        row.Children.Add(Ui.ActionIconButton("close",Cancel,"Отмена","Esc"));

        async Task RunCaptureAsync()
        {
            if(_running)return;
            _running=true;
            try
            {
                Result=await _service.RunAsync(area,settings,
                    new Progress<ScrollProgress>(_=>{}),_cancel.Token,_controlBounds);
                _finished=true;DialogResult=true;
            }
            catch(OperationCanceledException){_finished=true;DialogResult=false;}
            catch(Exception ex){Ui.Error(ex);_finished=true;DialogResult=false;}
            finally{_running=false;}
        }

        KeyDown+=(_,e)=>
        {
            if(e.Key==System.Windows.Input.Key.Escape){Cancel();e.Handled=true;}
            else if(e.Key==System.Windows.Input.Key.Enter){Stop();e.Handled=true;}
        };
        SourceInitialized+=(_,_)=>NativeMethods.ExcludeFromCapture(this);
        Loaded+=(_,_)=>
        {
            var handle=new WindowInteropHelper(this).Handle;
            if(!NativeMethods.GetWindowRect(handle,out var bounds))
                throw new InvalidOperationException("Не удалось разместить панель управления захватом.");
            var width=bounds.Right-bounds.Left;
            var height=bounds.Bottom-bounds.Top;
            var work=System.Windows.Forms.Screen.FromPoint(toolbarPosition).WorkingArea;
            var x=Math.Clamp(toolbarPosition.X,work.Left,Math.Max(work.Left,work.Right-width));
            var y=Math.Clamp(toolbarPosition.Y,work.Top,Math.Max(work.Top,work.Bottom-height));
            _controlBounds=new DRect(x,y,width,height);
            NativeMethods.PlacePixels(this,_controlBounds);
            if(NativeMethods.GetWindowRect(handle,out var placed))_controlBounds=placed.Rectangle;
            _=RunCaptureAsync();
        };
        Closing+=OnClosing;
        Closed+=(_,_)=>_cancel.Dispose();
    }

    public void Stop()
    {
        if(_running)_service.StopRequested=true;
        else if(!_finished){_finished=true;DialogResult=false;}
    }
    public void Cancel()
    {
        if(_running)_cancel.Cancel();
        else if(!_finished){_finished=true;DialogResult=false;}
    }
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(_running&&!_finished){e.Cancel=true;_service.StopRequested=true;}
    }
}
