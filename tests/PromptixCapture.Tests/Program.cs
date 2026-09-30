using PromptixCapture.Services;
using PromptixCapture.Models;

int passed=0,failed=0;
var lines=new List<string>();
void Test(string name,Action run)
{
    try{run();passed++;lines.Add("PASS "+name);}
    catch(Exception ex){failed++;lines.Add("FAIL "+name+": "+ex.Message);}
}
void Assert(bool condition,string message="Assertion failed"){if(!condition)throw new Exception(message);}
void Throws(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected ArgumentException");}

var date=new DateTime(2026,9,21,20,5,7,123);
Test("Date and size template",()=>Assert(FileNames.Format("Shot_{yyyy-MM-dd}_{HH-mm-ss}_{width}x{height}",date,800,600)=="Shot_2026-09-21_20-05-07_800x600"));
Test("Milliseconds",()=>Assert(FileNames.Format("S_{fff}",date,0,0)=="S_123"));
Test("Illegal Windows characters",()=>Assert(FileNames.Format("a:b/c\\d?e*",date,0,0)=="a_b_c_d_e_"));
Test("Reserved device name",()=>Assert(FileNames.Format("CON",date,0,0)=="_CON"));
Test("Reserved device with extension",()=>Assert(FileNames.Format("COM1.txt",date,0,0)=="_COM1.txt"));
Test("Trailing dots and spaces",()=>Assert(FileNames.Format("test...  ",date,0,0)=="test"));
Test("Empty template rejected",()=>Throws(()=>FileNames.Format("",date,0,0)));
Test("Unknown token rejected",()=>Throws(()=>FileNames.Format("{password}",date,0,0)));
Test("Unbalanced braces rejected",()=>Throws(()=>FileNames.Format("x{yyyy",date,0,0)));
Test("Maximum filename length",()=>Assert(FileNames.Format(new string('a',300),date,0,0).Length==180));
Test("Collision creates suffix",()=>
{
    var dir=Path.Combine(Path.GetTempPath(),"promptix-filename-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
    try{File.WriteAllText(Path.Combine(dir,"Shot.png"),"test");Assert(FileNames.Unique(dir,"Shot",".png")==Path.Combine(dir,"Shot-2.png"));}
    finally{File.Delete(Path.Combine(dir,"Shot.png"));Directory.Delete(dir);}
});
Test("Settings JSON round trip",()=>
{
    var settings=new AppSettings();settings.Video.CaptureMicrophone=false;settings.Screenshot.Format=ImageFileFormat.Jpeg;
    var clone=LocalData.Clone(settings);Assert(!clone.Video.CaptureMicrophone&&clone.Screenshot.Format==ImageFileFormat.Jpeg);
});
// Validation requires fully-qualified output folders, including on headless Linux runners.
AppSettings Valid(){var s=new AppSettings();s.Screenshot.Folder=Path.GetTempPath();s.Video.Folder=Path.GetTempPath();return s;}
Test("Valid settings",()=>SettingsService.Validate(Valid()));
Test("Invalid FPS rejected",()=>{var s=Valid();s.Video.FramesPerSecond=0;Throws(()=>SettingsService.Validate(s));});
Test("Invalid JPEG quality rejected",()=>{var s=Valid();s.Screenshot.JpegQuality=101;Throws(()=>SettingsService.Validate(s));});
Test("Invalid scroll height rejected",()=>{var s=Valid();s.Scrolling.MaxOutputHeight=int.MaxValue;Throws(()=>SettingsService.Validate(s));});
Test("Relative folder rejected",()=>{var s=Valid();s.Screenshot.Folder="../shots";Throws(()=>SettingsService.Validate(s));});
Test("Future schema rejected",()=>{var s=Valid();s.SchemaVersion=2;Throws(()=>SettingsService.Validate(s));});
Test("Unknown tool rejected",()=>{var s=Valid();s.Screenshot.DefaultTool=(AnnotationTool)99;Throws(()=>SettingsService.Validate(s));});
Test("Unknown format rejected",()=>{var s=Valid();s.Screenshot.Format=(ImageFileFormat)99;Throws(()=>SettingsService.Validate(s));});
Test("Unknown quality rejected",()=>{var s=Valid();s.Video.Quality=(VideoQuality)99;Throws(()=>SettingsService.Validate(s));});
Test("Unsupported countdown rejected",()=>{var s=Valid();s.Video.CountdownSeconds=2;Throws(()=>SettingsService.Validate(s));});
Test("Unknown tray action rejected",()=>{var s=Valid();s.General.LeftClickAction="unknown";Throws(()=>SettingsService.Validate(s));});
Test("Video resize keeps the starting aspect ratio",()=>
{
    var size=VideoRegionSize.Scale(846,608,.75,1000,1000);
    Assert(size.Width%2==0&&size.Height%2==0);
    Assert(Math.Abs((double)size.Width/size.Height-846d/608)<.01);
});
Test("Video resize stays inside monitor",()=>
{
    var size=VideoRegionSize.Scale(846,608,2,650,470);
    Assert(size.Width<=650&&size.Height<=470);
});
Test("Video resize enforces minimum dimensions",()=>
{
    var size=VideoRegionSize.Scale(846,608,.01,1000,1000);
    Assert(size.Width>=64&&size.Height>=64);
});

const int w=160,h=360;
var random=new Random(27);var page=new byte[w*h*3];random.NextBytes(page);
byte[] Frame(int y)=>page.Skip(y*w).Take(w*h).ToArray();
foreach(int shift in new[]{1,7,45,93,180,260})
{
    int d=shift;Test("Overlap shift "+d,()=>{var m=FrameMatcher.Match(Frame(0),Frame(d),w,h);Assert(m.Reliable&&!m.Unchanged&&m.Shift==d,$"Expected {d}, got {m}");});
}
Test("End of page detection",()=>{var m=FrameMatcher.Match(Frame(0),Frame(0),w,h);Assert(m.Unchanged&&m.Shift==0);});
Test("Blank page does not invent shift",()=>Assert(FrameMatcher.Match(new byte[w*h],new byte[w*h],w,h).Unchanged));
Test("Unrelated images rejected",()=>Assert(!FrameMatcher.Match(Frame(0),Frame(h),w,h).Reliable));
Test("Large unsupported scroll rejected",()=>Assert(!FrameMatcher.Match(Frame(0),Frame(h-20),w,h).Reliable));
Test("Wrong dimensions rejected",()=>Throws(()=>FrameMatcher.Match(new byte[10],new byte[10],w,h)));
Test("Small brightness differences tolerated",()=>
{
    var next=Frame(90);for(int i=0;i<next.Length;i++)next[i]=(byte)Math.Min(255,next[i]+2);
    var m=FrameMatcher.Match(Frame(0),next,w,h);Assert(m.Reliable&&m.Shift==90);
});
Test("Nested scrolling matches only the moving sidebar",()=>
{
    const int width=480,height=360,sidebar=96,header=24,shift=42;
    var rng=new Random(119);
    var background=new byte[width*height];rng.NextBytes(background);
    var list=new byte[sidebar*(height*2)];rng.NextBytes(list);
    byte[] Picture(int offset)
    {
        var pixels=(byte[])background.Clone();
        for(int y=header;y<height;y++)for(int x=8;x<sidebar-8;x++)
            pixels[y*width+x]=list[(y-header+offset)*sidebar+x];
        return pixels;
    }
    var first=Picture(0);var second=Picture(shift);
    var result=ScrollingFrameMatcher.Match(first,second,width,height,sidebar/2);
    Assert(result.Match.Reliable&&result.Match.Shift==shift,$"Expected sidebar shift {shift}, got {result.Match}");
    Assert(result.Viewport.Width<width/2&&result.Viewport.Left<=8&&result.Viewport.Right>=sidebar-8);
    Assert(result.Viewport.Top<=header&&result.Viewport.Height>=height-header);
});
Test("Unchanged pane can trigger another scroll target",()=>
{
    var frame=Frame(0);
    var result=ScrollingFrameMatcher.Match(frame,frame,w,h,w/2);
    Assert(result.Match.Unchanged&&!result.SignificantMotion);
});
Test("Whole page scrolling keeps the selected frame",()=>
{
    var result=ScrollingFrameMatcher.Match(Frame(0),Frame(45),w,h,w/2);
    Assert(result.Match.Reliable&&result.Match.Shift==45);
    Assert(result.Viewport.X==0&&result.Viewport.Y==0&&result.Viewport.Width==w&&result.Viewport.Height==h);
});
Test("Sparse chat rows remain matchable inside a wide window",()=>
{
    const int width=480,height=360,sidebar=96,header=24,shift=36;
    var rng=new Random(208);
    var background=new byte[width*height];rng.NextBytes(background);
    var list=Enumerable.Repeat((byte)245,sidebar*(height*2)).ToArray();
    for(int row=0;row<height*2/56;row++)
    {
        byte shade=(byte)rng.Next(40,190);
        for(int y=8;y<42;y++)for(int x=10;x<40;x++)
            if((x-25)*(x-25)+(y-25)*(y-25)<200)list[(row*56+y)*sidebar+x]=shade;
        for(int y=13;y<17;y++)for(int x=45;x<88;x++)list[(row*56+y)*sidebar+x]=(byte)rng.Next(25,110);
        for(int y=24;y<27;y++)for(int x=45;x<76;x++)list[(row*56+y)*sidebar+x]=(byte)rng.Next(110,190);
    }
    byte[] Picture(int offset)
    {
        var pixels=(byte[])background.Clone();
        for(int y=header;y<height;y++)for(int x=0;x<sidebar;x++)
            pixels[y*width+x]=list[(y-header+offset)*sidebar+x];
        return pixels;
    }
    var result=ScrollingFrameMatcher.Match(Picture(0),Picture(shift),width,height,sidebar/2);
    Assert(result.Match.Reliable&&result.Match.Shift==shift,$"Expected sparse sidebar shift {shift}, got {result.Match}");
    Assert(result.Viewport.Width<width/2);
});
Test("Nested scroll area keeps its full width and vertical position",()=>
{
    const int width=480,height=360,left=140,top=18,paneWidth=190,paneHeight=210,shift=34;
    var rng=new Random(77);
    var background=new byte[width*height];rng.NextBytes(background);
    var content=new byte[paneWidth*(paneHeight+shift+20)];rng.NextBytes(content);
    byte[] Picture(int offset)
    {
        var pixels=(byte[])background.Clone();
        for(int y=0;y<paneHeight;y++)for(int x=0;x<paneWidth;x++)
            pixels[(top+y)*width+left+x]=content[(y+offset)*paneWidth+x];
        return pixels;
    }
    var result=ScrollingFrameMatcher.Match(Picture(0),Picture(shift),width,height,left+paneWidth/2);
    Assert(result.Match.Reliable&&result.Match.Shift==shift);
    Assert(result.Viewport.Left<=left&&result.Viewport.Right>=left+paneWidth);
    Assert(result.Viewport.Top<=top&&result.Viewport.Bottom>=top+paneHeight);
    Assert(result.Viewport.Width<width/2&&result.Viewport.Height<height);
});
Test("Wheel input continues while a frame is captured",()=>Task.Run(async()=>
{
    using var cancel=new CancellationTokenSource();
    int pulses=0;
    var pump=ContinuousWheelPump.RunAsync(()=>true,()=>true,()=>120,
        delta=>{Assert(delta==120);Interlocked.Increment(ref pulses);},()=>20,cancel.Token);
    await Task.Delay(180);
    int duringCapture=Volatile.Read(ref pulses);
    cancel.Cancel();
    try{await pump;}catch(OperationCanceledException){}
    Assert(duringCapture>=3,$"Expected repeated scrolling during capture, got {duringCapture} pulses");
}).GetAwaiter().GetResult());
UpdateTests.Run(Test,Assert);
foreach(var line in lines)Console.WriteLine(line);
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
Environment.ExitCode=failed==0?0:1;
