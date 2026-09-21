using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PromptixCapture.Helpers;

public static class BitmapTools
{
    public static BitmapSource ToSource(Bitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();
        try { var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze(); return source; }
        finally { NativeMethods.DeleteObject(handle); }
    }
    public static BitmapSource Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }
    public static BitmapSource Load(Stream stream)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    public static BitmapSource Crop(BitmapSource image, Rectangle rect)
    {
        var source = new CroppedBitmap(image, new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height)); source.Freeze(); return source;
    }
}
