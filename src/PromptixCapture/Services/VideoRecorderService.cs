using System.Diagnostics;
using System.Windows.Threading;
using PromptixCapture.Models;
using ScreenRecorderLib;
using DRect=System.Drawing.Rectangle;

namespace PromptixCapture.Services;

public sealed class VideoRecorderService : IDisposable
{
    private Recorder? _recorder;
    private DisplayRecordingSource? _source;
    private System.Drawing.Rectangle _monitor;
    private int _frameWidth,_frameHeight;
    private readonly Dispatcher _dispatcher;
    private readonly Stopwatch _elapsed=new();
    private readonly TaskCompletionSource<string> _done=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;
    public VideoRecorderState State { get; private set; }=VideoRecorderState.Idle;
    public TimeSpan Elapsed=>_elapsed.Elapsed;
    public Task<string> Completion=>_done.Task;
    public event Action? Changed;
    public VideoRecorderService(Dispatcher dispatcher)=>_dispatcher=dispatcher;
    private void SetState(VideoRecorderState state){State=state;Changed?.Invoke();}
    public void Start(DRect area,VideoSettings settings,string path)
    {
        if(State!=VideoRecorderState.Idle)throw new InvalidOperationException("Запись уже запущена.");
        area.Width-=area.Width%2;area.Height-=area.Height%2;
        if(area.Width<2||area.Height<2)throw new ArgumentException("Слишком маленькая область видео.");
        if(!System.Windows.Forms.Screen.AllScreens.Any(s=>s.Bounds.Contains(area)))throw new ArgumentException("Выделите видео внутри одного монитора.");
        int bitrate=(settings.Quality switch{VideoQuality.Economy=>3,VideoQuality.High=>10,_=>6})*1_000_000;
        if(settings.FramesPerSecond==60)bitrate=(int)(bitrate*1.6);
        var sources=new List<AudioSourceBase>();
        if(settings.CaptureSystemAudio)sources.Add(LoopbackAudioSource.Default);
        if(settings.CaptureMicrophone)
        {
            if(string.IsNullOrWhiteSpace(settings.MicrophoneDeviceName))sources.Add(CaptureAudioSource.Default);
            else sources.Add(Recorder.GetSystemAudioCaptureDevices().FirstOrDefault(x=>x.DeviceName==settings.MicrophoneDeviceName)??throw new InvalidOperationException("Выбранный микрофон отключён. Выберите устройство в настройках."));
        }
        var sourceOptions=CaptureService.Sources(area,settings.IncludeCursor);
        _source=sourceOptions.RecordingSources.OfType<DisplayRecordingSource>().Single();
        _source.AnchorPoint=Anchor.TopLeft;
        _source.Stretch=StretchMode.Fill;
        _frameWidth=area.Width;_frameHeight=area.Height;
        _monitor=System.Windows.Forms.Screen.AllScreens.Single(s=>s.Bounds.Contains(area)).Bounds;
        var options=new RecorderOptions
        {
            SourceOptions=sourceOptions,
            OutputOptions=new OutputOptions{RecorderMode=RecorderMode.Video,OutputFrameSize=new ScreenSize(_frameWidth,_frameHeight)},
            VideoEncoderOptions=new VideoEncoderOptions
            {
                Framerate=settings.FramesPerSecond,Bitrate=bitrate,IsFixedFramerate=true,IsHardwareEncodingEnabled=true,
                IsThrottlingDisabled=false,IsMp4FastStartEnabled=true,
                Encoder=new H264VideoEncoder{BitrateMode=H264BitrateControlMode.CBR,EncoderProfile=H264Profile.Main}
            },
            AudioOptions=new AudioOptions{IsAudioEnabled=sources.Count>0,AudioSources=sources,Channels=AudioChannels.Stereo,Bitrate=AudioBitrate.bitrate_128kbps},
            MouseOptions=new MouseOptions{IsMousePointerEnabled=settings.IncludeCursor,IsMouseClicksDetected=false},
            LogOptions=new LogOptions{IsLogEnabled=false}
        };
        SetState(VideoRecorderState.Starting);
        try
        {
            _recorder=Recorder.CreateRecorder(options);
            _recorder.OnStatusChanged+=(_,e)=>_dispatcher.BeginInvoke(new Action(()=>
            {
                if(_disposed||State==VideoRecorderState.Failed)return;
                if(State==VideoRecorderState.Stopping)
                {
                    // An early Stop can be ignored until native recording starts.
                    if(e.Status==RecorderStatus.Recording)TryStopRecorder();
                    return;
                }
                if(e.Status==RecorderStatus.Recording)
                {
                    _elapsed.Start();SetState(VideoRecorderState.Recording);
                }
                else if(e.Status==RecorderStatus.Paused){_elapsed.Stop();SetState(VideoRecorderState.Paused);}
            }));
            _recorder.OnRecordingComplete+=(_,e)=>_done.TrySetResult(e.FilePath);
            _recorder.OnRecordingFailed+=(_,e)=>_done.TrySetException(new InvalidOperationException("Не удалось записать видео: "+e.Error+"\nПроверьте VC++ Runtime x64, Media Foundation и аудиоустройства."));
            _recorder.Record(path);
        }
        catch{SetState(VideoRecorderState.Failed);throw;}
    }
    public void TogglePause()
    {
        if(State==VideoRecorderState.Recording){_recorder?.Pause();_elapsed.Stop();SetState(VideoRecorderState.Paused);}
        else if(State==VideoRecorderState.Paused){_recorder?.Resume();_elapsed.Start();SetState(VideoRecorderState.Recording);}
    }
    public bool UpdateRegion(DRect area)
    {
        if(_recorder is null || _source is null || State is VideoRecorderState.Stopping or VideoRecorderState.Failed)return false;
        if(!_monitor.Contains(area) || area.Width<2 || area.Height<2)return false;
        var width=area.Width-area.Width%2;var height=area.Height-area.Height%2;
        if(width<2||height<2)return false;
        // Even pixel dimensions can move the ratio by a few hundredths near
        // the 64 px minimum; larger changes would visibly stretch the video.
        if(Math.Abs((double)width/height-(double)_frameWidth/_frameHeight)>.04)return false;
        var previousRect=_source.SourceRect;
        var previousSize=_source.OutputSize;
        var previousPosition=_source.Position;
        _source.SourceRect=new ScreenRect(area.X-_monitor.X,area.Y-_monitor.Y,width,height);
        _source.OutputSize=new ScreenSize(_frameWidth,_frameHeight);
        _source.Position=new ScreenPoint(0,0);
        try
        {
            if(_recorder.GetDynamicOptionsBuilder().SetUpdatedRecordingSource(_source).Apply())return true;
        }
        catch
        {
            _source.SourceRect=previousRect;_source.OutputSize=previousSize;_source.Position=previousPosition;
            throw;
        }
        _source.SourceRect=previousRect;_source.OutputSize=previousSize;_source.Position=previousPosition;
        return false;
    }
    public void Stop()
    {
        if(State is VideoRecorderState.Idle or VideoRecorderState.Stopping or VideoRecorderState.Failed)return;
        SetState(VideoRecorderState.Stopping);_elapsed.Stop();TryStopRecorder();
        _=WatchStopAsync();
    }
    private async Task WatchStopAsync()
    {
        try{await _done.Task.WaitAsync(TimeSpan.FromSeconds(90));}
        catch(TimeoutException)
        {
            _done.TrySetException(new TimeoutException("Запись не завершилась за 90 секунд после остановки. Проверьте файл в папке видео."));
        }
        catch{ /* The completion task already contains the recording error. */ }
    }
    private void TryStopRecorder()
    {
        try{_recorder?.Stop();}
        catch(Exception ex){_done.TrySetException(ex);}
    }
    public static List<(string Id,string Name)> Microphones()=>Recorder.GetSystemAudioCaptureDevices().Select(d=>(d.DeviceName,d.FriendlyName)).ToList();
    public void Dispose(){if(_disposed)return;_disposed=true;_elapsed.Stop();_recorder?.Dispose();_recorder=null;}
}
