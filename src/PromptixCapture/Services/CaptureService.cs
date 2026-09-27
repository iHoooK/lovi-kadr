using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using PromptixCapture.Helpers;
using ScreenRecorderLib;
using Forms = System.Windows.Forms;

namespace PromptixCapture.Services;

public sealed class CaptureService
{
    public string LastBackend { get; private set; } = "DXGI";
    public static Rectangle Desktop => Forms.SystemInformation.VirtualScreen;
    public static Rectangle ActiveWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return Desktop;
        if (NativeMethods.DwmGetWindowAttribute(handle, 9, out var rect, Marshal.SizeOf<NativeMethods.RECT>()) != 0)
            NativeMethods.GetWindowRect(handle, out rect);
        var result = Rectangle.Intersect(rect.Rectangle, Desktop);
        return result.Width > 0 && result.Height > 0 ? result : Desktop;
    }
    public async Task<BitmapSource> CaptureAsync(Rectangle bounds, bool cursor, bool useGdi = false)
    {
        if (bounds.Width < 1 || bounds.Height < 1) throw new ArgumentException("Пустая область.");
        if (!useGdi)
        {
            try
            {
                var result = await Task.Run(() => CaptureNativeAsync(bounds, cursor));
                LastBackend = "DXGI Desktop Duplication";
                return result;
            }
            catch (Exception ex) { AppLog.Error("DXGI capture; trying GDI fallback", ex); }
        }
        LastBackend = "GDI fallback";
        return await Task.Run(() => CaptureGdi(bounds, cursor));
    }
    internal static SourceOptions Sources(Rectangle bounds, bool cursor)
    {
        var sources = new List<RecordingSourceBase>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var part = Rectangle.Intersect(bounds, screen.Bounds);
            if (part.Width < 1 || part.Height < 1) continue;
            sources.Add(new DisplayRecordingSource(screen.DeviceName)
            {
                RecorderApi = RecorderApi.DesktopDuplication,
                IsCursorCaptureEnabled = cursor,
                SourceRect = new ScreenRect(part.X - screen.Bounds.X, part.Y - screen.Bounds.Y, part.Width, part.Height),
                Position = new ScreenPoint(part.X - bounds.X, part.Y - bounds.Y),
                OutputSize = new ScreenSize(part.Width, part.Height)
            });
        }
        if (sources.Count == 0) throw new InvalidOperationException("Область находится вне подключённых мониторов.");
        return new SourceOptions { RecordingSources = sources };
    }
    private static async Task<BitmapSource> CaptureNativeAsync(Rectangle bounds, bool cursor)
    {
        var options = new RecorderOptions
        {
            SourceOptions = Sources(bounds, cursor),
            OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Screenshot, OutputFrameSize = new ScreenSize(bounds.Width, bounds.Height) },
            AudioOptions = new AudioOptions { IsAudioEnabled = false },
            MouseOptions = new MouseOptions { IsMousePointerEnabled = cursor },
            SnapshotOptions = new SnapshotOptions { SnapshotFormat = ScreenRecorderLib.ImageFormat.PNG },
            LogOptions = new LogOptions { IsLogEnabled = false }
        };
        using var stream = new MemoryStream();
        using var recorder = Recorder.CreateRecorder(options);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.OnRecordingComplete += (_, _) => done.TrySetResult(true);
        recorder.OnRecordingFailed += (_, e) => done.TrySetException(new InvalidOperationException(e.Error));
        recorder.Record(stream);
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
        catch { recorder.Stop(); throw; }
        stream.Position = 0;
        return BitmapTools.Load(stream);
    }
    private static BitmapSource CaptureGdi(Rectangle bounds, bool cursor)
    {
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var screenDc=NativeMethods.GetDC(IntPtr.Zero);
        if(screenDc==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var bitmapDc=graphics.GetHdc();
            try
            {
                // Graphics.CopyFromScreen rejects SRCCOPY | CAPTUREBLT because the
                // combined value is not a named CopyPixelOperation enum member.
                const uint sourceCopyWithLayeredWindows=0x40CC0020;
                if(!NativeMethods.BitBlt(bitmapDc,0,0,bounds.Width,bounds.Height,screenDc,bounds.X,bounds.Y,sourceCopyWithLayeredWindows))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally{graphics.ReleaseHdc(bitmapDc);}
        }
        finally{NativeMethods.ReleaseDC(IntPtr.Zero,screenDc);}
        if (cursor)
        {
            var ci = new NativeMethods.CURSORINFO { Size = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
            if (NativeMethods.GetCursorInfo(ref ci) && ci.Flags == 1 && NativeMethods.GetIconInfo(ci.Cursor, out var info))
            {
                var dc = graphics.GetHdc();
                try { NativeMethods.DrawIconEx(dc, ci.Position.X - bounds.X - (int)info.XHotspot, ci.Position.Y - bounds.Y - (int)info.YHotspot, ci.Cursor, 0, 0, 0, IntPtr.Zero, 3); }
                finally { graphics.ReleaseHdc(dc); if (info.Mask != IntPtr.Zero) NativeMethods.DeleteObject(info.Mask); if (info.Color != IntPtr.Zero) NativeMethods.DeleteObject(info.Color); }
            }
        }
        return BitmapTools.ToSource(bitmap);
    }
}
