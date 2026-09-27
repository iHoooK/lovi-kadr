using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using PromptixCapture.Editor;
using PromptixCapture.Models;
using PromptixCapture.Helpers;
using PromptixCapture.Services;
using DRect = System.Drawing.Rectangle;

namespace PromptixCapture.Windows;

public enum SelectionAction { Confirm, Copy, Save, Editor }

// One native window sized in physical pixels; drawing and hit testing use its
// measured client-size ratios, not the primary monitor's DPI.
public sealed class RegionSelectionWindow : Window
{
    private readonly SelectionSurface _surface;
    private readonly DRect _desktop;
    private readonly Grid _root;
    private readonly Border _banner;
    private readonly Border _tools;
    private readonly Border _actions;
    private readonly Button _compactTools;
    private readonly Button _compactActions;
    private Button? _scrollStart,_scrollStop;
    private MenuItem? _scrollStartMenu,_scrollStopMenu;
    private readonly TextBlock _heading;
    private readonly TextBlock _description;
    private readonly CaptureService? _capture;
    private readonly ScrollingSettings? _scrollSettings;
    private ScrollingCaptureService? _scrollService;
    private readonly CancellationTokenSource _scrollCancel=new();
    private bool _scrollingActive;
    private bool _scrollingFinished;
    private bool _captureExcluded;
    private Action<AnnotationTool>? _selectTool;
    private readonly VideoSettings? _videoSettings;
    private readonly HistoryService? _history;
    private readonly DispatcherTimer _videoTimer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private VideoRecorderService? _recorder;
    private VideoAnnotationWindow? _videoAnnotations;
    private bool _videoRegionUpdateQueued;
    private Button? _recordButton,_stopButton;
    private MenuItem? _recordMenuItem,_stopMenuItem;
    private TextBlock? _videoTime;
    private DRect _appliedRegion,_outputRegion;
    private string? _videoPath;
    private bool _videoStarted,_videoFinished,_deleteVideo;
    private VideoRecorderState? _shownVideoState;
    public event Action? Finished;
    public ScrollResult? ScrollingResult { get; private set; }
    public CapturePurpose Purpose { get; }
    public DRect? Result { get; private set; }
    public SelectionAction Action { get; private set; } = SelectionAction.Confirm;
    public RegionSelectionWindow(BitmapSource? screen, DRect desktop, CapturePurpose purpose=CapturePurpose.Screenshot, VideoSettings? video=null, DRect? previous = null, CaptureService? capture=null, ScrollingSettings? scrolling=null, HistoryService? history=null)
    {
        _desktop = desktop;Purpose=purpose;_capture=capture;_scrollSettings=scrolling;_videoSettings=video;_history=history;
        Ui.ThemeWindow(this);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross;
        Title = "ЛовиКадр — выбор области";
        _surface = new SelectionSurface(screen, desktop, previous,purpose==CapturePurpose.Video);
        _root=new Grid();_root.Children.Add(_surface);
        var overlay=new Canvas();_root.Children.Add(overlay);
        _banner=FloatingPanel();_banner.IsHitTestVisible=false;
        var heading=purpose switch
        {
            CapturePurpose.Video=>"ЗАПИСЬ ВИДЕО · выделите область",
            CapturePurpose.ScrollingScreenshot=>"ДЛИННЫЙ СНИМОК · выделите область прокрутки",
            _=>"СНИМОК ОБЛАСТИ · выделите область"
        };
        var description=purpose switch
        {
            CapturePurpose.Video=>$"Выделите область · звук и микрофон — в настройках · {video?.FramesPerSecond??30} FPS",
            CapturePurpose.ScrollingScreenshot=>"Выделите область, добавьте пометки и нажмите «Начать захват».",
            _=>"После выделения можно добавить пометки, скопировать или сохранить снимок."
        };
        _heading=OverlayText(heading,16);_description=OverlayText(description,12);
        _banner.Child=new StackPanel{Children={_heading,_description}};
        overlay.Children.Add(_banner);Canvas.SetLeft(_banner,16);Canvas.SetTop(_banner,16);

        var toolColumn=new StackPanel();_tools=FloatingPanel();_tools.Child=toolColumn;overlay.Children.Add(_tools);
        _compactTools=Ui.IconButton("✎",()=>OpenMenu(_compactTools),"Инструменты · меню");overlay.Children.Add(_compactTools);
        var toolItems=new (AnnotationTool Tool,string Icon,string Label)[]
        {
            (AnnotationTool.Select,"⌖","Выбор / перемещение"),(AnnotationTool.Pen,"✎","Ручка"),
            (AnnotationTool.Highlighter,"●","Маркер"),(AnnotationTool.Line,"╱","Линия"),
            (AnnotationTool.Arrow,"↗","Стрелка"),(AnnotationTool.Rectangle,"▭","Прямоугольник"),
            (AnnotationTool.Ellipse,"◯","Эллипс"),(AnnotationTool.Text,"T","Текст")
        };
        var toolButtons=new Dictionary<AnnotationTool,Button>();
        void SelectTool(AnnotationTool tool)
        {
            _surface.SetTool(tool);
            if(_scrollingActive && _scrollService is not null)
            {
                _scrollService.ManuallyPaused=tool!=AnnotationTool.Select;
                _description.Text=tool==AnnotationTool.Select
                    ? "Наведите курсор внутрь рамки для прокрутки. Уберите курсор, чтобы приостановить. Нажмите «Стоп», чтобы скопировать снимок."
                    : "Пометки: прокрутка приостановлена. Выберите курсор, чтобы продолжить.";
            }
            foreach(var (key,button) in toolButtons)
                button.SetResourceReference(Button.BorderBrushProperty,key==tool?"AccentBrush":"BorderBrush");
        }
        _selectTool=SelectTool;
        if(purpose is CapturePurpose.Screenshot or CapturePurpose.ScrollingScreenshot or CapturePurpose.Video)
        {
            foreach(var (tool,icon,label) in toolItems)
            {
                var selected=tool;var button=Ui.IconButton(icon,()=>SelectTool(selected),label);
                button.Width=38;button.Height=36;toolButtons.Add(tool,button);toolColumn.Children.Add(button);
            }
            var palette=new WrapPanel{Width=84,Margin=new Thickness(2,7,0,2)};
            foreach(var color in new[]{Colors.OrangeRed,Colors.Gold,Colors.LimeGreen,Colors.DeepSkyBlue,Colors.White,Colors.Black})
            {
                var selected=color;var swatch=Ui.Button("",()=>_surface.SetColor(selected),"Цвет пометки");
                swatch.Width=22;swatch.Height=22;swatch.MinHeight=22;swatch.Padding=new Thickness(0);
                swatch.Background=new SolidColorBrush(color);palette.Children.Add(swatch);
            }
            toolColumn.Children.Add(palette);
            toolColumn.Children.Add(OverlayText("Толщина",11));
            var widthChoice=new ComboBox{ItemsSource=new double[]{2,4,6,8,12},SelectedItem=4d,Width=78,Margin=new Thickness(2,0,2,4)};
            widthChoice.SelectionChanged+=(_,_)=>{if(widthChoice.SelectedItem is double width)_surface.SetStrokeWidth(width);};
            toolColumn.Children.Add(widthChoice);
            toolColumn.Children.Add(Ui.IconButton("↶",_surface.Undo,"Убрать последнюю пометку · Ctrl+Z"));
            SelectTool(AnnotationTool.Select);
            var toolMenu=new ContextMenu();
            foreach(var (tool,_,label) in toolItems)
            {
                var selected=tool;var item=new MenuItem{Header=label};item.Click+=(_,_)=>SelectTool(selected);toolMenu.Items.Add(item);
            }
            toolMenu.Items.Add(new Separator());
            foreach(var color in new[]{Colors.OrangeRed,Colors.Gold,Colors.LimeGreen,Colors.DeepSkyBlue,Colors.White,Colors.Black})
            {
                var selected=color;var item=new MenuItem{Header="Цвет · "+color};item.Click+=(_,_)=>_surface.SetColor(selected);toolMenu.Items.Add(item);
            }
            foreach(double width in new double[]{2,4,6,8,12})
            {
                var selected=width;var item=new MenuItem{Header=$"Толщина · {width:0}"};item.Click+=(_,_)=>_surface.SetStrokeWidth(selected);toolMenu.Items.Add(item);
            }
            var undo=new MenuItem{Header="Убрать последнюю пометку"};undo.Click+=(_,_)=>_surface.Undo();toolMenu.Items.Add(undo);
            ThemeMenu(toolMenu);_compactTools.ContextMenu=toolMenu;
        }

        var actionRow=new WrapPanel();_actions=FloatingPanel();_actions.Child=actionRow;overlay.Children.Add(_actions);
        var actionMenu=new ContextMenu();
        void AddAction(string label,SelectionAction? action,string icon="",string shortcut="")
        {
            Action run=()=>{if(action is null){if(Purpose==CapturePurpose.Video)CancelVideo();else if(_scrollingActive)CancelScrolling();else DialogResult=false;}else Finish(action.Value);};
            actionRow.Children.Add(icon.Length==0?Ui.Button(label,run):Ui.ActionIconButton(icon,run,label,shortcut));
            var item=new MenuItem{Header=label,InputGestureText=shortcut};item.Click+=(_,_)=>run();actionMenu.Items.Add(item);
        }
        if(purpose==CapturePurpose.Screenshot)
        {
            AddAction("Копировать",SelectionAction.Copy,"copy","Ctrl+C");AddAction("Сохранить",SelectionAction.Save,"save","Ctrl+S");
            AddAction("Полный редактор",SelectionAction.Editor,"edit","Ctrl+E");
        }
        else if(purpose==CapturePurpose.Video)
        {
            _videoTime=OverlayText("00:00:00",12);_videoTime.Visibility=Visibility.Collapsed;_videoTime.Width=88;_videoTime.TextAlignment=TextAlignment.Right;
            _videoTime.VerticalAlignment=VerticalAlignment.Center;actionRow.Children.Add(_videoTime);
            _recordButton=Ui.ActionIconButton("record",StartOrToggleVideo,"Начать запись","Enter");actionRow.Children.Add(_recordButton);
            _stopButton=Ui.ActionIconButton("stop",Stop,"Остановить и сохранить","Ctrl+Shift+PrintScreen");
            _stopButton.IsEnabled=false;actionRow.Children.Add(_stopButton);
            _recordMenuItem=new MenuItem{Header="Начать запись",InputGestureText="Enter"};
            _recordMenuItem.Click+=(_,_)=>StartOrToggleVideo();actionMenu.Items.Add(_recordMenuItem);
            _stopMenuItem=new MenuItem{Header="Стоп",IsEnabled=false};
            _stopMenuItem.Click+=(_,_)=>Stop();actionMenu.Items.Add(_stopMenuItem);
        }
        else
        {
            var start=Ui.Button("Начать захват",()=>_ = StartScrolling(),"Начать захват длинного снимка · Enter");
            actionRow.Children.Add(start);
            var stop=Ui.Button("Стоп",Stop,"Остановить и скопировать снимок");stop.Visibility=Visibility.Collapsed;
            actionRow.Children.Add(stop);
            var item=new MenuItem{Header="Начать захват",InputGestureText="Enter"};item.Click+=(_,_)=>_ = StartScrolling();actionMenu.Items.Add(item);
            var stopItem=new MenuItem{Header="Стоп",InputGestureText="Enter",Visibility=Visibility.Collapsed};stopItem.Click+=(_,_)=>Stop();actionMenu.Items.Add(stopItem);
            _scrollStart=start;_scrollStop=stop;_scrollStartMenu=item;_scrollStopMenu=stopItem;
        }
        AddAction("Отмена",null,"close","Esc");
        ThemeMenu(actionMenu);
        _compactActions=Ui.IconButton("⋯",()=>OpenMenu(_compactActions),"Действия · меню");_compactActions.ContextMenu=actionMenu;overlay.Children.Add(_compactActions);
        _surface.SelectionUpdated+=()=>
        {
            UpdateFloatingUi();
            _videoAnnotations?.Refresh();
            if(_videoStarted)QueueVideoRegionUpdate();
        };
        _surface.AnnotationsUpdated+=()=>_videoAnnotations?.Refresh();
        _root.SizeChanged+=(_,_)=>UpdateFloatingUi();
        Content=_root;UpdateFloatingUi();
        SourceInitialized += (_, _) => { NativeMethods.PlacePixels(this, desktop);if(purpose is CapturePurpose.ScrollingScreenshot or CapturePurpose.Video){_captureExcluded=NativeMethods.ExcludeFromCapture(this);((HwndSource)PresentationSource.FromVisual(this)!).AddHook(HitTestHook);} };
        Loaded += (_, _) =>
        {
            NativeMethods.PlacePixels(this, desktop);
            if(purpose==CapturePurpose.Video)
            {
                _videoAnnotations=new VideoAnnotationWindow(_surface,desktop);
                _surface.DrawAnnotationsOnSurface=false;
                _videoAnnotations.Show();
                if(!_videoAnnotations.ClickThrough)
                {
                    _videoAnnotations.Close();_videoAnnotations=null;
                    _surface.DrawAnnotationsOnSurface=true;
                    Dispatcher.BeginInvoke(new Action(()=>Ui.Error(new InvalidOperationException("Не удалось создать прозрачный слой пометок для записи."))));
                }
            }
            Activate(); Focus(); UpdateFloatingUi();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { if(purpose==CapturePurpose.Video)CancelVideo();else if(_scrollingActive)CancelScrolling();else DialogResult = false; e.Handled = true; return; }
            if (purpose==CapturePurpose.Screenshot && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key==Key.C) { Finish(SelectionAction.Copy); e.Handled=true; return; }
            if (purpose==CapturePurpose.Screenshot && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key==Key.S) { Finish(SelectionAction.Save); e.Handled=true; return; }
            if (purpose==CapturePurpose.Screenshot && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key==Key.E) { Finish(SelectionAction.Editor); e.Handled=true; return; }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key==Key.Z) { _surface.Undo(); e.Handled=true; return; }
            if (purpose==CapturePurpose.ScrollingScreenshot && e.Key==Key.Enter){if(_scrollingActive)Stop();else _=StartScrolling();e.Handled=true;return;}
            if (purpose==CapturePurpose.Video && e.Key == Key.Enter)
            { StartOrToggleVideo(); e.Handled = true; return; }
            var delta = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                _surface.Move(e.Key == Key.Left ? -delta : e.Key == Key.Right ? delta : 0, e.Key == Key.Up ? -delta : e.Key == Key.Down ? delta : 0);
                e.Handled = true;
            }
        };
        MouseRightButtonDown += (_, _) => {if(purpose==CapturePurpose.Video){if(!_videoStarted)CancelVideo();}else if(!_scrollingActive)DialogResult=false;};
        Closing+=(_,e)=>{if(purpose==CapturePurpose.Video&&_videoStarted&&!_videoFinished){e.Cancel=true;Stop();}};
        Closed+=(_,_)=>{_scrollCancel.Dispose();_videoTimer.Stop();_videoAnnotations?.Close();if(purpose==CapturePurpose.Video)Finished?.Invoke();};
    }
    private static Border FloatingPanel()=>new(){Background=new SolidColorBrush(Color.FromArgb(240,21,27,35)),BorderBrush=new SolidColorBrush(Color.FromRgb(92,113,137)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Padding=new Thickness(5)};
    private void StartOrToggleVideo()
    {
        if(Purpose!=CapturePurpose.Video||_videoFinished)return;
        if(_recorder is not null){TogglePause();return;}
        if(!_videoStarted)_=RunVideo();
    }
    public void StartOrStop(){if(Purpose!=CapturePurpose.Video)return;if(_recorder is null)StartOrToggleVideo();else Stop();}
    public void TogglePause(){if(Purpose!=CapturePurpose.Video)return;_recorder?.TogglePause();RefreshVideoControls();}
    private async Task RunVideo()
    {
        if(_videoSettings is null||_history is null)return;
        if(!_captureExcluded||!NativeMethods.IsExcludedFromCapture(this)||_videoAnnotations is null)
        {
            Ui.Error(new InvalidOperationException("Не удалось скрыть рамку из захвата или создать слой пометок. Запись не начата."));
            return;
        }
        var region=_surface.PixelRectangle();
        var monitor=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.Bounds.Contains(region));
        if(region.Width<64||region.Height<64||monitor is null)
        {
            _description.Text="Выделите область от 64 × 64 px внутри одного монитора.";
            return;
        }
        region.Width-=region.Width%2;region.Height-=region.Height%2;
        Exception? failure=null;
        try
        {
            _videoPath=FileNames.Unique(_videoSettings.Folder,_videoSettings.FileNameTemplate,".mp4",region.Width,region.Height);
            _recorder=new VideoRecorderService(Dispatcher);
            _recorder.Start(region,_videoSettings,_videoPath);
            _videoStarted=true;_outputRegion=_appliedRegion=region;
            _surface.SetSelection(region);
            _surface.BeginVideoRecording(monitor.Bounds);
            _videoTime!.Visibility=Visibility.Visible;
            _videoTimer.Tick+=(_,_)=>
            {
                RefreshVideoControls();
                if(_surface.PixelRectangle()!=_appliedRegion)QueueVideoRegionUpdate();
            };
            _videoTimer.Start();RefreshVideoControls();UpdateFloatingUi();
            await _recorder.Completion;
            var saved=_videoPath!;
            if(_deleteVideo){if(File.Exists(saved))File.Delete(saved);}
            else
            {
                if(!File.Exists(saved) || new FileInfo(saved).Length==0)
                    throw new IOException("Запись завершилась, но видеофайл не найден в выбранной папке.");
                _history.Add(new HistoryItem{Path=saved,Type=HistoryMediaType.Video,Width=_outputRegion.Width,Height=_outputRegion.Height,DurationSeconds=_recorder.Elapsed.TotalSeconds});
                AppNotifications.Show("Видео сохранено: "+Path.GetFileName(saved));
                if(_videoSettings.OpenFolderAfterRecording)Ui.Reveal(saved);
            }
        }
        catch(Exception ex){failure=ex;}
        finally
        {
            _videoTimer.Stop();
            if(_deleteVideo&&_videoPath is not null&&File.Exists(_videoPath))
                try{File.Delete(_videoPath);}catch(Exception ex){AppLog.Error("Delete cancelled video",ex);}
            _videoFinished=true;Close();
            if(failure is not null)Ui.Error(failure);
            if(_recorder is not null)
                try{await Task.Run(_recorder.Dispose);}catch(Exception ex){AppLog.Error("Dispose video recorder",ex);}
        }
    }
    private void RefreshVideoControls()
    {
        if(_recordButton is null||_stopButton is null||_videoTime is null)return;
        var state=_recorder?.State;
        _videoTime.Text=state==VideoRecorderState.Paused?$"Ⅱ {_recorder!.Elapsed:hh\\:mm\\:ss}":$"{_recorder?.Elapsed??TimeSpan.Zero:hh\\:mm\\:ss}";
        var paused=state==VideoRecorderState.Paused;
        if(_shownVideoState!=state)
        {
            Ui.SetActionIcon(_recordButton,paused?"play":_videoStarted?"pause":"record",paused?"Продолжить запись":_videoStarted?"Пауза":"Начать запись","Enter");
            _shownVideoState=state;
        }
        _recordButton.IsEnabled=state is not VideoRecorderState.Stopping;
        _stopButton.IsEnabled=_videoStarted&&state is not VideoRecorderState.Stopping;
        if(_recordMenuItem is not null)_recordMenuItem.Header=paused?"Продолжить запись":_videoStarted?"Пауза":"Начать запись";
        if(_stopMenuItem is not null)_stopMenuItem.IsEnabled=_stopButton.IsEnabled;
    }
    private void ApplyVideoRegion()
    {
        if(_recorder?.State is not (VideoRecorderState.Recording or VideoRecorderState.Paused))return;
        var region=_surface.PixelRectangle();
        region.Width-=region.Width%2;region.Height-=region.Height%2;
        if(region==_appliedRegion)return;
        try
        {
            if(_recorder.UpdateRegion(region))_appliedRegion=region;
            else{_surface.SetSelection(_appliedRegion);_description.Text="Область не изменена: запись не приняла новый размер.";}
        }
        catch(Exception ex){AppLog.Error("Update video region",ex);_surface.SetSelection(_appliedRegion);_description.Text="Область не изменена: запись не приняла новый размер.";}
    }
    private void QueueVideoRegionUpdate()
    {
        if(_videoRegionUpdateQueued)return;
        _videoRegionUpdateQueued=true;
        Dispatcher.BeginInvoke(new Action(()=>
        {
            _videoRegionUpdateQueued=false;
            if(_videoStarted&&!_videoFinished)ApplyVideoRegion();
        }),DispatcherPriority.Render);
    }
    private void CancelVideo()
    {
        if(!_videoStarted){_videoFinished=true;Close();return;}
        _deleteVideo=true;Stop();
    }
    private async Task StartScrolling()
    {
        if(_scrollingActive || Purpose!=CapturePurpose.ScrollingScreenshot || _capture is null || _scrollSettings is null)return;
        if(!_captureExcluded){Ui.Error(new InvalidOperationException("Windows не разрешила скрыть рамку из снимка. Обновите Windows и повторите захват."));return;}
        if(_surface.Selection.Width<64 || _surface.Selection.Height<128)
        {Ui.Error(new InvalidOperationException("Для длинного снимка выделите область минимум 64 × 128 px."));return;}
        var region=_surface.PixelRectangle();
        if(!System.Windows.Forms.Screen.AllScreens.Any(screen=>screen.Bounds.Contains(region)))
        {Ui.Error(new InvalidOperationException("Для длинного снимка выделите область внутри одного монитора."));return;}
        _scrollingActive=true;
        _scrollService=new ScrollingCaptureService(_capture);
        _surface.LiveMode=true;
        _selectTool?.Invoke(AnnotationTool.Select);
        _heading.Text="ДЛИННЫЙ СНИМОК · захват";
        _description.Text="Наведите курсор внутрь рамки для прокрутки. Уберите курсор, чтобы приостановить. Нажмите «Стоп», чтобы скопировать снимок.";
        _scrollStart!.Visibility=Visibility.Collapsed;_scrollStop!.Visibility=Visibility.Visible;
        _scrollStartMenu!.Visibility=Visibility.Collapsed;_scrollStopMenu!.Visibility=Visibility.Visible;
        UpdateFloatingUi();
        var progress=new Progress<ScrollProgress>(p=>_heading.Text=$"ДЛИННЫЙ СНИМОК · {p.State} · {p.Frames} кадр. · {p.Height} px");
        try
        {
            // Let the start-button click finish before the overlay becomes click-through.
            await Task.Delay(100,_scrollCancel.Token);
            ScrollingResult=await _scrollService.RunAsync(region,_scrollSettings,progress,_scrollCancel.Token);
            _scrollingFinished=true;DialogResult=true;
        }
        catch(OperationCanceledException){_scrollingFinished=true;DialogResult=false;}
        catch(Exception ex){_scrollingFinished=true;DialogResult=false;Ui.Error(ex);}
    }
    public void Stop()
    {
        if(Purpose==CapturePurpose.Video)
        {
            _recorder?.Stop();RefreshVideoControls();
            if(_recorder?.State==VideoRecorderState.Stopping)
            {
                Hide();
                _videoAnnotations?.Hide();
            }
            return;
        }
        if(_scrollingActive)_scrollService!.StopRequested=true;
        else if(Purpose==CapturePurpose.ScrollingScreenshot)DialogResult=false;
    }
    private void CancelScrolling()
    {
        if(_scrollingActive)_scrollCancel.Cancel();
        else DialogResult=false;
    }
    private IntPtr HitTestHook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message!=0x84 || !(_scrollingActive&&!_scrollingFinished || _videoStarted&&!_videoFinished))return IntPtr.Zero;
        var cursor=NativeMethods.CursorPosition;
        var point=_root.PointFromScreen(new Point(cursor.X,cursor.Y));
        static bool Contains(FrameworkElement element,Point point)
        {
            if(element.Visibility!=Visibility.Visible)return false;
            return new Rect(Canvas.GetLeft(element),Canvas.GetTop(element),element.ActualWidth,element.ActualHeight).Contains(point);
        }
        if(Contains(_actions,point)||Contains(_compactActions,point)||Contains(_tools,point)||Contains(_compactTools,point))return IntPtr.Zero;
        var local=new Point(cursor.X-_desktop.X,cursor.Y-_desktop.Y);
        if(_surface.WantsPointer(local))return IntPtr.Zero;
        handled=true;return new IntPtr(-1);
    }
    private static TextBlock OverlayText(string text,double size)=>new(){Text=text,FontSize=size,Foreground=Brushes.White,Margin=new Thickness(4,2,4,2),TextWrapping=TextWrapping.Wrap};
    private static void ThemeMenu(ContextMenu menu)
    {
        menu.SetResourceReference(ContextMenu.BackgroundProperty,"SurfaceBrush");
        menu.SetResourceReference(ContextMenu.ForegroundProperty,"TextPrimaryBrush");
    }
    private static void OpenMenu(Button? button){if(button?.ContextMenu is {} menu){menu.PlacementTarget=button;menu.IsOpen=true;}}
    private void UpdateFloatingUi()
    {
        if(_root.ActualWidth<=0||_root.ActualHeight<=0)return;
        _banner.MaxWidth=Math.Max(120,_root.ActualWidth-32);
        var selection=_surface.Selection;
        bool selected=selection.Width>2&&selection.Height>2;
        _actions.Visibility=selected?Visibility.Visible:Visibility.Collapsed;
        _compactActions.Visibility=Visibility.Collapsed;
        _tools.Visibility=selected&&(Purpose is CapturePurpose.Screenshot or CapturePurpose.ScrollingScreenshot or CapturePurpose.Video)?Visibility.Visible:Visibility.Collapsed;
        _compactTools.Visibility=Visibility.Collapsed;
        if(!selected)return;
        double sx=_root.ActualWidth/_desktop.Width,sy=_root.ActualHeight/_desktop.Height;
        var rect=new Rect(selection.X*sx,selection.Y*sy,selection.Width*sx,selection.Height*sy);
        double maxW=Math.Max(150,_root.ActualWidth-16),maxH=_root.ActualHeight;
        _actions.MaxWidth=maxW;
        _actions.Measure(new Size(maxW,double.PositiveInfinity));
        var a=_actions.DesiredSize;
        double actionX=Math.Clamp(rect.Left+(rect.Width-a.Width)/2,8,Math.Max(8,_root.ActualWidth-a.Width-8));
        double actionY=rect.Bottom+8;
        if(actionY+a.Height>maxH-8)
        {
            if(rect.Top-a.Height-8>=8)actionY=rect.Top-a.Height-8;
            else if(rect.Height>=a.Height+16)actionY=rect.Bottom-a.Height-8;
            else
            {
                _actions.Visibility=Visibility.Collapsed;_compactActions.Visibility=Visibility.Visible;
                _compactActions.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                var c=_compactActions.DesiredSize;
                Canvas.SetLeft(_compactActions,Math.Clamp(rect.Right-c.Width,8,Math.Max(8,_root.ActualWidth-c.Width-8)));
                Canvas.SetTop(_compactActions,Math.Clamp(rect.Bottom-c.Height,8,Math.Max(8,maxH-c.Height-8)));
            }
        }
        Canvas.SetLeft(_actions,actionX);Canvas.SetTop(_actions,actionY);
        if(Purpose is not (CapturePurpose.Screenshot or CapturePurpose.ScrollingScreenshot or CapturePurpose.Video))return;
        _tools.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        var t=_tools.DesiredSize;
        void PlaceCompactTools()
        {
            _tools.Visibility=Visibility.Collapsed;_compactTools.Visibility=Visibility.Visible;
            _compactTools.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            var c=_compactTools.DesiredSize;
            double x=rect.Right+c.Width+8<=_root.ActualWidth?rect.Right+8:rect.Right-c.Width-8;
            Canvas.SetLeft(_compactTools,Math.Clamp(x,8,Math.Max(8,_root.ActualWidth-c.Width-8)));
            Canvas.SetTop(_compactTools,Math.Clamp(rect.Top,8,Math.Max(8,maxH-c.Height-8)));
        }
        bool enoughHeight=rect.Height>=Math.Min(t.Height,240)&&t.Height<=maxH-16;
        if(!enoughHeight)
        {
            PlaceCompactTools();
            return;
        }
        double toolX=rect.Right+t.Width+8<=_root.ActualWidth?rect.Right+8:rect.Right-t.Width-8;
        toolX=Math.Clamp(toolX,8,Math.Max(8,_root.ActualWidth-t.Width-8));
        double toolY=Math.Clamp(rect.Top,8,Math.Max(8,maxH-t.Height-8));
        if(_actions.Visibility==Visibility.Visible && new Rect(toolX,toolY,t.Width,t.Height).IntersectsWith(new Rect(actionX,actionY,a.Width,a.Height)))
        {
            PlaceCompactTools();return;
        }
        Canvas.SetLeft(_tools,toolX);Canvas.SetTop(_tools,toolY);
    }
    private void Finish(SelectionAction action)
    {
        if(_surface.Selection.Width<=2 || _surface.Selection.Height<=2)return;
        if(!SupportsAction(Purpose,action))return;
        Action=action;Result=_surface.PixelRectangle();DialogResult=true;
    }
    internal static bool SupportsAction(CapturePurpose purpose,SelectionAction action)=>
        purpose==CapturePurpose.Screenshot ? action is SelectionAction.Copy or SelectionAction.Save or SelectionAction.Editor : purpose==CapturePurpose.ScrollingScreenshot&&action==SelectionAction.Confirm;
    public BitmapSource ExportSelection()=>_surface.ExportSelection();
    public AnnotationDocument ExportDocument()=>_surface.ExportDocument();
    public IReadOnlyList<AnnotationModel> ExportAnnotations()=>_surface.ExportAnnotations();

    private sealed class VideoAnnotationWindow : Window
    {
        private readonly AnnotationSurface _annotations;
        public bool ClickThrough { get; private set; }
        public VideoAnnotationWindow(SelectionSurface selection,DRect desktop)
        {
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true;Background=Brushes.Transparent;
            ShowInTaskbar=false;ShowActivated=false;Topmost=true;
            Focusable=false;IsHitTestVisible=false;
            _annotations=new AnnotationSurface(selection,desktop);
            Content=_annotations;
            SourceInitialized+=(_,_)=>
            {
                NativeMethods.PlacePixels(this,desktop);
                ClickThrough=NativeMethods.MakeClickThrough(this);
            };
            Loaded+=(_,_)=>NativeMethods.PlacePixels(this,desktop);
        }
        public void Refresh()=>_annotations.InvalidateVisual();
        private sealed class AnnotationSurface(SelectionSurface selection,DRect desktop) : FrameworkElement
        {
            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                if(ActualWidth<=0||ActualHeight<=0)return;
                selection.DrawVideoAnnotations(dc,new Size(ActualWidth,ActualHeight),desktop);
            }
        }
    }

    private sealed class SelectionSurface : FrameworkElement
    {
        private readonly BitmapSource? _image;
        private readonly DRect _desktop;
        private readonly bool _videoLive;
        private readonly List<AnnotationModel> _annotations=new();
        private AnnotationModel? _annotationDraft;
        private AnnotationTool _tool=AnnotationTool.Select;
        public AnnotationTool CurrentTool=>_tool;
        private bool _liveMode;
        private bool _videoRecording;
        private int _videoBaseWidth,_videoBaseHeight;
        private bool _drawAnnotationsOnSurface=true;
        private Rect _allowed;
        public bool LiveMode { get=>_liveMode;set{_liveMode=value;InvalidateVisual();} }
        private Color _color=Colors.OrangeRed;
        private double _strokeWidth=4;
        public event Action? SelectionUpdated;
        public event Action? AnnotationsUpdated;
        public bool DrawAnnotationsOnSurface
        {
            get=>_drawAnnotationsOnSurface;
            set{_drawAnnotationsOnSurface=value;InvalidateVisual();}
        }
        public Rect Selection { get; private set; }
        private Point _start;
        private Rect _before;
        private int _drag; // 0 new, 1 move, 2..9 handles
        public SelectionSurface(BitmapSource? image, DRect desktop, DRect? previous,bool videoLive)
        {
            _image = image; _desktop = desktop;_videoLive=videoLive;
            if (previous is { } p && desktop.Contains(p)) Selection = new Rect(p.X - desktop.X, p.Y - desktop.Y, p.Width, p.Height);
            MouseLeftButtonDown += Down;
            MouseMove += Drag;
            MouseLeftButtonUp += (_, e) => { _annotationDraft=null;ReleaseMouseCapture(); UpdateCursor(PixelPoint(e)); SelectionUpdated?.Invoke(); };
        }
        private Point PixelPoint(MouseEventArgs e) { var p = e.GetPosition(this); return new Point(p.X * _desktop.Width / Math.Max(1,ActualWidth), p.Y * _desktop.Height / Math.Max(1,ActualHeight)); }
        public void BeginVideoRecording(DRect monitor)
        {
            _videoRecording=true;
            _videoBaseWidth=(int)Selection.Width;_videoBaseHeight=(int)Selection.Height;
            _allowed=new Rect(monitor.X-_desktop.X,monitor.Y-_desktop.Y,monitor.Width,monitor.Height);
            InvalidateVisual();
        }
        public void SetSelection(DRect absolute)
        {
            var moved=new Rect(absolute.X-_desktop.X,absolute.Y-_desktop.Y,absolute.Width,absolute.Height);
            var delta=moved.TopLeft-Selection.TopLeft;
            foreach(var annotation in _annotations)annotation.Translate(delta);
            Selection=moved;InvalidateVisual();SelectionUpdated?.Invoke();
        }
        public bool WantsPointer(Point point)
        {
            if(_tool!=AnnotationTool.Select&&Selection.Contains(point))return true;
            var r=Selection;
            if(r.Width<=0)return false;
            return point.X>=r.Left-10&&point.X<=r.Right+10&&point.Y>=r.Top-10&&point.Y<=r.Bottom+10&&
                (Math.Abs(point.X-r.Left)<=10||Math.Abs(point.X-r.Right)<=10||Math.Abs(point.Y-r.Top)<=10||Math.Abs(point.Y-r.Bottom)<=10);
        }
        private IEnumerable<Point> Handles()
        {
            var r = Selection;
            yield return r.TopLeft; yield return new Point(r.Left + r.Width/2,r.Top); yield return r.TopRight;
            yield return new Point(r.Right,r.Top+r.Height/2); yield return r.BottomRight; yield return new Point(r.Left+r.Width/2,r.Bottom);
            yield return r.BottomLeft; yield return new Point(r.Left,r.Top+r.Height/2);
        }
        private void Down(object sender, MouseButtonEventArgs e)
        {
            _start = PixelPoint(e); _before = Selection; _drag = 0;
            if(_tool!=AnnotationTool.Select)
            {
                if(Selection.Width<=2||Selection.Height<=2||!Selection.Contains(_start))return;
                if(_tool==AnnotationTool.Text)
                {
                    var value=Ui.Prompt(Window.GetWindow(this),"Текст на снимке","Введите текст:","");
                    if(!string.IsNullOrWhiteSpace(value))_annotations.Add(new AnnotationModel{Tool=_tool,Start=_start,End=_start,Color=_color,Thickness=_strokeWidth,FontSize=24,Text=value});
                    InvalidateVisual();AnnotationsUpdated?.Invoke();return;
                }
                _annotationDraft=new AnnotationModel{Tool=_tool,Start=_start,End=_start,Color=_color,Thickness=_strokeWidth};
                if(_tool is AnnotationTool.Pen or AnnotationTool.Highlighter)_annotationDraft.Points.Add(_start);
                _annotations.Add(_annotationDraft);CaptureMouse();InvalidateVisual();AnnotationsUpdated?.Invoke();return;
            }
            if (Selection.Width > 0)
            {
                int i = 0; foreach (var p in Handles()) { if ((p - _start).Length < 12) { _drag = i + 2; break; } i++; }
                if(_drag==0&&_videoRecording&&WantsPointer(_start))
                {
                    _drag=Math.Abs(_start.X-Selection.Left)<=10?9:
                        Math.Abs(_start.X-Selection.Right)<=10?5:
                        Math.Abs(_start.Y-Selection.Bottom)<=10?7:1;
                }
                else if(_drag==0&&Selection.Contains(_start))_drag=1;
            }
            if(_videoRecording&&_drag==0)return;
            if (_drag == 0) Selection = new Rect(_start, _start);
            CaptureMouse(); InvalidateVisual();SelectionUpdated?.Invoke();
        }
        private int HitHandle(Point point)
        {
            int i=0;foreach(var handle in Handles()){if((handle-point).Length<12)return i;i++;}return -1;
        }
        private void UpdateCursor(Point point)
        {
            if(_tool!=AnnotationTool.Select){Cursor=Cursors.Pen;return;}
            if(Selection.Width<=0){Cursor=Cursors.Cross;return;}
            if(_videoRecording&&HitHandle(point)<0&&WantsPointer(point))
            {
                Cursor=Math.Abs(point.X-Selection.Left)<=10||Math.Abs(point.X-Selection.Right)<=10?Cursors.SizeWE:
                    Math.Abs(point.Y-Selection.Bottom)<=10?Cursors.SizeNS:Cursors.SizeAll;
                return;
            }
            Cursor=HitHandle(point) switch
            {
                0 or 4=>Cursors.SizeNWSE,
                2 or 6=>Cursors.SizeNESW,
                1 or 5=>Cursors.SizeNS,
                3 or 7=>Cursors.SizeWE,
                _ when Selection.Contains(point)=>Cursors.SizeAll,
                _=>Cursors.Cross
            };
        }
        private void Drag(object sender, MouseEventArgs e)
        {
            if (!IsMouseCaptured){UpdateCursor(PixelPoint(e));return;}
            var p = PixelPoint(e); p.X = Math.Clamp(p.X, 0, _desktop.Width); p.Y = Math.Clamp(p.Y, 0, _desktop.Height);
            if(_annotationDraft is not null)
            {
                _annotationDraft.End=p;if(_annotationDraft.Tool is AnnotationTool.Pen or AnnotationTool.Highlighter)_annotationDraft.Points.Add(p);InvalidateVisual();AnnotationsUpdated?.Invoke();return;
            }
            var d = p - _start;
            if (_drag == 0) Selection = new Rect(_start, p);
            else if (_drag == 1)
            {
                var limits=_videoRecording?_allowed:new Rect(0,0,_desktop.Width,_desktop.Height);
                var moved=new Point(Math.Clamp(_before.X + d.X, limits.Left, Math.Max(limits.Left,limits.Right - _before.Width)), Math.Clamp(_before.Y + d.Y, limits.Top, Math.Max(limits.Top,limits.Bottom - _before.Height)));
                var delta=moved-_before.TopLeft;foreach(var annotation in _annotations)annotation.Translate(delta);
                _before=new Rect(moved,_before.Size);_start=p;Selection=_before;
            }
            else
            {
                var a = _before.TopLeft; var b = _before.BottomRight; int h = _drag - 2;
                if (h is 0 or 6 or 7) a.X = p.X;
                if (h is 0 or 1 or 2) a.Y = p.Y;
                if (h is 2 or 3 or 4) b.X = p.X;
                if (h is 4 or 5 or 6) b.Y = p.Y;
                if(_videoRecording)
                {
                    Selection=ResizeVideoSelection(p,h);
                }
                else Selection = new Rect(a, b);
            }
            InvalidateVisual();UpdateCursor(p);SelectionUpdated?.Invoke();
        }
        private Rect ResizeVideoSelection(Point pointer,int handle)
        {
            var r=_before;
            bool left=handle is 0 or 6 or 7,right=handle is 2 or 3 or 4;
            bool top=handle is 0 or 1 or 2,bottom=handle is 4 or 5 or 6;
            double centerX=r.Left+r.Width/2,centerY=r.Top+r.Height/2;
            double width=left?r.Right-pointer.X:right?pointer.X-r.Left:r.Width;
            double height=top?r.Bottom-pointer.Y:bottom?pointer.Y-r.Top:r.Height;
            double widthScale=width/_videoBaseWidth,heightScale=height/_videoBaseHeight;
            double scale=left||right
                ? top||bottom
                    ? Math.Abs(width-r.Width)/_videoBaseWidth>=Math.Abs(height-r.Height)/_videoBaseHeight?widthScale:heightScale
                    : widthScale
                : heightScale;
            double maxWidth=left?r.Right-_allowed.Left:right?_allowed.Right-r.Left:
                2*Math.Min(centerX-_allowed.Left,_allowed.Right-centerX);
            double maxHeight=top?r.Bottom-_allowed.Top:bottom?_allowed.Bottom-r.Top:
                2*Math.Min(centerY-_allowed.Top,_allowed.Bottom-centerY);
            var size=VideoRegionSize.Scale(_videoBaseWidth,_videoBaseHeight,scale,maxWidth,maxHeight);
            double x=left?r.Right-size.Width:right?r.Left:centerX-size.Width/2;
            double y=top?r.Bottom-size.Height:bottom?r.Top:centerY-size.Height/2;
            return new Rect(x,y,size.Width,size.Height);
        }
        public void Move(int x, int y)
        {
            var r = Selection;
            var limits=_videoRecording?_allowed:new Rect(0,0,_desktop.Width,_desktop.Height);
            var moved=new Rect(Math.Clamp(r.X+x,limits.Left,Math.Max(limits.Left,limits.Right-r.Width)),Math.Clamp(r.Y+y,limits.Top,Math.Max(limits.Top,limits.Bottom-r.Height)),r.Width,r.Height);
            var delta=moved.TopLeft-r.TopLeft;foreach(var annotation in _annotations)annotation.Translate(delta);Selection=moved;
            InvalidateVisual();SelectionUpdated?.Invoke();
        }
        public void SetTool(AnnotationTool tool){_tool=tool;Cursor=tool==AnnotationTool.Select?Cursors.Cross:Cursors.Pen;InvalidateVisual();}
        public void SetColor(Color color)=>_color=color;
        public void SetStrokeWidth(double value)=>_strokeWidth=Math.Clamp(value,1,24);
        public void Undo(){if(_annotations.Count==0)return;_annotations.RemoveAt(_annotations.Count-1);InvalidateVisual();AnnotationsUpdated?.Invoke();}
        public DRect PixelRectangle() => new(_desktop.X+(int)Selection.X,_desktop.Y+(int)Selection.Y,Math.Max(1,(int)Selection.Width),Math.Max(1,(int)Selection.Height));
        public AnnotationDocument ExportDocument()
        {
            var crop=BitmapTools.Crop(_image??throw new InvalidOperationException("Для видео нет неподвижного кадра."),new System.Drawing.Rectangle((int)Selection.X,(int)Selection.Y,Math.Max(1,(int)Selection.Width),Math.Max(1,(int)Selection.Height)));
            var document=new AnnotationDocument(crop);
            foreach(var annotation in ExportAnnotations())document.Items.Add(annotation);
            return document;
        }
        public IReadOnlyList<AnnotationModel> ExportAnnotations()
        {
            return _annotations.Select(annotation=>
            {
                var copy=annotation.Clone();copy.Translate(new Vector(-Selection.X,-Selection.Y));return copy;
            }).ToList();
        }
        public BitmapSource ExportSelection()=>new AnnotationCanvas(ExportDocument()).Export();
        public void DrawVideoAnnotations(DrawingContext dc,Size size,DRect desktop)
        {
            dc.PushTransform(new ScaleTransform(size.Width/desktop.Width,size.Height/desktop.Height));
            dc.PushClip(new RectangleGeometry(Selection));
            foreach(var annotation in _annotations)DrawQuickAnnotation(dc,annotation);
            dc.Pop();dc.Pop();
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            double sx = ActualWidth / _desktop.Width, sy = ActualHeight / _desktop.Height;
            dc.PushTransform(new ScaleTransform(sx, sy));
            if(!_liveMode&&!_videoLive&&_image is not null)dc.DrawImage(_image,new Rect(0,0,_desktop.Width,_desktop.Height));
            var mask = new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(new Rect(0,0,_desktop.Width,_desktop.Height)),new RectangleGeometry(Selection));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(155,0,0,0)),null,mask);
            if(_liveMode&&_tool!=AnnotationTool.Select)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1,0,0,0)),null,Selection);
            if(_videoLive&&Selection.Width>0)
            {
                if(!_videoRecording||_tool!=AnnotationTool.Select)
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1,0,0,0)),null,Selection);
                dc.DrawRectangle(null,new Pen(new SolidColorBrush(Color.FromArgb(1,0,0,0)),18),Selection);
            }
            dc.DrawRectangle(null,new Pen(Brushes.DeepSkyBlue,2),Selection);
            if(_drawAnnotationsOnSurface)
                foreach(var annotation in _annotations)DrawQuickAnnotation(dc,annotation);
            if (Selection.Width > 0) foreach (var p in Handles()) dc.DrawRectangle(Brushes.White,new Pen(Brushes.DeepSkyBlue,1),new Rect(p.X-4,p.Y-4,8,8));
            var label = $"{(int)Selection.Width} × {(int)Selection.Height}";
            var ft = new FormattedText(label,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),16,Brushes.White,1);
            var pos = new Point(Math.Clamp(Selection.X, 10, Math.Max(10,_desktop.Width-ft.Width-20)),Math.Max(12,Selection.Y-40));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(13,17,23)),null,new Rect(pos.X-8,pos.Y-6,ft.Width+16,ft.Height+12),8,8);
            dc.DrawText(ft,pos); dc.Pop();
        }
        private static void DrawQuickAnnotation(DrawingContext dc,AnnotationModel annotation)
        {
            var pen=new Pen(new SolidColorBrush(annotation.Color),annotation.Thickness){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            if(annotation.Tool is AnnotationTool.Pen or AnnotationTool.Highlighter && annotation.Points.Count>1)
            {
                if(annotation.Tool==AnnotationTool.Highlighter){dc.PushOpacity(.35);pen.Thickness=Math.Max(12,annotation.Thickness*4);}
                var path=new StreamGeometry();using var context=path.Open();context.BeginFigure(annotation.Points[0],false,false);context.PolyLineTo(annotation.Points.Skip(1).ToArray(),true,false);dc.DrawGeometry(null,pen,path);
                if(annotation.Tool==AnnotationTool.Highlighter)dc.Pop();return;
            }
            if(annotation.Tool==AnnotationTool.Rectangle){dc.DrawRectangle(null,pen,annotation.Bounds);return;}
            if(annotation.Tool==AnnotationTool.Ellipse){var r=annotation.Bounds;dc.DrawEllipse(null,pen,new Point(r.X+r.Width/2,r.Y+r.Height/2),r.Width/2,r.Height/2);return;}
            if(annotation.Tool==AnnotationTool.Text)
            {
                var ft=new FormattedText(annotation.Text,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),annotation.FontSize,new SolidColorBrush(annotation.Color),1);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(180,13,17,23)),null,new Rect(annotation.Start.X-4,annotation.Start.Y-2,ft.Width+8,ft.Height+4),3,3);
                dc.DrawText(ft,annotation.Start);return;
            }
            dc.DrawLine(pen,annotation.Start,annotation.End);
            if(annotation.Tool==AnnotationTool.Arrow && (annotation.End-annotation.Start).Length>1)
            {var vector=annotation.End-annotation.Start;vector.Normalize();var side=new Vector(-vector.Y,vector.X);var size=Math.Max(12,annotation.Thickness*4);dc.DrawLine(pen,annotation.End,annotation.End-vector*size+side*size*.5);dc.DrawLine(pen,annotation.End,annotation.End-vector*size-side*size*.5);}
        }
    }
}
