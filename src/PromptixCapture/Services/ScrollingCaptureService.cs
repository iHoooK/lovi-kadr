using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PromptixCapture.Helpers;
using PromptixCapture.Models;
using DRect=System.Drawing.Rectangle;

namespace PromptixCapture.Services;

public sealed record ScrollProgress(string State,int Frames,int Height);
public sealed record ScrollResult(BitmapSource Image,string Reason,DRect SourceCrop);

public sealed class ScrollingCaptureService
{
    private readonly CaptureService _capture;
    public bool StopRequested { get; set; }
    public ScrollingCaptureService(CaptureService capture)=>_capture=capture;
    public async Task<ScrollResult> RunAsync(DRect region,ScrollingSettings options,IProgress<ScrollProgress> progress,CancellationToken cancellation,DRect controlBounds)
    {
        if(region.Width<64 || region.Height<128)
            throw new ArgumentException("Для прокрутки выделите область минимум 64 × 128 px.");
        cancellation.ThrowIfCancellationRequested();

        bool useGdi=false;
        async Task<BitmapSource> CaptureFrameAsync()
        {
            var frame=await CaptureExactAsync(region,useGdi,backend=>_capture.CaptureAsync(region,false,backend));
            useGdi=frame.UseGdi;
            return frame.Image;
        }

        var first=await CaptureFrameAsync();
        var segments=new List<BitmapSource>{first};
        var previous=Gray(first);
        int total=first.PixelHeight,frames=1;
        int maxHeight=Math.Min(options.MaxOutputHeight,(int)(60_000_000L/region.Width));
        if(region.Height>maxHeight)
            throw new InvalidOperationException("Слишком большая область для доступного лимита памяти.");

        var scrollPoint=new System.Drawing.Point(region.Left+region.Width/2,region.Top+region.Height/2);
        bool wheelErrorLogged=false;
        string reason="Остановлено пользователем";
        int wheelDelta=Math.Min(options.WheelDelta,Math.Max(120,region.Height/4));
        if(!StopRequested)
        {
            var target=NativeMethods.ExternalWindowAt(scrollPoint);
            if(target!=IntPtr.Zero)
                NativeMethods.SetForegroundWindow(target);
            await Task.Delay(80,cancellation);
            var ready=await CaptureFrameAsync();
            segments[0]=ready;
            previous=Gray(ready);
        }

        while(!StopRequested)
        {
            cancellation.ThrowIfCancellationRequested();
            var cursor=NativeMethods.CursorPosition;
            if(!region.Contains(cursor) || controlBounds.Contains(cursor))
            {
                progress.Report(new("Пауза — курсор вне области",frames,total));
                await Task.Delay(100,cancellation);
                continue;
            }
            try
            {
                NativeMethods.WheelDown(wheelDelta);
                if(wheelErrorLogged)reason="Остановлено пользователем";
                wheelErrorLogged=false;
            }
            catch(Exception ex)
            {
                reason="Автопрокрутка недоступна; сохранён текущий результат";
                if(!wheelErrorLogged)AppLog.Error("Scrolling wheel",ex);
                wheelErrorLogged=true;
                await Task.Delay(500,cancellation);
                continue;
            }
            progress.Report(new("Прокрутка страницы",frames,total));
            await Task.Delay(options.SettleDelayMs,cancellation);
            if(total>=maxHeight)continue;

            var image=await CaptureFrameAsync();
            var pixels=await Task.Run(()=>Gray(image),cancellation);
            var match=await Task.Run(
                ()=>FrameMatcher.Match(previous,pixels,region.Width,region.Height),cancellation);
            if(match.Unchanged)
            {
                // A page may briefly stop moving while content loads. Keep
                // scrolling until the user moves the cursor away or presses Stop.
                continue;
            }
            if(!match.Reliable)
            {
                // Do not let an animated or ambiguous frame halt wheel input.
                // Resume matching from the latest visible frame on the next step.
                previous=pixels;
                continue;
            }

            int take=Math.Min(match.Shift,maxHeight-total);
            if(take<=0)
            {
                reason="Достигнут лимит высоты / памяти";
                continue;
            }
            var strip=new CroppedBitmap(image,new Int32Rect(0,region.Height-match.Shift,region.Width,take));
            strip.Freeze();
            var detached=new WriteableBitmap(strip);detached.Freeze();
            segments.Add(detached);
            total+=take;frames++;previous=pixels;
            progress.Report(new("Прокрутка страницы",frames,total));
            if(total>=maxHeight)
            {
                reason="Достигнут лимит высоты / памяти";
            }
        }

        cancellation.ThrowIfCancellationRequested();
        progress.Report(new("Склейка результата…",frames,total));
        return new(ComposeLossless(segments,region.Width,total),reason,
            new DRect(0,0,region.Width,region.Height));
    }
    internal static async Task<(BitmapSource Image,bool UseGdi)> CaptureExactAsync(
        DRect region,bool useGdi,Func<bool,Task<BitmapSource>> capture)
    {
        var image=await capture(useGdi);
        if(!useGdi && (image.PixelWidth!=region.Width || image.PixelHeight!=region.Height))
        {
            // Some capture backends round the screenshot dimensions. The matcher
            // and stitched strips must use the same physical size as the region.
            useGdi=true;
            image=await capture(true);
        }
        if(image.PixelWidth!=region.Width || image.PixelHeight!=region.Height)
            throw new InvalidOperationException($"Захват вернул кадр {image.PixelWidth} × {image.PixelHeight} вместо {region.Width} × {region.Height} px.");
        return (image,useGdi);
    }
    internal static BitmapSource ComposeLossless(IReadOnlyList<BitmapSource> segments,int width,int height)
    {
        var result=new WriteableBitmap(width,height,96,96,PixelFormats.Pbgra32,null);
        var stride=checked(width*4);
        int y=0;
        foreach(var segment in segments)
        {
            if(segment.PixelWidth!=width || segment.PixelHeight>height-y)
                throw new ArgumentException("Размер сегмента не соответствует итоговому снимку.");
            BitmapSource source=segment.Format==PixelFormats.Pbgra32
                ? segment : new FormatConvertedBitmap(segment,PixelFormats.Pbgra32,null,0);
            var pixels=new byte[checked(stride*segment.PixelHeight)];
            source.CopyPixels(pixels,stride,0);
            result.WritePixels(new Int32Rect(0,y,width,segment.PixelHeight),pixels,stride,0);
            y+=segment.PixelHeight;
        }
        if(y!=height)throw new ArgumentException("Высота сегментов не соответствует итоговому снимку.");
        result.Freeze();return result;
    }
    private static byte[] Gray(BitmapSource image)
    {var gray=new FormatConvertedBitmap(image,PixelFormats.Gray8,null,0);var data=new byte[image.PixelWidth*image.PixelHeight];gray.CopyPixels(data,image.PixelWidth,0);return data;}
}

