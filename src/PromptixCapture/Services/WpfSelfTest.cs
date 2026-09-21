using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PromptixCapture.Editor;
using PromptixCapture.Models;

namespace PromptixCapture.Services;

// Synthetic images only: this mode never records the real desktop, microphone,
// clipboard, startup registry or user's history. Runs on Windows's STA dispatcher.
internal static class WpfSelfTest
{
    internal static int Run(string report)
    {
        var results=new List<string>();int failures=0;
        void Check(string name,Action action){try{action();results.Add("PASS "+name);}catch(Exception ex){failures++;results.Add("FAIL "+name+": "+ex);}}
        void Assert(bool v){if(!v)throw new InvalidOperationException("Assertion failed");}
        const int size=64;var pixels=Enumerable.Repeat((byte)255,size*size*4).ToArray();
        var image=BitmapSource.Create(size,size,96,96,PixelFormats.Bgra32,null,pixels,size*4);image.Freeze();
        Check("Document undo/redo",()=>
        {
            var d=new AnnotationDocument(image);d.Checkpoint();d.Items.Add(new AnnotationModel{Tool=AnnotationTool.Arrow,Start=new Point(4,4),End=new Point(40,40)});d.Notify();
            d.Undo();Assert(d.Items.Count==0);d.Redo();Assert(d.Items.Count==1);
            d.Undo();d.Checkpoint();d.Items.Add(new AnnotationModel{Tool=AnnotationTool.Text,Text="new"});d.Notify();d.Redo();Assert(d.Items[0].Tool==AnnotationTool.Text);
        });
        Check("All tools render",()=>
        {
            foreach(var tool in Enum.GetValues<AnnotationTool>().Where(t=>t!=AnnotationTool.Select))
            {
                var d=new AnnotationDocument(image);d.Items.Add(new AnnotationModel{Tool=tool,Start=new Point(5,5),End=new Point(45,45),Points=new(){new Point(5,5),new Point(40,40)},Text="тест",Number=1});
                var result=new AnnotationCanvas(d).Export();Assert(result.PixelWidth==size&&result.PixelHeight==size);
            }
        });
        Check("Redaction remains opaque after blur",()=>
        {
            var d=new AnnotationDocument(image);d.Items.Add(new AnnotationModel{Tool=AnnotationTool.Redact,Start=new Point(10,10),End=new Point(40,40),Color=Colors.Black});
            d.Items.Add(new AnnotationModel{Tool=AnnotationTool.Blur,Start=new Point(8,8),End=new Point(44,44)});
            var result=new AnnotationCanvas(d).Export();var buffer=new byte[size*size*4];result.CopyPixels(buffer,size*4,0);int at=(20*size+20)*4;
            Assert(buffer[at]==0&&buffer[at+1]==0&&buffer[at+2]==0&&buffer[at+3]==255);
        });
        Check("PNG and JPEG encode",()=>
        {
            var folder=Path.Combine(Path.GetTempPath(),"promptix-wpf-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try{foreach(var ext in new[]{".png",".jpg"}){var path=Path.Combine(folder,"synthetic"+ext);ImageExportService.Save(image,path);var decoded=Helpers.BitmapTools.Load(path);Assert(decoded.PixelWidth==size);File.Delete(path);}}
            finally{Directory.Delete(folder);}
        });
        Check("Hotkey parser",()=>{var h=HotkeyService.Parse("Ctrl+Shift+PrintScreen");Assert(h.Modifiers==6&&h.Key==0x2c);});
        results.Add($"RESULT: {results.Count-failures} passed, {failures} failed");
        File.WriteAllLines(report,results);return failures==0?0:1;
    }
}
