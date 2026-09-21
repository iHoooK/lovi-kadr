using System.Windows;
using System.Windows.Media.Imaging;
using PromptixCapture.Models;

namespace PromptixCapture.Services;

public static class ImageExportService
{
    public static async Task CopyAsync(BitmapSource source)
    {
        Exception? error = null;
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetImage(source); return; }
            catch (System.Runtime.InteropServices.ExternalException ex) { error = ex; await Task.Delay(80); }
        }
        throw new InvalidOperationException("Буфер обмена занят. Повторите копирование.", error);
    }
    public static void Save(BitmapSource source, string path, int quality = 90)
    {
        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg"
            ? new JpegBitmapEncoder { QualityLevel = quality } : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = File.Create(temp)) encoder.Save(stream); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string DefaultPath(BitmapSource source, ScreenshotSettings settings) => FileNames.Unique(settings.Folder, settings.FileNameTemplate, settings.Format == ImageFileFormat.Png ? ".png" : ".jpg", source.PixelWidth, source.PixelHeight);
}
