using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PromptixCapture.Helpers;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; public System.Drawing.Rectangle Rectangle => System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct CURSORINFO { public int Size; public int Flags; public IntPtr Cursor; public POINT Position; }
    [StructLayout(LayoutKind.Sequential)] internal struct ICONINFO { [MarshalAs(UnmanagedType.Bool)] public bool Icon; public uint XHotspot, YHotspot; public IntPtr Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }

    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr handle, int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr handle, int attr, out RECT rect, int size);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll",EntryPoint="SetCursorPos")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] internal static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] internal static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll",SetLastError=true)] internal static extern bool BitBlt(IntPtr destination,int x,int y,int width,int height,IntPtr source,int sourceX,int sourceY,uint rasterOperation);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] input, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool SetWindowDisplayAffinity(IntPtr handle, uint affinity);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr handle, out uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static readonly uint ShowSettingsMessage = RegisterWindowMessage("LoviKadr.ShowSettings.1");
    internal static void BroadcastShowSettingsMessage() => PostMessage(new IntPtr(0xffff), ShowSettingsMessage, IntPtr.Zero, IntPtr.Zero);
    internal static System.Drawing.Point CursorPosition { get { GetCursorPos(out var p); return new(p.X, p.Y); } }
    internal static void SetCursorPosition(System.Drawing.Point point)
    {
        if(!SetCursorPos(point.X,point.Y))throw new InvalidOperationException("Windows не разрешила переместить курсор к прокручиваемой области.");
    }
    internal static void WheelDown(int delta)
    {
        var input = new INPUT { Type = 0, Mouse = new MOUSEINPUT { Data = unchecked((uint)-delta), Flags = 0x0800 } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) != 1) throw new InvalidOperationException("Windows не разрешила автопрокрутку. Приложения должны работать с одинаковыми правами.");
    }
    internal static void PlacePixels(Window window, System.Drawing.Rectangle bounds)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowPos(hwnd, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040);
    }
    internal static bool ExcludeFromCapture(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return SetWindowDisplayAffinity(hwnd, 0x11);
    }
    internal static bool IsExcludedFromCapture(Window window)
    {
        var hwnd=new WindowInteropHelper(window).Handle;
        return GetWindowDisplayAffinity(hwnd,out var affinity)&&affinity==0x11;
    }
    internal static bool MakeClickThrough(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        const int extendedStyle=-20;
        const long transparent=0x20, noActivate=0x08000000;
        var style=GetWindowLongPtr(hwnd,extendedStyle).ToInt64();
        SetWindowLongPtr(hwnd,extendedStyle,new IntPtr(style|transparent|noActivate));
        SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x0037); // FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER | NOACTIVATE
        return (GetWindowLongPtr(hwnd,extendedStyle).ToInt64()&(transparent|noActivate))==(transparent|noActivate);
    }
}
