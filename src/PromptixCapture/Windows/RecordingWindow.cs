using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PromptixCapture.Helpers;
using PromptixCapture.Models;
using PromptixCapture.Services;
using DRect=System.Drawing.Rectangle;

namespace PromptixCapture.Windows;

public sealed class RecordingWindow : Window
{
    private VideoRecorderService? _recorder;
    private readonly CancellationTokenSource _countdown=new();
    private bool _finished;
    private bool _cancelAndDelete;
    private readonly TextBlock _status=Ui.Text("Подготовка",16);
    public event Action? Finished;
    public RecordingWindow(DRect region,VideoSettings settings,HistoryService history)
    {
        Title="ЛовиКадр — видео";Width=480;Height=180;ResizeMode=ResizeMode.NoResize;Topmost=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var panel=new StackPanel{Margin=new Thickness(14)};Content=panel;panel.Children.Add(_status);
        panel.Children.Add(Ui.Text($"{region.Width/2*2} × {region.Height/2*2} • {settings.FramesPerSecond} FPS • система: {(settings.CaptureSystemAudio?"да":"нет")} • микрофон: {(settings.CaptureMicrophone?"да":"нет")}",12,true));
        var row=new StackPanel{Orientation=Orientation.Horizontal};panel.Children.Add(row);
        Button? pause=null;pause=Ui.Button("Пауза / продолжить",TogglePause,"Shift+Pause");row.Children.Add(pause);
        row.Children.Add(Ui.Button("Стоп",Stop,"Ctrl+Shift+PrtScr"));
        row.Children.Add(Ui.Button("Отменить",()=>
        {
            if(MessageBox.Show(this,"Остановить и удалить только эту запись?","Отмена записи",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes){_cancelAndDelete=true;Stop();}
        }));
        SourceInitialized+=(_,_)=>NativeMethods.ExcludeFromCapture(this);
        Loaded+=async(_,_)=>
        {
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
            try
            {
                for(int i=settings.CountdownSeconds;i>0;i--){_status.Text=$"● Запись начнётся через {i}…";await Task.Delay(1000,_countdown.Token);}
                _countdown.Token.ThrowIfCancellationRequested();
                var path=FileNames.Unique(settings.Folder,settings.FileNameTemplate,".mp4",region.Width/2*2,region.Height/2*2);
                _recorder=new VideoRecorderService(Dispatcher);
                timer.Tick+=(_,_)=>_status.Text=$"{(_recorder.State==VideoRecorderState.Paused?"Ⅱ ПАУЗА":_recorder.State==VideoRecorderState.Stopping?"Завершение MP4…":"● REC")}   {_recorder.Elapsed:hh\\:mm\\:ss}";
                _recorder.Start(region,settings,path);timer.Start();
                string saved=await _recorder.Completion;
                if(_cancelAndDelete){if(File.Exists(saved))File.Delete(saved);}
                else
                {
                    history.Add(new HistoryItem{Path=saved,Type=HistoryMediaType.Video,Width=region.Width/2*2,Height=region.Height/2*2,DurationSeconds=_recorder.Elapsed.TotalSeconds});
                    if(settings.OpenFolderAfterRecording)Ui.Reveal(saved);
                }
            }
            catch(OperationCanceledException){ }
            catch(Exception ex){Ui.Error(ex);}
            finally{timer.Stop();_recorder?.Dispose();_finished=true;Close();Finished?.Invoke();}
        };
        Closing+=OnClosing;Closed+=(_,_)=>_countdown.Dispose();
    }
    public void TogglePause()=>_recorder?.TogglePause();
    public void Stop(){if(_recorder is null)_countdown.Cancel();else _recorder.Stop();}
    private void OnClosing(object? sender,CancelEventArgs e){if(!_finished){e.Cancel=true;Stop();}}
}
