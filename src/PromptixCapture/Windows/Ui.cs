using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PromptixCapture.Windows;

internal static class Ui
{
    internal static readonly Brush Cyan=new SolidColorBrush(Color.FromRgb(53,208,255));
    internal static readonly SolidColorBrush TextPrimary=new(Color.FromRgb(24,34,48));
    internal static readonly SolidColorBrush Muted=new(Color.FromRgb(82,102,127));
    internal static void ApplyTheme(string preference)
    {
        bool dark=preference=="Dark" || (preference=="System" && SystemPrefersDark());
        var colors=dark
            ? new Dictionary<string,Color>{{"Graphite900Brush",Color.FromRgb(13,17,23)},{"Graphite800Brush",Color.FromRgb(21,27,35)},{"Graphite700Brush",Color.FromRgb(32,41,54)},{"Graphite600Brush",Color.FromRgb(52,65,84)},{"TextPrimaryBrush",Color.FromRgb(244,247,251)},{"TextSecondaryBrush",Color.FromRgb(169,182,199)},{"ButtonTextBrush",Color.FromRgb(244,247,251)}}
            : new Dictionary<string,Color>{{"Graphite900Brush",Color.FromRgb(247,249,252)},{"Graphite800Brush",Colors.White},{"Graphite700Brush",Color.FromRgb(32,41,54)},{"Graphite600Brush",Color.FromRgb(52,65,84)},{"TextPrimaryBrush",Color.FromRgb(24,34,48)},{"TextSecondaryBrush",Color.FromRgb(82,102,127)},{"ButtonTextBrush",Color.FromRgb(244,247,251)}};
        // Brushes declared in XAML may be frozen once WPF seals the resource/style
        // graph. Updating Color on such a brush throws "read-only state" during
        // startup. Replacing the resource is safe and lets DynamicResource users
        // pick up the new value.
        foreach(var pair in colors)Application.Current.Resources[pair.Key]=new SolidColorBrush(pair.Value);
        TextPrimary.Color=colors["TextPrimaryBrush"];Muted.Color=colors["TextSecondaryBrush"];
    }
    private static bool SystemPrefersDark()
    {
        try{return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int value && value==0;}
        catch{return false;}
    }
    internal static Button Button(string text,Action action,string? tip=null)
    {
        var button=new Button{Content=text,Margin=new Thickness(3),MinHeight=36,ToolTip=tip??text};button.Click+=(_,_)=>action();return button;
    }
    internal static Button IconButton(string icon,Action action,string tip)
    {
        var button=new Button{Content=new TextBlock{Text=icon,FontSize=20,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center},
            Width=42,Height=38,MinHeight=38,Padding=new Thickness(0),Margin=new Thickness(3),ToolTip=tip};
        button.Click+=(_,_)=>action();return button;
    }
    internal static Button AsyncButton(string text,Func<Task> action,string? tip=null)
    {
        var button=new Button{Content=text,Margin=new Thickness(3),MinHeight=36,ToolTip=tip??text};
        button.Click+=async(_,_)=>{button.IsEnabled=false;try{await action();}catch(Exception ex){Error(ex);}finally{button.IsEnabled=true;}};return button;
    }
    internal static TextBlock Text(string text,double size=14,bool muted=false)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,6,3,6),Foreground=muted?Muted:TextPrimary};
    internal static void Error(Exception ex){Services.AppLog.Error("User operation",ex);MessageBox.Show(ex.Message,"ЛовиКадр",MessageBoxButton.OK,MessageBoxImage.Warning);}
    internal static void Open(string path)
    {try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){Error(ex);}}
    internal static void Reveal(string path)
    {try{Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{path}\""){UseShellExecute=true});}catch(Exception ex){Error(ex);}}
    internal static string? Prompt(Window? owner,string title,string label,string initial)
    {
        var dialog=new Window{Title=title,Width=440,Height=285,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=owner,ResizeMode=ResizeMode.NoResize};
        var panel=new StackPanel{Margin=new Thickness(18)};var input=new TextBox{Text=initial,Height=120,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text(label));panel.Children.Add(input);var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(Button("Отмена",()=>dialog.DialogResult=false));actions.Children.Add(Button("Готово",()=>dialog.DialogResult=true));panel.Children.Add(actions);dialog.Content=panel;
        dialog.Loaded+=(_,_)=>input.Focus();return dialog.ShowDialog()==true?input.Text:null;
    }
}
