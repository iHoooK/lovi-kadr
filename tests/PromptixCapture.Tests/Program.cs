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
foreach(var line in lines)Console.WriteLine(line);
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
Environment.ExitCode=failed==0?0:1;
