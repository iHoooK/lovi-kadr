using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PromptixCapture.Windows;

internal static class Ui
{
    internal static readonly Brush Cyan=new SolidColorBrush(Color.FromRgb(53,208,255));
    internal static readonly Brush TextPrimary=new SolidColorBrush(Color.FromRgb(24,34,48));
    internal static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(82,102,127));
    internal static Button Button(string text,Action action,string? tip=null)
    {
        var button=new Button{Content=text,Margin=new Thickness(3),MinHeight=36,ToolTip=tip??text};button.Click+=(_,_)=>action();return button;
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
