using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using PromptixCapture.Models;
using PromptixCapture.Services;

namespace PromptixCapture.Windows;

public sealed partial class SettingsWindow : Window
{
    private AppSettings _draft;
    private string _savedTheme;
    private readonly Action<AppSettings> _save;
    private readonly Action? _openRecentScreenshots;
    private readonly List<Action> _readers=new();
    private readonly StackPanel _page=new(){Margin=new Thickness(26,12,26,20)};
    private readonly ListBox _nav=new(){Width=200,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Margin=new Thickness(12,18,8,12)};
    private int _current=-1;
    public SettingsWindow(AppSettings settings,Action<AppSettings> save,int tab=0,Action? openRecentScreenshots=null,Func<DownloadedInstaller,bool>? installUpdate=null)
    {
        Ui.ThemeWindow(this);
        _draft=LocalData.Clone(settings);_savedTheme=_draft.General.Theme;_save=save;_openRecentScreenshots=openRecentScreenshots;_installUpdate=installUpdate;
        _nav.SetResourceReference(Control.ForegroundProperty,"TextPrimaryBrush");
        Title="ЛовиКадр — настройки";Width=930;Height=720;MinWidth=760;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel();Content=root;
        var header=new StackPanel{Margin=new Thickness(24,18,20,4)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(Ui.Text("ЛОВИ-КАДР",24));header.Children.Add(Ui.Text("Снимки. Видео. Длинные страницы. Всё на вашем компьютере.",13,true));
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(16)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        footer.Children.Add(Ui.Button("По умолчанию",()=>{if(MessageBox.Show(this,"Сбросить все поля? Изменения сохранятся только после «Сохранить».","Настройки",MessageBoxButton.YesNo)==MessageBoxResult.Yes){_draft=AppSettings.CreateDefault();_readers.Clear();Build(_current);}}));
        footer.Children.Add(Ui.Button("Отмена",()=>{Ui.ApplyTheme(_savedTheme);Close();}));footer.Children.Add(Ui.Button("Сохранить",()=>
        {
            try{Read();SettingsService.Validate(_draft);ValidateHotkeys();_save(_draft);_savedTheme=_draft.General.Theme;Title="ЛовиКадр — настройки • сохранено";}catch(Exception ex){Ui.Error(ex);}
        }));
        foreach(var title in new[]{"Общие","Горячие клавиши","Скриншоты","Длинный снимок","Видео","О программе","О разработчике"})_nav.Items.Add(new ListBoxItem{Content=title,Padding=new Thickness(13,12,13,12),Margin=new Thickness(0,3,0,3)});
        DockPanel.SetDock(_nav,Dock.Left);root.Children.Add(_nav);
        root.Children.Add(new ScrollViewer{Content=_page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        _nav.SelectionChanged+=(_,_)=>
        {
            if(_nav.SelectedIndex==_current)return;
            try{Read();Build(_nav.SelectedIndex);}catch(Exception ex){Ui.Error(ex);_nav.SelectedIndex=_current;}
        };
        // Theme choice is previewed immediately, but closing without a later
        // save must leave the rest of the application on the saved theme.
        Closed+=(_,_)=>{Ui.ApplyTheme(_savedTheme);_updatesClosed=true;_updateCancellation?.Cancel();};
        _nav.SelectedIndex=tab;
    }
    private void Read(){foreach(var read in _readers)read();}
    private void ValidateHotkeys()
    {
        var seen=new HashSet<(uint,uint)>();
        foreach(var p in typeof(HotkeySettings).GetProperties())
        {var value=(string?)p.GetValue(_draft.Hotkeys);if(string.IsNullOrWhiteSpace(value))continue;var combination=HotkeyService.Parse(value);if(!seen.Add(combination))throw new ArgumentException("Комбинация назначена дважды: "+value);}
    }
    private void Build(int index)
    {
        _current=index;_readers.Clear();_page.Children.Clear();
        switch(index)
        {
            case 0:
                Heading("Общие настройки");
                Check("Запускать при входе в Windows",_draft.General.StartWithWindows,v=>_draft.General.StartWithWindows=v);
                Check("При обычном запуске скрываться в трей",_draft.General.StartMinimized,v=>_draft.General.StartMinimized=v);
                Check("Показывать уведомления",_draft.General.ShowNotifications,v=>_draft.General.ShowNotifications=v);
                Check("Звук уведомлений",_draft.General.PlaySounds,v=>_draft.General.PlaySounds=v);
                Check("Тихий режим (без звуков и уведомлений)",_draft.General.QuietMode,v=>_draft.General.QuietMode=v);
                Choice("Тема",new[]{"Системная","Светлая","Тёмная"},_draft.General.Theme switch{"Light"=>"Светлая","Dark"=>"Тёмная",_=>"Системная"},v=>_draft.General.Theme=v switch{"Светлая"=>"Light","Тёмная"=>"Dark",_=>"System"},()=>Ui.ApplyTheme(_draft.General.Theme));
                Choice("Левый щелчок по значку",new[]{"Быстрая панель","Снимок области","Настройки"},_draft.General.LeftClickAction switch{"Screenshot"=>"Снимок области","Settings"=>"Настройки",_=>"Быстрая панель"},v=>_draft.General.LeftClickAction=v switch{"Снимок области"=>"Screenshot","Настройки"=>"Settings",_=>"QuickPanel"});
                break;
            case 1:
                Heading("Горячие клавиши");Note("Щёлкните поле и нажмите нужное сочетание. Backspace / Delete очищает поле.");
                Note("Пока ЛовиКадр запущен и использует Print Screen, он временно отключает запуск Ножниц Windows этой кнопкой. При обычном выходе исходная системная настройка возвращается.");
                var names=new[]{"Область","Активное окно","Монитор под курсором","Весь рабочий стол","Длинный снимок / стоп","Видео / стоп","Пауза видео"};int i=0;
                foreach(var property in typeof(HotkeySettings).GetProperties())
                {
                    var p=property;var box=Input(names[i++],(string?)p.GetValue(_draft.Hotkeys)??"",v=>p.SetValue(_draft.Hotkeys,v));box.IsReadOnly=true;
                    box.PreviewKeyDown+=(_,e)=>
                    {
                        e.Handled=true;var k=e.Key==Key.System?e.SystemKey:e.Key;
                        if(k is Key.Back or Key.Delete){box.Text="";return;}
                        if(k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)return;
                        var m=Keyboard.Modifiers;box.Text=(m.HasFlag(ModifierKeys.Control)?"Ctrl+":"")+(m.HasFlag(ModifierKeys.Alt)?"Alt+":"")+(m.HasFlag(ModifierKeys.Shift)?"Shift+":"")+(m.HasFlag(ModifierKeys.Windows)?"Win+":"")+k;
                    };
                }break;
            case 2:
                Heading("Скриншоты");Folder("Папка",_draft.Screenshot.Folder,v=>_draft.Screenshot.Folder=v);
                Input("Шаблон имени",_draft.Screenshot.FileNameTemplate,v=>_draft.Screenshot.FileNameTemplate=v);
                Note("Токены: {yyyy-MM-dd}, {HH-mm-ss}, {fff}, {width}, {height}. При совпадении имени добавляется -2, -3…");
                Choice("Формат",Enum.GetValues<ImageFileFormat>(),_draft.Screenshot.Format,v=>_draft.Screenshot.Format=v);
                Number("Качество JPEG (40–100)",_draft.Screenshot.JpegQuality,v=>_draft.Screenshot.JpegQuality=v);
                Check("Копировать в буфер без редактора",_draft.Screenshot.CopyToClipboard,v=>_draft.Screenshot.CopyToClipboard=v);
                Check("Автосохранение (в том числе при копировании из редактора)",_draft.Screenshot.AutoSave,v=>_draft.Screenshot.AutoSave=v);
                Check("Открывать подробный редактор после снимка",_draft.Screenshot.OpenEditor,v=>_draft.Screenshot.OpenEditor=v);
                Check("Включать курсор в снимок",_draft.Screenshot.IncludeCursor,v=>_draft.Screenshot.IncludeCursor=v);
                Choice("Инструмент по умолчанию",Enum.GetValues<AnnotationTool>(),_draft.Screenshot.DefaultTool,v=>_draft.Screenshot.DefaultTool=v);
                _page.Children.Add(Ui.Button("Проверить имя",()=>{try{Read();MessageBox.Show(this,FileNames.Format(_draft.Screenshot.FileNameTemplate,DateTime.Now,1920,1080),"Пример имени");}catch(Exception ex){Ui.Error(ex);}}));
                if(_openRecentScreenshots is not null)_page.Children.Add(Ui.Button("Недавние снимки",_openRecentScreenshots,"Открыть последние сохранённые снимки"));
                break;
            case 3:
                Heading("Автоматический длинный снимок");
                Number("Задержка после шага, мс (150–2000)",_draft.Scrolling.SettleDelayMs,v=>_draft.Scrolling.SettleDelayMs=v);
                Number("Шаг колеса (120–960)",_draft.Scrolling.WheelDelta,v=>_draft.Scrolling.WheelDelta=v);
                Number("Максимальная высота (1000–100000 px)",_draft.Scrolling.MaxOutputHeight,v=>_draft.Scrolling.MaxOutputHeight=v);
                Note("Выделите область и нажмите «Начать захват». Пока курсор внутри рамки, программа прокручивает страницу. При выходе курсора прокрутка приостанавливается. «Стоп» сохранит снимок, даже если страница не прокручивалась. Для сложных страниц уменьшите шаг до 120.");break;
            case 4:
                Heading("Видео • MP4 / H.264");Folder("Папка",_draft.Video.Folder,v=>_draft.Video.Folder=v);
                Input("Шаблон имени",_draft.Video.FileNameTemplate,v=>_draft.Video.FileNameTemplate=v);
                Choice("Кадров в секунду",new[]{30,60},_draft.Video.FramesPerSecond,v=>_draft.Video.FramesPerSecond=v);
                Choice("Качество",Enum.GetValues<VideoQuality>(),_draft.Video.Quality,v=>_draft.Video.Quality=v);
                Check("Системный звук (основное устройство Windows)",_draft.Video.CaptureSystemAudio,v=>_draft.Video.CaptureSystemAudio=v);
                Check("Микрофон",_draft.Video.CaptureMicrophone,v=>_draft.Video.CaptureMicrophone=v);
                _page.Children.Add(Ui.Button("Выбрать микрофон…",ChooseMicrophone));
                Note(string.IsNullOrWhiteSpace(_draft.Video.MicrophoneDeviceName)?"Микрофон: устройство по умолчанию":"Микрофон: выбранное устройство");
                Check("Показывать курсор",_draft.Video.IncludeCursor,v=>_draft.Video.IncludeCursor=v);
                Check("Показать файл после записи",_draft.Video.OpenFolderAfterRecording,v=>_draft.Video.OpenFolderAfterRecording=v);
                Note("Запись начинается сразу после нажатия значка. Для H.264 нечётные размеры округляются вниз на 1 px.");break;
            case 5:
                Heading("О программе");
                Note("ЛовиКадр — локальная программа для Windows: снимайте область экрана, создавайте длинные снимки и записывайте видео. Добавляйте пометки к снимкам, копируйте их или сохраняйте на компьютер. Аккаунт и облако не требуются.");
                Note("Версия: "+App.DisplayVersion);
                Note("Снимки и видео сохраняются в выбранных вами папках. Настройки и история хранятся на этом компьютере. Код программы распространяется по лицензии MIT.");
                BuildUpdateSection();
                _page.Children.Add(Ui.Text("Диагностика",16));
                _page.Children.Add(Ui.Button("Открыть журнал",()=>{if(File.Exists(AppLog.PathName))Ui.Open(AppLog.PathName);else MessageBox.Show(this,"Журнал пока пуст.");}));
                _page.Children.Add(Ui.Button("Скопировать диагностику",()=>Clipboard.SetText($"ЛовиКадр {App.DisplayVersion}\nOS: {Environment.OSVersion}\n64-bit: {Environment.Is64BitProcess}\nMonitors: {System.Windows.Forms.Screen.AllScreens.Length}\n.NET: {Environment.Version}")));
                break;
            case 6:
                Heading("О разработчике");
                Note("Я Андрей, автор Promptix и ЛовиКадра. Разрабатываю программы, сайты и инструменты для авторов, а на YouTube показываю, как рождаются проекты.");
                _page.Children.Add(Ui.Text("Сайт и каналы",16));
                Link("Сайт Promptix","https://promptix.ru/");
                Link("YouTube · Promptix","https://www.youtube.com/@promptix");
                Link("Telegram · Promptix","https://t.me/promptix_ru");
                Link("Telegram · Кодовая Артель","https://t.me/codeartel");
                _page.Children.Add(Ui.Text("Поддержать разработку",16));
                Note("Если ЛовиКадр полезен, вы можете поддержать развитие проекта.");
                Link("DonationAlerts","https://www.donationalerts.com/r/promptix");
                Link("Boosty","https://boosty.to/promptix");
                break;
        }
    }
    private void Heading(string title)=>_page.Children.Add(Ui.Text(title,22));
    private void Note(string text)=>_page.Children.Add(Ui.Text(text,13,true));
    private void Link(string label,string address)
    {
        var line=new TextBlock{Margin=new Thickness(3,4,3,8),TextWrapping=TextWrapping.Wrap};
        var link=new Hyperlink(new Run(label+" ↗")){NavigateUri=new Uri(address)};
        link.SetResourceReference(TextElement.ForegroundProperty,"AccentBrush");
        link.RequestNavigate+=(_,e)=>{Ui.Open(e.Uri.ToString());e.Handled=true;};
        line.Inlines.Add(link);_page.Children.Add(line);
    }
    private TextBox Input(string label,string initial,Action<string> setter)
    {
        _page.Children.Add(Ui.Text(label,13));var box=new TextBox{Text=initial,Margin=new Thickness(3,0,3,8)};_page.Children.Add(box);_readers.Add(()=>setter(box.Text.Trim()));return box;
    }
    private void Number(string label,int value,Action<int> setter)=>Input(label,value.ToString(),s=>{if(!int.TryParse(s,out var n))throw new ArgumentException(label+": введите целое число.");setter(n);});
    private void Check(string label,bool value,Action<bool> setter){var c=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(3,9,3,9)};_page.Children.Add(c);_readers.Add(()=>setter(c.IsChecked==true));}
    private void Choice<T>(string label,IEnumerable<T> values,T current,Action<T> setter,Action? changed=null)
    {
        _page.Children.Add(Ui.Text(label,13));var c=new ComboBox{ItemsSource=values.ToArray(),SelectedItem=current,Margin=new Thickness(3,0,3,8)};_page.Children.Add(c);
        void ReadChoice(){if(c.SelectedItem is T v)setter(v);}
        _readers.Add(ReadChoice);
        if(changed is not null)c.SelectionChanged+=(_,_)=>{ReadChoice();changed();};
    }
    private void Folder(string label,string path,Action<string> setter)
    {
        var box=Input(label,path,setter);_page.Children.Add(Ui.Button("Обзор…",()=>{using var d=new System.Windows.Forms.FolderBrowserDialog{SelectedPath=box.Text,ShowNewFolderButton=true};if(d.ShowDialog()==System.Windows.Forms.DialogResult.OK)box.Text=d.SelectedPath;}));
    }
    private void ChooseMicrophone()
    {
        try
        {
            Read();var devices=VideoRecorderService.Microphones();devices.Insert(0,("","По умолчанию"));
            var dialog=new Window{Owner=this,Title="Микрофон",Width=490,Height=180,WindowStartupLocation=WindowStartupLocation.CenterOwner};Ui.ThemeWindow(dialog);
            var panel=new StackPanel{Margin=new Thickness(18)};var box=new ComboBox{ItemsSource=devices.Select(x=>x.Name).ToArray(),SelectedIndex=Math.Max(0,devices.FindIndex(x=>x.Id==(_draft.Video.MicrophoneDeviceName??"")))};
            panel.Children.Add(box);panel.Children.Add(Ui.Button("Выбрать",()=>dialog.DialogResult=true));dialog.Content=panel;
            if(dialog.ShowDialog()==true && box.SelectedIndex>=0){_draft.Video.MicrophoneDeviceName=devices[box.SelectedIndex].Id;Build(4);}
        }
        catch(Exception ex){Ui.Error(ex);}
    }
}
