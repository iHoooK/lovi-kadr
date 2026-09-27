using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PromptixCapture.Helpers;
using PromptixCapture.Models;
using PromptixCapture.Services;
using PromptixCapture.Windows;
using PromptixCapture.Editor;
using Forms=System.Windows.Forms;
using WpfApplication=System.Windows.Application;

namespace PromptixCapture;

public sealed class AppController : IDisposable
{
    private readonly SettingsService _settings=new();
    private readonly HistoryService _history=new();
    private readonly CaptureService _capture=new();
    private readonly HotkeyService _hotkeys=new();
    private readonly PrintScreenSnippingService _printScreenSnipping=new();
    private Forms.NotifyIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private Window? _quickPanel;
    private RegionSelectionWindow? _recording;
    private RegionSelectionWindow? _scrolling;
    private bool _capturing;
    private Dispatcher Dispatcher=>WpfApplication.Current.Dispatcher;
    public void Initialize(string[] args)
    {
        bool first=!File.Exists(Path.Combine(LocalData.Folder,"settings.json"));
        _settings.Load();Ui.ApplyTheme(_settings.Current.General.Theme);_history.Load();
        AppNotifications.Configure(()=>_settings.Current.General);
        if(typeof(HotkeySettings).GetProperties().Select(p=>p.GetValue(_settings.Current.Hotkeys) as string).Any(v=>v?.Contains("PrintScreen",StringComparison.OrdinalIgnoreCase)==true))_printScreenSnipping.DisableForThisSession();
        _settings.Current.General.StartWithWindows=AutostartService.IsEnabled();
        _tray=new Forms.NotifyIcon{Text="ЛовиКадр • снимки и видео",Icon=CreateIcon(),Visible=true};
        BuildMenu();
        _tray.MouseClick+=async(_,e)=>
        {
            if(e.Button!=Forms.MouseButtons.Left)return;
            var click=NativeMethods.CursorPosition;
            // The shell still owns the tray callback here.  Showing a WPF window
            // immediately can race its close and throws WindowInteropHelper's
            // "Visibility" exception.  Yield until that native callback is gone.
            await Task.Delay(150);
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                switch(_settings.Current.General.LeftClickAction){case "Screenshot":_ = Capture(CaptureMode.Region,CapturePurpose.Screenshot);break;case "Settings":ShowSettings();break;default:ShowQuickPanel(click);break;}
            }));
        };
        _tray.DoubleClick+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>ShowSettings()));
        _hotkeys.ShowSettings+=()=>ShowSettings();
        _hotkeys.Pressed+=action=>
        {
            switch(action)
            {
                case "VideoStartStop":if(_recording is not null)_recording.StartOrStop();else _=Capture(CaptureMode.Region,CapturePurpose.Video);break;
                case "VideoPauseResume":_recording?.TogglePause();break;
                case "ScrollingScreenshot":if(_scrolling is not null)_scrolling.Stop();else _=Capture(CaptureMode.Region,CapturePurpose.ScrollingScreenshot);break;
                default:if(Enum.TryParse<CaptureMode>(action,out var mode))_=Capture(mode,CapturePurpose.Screenshot);break;
            }
        };
        ReportHotkeys(_hotkeys.Register(_settings.Current.Hotkeys));
        if(first){_settings.Save(_settings.Current);ShowSettings();}
        else if(args.Contains("--settings") || (!args.Contains("--startup") && !_settings.Current.General.StartMinimized))ShowSettings();
    }
    private void BuildMenu()
    {
        var menu=new Forms.ContextMenuStrip();
        void Add(string title,Action action)
        {
            var item=new Forms.ToolStripMenuItem(title);
            item.Click+=async(_,_)=>
            {
                // A modal WPF selector cannot be shown while the WinForms tray menu is closing.
                await Task.Delay(150);_ = Dispatcher.BeginInvoke(action,DispatcherPriority.ContextIdle);
            };
            menu.Items.Add(item);
        }
        Add("Снимок области",()=>_=Capture(CaptureMode.Region,CapturePurpose.Screenshot));
        Add("Снимок последней области",()=>_=Capture(CaptureMode.LastRegion,CapturePurpose.Screenshot));
        Add("Запись / стоп видео",()=>{if(_recording is not null)_recording.StartOrStop();else _=Capture(CaptureMode.Region,CapturePurpose.Video);});
        Add("Длинный снимок / стоп",()=>{if(_scrolling is not null)_scrolling.Stop();else _=Capture(CaptureMode.Region,CapturePurpose.ScrollingScreenshot);});
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add("История",()=>new HistoryWindow(_history).Show());
        var quiet=new Forms.ToolStripMenuItem("Тихий режим"){Checked=_settings.Current.General.QuietMode,CheckOnClick=true};
        quiet.CheckedChanged+=(_,_)=>{try{_settings.Current.General.QuietMode=quiet.Checked;_settings.Save(_settings.Current);}catch(Exception ex){Ui.Error(ex);}};menu.Items.Add(quiet);
        Add("Настройки",()=>ShowSettings());Add("О программе",()=>ShowSettings(5));menu.Items.Add(new Forms.ToolStripSeparator());
        Add("Выход",Exit);
        if(_tray is not null){var old=_tray.ContextMenuStrip;_tray.ContextMenuStrip=menu;old?.Dispose();}
    }
    private void ShowSettings(int tab=0)
    {
        CloseQuickPanel();
        if(_settingsWindow is not null){_settingsWindow.Activate();return;}
        _settingsWindow=new SettingsWindow(_settings.Current,settings=>
        {
            AutostartService.SetEnabled(settings.General.StartWithWindows);
            _settings.Save(settings);Ui.ApplyTheme(settings.General.Theme);
            if(typeof(HotkeySettings).GetProperties().Select(p=>p.GetValue(settings.Hotkeys) as string).Any(v=>v?.Contains("PrintScreen",StringComparison.OrdinalIgnoreCase)==true))_printScreenSnipping.DisableForThisSession();
            ReportHotkeys(_hotkeys.Register(settings.Hotkeys));BuildMenu();
        },tab,()=>new HistoryWindow(_history,true).Show());
        _settingsWindow.Closed+=(_,_)=>_settingsWindow=null;_settingsWindow.Show();
    }
    private void ReportHotkeys(List<string> errors)
    {if(errors.Count>0)Dispatcher.BeginInvoke(new Action(()=>MessageBox.Show(string.Join("\n",errors)+"\n\nИзмените сочетания в настройках. Для Print Screen можно отключить запуск Ножниц в параметрах Windows.","Горячие клавиши",MessageBoxButton.OK,MessageBoxImage.Warning)));}
    private void ShowQuickPanel(System.Drawing.Point? click=null)
    {
        if(_quickPanel is not null){CloseQuickPanel();return;}
        var window=new Window{Title="ЛовиКадр",Width=164,Height=60,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true};
        Ui.ThemeWindow(window);
        var row=CreateQuickPanelActions(
            ()=>_=Capture(CaptureMode.Region,CapturePurpose.Screenshot),
            ()=>{if(_recording is not null)_recording.StartOrStop();else _=Capture(CaptureMode.Region,CapturePurpose.Video);},
            ()=>_=Capture(CaptureMode.Region,CapturePurpose.ScrollingScreenshot));
        window.Content=new Border{Child=row,Padding=new Thickness(8),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8)};
        ((Border)window.Content).SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        ((Border)window.Content).SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        window.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)CloseQuickPanel();};
        window.Loaded+=(_,_)=>
        {
            var anchor=click??NativeMethods.CursorPosition;
            var work=Forms.Screen.FromPoint(anchor).WorkingArea;
            NativeMethods.PlacePixels(window,QuickPanelBounds(anchor,work,new System.Drawing.Size(164,60)));
        };
        window.Closing+=(_,_)=>{if(ReferenceEquals(_quickPanel,window))_quickPanel=null;};
        window.Deactivated+=(_,_)=>{if(ReferenceEquals(_quickPanel,window))CloseQuickPanel();};
        window.Closed+=(_,_)=>{if(ReferenceEquals(_quickPanel,window))_quickPanel=null;};
        _quickPanel=window;window.Show();window.Activate();
    }
    internal static Rectangle QuickPanelBounds(System.Drawing.Point anchor,Rectangle work,System.Drawing.Size size)
    {
        int x=Math.Clamp(anchor.X-size.Width/2,work.Left,work.Right-size.Width);
        int y=anchor.Y<work.Top?work.Top+8:anchor.Y>=work.Bottom?work.Bottom-size.Height-8:anchor.Y-size.Height-12;
        return new Rectangle(x,Math.Clamp(y,work.Top,work.Bottom-size.Height),size.Width,size.Height);
    }
    private void CloseQuickPanel()
    {
        var window=_quickPanel;
        if(window is null)return;
        // Deactivated can fire while Close is in progress. Release this window
        // first so that the event cannot close it a second time.
        _quickPanel=null;
        window.Close();
    }
    internal static StackPanel CreateQuickPanelActions(Action screenshot,Action video,Action scrolling)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        row.Children.Add(Ui.ActionIconButton("screenshot",screenshot,"Снимок области"));
        row.Children.Add(Ui.ActionIconButton("video",video,"Видео"));
        row.Children.Add(Ui.ActionIconButton("scroll",scrolling,"Длинный снимок"));
        return row;
    }
    private async Task Capture(CaptureMode mode,CapturePurpose purpose)
    {
        if(_capturing)return;
        if(_recording is not null || _scrolling is not null){Notify("Сначала завершите текущую запись или прокрутку.");return;}
        _capturing=true;
        var active=NativeMethods.GetForegroundWindow();var cursor=NativeMethods.CursorPosition;
        try
        {
            CloseQuickPanel();await Task.Delay(120);
            var all=CaptureService.Desktop;var bounds=mode switch
            {
                CaptureMode.ActiveWindow=>CaptureService.ActiveWindow(active),
                CaptureMode.CurrentMonitor=>Forms.Screen.FromPoint(cursor).Bounds,
                CaptureMode.LastRegion=>_settings.Current.Screenshot.LastRegion?.ToRectangle()??all,
                _=>all
            };
            bounds=Rectangle.Intersect(bounds,all);
            if(bounds.Width<1||bounds.Height<1)throw new InvalidOperationException("Последняя область больше не находится на подключённом мониторе.");
            if(purpose==CapturePurpose.Video)
            {
                var picker=new RegionSelectionWindow(null,all,purpose,LocalData.Clone(_settings.Current.Video),history:_history);
                _recording=picker;
                picker.Finished+=()=>{if(ReferenceEquals(_recording,picker))_recording=null;};
                picker.Show();return;
            }
            var image=await _capture.CaptureAsync(bounds,purpose==CapturePurpose.Screenshot && _settings.Current.Screenshot.IncludeCursor);
            if(mode==CaptureMode.Region)
            {
                var picker=new RegionSelectionWindow(image,all,purpose,_settings.Current.Video,capture:purpose==CapturePurpose.ScrollingScreenshot?_capture:null,scrolling:_settings.Current.Scrolling);
                if(purpose==CapturePurpose.ScrollingScreenshot)_scrolling=picker;
                bool accepted;
                try{accepted=picker.ShowDialog()==true;}
                finally{if(ReferenceEquals(_scrolling,picker))_scrolling=null;}
                if(!accepted)return;
                // Let the selection overlay finish closing before writing to
                // the clipboard, so its window no longer covers other apps.
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
                if(purpose==CapturePurpose.ScrollingScreenshot && picker.ScrollingResult is {} scrollResult)
                {
                    var document=new AnnotationDocument(scrollResult.Image);
                    foreach(var mark in picker.ExportAnnotations())
                    {
                        mark.Translate(new System.Windows.Vector(-scrollResult.SourceCrop.X,-scrollResult.SourceCrop.Y));
                        document.Items.Add(mark);
                    }
                    var output=document.Items.Count==0?scrollResult.Image:new AnnotationCanvas(document).Export();
                    await ImageExportService.CopyAsync(output);
                    Notify("Длинный снимок скопирован в буфер обмена");
                    return;
                }
                if(picker.Result is not {} selection)return;
                bounds=selection;
                if(purpose==CapturePurpose.Screenshot)switch(picker.Action)
                {
                    case SelectionAction.Copy:
                        image=picker.ExportSelection();
                        await ImageExportService.CopyAsync(image);Notify("Снимок скопирован в буфер обмена");return;
                    case SelectionAction.Save:
                        image=picker.ExportSelection();
                        SaveScreenshot(image,HistoryMediaType.Screenshot);return;
                    case SelectionAction.Editor:
                        new EditorWindow(picker.ExportDocument(),_settings.Current.Screenshot,_history,HistoryMediaType.Screenshot).Show();return;
                }
                if(purpose==CapturePurpose.Screenshot)image=picker.ExportSelection();
            }
            if(purpose!=CapturePurpose.Screenshot && !Forms.Screen.AllScreens.Any(s=>s.Bounds.Contains(bounds)))throw new InvalidOperationException("Для видео и длинного снимка выделите область внутри одного монитора.");
            if(purpose==CapturePurpose.ScrollingScreenshot)throw new InvalidOperationException("Для длинного снимка выделите область экрана.");
            else await Present(image,HistoryMediaType.Screenshot,_settings.Current.Screenshot.OpenEditor);
        }
        catch(Exception ex){Ui.Error(ex);_scrolling=null;}
        finally{_capturing=false;}
    }
    private async Task Present(System.Windows.Media.Imaging.BitmapSource image,HistoryMediaType type,bool editor)
    {
        var settings=_settings.Current.Screenshot;
        if(editor){new EditorWindow(image,settings,_history,type).Show();return;}
        if(settings.CopyToClipboard){await ImageExportService.CopyAsync(image);Notify("Снимок скопирован в буфер обмена");}
        if(settings.AutoSave || !settings.CopyToClipboard)
        {
            SaveScreenshot(image,type);
        }
    }
    private bool EnsureScreenshotFolder(ScreenshotSettings settings)
    {
        if(!string.IsNullOrWhiteSpace(settings.Folder))return true;
        using var dialog=new Forms.FolderBrowserDialog{Description="Выберите папку для всех скриншотов ЛовиКадра",ShowNewFolderButton=true};
        if(dialog.ShowDialog()!=Forms.DialogResult.OK)return false;
        settings.Folder=dialog.SelectedPath;_settings.Save(_settings.Current);return true;
    }
    private void SaveScreenshot(System.Windows.Media.Imaging.BitmapSource image,HistoryMediaType type)
    {
        var settings=_settings.Current.Screenshot;
        if(!EnsureScreenshotFolder(settings))return;
        var path=ImageExportService.DefaultPath(image,settings);ImageExportService.Save(image,path,settings.JpegQuality);
        _history.Add(new HistoryItem{Path=path,Type=type,Width=image.PixelWidth,Height=image.PixelHeight});
        Notify((type==HistoryMediaType.ScrollingScreenshot?"Длинный снимок сохранён: ":"Снимок сохранён: ")+Path.GetFileName(path));
    }
    private void Notify(string text)
    {AppNotifications.Show(text);}
    private void Exit()
    {
        if(_recording is not null||_scrolling is not null){MessageBox.Show("Сначала остановите запись и дождитесь сохранения файла.","ЛовиКадр");return;}
        foreach(var window in WpfApplication.Current.Windows.Cast<Window>().ToArray()){window.Close();if(window.IsVisible)return;}
        WpfApplication.Current.Shutdown();
    }
    private static Icon CreateIcon()
    {
        var asset=Path.Combine(AppContext.BaseDirectory,"Assets","lovi-kadr.ico");
        if(File.Exists(asset))return new Icon(asset);
        using var bitmap=new Bitmap(32,32);using(var g=Graphics.FromImage(bitmap))
        {g.Clear(Color.FromArgb(13,17,23));using var cyan=new Pen(Color.FromArgb(53,208,255),3);using var gold=new Pen(Color.FromArgb(240,164,58),2);g.DrawRectangle(cyan,4,4,24,24);g.DrawRectangle(gold,10,10,12,12);}
        var handle=bitmap.GetHicon();try{return (Icon)Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    public void Dispose(){_hotkeys.Dispose();_printScreenSnipping.Dispose();if(_tray is not null){_tray.Visible=false;_tray.Icon?.Dispose();_tray.ContextMenuStrip?.Dispose();_tray.Dispose();}}
}
