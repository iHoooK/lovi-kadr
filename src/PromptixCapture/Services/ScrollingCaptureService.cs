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
    public bool ManuallyPaused { get; set; }
    public bool StopRequested { get; set; }
    public ScrollingCaptureService(CaptureService capture)=>_capture=capture;
    public async Task<ScrollResult> RunAsync(DRect region,ScrollingSettings options,IProgress<ScrollProgress> progress,CancellationToken cancellation)
    {
        if(region.Width<64 || region.Height<128)throw new ArgumentException("Для прокрутки выделите область минимум 64 × 128 px.");
        while(!StopRequested && !region.Contains(NativeMethods.CursorPosition))
        {
            cancellation.ThrowIfCancellationRequested();
            progress.Report(new("Наведите курсор внутрь рамки",0,0));
            await Task.Delay(100,cancellation);
        }
        bool useGdi=false;
        async Task<BitmapSource> CaptureFrameAsync()
        {
            var frame=await CaptureExactAsync(region,useGdi,backend=>_capture.CaptureAsync(region,false,backend));
            useGdi=frame.UseGdi;
            return frame.Image;
        }
        var first=await CaptureFrameAsync();
        var segments=new List<BitmapSource>{first};var previous=Gray(first);
        var viewport=new DRect(0,0,region.Width,region.Height);
        var originalCursor=NativeMethods.CursorPosition;
        var targets=ScrollTargets(region,originalCursor);
        int targetIndex=0,remainingWheel=0;
        bool foundTarget=false,autoMoved=false;
        int total=first.PixelHeight,frames=1,unchanged=0;
        string reason="Остановлено пользователем";
        long maxPixels=60_000_000; // at most ~240 MB final raster; allow room for working copies
        int maxHeight=Math.Min(options.MaxOutputHeight,(int)(maxPixels/viewport.Width));
        if(viewport.Height>maxHeight)throw new InvalidOperationException("Слишком большая область для доступного лимита памяти.");
        try
        {
            while(!StopRequested)
            {
                cancellation.ThrowIfCancellationRequested();
                var cursor=NativeMethods.CursorPosition;
                if(ManuallyPaused || !region.Contains(cursor))
                {progress.Report(new("Пауза — курсор вне области или включена пауза",frames,total));await Task.Delay(100,cancellation);continue;}
                var desired=targets[targetIndex];
                if((targetIndex>0 || foundTarget) && cursor!=desired)
                {
                    NativeMethods.SetCursorPosition(desired);autoMoved=true;
                    await Task.Delay(70,cancellation);
                    cursor=NativeMethods.CursorPosition;
                }
                var point=new NativeMethods.POINT{X=cursor.X,Y=cursor.Y};
                var target=NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point),2);
                if(target==IntPtr.Zero){await Task.Delay(100,cancellation);continue;}
                NativeMethods.GetWindowThreadProcessId(target,out var processId);
                if(processId==Environment.ProcessId)
                {
                    if(!foundTarget)
                    {
                        if(++targetIndex>=targets.Count){reason="В выбранной области не найдено доступное для прокрутки окно";break;}
                        continue;
                    }
                    progress.Report(new("Наведите курсор на содержимое внутри рамки",frames,total));await Task.Delay(100,cancellation);continue;
                }
                if(target!=NativeMethods.GetForegroundWindow())NativeMethods.SetForegroundWindow(target);
                int wheel=Math.Min(120,foundTarget && remainingWheel>0?remainingWheel:options.WheelDelta);
                progress.Report(new(foundTarget?"Плавная прокрутка":"Поиск прокручиваемой области",frames,total));
                await WheelSmoothAsync(wheel,cancellation);
                await Task.Delay(options.SettleDelayMs,cancellation);
                if(!region.Contains(NativeMethods.CursorPosition)||ManuallyPaused)
                    progress.Report(new("Завершаю текущий кадр перед паузой",frames,total));
                var image=await CaptureFrameAsync();
                var pixels=Gray(image);
                ScrollFrameMatch result;
                if(foundTarget)
                {
                    var before=ScrollingFrameMatcher.Crop(previous,region.Width,viewport);
                    var after=ScrollingFrameMatcher.Crop(pixels,region.Width,viewport);
                    result=new(FrameMatcher.Match(before,after,viewport.Width,viewport.Height),viewport,true);
                }
                else result=ScrollingFrameMatcher.Match(previous,pixels,region.Width,region.Height,cursor.X-region.X);
                var match=result.Match;
                if(!foundTarget && !match.Reliable)
                {
                    if(result.SignificantMotion && !match.Unchanged)
                    {reason="Не удалось точно совместить прокручиваемые кадры. Попробуйте выделить саму прокручиваемую область или уменьшить шаг колеса.";break;}
                    if(++targetIndex>=targets.Count){reason="В выбранной области не найдена прокручиваемая область";break;}
                    continue;
                }
                if(match.Unchanged)
                {
                    remainingWheel=0;
                    if(++unchanged>=options.UnchangedFramesToStop){reason="Достигнут конец или прокрутка не отвечает";break;}
                    continue;
                }
                unchanged=0;
                if(!match.Reliable){reason="Склейка неоднозначна. Сохранена точная часть до проблемного кадра; попробуйте меньший шаг прокрутки.";break;}
                if(!foundTarget)
                {
                    foundTarget=true;targets[targetIndex]=cursor;
                    viewport=result.Viewport;
                    maxHeight=Math.Min(options.MaxOutputHeight,(int)(maxPixels/viewport.Width));
                    if(viewport.Height>maxHeight)throw new InvalidOperationException("Слишком большая область для доступного лимита памяти.");
                    if(viewport.Width!=region.Width || viewport.Height!=region.Height)
                    {
                        var start=new CroppedBitmap(first,new Int32Rect(viewport.X,viewport.Y,viewport.Width,viewport.Height));
                        start.Freeze();segments.Clear();segments.Add(start);total=viewport.Height;
                    }
                    remainingWheel=Math.Max(0,options.WheelDelta-wheel);
                    progress.Report(new(viewport.Width==region.Width?"Плавная прокрутка":"Найден прокручиваемый участок",frames,total));
                }
                else remainingWheel=Math.Max(0,(remainingWheel>0?remainingWheel:options.WheelDelta)-wheel);
                int take=Math.Min(match.Shift,maxHeight-total);
                if(take<=0){reason="Достигнут лимит высоты / памяти";break;}
                var strip=new CroppedBitmap(image,new Int32Rect(viewport.X,viewport.Bottom-match.Shift,viewport.Width,take));strip.Freeze();
                var detached=new WriteableBitmap(strip);detached.Freeze();segments.Add(detached);
                total+=take;frames++;previous=pixels;
                progress.Report(new("Плавная прокрутка",frames,total));
                if(total>=maxHeight){reason="Достигнут лимит высоты / памяти";break;}
            }
        }
        finally
        {
            if(autoMoved && region.Contains(NativeMethods.CursorPosition))
            {
                var cursor=NativeMethods.CursorPosition;
                var target=targets[Math.Min(targetIndex,targets.Count-1)];
                if(Math.Abs(cursor.X-target.X)<=2 && Math.Abs(cursor.Y-target.Y)<=2)
                    try{NativeMethods.SetCursorPosition(originalCursor);}
                    catch(Exception ex){AppLog.Error("Restore scroll cursor",ex);}
            }
        }
        if(!foundTarget && !StopRequested)throw new InvalidOperationException(reason);
        cancellation.ThrowIfCancellationRequested();progress.Report(new("Склейка результата…",frames,total));
        return new(ComposeLossless(segments,viewport.Width,total),reason,viewport);
    }
    internal static List<System.Drawing.Point> ScrollTargets(DRect region,System.Drawing.Point initial)
    {
        var targets=new List<System.Drawing.Point>{initial};
        var candidates=from double y in new[]{.1,.3,.5,.7,.9}
                       from double x in new[]{.1,.3,.5,.7,.9}
                       select new System.Drawing.Point(region.Left+(int)(region.Width*x),region.Top+(int)(region.Height*y));
        foreach(var candidate in candidates.OrderBy(point=>
        {
            long dx=point.X-initial.X,dy=point.Y-initial.Y;
            return dx*dx+dy*dy;
        }))
        {
            if(targets.Any(point=>Math.Abs(point.X-candidate.X)<12 && Math.Abs(point.Y-candidate.Y)<12))continue;
            targets.Add(candidate);
        }
        return targets;
    }
    private async Task WheelSmoothAsync(int amount,CancellationToken cancellation)
    {
        const int pulses=8;
        for(int i=0;i<pulses;i++)
        {
            cancellation.ThrowIfCancellationRequested();
            if(ManuallyPaused)break;
            NativeMethods.WheelDown(amount/pulses+(i<amount%pulses?1:0));
            await Task.Delay(22,cancellation);
        }
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
