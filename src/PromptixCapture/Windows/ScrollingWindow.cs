using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PromptixCapture.Helpers;
using PromptixCapture.Models;
using PromptixCapture.Services;
using DRect=System.Drawing.Rectangle;

namespace PromptixCapture.Windows;

public sealed class ScrollingWindow : Window
{
    private readonly ScrollingCaptureService _service;
    private readonly CancellationTokenSource _cancel=new();
    private bool _running,_finished;
    public ScrollResult? Result { get; private set; }
    public ScrollingWindow(CaptureService capture,DRect area,ScrollingSettings settings)
    {
        Ui.ThemeWindow(this);
        Title="ЛовиКадр — длинный снимок";Width=480;Height=225;ResizeMode=ResizeMode.NoResize;Topmost=true;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        _service=new ScrollingCaptureService(capture);
        var panel=new StackPanel{Margin=new Thickness(16)};Content=panel;
        panel.Children.Add(Ui.Text("ДЛИННЫЙ СНИМОК",18));
        var status=Ui.Text("Нажмите «Начать», затем активируйте нужное окно и поместите курсор внутри выбранной области.",13,true);panel.Children.Add(status);
        var stats=Ui.Text($"Область: {area.Width} × {area.Height}",12,true);panel.Children.Add(stats);
        var row=new StackPanel{Orientation=Orientation.Horizontal};panel.Children.Add(row);
        var start=Ui.AsyncButton("Начать",async()=>
        {
            if(_running)return;_running=true;
            var progress=new Progress<ScrollProgress>(p=>{status.Text=p.State;stats.Text=$"Кадров: {p.Frames}   •   {area.Width} × {p.Height}";});
            try
            {
                await Task.Delay(600,_cancel.Token);
                Result=await _service.RunAsync(area,settings,progress,_cancel.Token);_finished=true;DialogResult=true;
            }
            catch(OperationCanceledException){_finished=true;DialogResult=false;}
            catch(Exception ex){Ui.Error(ex);_finished=true;DialogResult=false;}
            finally{_running=false;}
        });
        row.Children.Add(start);
        Button? pause=null;pause=Ui.Button("Пауза",()=>{_service.ManuallyPaused=!_service.ManuallyPaused;pause!.Content=_service.ManuallyPaused?"Продолжить":"Пауза";});row.Children.Add(pause);
        row.Children.Add(Ui.Button("Стоп",Stop));row.Children.Add(Ui.Button("Отмена",()=>{if(_running)_cancel.Cancel();else{_finished=true;DialogResult=false;}}));
        SourceInitialized+=(_,_)=>NativeMethods.ExcludeFromCapture(this);
        Loaded+=(_,_)=>
        {
            // Prefer the opposite monitor or an area outside the capture rectangle.
            var other=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>!s.Bounds.IntersectsWith(area));
            var work=(other??System.Windows.Forms.Screen.FromRectangle(area)).WorkingArea;
            int x=work.X+16,y=area.Top-250>=work.Top?area.Top-240:Math.Min(work.Bottom-240,area.Bottom+10);
            NativeMethods.PlacePixels(this,new DRect(x,Math.Max(work.Top,y),480,225));
        };
        Closing+=OnClosing;
        Closed+=(_,_)=>_cancel.Dispose();
    }
    public void Stop(){if(_running)_service.StopRequested=true;else{_finished=true;DialogResult=false;}}
    private void OnClosing(object? sender,CancelEventArgs e){if(_running&&!_finished){e.Cancel=true;_service.StopRequested=true;}}
}
