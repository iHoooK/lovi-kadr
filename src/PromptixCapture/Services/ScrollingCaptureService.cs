using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PromptixCapture.Helpers;
using PromptixCapture.Models;
using DRect=System.Drawing.Rectangle;

namespace PromptixCapture.Services;

public sealed record ScrollProgress(string State,int Frames,int Height);
public sealed record ScrollResult(BitmapSource Image,string Reason);

public sealed class ScrollingCaptureService
{
    private readonly CaptureService _capture;
    public bool ManuallyPaused { get; set; }
    public bool StopRequested { get; set; }
    public ScrollingCaptureService(CaptureService capture)=>_capture=capture;
    public async Task<ScrollResult> RunAsync(DRect region,ScrollingSettings options,IProgress<ScrollProgress> progress,CancellationToken cancellation)
    {
        if(region.Width<64 || region.Height<128)throw new ArgumentException("Для прокрутки выделите область минимум 64 × 128 px.");
        var first=await _capture.CaptureAsync(region,false);
        var segments=new List<BitmapSource>{first};var previous=Gray(first);
        int total=first.PixelHeight,frames=1,unchanged=0;
        string reason="Остановлено пользователем";
        long maxPixels=60_000_000; // at most ~240 MB final raster; allow room for working copies
        int maxHeight=Math.Min(options.MaxOutputHeight,(int)(maxPixels/region.Width));
        if(region.Height>maxHeight)throw new InvalidOperationException("Слишком большая область для доступного лимита памяти.");
        while(!StopRequested)
        {
            cancellation.ThrowIfCancellationRequested();
            var cursor=NativeMethods.CursorPosition;
            if(ManuallyPaused || !region.Contains(cursor))
            {progress.Report(new("Пауза — курсор вне области или включена пауза",frames,total));await Task.Delay(100,cancellation);continue;}
            var point=new NativeMethods.POINT{X=cursor.X,Y=cursor.Y};
            var target=NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point),2);
            if(target!=NativeMethods.GetForegroundWindow())
            {progress.Report(new("Активируйте прокручиваемое окно щелчком внутри",frames,total));await Task.Delay(150,cancellation);continue;}
            progress.Report(new("Автопрокрутка",frames,total));
            NativeMethods.WheelDown(options.WheelDelta);
            await Task.Delay(options.SettleDelayMs,cancellation);
            if(!region.Contains(NativeMethods.CursorPosition)||ManuallyPaused)
            {
                // A scroll has already happened. Capture it before pausing, so content is not lost.
                progress.Report(new("Завершаю текущий кадр перед паузой",frames,total));
            }
            var image=await _capture.CaptureAsync(region,false);
            var pixels=Gray(image);
            var match=await Task.Run(()=>FrameMatcher.Match(previous,pixels,region.Width,region.Height),cancellation);
            if(match.Unchanged)
            {
                if(++unchanged>=options.UnchangedFramesToStop){reason="Достигнут конец или прокрутка не отвечает";break;}
                continue;
            }
            unchanged=0;
            if(!match.Reliable){reason="Склейка неоднозначна. Сохранена точная часть до проблемного кадра; попробуйте меньший шаг прокрутки.";break;}
            int take=Math.Min(match.Shift,maxHeight-total);
            if(take<=0){reason="Достигнут лимит высоты / памяти";break;}
            // Use the beginning of newly revealed content if the height limit cuts this strip.
            var strip=new CroppedBitmap(image,new Int32Rect(0,region.Height-match.Shift,region.Width,take));strip.Freeze();
            // Copy pixels so a narrow strip doesn't retain a complete frame's backing bitmap.
            var detached=new WriteableBitmap(strip);detached.Freeze();segments.Add(detached);
            total+=take;frames++;previous=pixels;
            progress.Report(new("Автопрокрутка",frames,total));
            if(total>=maxHeight){reason="Достигнут лимит высоты / памяти";break;}
        }
        cancellation.ThrowIfCancellationRequested();progress.Report(new("Склейка результата…",frames,total));
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {int y=0;foreach(var segment in segments){dc.DrawImage(segment,new Rect(0,y,region.Width,segment.PixelHeight));y+=segment.PixelHeight;}}
        var result=new RenderTargetBitmap(region.Width,total,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return new(result,reason);
    }
    private static byte[] Gray(BitmapSource image)
    {var gray=new FormatConvertedBitmap(image,PixelFormats.Gray8,null,0);var data=new byte[image.PixelWidth*image.PixelHeight];gray.CopyPixels(data,image.PixelWidth,0);return data;}
}
