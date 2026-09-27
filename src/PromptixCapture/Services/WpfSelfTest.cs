using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Documents;
using PromptixCapture.Editor;
using PromptixCapture.Models;
using PromptixCapture.Windows;

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
        Check("Themes update existing controls",()=>
        {
            Ui.ApplyTheme("Light");
            var window=new Window();Ui.ThemeWindow(window);
            var label=Ui.Text("Проверка");window.Content=label;
            Assert(((SolidColorBrush)window.Background).Color==Color.FromRgb(245,247,250));
            Assert(((SolidColorBrush)label.Foreground).Color==Color.FromRgb(24,34,48));
            Ui.ApplyTheme("Dark");
            Assert(((SolidColorBrush)window.Background).Color==Color.FromRgb(13,17,23));
            Assert(((SolidColorBrush)label.Foreground).Color==Color.FromRgb(244,247,251));
            Assert(((SolidColorBrush)System.Windows.Application.Current.Resources["ButtonTextBrush"]).Color==Colors.White);
            var appsUseLight=Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1);
            Ui.ApplyTheme("System");
            var expected=appsUseLight is int value && value==0?Color.FromRgb(13,17,23):Color.FromRgb(245,247,250);
            Assert(((SolidColorBrush)window.Background).Color==expected);
            window.Close();Ui.ApplyTheme("Light");
        });
        Check("Scrolling stitch preserves source pixels",()=>
        {
            const int width=13,topHeight=7,bottomHeight=5,stride=width*4;
            static byte[] Pattern(int width,int height,int seed)
            {
                var bytes=new byte[width*height*4];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int at=(y*width+x)*4;
                    bytes[at]=(byte)((x+y+seed)%2==0?0:255);
                    bytes[at+1]=(byte)((x*31+y*17+seed)%256);
                    bytes[at+2]=(byte)((x*13+y*43+seed)%256);
                    bytes[at+3]=255;
                }
                return bytes;
            }
            var top=Pattern(width,topHeight,0);var bottom=Pattern(width,bottomHeight,1);
            var first=BitmapSource.Create(width,topHeight,96,96,PixelFormats.Pbgra32,null,top,stride);
            var second=BitmapSource.Create(width,bottomHeight,96,96,PixelFormats.Pbgra32,null,bottom,stride);
            var stitched=ScrollingCaptureService.ComposeLossless(new[]{first,second},width,topHeight+bottomHeight);
            var actual=new byte[(topHeight+bottomHeight)*stride];stitched.CopyPixels(actual,stride,0);
            Assert(actual.SequenceEqual(top.Concat(bottom)));
        });
        Check("Scrolling capture retries a rounded frame at exact region size",()=>
        {
            const int width=101,height=129;
            static BitmapSource Frame(int width,int height)
            {
                var pixels=new byte[width*height*4];
                var frame=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
                frame.Freeze();return frame;
            }
            var native=Frame(width-1,height-1);
            var gdi=Frame(width,height);
            var calls=new List<bool>();
            Task<BitmapSource> Capture(bool useGdi)
            {calls.Add(useGdi);return Task.FromResult(useGdi?gdi:native);}
            var region=new System.Drawing.Rectangle(0,0,width,height);
            var first=ScrollingCaptureService.CaptureExactAsync(region,false,Capture).GetAwaiter().GetResult();
            Assert(ReferenceEquals(first.Image,gdi)&&first.UseGdi);
            var second=ScrollingCaptureService.CaptureExactAsync(region,first.UseGdi,Capture).GetAwaiter().GetResult();
            Assert(ReferenceEquals(second.Image,gdi)&&calls.SequenceEqual(new[]{false,true,true}));
            var gray=new FormatConvertedBitmap(second.Image,PixelFormats.Gray8,null,0);
            var data=new byte[width*height];gray.CopyPixels(data,width,0);
            Assert(FrameMatcher.Match(data,data,width,height).Unchanged);
        });
        Check("Scroll target search covers the full selected area",()=>
        {
            var region=new System.Drawing.Rectangle(-200,100,1000,600);
            var initial=new System.Drawing.Point(300,400);
            var targets=ScrollingCaptureService.ScrollTargets(region,initial);
            Assert(targets[0]==initial&&targets.All(region.Contains));
            Assert(targets.Any(point=>point.X<region.Left+region.Width/5&&point.Y<region.Top+region.Height/5));
            Assert(targets.Any(point=>point.X>region.Right-region.Width/5&&point.Y>region.Bottom-region.Height/5));
        });
        Check("Theme text contrast",()=>
        {
            static double Luminance(Color c)
            {
                static double Channel(byte value){double v=value/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}
                return .2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B);
            }
            static double Contrast(Color a,Color b)
            {
                double x=Luminance(a),y=Luminance(b);
                return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
            }
            Color Brush(string key)=>((SolidColorBrush)System.Windows.Application.Current.Resources[key]).Color;
            foreach(var theme in new[]{"Light","Dark"})
            {
                Ui.ApplyTheme(theme);
                Assert(Contrast(Brush("TextPrimaryBrush"),Brush("SurfaceBrush"))>=4.5);
                Assert(Contrast(Brush("TextSecondaryBrush"),Brush("SurfaceBrush"))>=4.5);
                Assert(Contrast(Brush("ButtonTextBrush"),Brush("ButtonBrush"))>=4.5);
                Assert(Contrast(Brush("ButtonTextBrush"),Brush("ButtonHoverBrush"))>=4.5);
                Assert(Contrast(Brush("OnAccentBrush"),Brush("AccentBrush"))>=4.5);
            }
            Ui.ApplyTheme("Light");
        });
        Check("Transparent capture overlay can be excluded",()=>
        {
            var overlay=new Window{WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,
                Width=12,Height=12,ShowInTaskbar=false,ShowActivated=false};
            try{overlay.Show();Assert(Helpers.NativeMethods.ExcludeFromCapture(overlay));}
            finally{overlay.Close();}
        });
        Check("Video controls are excluded while annotations remain capturable",()=>
        {
            var before=System.Windows.Application.Current.Windows.Cast<Window>().ToHashSet();
            var picker=new RegionSelectionWindow(null,new System.Drawing.Rectangle(0,0,size,size),CapturePurpose.Video,
                video:new VideoSettings(),previous:new System.Drawing.Rectangle(8,9,30,22));
            try
            {
                picker.Show();
                var created=System.Windows.Application.Current.Windows.Cast<Window>().Where(window=>!before.Contains(window)).ToArray();
                Assert(created.Length==2&&created.Contains(picker));
                Assert(Helpers.NativeMethods.IsExcludedFromCapture(picker));
                Assert(!Helpers.NativeMethods.IsExcludedFromCapture(created.Single(window=>window!=picker)));
            }
            finally{picker.Close();}
        });
        Check("Capture modes keep their actions",()=>
        {
            Assert(RegionSelectionWindow.SupportsAction(CapturePurpose.Screenshot,SelectionAction.Editor));
            Assert(!RegionSelectionWindow.SupportsAction(CapturePurpose.Screenshot,SelectionAction.Confirm));
            Assert(!RegionSelectionWindow.SupportsAction(CapturePurpose.Video,SelectionAction.Copy));
            Assert(!RegionSelectionWindow.SupportsAction(CapturePurpose.ScrollingScreenshot,SelectionAction.Save));
            Assert(!RegionSelectionWindow.SupportsAction(CapturePurpose.Video,SelectionAction.Confirm));
        });
        Check("Selection export excludes controls",()=>
        {
            var picker=new RegionSelectionWindow(image,new System.Drawing.Rectangle(0,0,size,size),CapturePurpose.Screenshot,previous:new System.Drawing.Rectangle(8,9,30,22));
            var document=picker.ExportDocument();var result=picker.ExportSelection();
            Assert(document.BaseImage.PixelWidth==30&&document.BaseImage.PixelHeight==22&&document.Items.Count==0);
            Assert(result.PixelWidth==30&&result.PixelHeight==22);
            var buffer=new byte[30*22*4];result.CopyPixels(buffer,30*4,0);
            Assert(buffer.All(value=>value==255));
            picker.Close();
        });
        Check("Selection actions are icons with shortcut tips",()=>
        {
            var picker=new RegionSelectionWindow(image,new System.Drawing.Rectangle(0,0,size,size),CapturePurpose.Screenshot,previous:new System.Drawing.Rectangle(8,9,30,22));
            var overlay=(Canvas)((Grid)picker.Content).Children[1];
            var tips=overlay.Children.OfType<Border>().SelectMany(x=>(x.Child as WrapPanel)?.Children.OfType<Button>()??Enumerable.Empty<Button>()).Select(x=>x.ToolTip?.ToString()).ToArray();
            Assert(tips.Contains("Копировать · Ctrl+C"));
            Assert(tips.Contains("Сохранить · Ctrl+S"));
            Assert(tips.Contains("Полный редактор · Ctrl+E"));
            Assert(tips.Contains("Отмена · Esc"));
            Assert(!tips.Any(x=>x?.Contains("По настройкам")==true));
            picker.Close();
        });
        Check("Video selection keeps controls and drawing tools",()=>
        {
            var picker=new RegionSelectionWindow(null,new System.Drawing.Rectangle(0,0,size,size),CapturePurpose.Video,previous:new System.Drawing.Rectangle(8,9,30,22));
            var overlay=(Canvas)((Grid)picker.Content).Children[1];
            var actions=overlay.Children.OfType<Border>().SelectMany(x=>(x.Child as WrapPanel)?.Children.OfType<Button>()??Enumerable.Empty<Button>()).ToArray();
            Assert(actions.Length==3);
            Assert(actions.All(x=>x.Content is System.Windows.Shapes.Path));
            Assert(actions.Select(x=>x.ToolTip?.ToString()).SequenceEqual(new[]{"Начать запись · Enter","Остановить и сохранить · Ctrl+Shift+PrintScreen","Отмена · Esc"}));
            Assert(!actions[1].IsEnabled);
            var tools=overlay.Children.OfType<Border>().Select(x=>x.Child).OfType<StackPanel>().First(x=>x.Children.OfType<Button>().Count()>5);
            Assert(((TextBlock)((Button)tools.Children[2]).Content).Text=="●");
            var screenshot=new RegionSelectionWindow(image,new System.Drawing.Rectangle(0,0,size,size),CapturePurpose.Screenshot,previous:new System.Drawing.Rectangle(8,9,30,22));
            var screenshotOverlay=(Canvas)((Grid)screenshot.Content).Children[1];
            var screenshotTools=screenshotOverlay.Children.OfType<Border>().Select(x=>x.Child).OfType<StackPanel>().First(x=>x.Children.OfType<Button>().Count()>5);
            Assert(tools.Children.OfType<Button>().Select(x=>x.ToolTip?.ToString()).SequenceEqual(screenshotTools.Children.OfType<Button>().Select(x=>x.ToolTip?.ToString())));
            screenshot.Close();
            picker.Close();
        });
        Check("Video frame keeps a live clear center and dark surroundings",()=>
        {
            var picker=new RegionSelectionWindow(null,new System.Drawing.Rectangle(0,0,100,100),CapturePurpose.Video,previous:new System.Drawing.Rectangle(30,30,40,40));
            var surface=(FrameworkElement)((Grid)picker.Content).Children[0];
            surface.Measure(new Size(100,100));surface.Arrange(new Rect(0,0,100,100));
            static byte Alpha(FrameworkElement visual,int x,int y)
            {
                var target=new RenderTargetBitmap(100,100,96,96,PixelFormats.Pbgra32);target.Render(visual);
                var bytes=new byte[100*100*4];target.CopyPixels(bytes,100*4,0);
                return bytes[(y*100+x)*4+3];
            }
            Assert(Alpha(surface,90,90)>100&&Alpha(surface,50,50)<5);
            surface.GetType().GetMethod("BeginVideoRecording")!.Invoke(surface,new object[]{new System.Drawing.Rectangle(0,0,100,100)});
            Assert(Alpha(surface,90,90)>100&&Alpha(surface,50,50)<5);
            var wantsPointer=surface.GetType().GetMethod("WantsPointer")!;
            Assert((bool)wantsPointer.Invoke(surface,new object[]{new Point(30,45)})!);
            Assert(!(bool)wantsPointer.Invoke(surface,new object[]{new Point(50,50)})!);
            surface.GetType().GetMethod("Move")!.Invoke(surface,new object[]{5,5});
            var moved=(System.Drawing.Rectangle)surface.GetType().GetMethod("PixelRectangle")!.Invoke(surface,null)!;
            Assert(moved.X==35&&moved.Y==35&&moved.Width==40&&moved.Height==40);
            picker.Close();
        });
        Check("Recent screenshots exclude video and limit results",()=>
        {
            var items=Enumerable.Range(0,25).Select(i=>new HistoryItem{Type=i==1?HistoryMediaType.Video:i==2?HistoryMediaType.ScrollingScreenshot:HistoryMediaType.Screenshot,CreatedAtUtc=DateTime.UtcNow.AddMinutes(-i)}).ToList();
            var recent=HistoryWindow.RecentScreenshots(items).ToList();
            Assert(recent.Count==20&&recent.All(x=>x.Type!=HistoryMediaType.Video));
            Assert(recent.Any(x=>x.Type==HistoryMediaType.ScrollingScreenshot));
        });
        Check("Quick panel has three icon actions",()=>
        {
            var row=AppController.CreateQuickPanelActions(()=>{},()=>{},()=>{});
            var buttons=row.Children.OfType<Button>().ToList();
            Assert(buttons.Count==3);
            Assert(buttons.All(x=>x.Content is System.Windows.Shapes.Path));
            Assert(buttons.Select(x=>x.ToolTip?.ToString()).SequenceEqual(new[]{"Снимок области","Видео","Длинный снимок"}));
        });
        Check("Quick panel opens above the clicked tray icon",()=>
        {
            var work=new System.Drawing.Rectangle(0,0,1920,1040);
            var nearRight=AppController.QuickPanelBounds(new System.Drawing.Point(1860,1060),work,new System.Drawing.Size(164,60));
            Assert(nearRight.Bottom<work.Bottom&&nearRight.Left<=1860&&nearRight.Right>=1860);
            var nearLeft=AppController.QuickPanelBounds(new System.Drawing.Point(20,1060),work,new System.Drawing.Size(164,60));
            Assert(nearLeft.Left==work.Left&&nearLeft.Bottom<work.Bottom);
        });
        Check("Mini editor annotations stay editable",()=>
        {
            var document=new AnnotationDocument(image);
            document.Items.Add(new AnnotationModel{Tool=AnnotationTool.Arrow,Start=new Point(2,2),End=new Point(30,30)});
            var editor=new EditorWindow(document,new ScreenshotSettings(),new HistoryService());
            Assert(ReferenceEquals(editor.Document,document)&&editor.Document.Items.Count==1);
        });
        Check("Editor fit handles large and long images",()=>
        {
            Assert(EditorWindow.FitZoom(1000,700,619,391)==1);
            var longZoom=EditorWindow.FitZoom(1000,700,600,6000);
            Assert(longZoom>0&&longZoom<.12);
            var wideZoom=EditorWindow.FitZoom(1000,700,6000,600);
            Assert(wideZoom>0&&wideZoom<.17);
        });
        Check("Selection controls stay on screen at an edge",()=>
        {
            foreach(var (desktop,selected) in new[]
            {
                (new System.Drawing.Rectangle(0,0,800,600),new System.Drawing.Rectangle(2,2,120,80)),
                (new System.Drawing.Rectangle(0,0,800,600),new System.Drawing.Rectangle(680,2,118,80)),
                (new System.Drawing.Rectangle(0,0,800,600),new System.Drawing.Rectangle(2,500,120,98)),
                (new System.Drawing.Rectangle(0,0,800,600),new System.Drawing.Rectangle(680,500,118,98)),
                (new System.Drawing.Rectangle(0,0,1600,1200),new System.Drawing.Rectangle(1300,950,280,230))
            })
            {
                var picker=new RegionSelectionWindow(image,desktop,CapturePurpose.Screenshot,previous:selected);
                var root=(Grid)picker.Content;root.Measure(new Size(800,600));root.Arrange(new Rect(0,0,800,600));root.UpdateLayout();
                var overlay=(Canvas)root.Children[1];
                foreach(var child in overlay.Children.OfType<FrameworkElement>().Where(x=>x.Visibility==Visibility.Visible))
                {
                    var x=Canvas.GetLeft(child);var y=Canvas.GetTop(child);
                    Assert(!double.IsNaN(x)&&!double.IsNaN(y));
                    Assert(x>=0&&y>=0&&x+child.DesiredSize.Width<=801&&y+child.DesiredSize.Height<=601);
                }
                picker.Close();
            }
        });
        Check("About pages contain expected content",()=>
        {
            IEnumerable<DependencyObject> Walk(DependencyObject root)
            {
                yield return root;
                foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                    foreach(var item in Walk(child))yield return item;
            }
            var about=new SettingsWindow(new AppSettings(),_=>{},5);
            var content=Walk(about).OfType<TextBlock>().Select(x=>x.Text).ToArray();
            Assert(content.Any(x=>x.Contains("локальная программа")));
            Assert(!content.Any(x=>x.Contains("TEST_REPORT")));
            about.Close();
            var developer=new SettingsWindow(new AppSettings(),_=>{},6);
            var links=Walk(developer).OfType<Hyperlink>().Select(x=>x.NavigateUri?.ToString()).ToArray();
            Assert(links.Contains("https://promptix.ru/"));
            Assert(links.Contains("https://boosty.to/promtex"));
            developer.Close();
        });
        results.Add($"RESULT: {results.Count-failures} passed, {failures} failed");
        File.WriteAllLines(report,results);return failures==0?0:1;
    }
}
