using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PromptixCapture.Helpers;
using PromptixCapture.Models;
using PromptixCapture.Services;
using PromptixCapture.Windows;
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
    private RecordingWindow? _recording;
    private ScrollingWindow? _scrolling;
    private bool _capturing;
    private Dispatcher Dispatcher=>WpfApplication.Current.Dispatcher;
    public void Initialize(string[] args)
    {
        bool first=!File.Exists(Path.Combine(LocalData.Folder,"settings.json"));
        _settings.Load();Ui.ApplyTheme(_settings.Current.General.Theme);_history.Load();
        if(typeof(HotkeySettings).GetProperties().Select(p=>p.GetValue(_settings.Current.Hotkeys) as string).Any(v=>v?.Contains("PrintScreen",StringComparison.OrdinalIgnoreCase)==true))_printScreenSnipping.DisableForThisSession();
        _settings.Current.General.StartWithWindows=AutostartService.IsEnabled();
        _tray=new Forms.NotifyIcon{Text="ЛовиКадр • снимки и видео",Icon=CreateIcon(),Visible=true};
        BuildMenu();
        _tray.MouseClick+=async(_,e)=>
        {
            if(e.Button!=Forms.MouseButtons.Left)return;
            // The shell still owns the tray callback here.  Showing a WPF window
            // immediately can race its close and throws WindowInteropHelper's
            // "Visibility" exception.  Yield until that native callback is gone.
            await Task.Delay(150);
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                switch(_settings.Current.General.LeftClickAction){case "Screenshot":_ = Capture(CaptureMode.Region,CapturePurpose.Screenshot);break;case "Settings":ShowSettings();break;default:ShowQuickPanel();break;}
            }));
        };
        _tray.DoubleClick+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>ShowSettings()));
        _hotkeys.ShowSettings+=()=>ShowSettings();
        _hotkeys.Pressed+=action=>
        {
            switch(action)
            {
                case "VideoStartStop":if(_recording is not null)_recording.Stop();else _=Capture(CaptureMode.Region,CapturePurpose.Video);break;
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
        Add("Запись / стоп видео",()=>{if(_recording is not null)_recording.Stop();else _=Capture(CaptureMode.Region,CapturePurpose.Video);});
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
        _quickPanel?.Close();
        if(_settingsWindow is not null){_settingsWindow.Activate();return;}
        _settingsWindow=new SettingsWindow(_settings.Current,settings=>
        {
            AutostartService.SetEnabled(settings.General.StartWithWindows);
            _settings.Save(settings);Ui.ApplyTheme(settings.General.Theme);
            if(typeof(HotkeySettings).GetProperties().Select(p=>p.GetValue(settings.Hotkeys) as string).Any(v=>v?.Contains("PrintScreen",StringComparison.OrdinalIgnoreCase)==true))_printScreenSnipping.DisableForThisSession();
            ReportHotkeys(_hotkeys.Register(settings.Hotkeys));BuildMenu();
        },tab);
        _settingsWindow.Closed+=(_,_)=>_settingsWindow=null;_settingsWindow.Show();
    }
    private void ReportHotkeys(List<string> errors)
    {if(errors.Count>0)Dispatcher.BeginInvoke(new Action(()=>MessageBox.Show(string.Join("\n",errors)+"\n\nИзмените сочетания в настройках. Для Print Screen можно отключить запуск Ножниц в параметрах Windows.","Горячие клавиши",MessageBoxButton.OK,MessageBoxImage.Warning)));}
    private void ShowQuickPanel()
    {
        if(_quickPanel is not null){_quickPanel.Close();return;}
        var window=new Window{Title="ЛовиКадр",Width=465,Height=150,WindowStyle=WindowStyle.ToolWindow,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true};
        var panel=new StackPanel{Margin=new Thickness(12)};panel.Children.Add(Ui.Text("ЧТО ЗАХВАТИМ?",13,true));var row=new StackPanel{Orientation=Orientation.Horizontal};panel.Children.Add(row);
        row.Children.Add(Ui.Button("Снимок",()=>_=Capture(CaptureMode.Region,CapturePurpose.Screenshot)));
        row.Children.Add(Ui.Button("Видео",()=>{if(_recording is not null)_recording.Stop();else _=Capture(CaptureMode.Region,CapturePurpose.Video);}));
        row.Children.Add(Ui.Button("Длинный",()=>_=Capture(CaptureMode.Region,CapturePurpose.ScrollingScreenshot)));
        row.Children.Add(Ui.Button("Ещё",()=>{window.Close();_tray?.ContextMenuStrip?.Show(Forms.Cursor.Position);}));window.Content=panel;
        window.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)window.Close();};
        window.Loaded+=(_,_)=>{var work=Forms.Screen.FromPoint(NativeMethods.CursorPosition).WorkingArea;NativeMethods.PlacePixels(window,new Rectangle(work.Right-480,work.Bottom-165,465,150));};
        window.Deactivated+=(_,_)=>window.Close();window.Closed+=(_,_)=>_quickPanel=null;_quickPanel=window;window.Show();window.Activate();
    }
    private async Task Capture(CaptureMode mode,CapturePurpose purpose)
    {
        if(_capturing)return;
        if(_recording is not null || _scrolling is not null){Notify("Сначала завершите текущую запись или прокрутку.");return;}
        _capturing=true;
        var active=NativeMethods.GetForegroundWindow();var cursor=NativeMethods.CursorPosition;
        try
        {
            _quickPanel?.Close();await Task.Delay(120);
            var all=CaptureService.Desktop;var bounds=mode switch
            {
                CaptureMode.ActiveWindow=>CaptureService.ActiveWindow(active),
                CaptureMode.CurrentMonitor=>Forms.Screen.FromPoint(cursor).Bounds,
                CaptureMode.LastRegion=>_settings.Current.Screenshot.LastRegion?.ToRectangle()??all,
                _=>all
            };
            bounds=Rectangle.Intersect(bounds,all);
            if(bounds.Width<1||bounds.Height<1)throw new InvalidOperationException("Последняя область больше не находится на подключённом мониторе.");
            var image=await _capture.CaptureAsync(bounds,purpose==CapturePurpose.Screenshot && _settings.Current.Screenshot.IncludeCursor);
            if(mode==CaptureMode.Region)
            {
                var picker=new RegionSelectionWindow(image,all);
                if(picker.ShowDialog()!=true||picker.Result is not {} selection)return;
                // ShowDialog returns before all native close work is necessarily
                // finished.  Do not show the editor during that closing callback.
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
                bounds=selection;image=picker.ExportSelection();
                switch(picker.Action)
                {
                    case SelectionAction.Copy:
                        await ImageExportService.CopyAsync(image);Notify("Снимок скопирован в буфер обмена");return;
                    case SelectionAction.Save:
                        SaveScreenshot(image,HistoryMediaType.Screenshot);return;
                    case SelectionAction.Editor:
                        new EditorWindow(image,_settings.Current.Screenshot,_history,HistoryMediaType.Screenshot).Show();return;
                }
            }
            if(purpose!=CapturePurpose.Screenshot && !Forms.Screen.AllScreens.Any(s=>s.Bounds.Contains(bounds)))throw new InvalidOperationException("Для видео и длинного снимка выделите область внутри одного монитора.");
            if(purpose==CapturePurpose.Video)
            {
                var video=LocalData.Clone(_settings.Current.Video);
                if(!ConfirmRecording(video))return;
                _recording=new RecordingWindow(bounds,video,_history);_recording.Finished+=()=>_recording=null;_recording.Show();
            }
            else if(purpose==CapturePurpose.ScrollingScreenshot)
            {
                _scrolling=new ScrollingWindow(_capture,bounds,_settings.Current.Scrolling);
                if(_scrolling.ShowDialog()==true && _scrolling.Result is {} result)
                {
                    Notify(result.Reason);
                    await Present(result.Image,HistoryMediaType.ScrollingScreenshot,_settings.Current.Scrolling.OpenEditor);
                }
                _scrolling=null;
            }
            else await Present(image,HistoryMediaType.Screenshot,_settings.Current.Screenshot.OpenEditor);
        }
        catch(Exception ex){Ui.Error(ex);_scrolling=null;}
        finally{_capturing=false;}
    }
    private bool ConfirmRecording(VideoSettings video)
    {
        var dialog=new Window{Title="Настройки этой записи",Width=470,Height=300,WindowStartupLocation=WindowStartupLocation.CenterScreen,ResizeMode=ResizeMode.NoResize};
        var panel=new StackPanel{Margin=new Thickness(20)};panel.Children.Add(Ui.Text("Перед началом записи",22));
        var system=new CheckBox{Content="Системный звук",IsChecked=video.CaptureSystemAudio};var mic=new CheckBox{Content="Микрофон",IsChecked=video.CaptureMicrophone};var cursor=new CheckBox{Content="Показывать курсор",IsChecked=video.IncludeCursor};
        panel.Children.Add(system);panel.Children.Add(mic);panel.Children.Add(cursor);panel.Children.Add(Ui.Text($"{video.FramesPerSecond} FPS • отсчёт {video.CountdownSeconds} с • MP4",12,true));
        var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(Ui.Button("Отмена",()=>dialog.DialogResult=false));row.Children.Add(Ui.Button("Начать запись",()=>dialog.DialogResult=true));panel.Children.Add(row);dialog.Content=panel;
        if(dialog.ShowDialog()!=true)return false;video.CaptureSystemAudio=system.IsChecked==true;video.CaptureMicrophone=mic.IsChecked==true;video.IncludeCursor=cursor.IsChecked==true;return true;
    }
    private async Task Present(System.Windows.Media.Imaging.BitmapSource image,HistoryMediaType type,bool editor)
    {
        var settings=_settings.Current.Screenshot;
        if(editor){new EditorWindow(image,settings,_history,type).Show();return;}
        if(settings.CopyToClipboard)await ImageExportService.CopyAsync(image);
        if(settings.AutoSave || !settings.CopyToClipboard)
        {
            SaveScreenshot(image,type);
        }
        Notify("Снимок готов"+(_capture.LastBackend.StartsWith("GDI")?" • резервный GDI-захват":""));
        if(_settings.Current.General.PlaySounds&&!_settings.Current.General.QuietMode)System.Media.SystemSounds.Asterisk.Play();
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
        Notify("Снимок сохранён: "+Path.GetFileName(path));
    }
    private void Notify(string text)
    {if(_settings.Current.General.ShowNotifications&&!_settings.Current.General.QuietMode)_tray?.ShowBalloonTip(2500,"ЛовиКадр",text,Forms.ToolTipIcon.Info);}
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
