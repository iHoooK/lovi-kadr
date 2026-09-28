using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PromptixCapture.Helpers;
using PromptixCapture.Models;
using PromptixCapture.Windows;

namespace PromptixCapture.Services;

public static class AppNotifications
{
    private static Func<GeneralSettings>? _settings;
    private static readonly List<Window> Visible=new();
    private static readonly SoundPlayer Chime=new(new MemoryStream(CreateChime()));

    public static void Configure(Func<GeneralSettings> settings)=>_settings=settings;

    public static void Show(string message,string? savedPath=null)
    {
        var app=Application.Current;
        if(app is null || _settings is null)return;
        if(!app.Dispatcher.CheckAccess()){app.Dispatcher.BeginInvoke(new Action(()=>Show(message,savedPath)));return;}
        var settings=_settings();
        if(settings.QuietMode)return;
        if(settings.ShowNotifications)
        {
            try{ShowWindow(message,savedPath);}catch(Exception ex){AppLog.Error("Notification window",ex);}
        }
        if(settings.PlaySounds)
        {
            try{Chime.Play();}catch(Exception ex){AppLog.Error("Notification sound",ex);}
        }
    }

    private static void ShowWindow(string message,string? savedPath)
    {
        var screen=System.Windows.Forms.Screen.FromPoint(NativeMethods.CursorPosition);
        var window=new Window
        {
            Title="ЛовиКадр",Width=350,Height=82,WindowStyle=WindowStyle.None,
            ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,
            ShowInTaskbar=false,ShowActivated=false,Topmost=true,Opacity=0
        };
        var card=new Border{CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Padding=new Thickness(14,10,14,10),Cursor=savedPath is null?System.Windows.Input.Cursors.Arrow:System.Windows.Input.Cursors.Hand};
        card.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty,"AccentBrush");
        var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var symbol=new TextBlock{Text="✓",FontSize=24,FontWeight=FontWeights.Bold,Width=36,VerticalAlignment=VerticalAlignment.Center};
        symbol.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");
        var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        var title=new TextBlock{Text=savedPath is null?"ЛОВИКАДР":"ЛОВИКАДР · ПОКАЗАТЬ В ПАПКЕ",FontSize=11,FontWeight=FontWeights.Bold};
        title.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");
        var detail=new TextBlock{Text=message,FontSize=13,TextWrapping=TextWrapping.Wrap,MaxWidth=270,MaxHeight=48};
        detail.SetResourceReference(TextBlock.ForegroundProperty,"TextPrimaryBrush");
        labels.Children.Add(title);labels.Children.Add(detail);
        row.Children.Add(symbol);row.Children.Add(labels);card.Child=row;window.Content=card;
        window.SourceInitialized+=(_,_)=>NativeMethods.ExcludeFromCapture(window);
        window.Closed+=(_,_)=>{Visible.Remove(window);Arrange(screen);};
        card.MouseLeftButtonDown+=(_,_)=>{window.Close();if(savedPath is not null)Ui.Reveal(savedPath);};
        if(Visible.Count>=3)Visible[0].Close();
        Visible.Add(window);window.Show();Arrange(screen);
        window.BeginAnimation(Window.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3.5)};
        timer.Tick+=(_,_)=>{timer.Stop();if(window.IsVisible)window.Close();};timer.Start();
    }

    private static void Arrange(System.Windows.Forms.Screen screen)
    {
        var work=screen.WorkingArea;
        for(int i=0;i<Visible.Count;i++)
        {
            var window=Visible[i];
            if(window.IsVisible)NativeMethods.PlacePixels(window,new System.Drawing.Rectangle(work.Right-366,work.Bottom-98-i*90,350,82));
        }
    }

    private static byte[] CreateChime()
    {
        const int rate=22050;
        const int samples=rate/4;
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);
        writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);
        writer.Write((short)2);writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);
        for(int i=0;i<samples;i++)
        {
            double t=(double)i/rate,fade=Math.Sin(Math.PI*i/samples);
            double tone=Math.Sin(2*Math.PI*659.25*t)*.65+Math.Sin(2*Math.PI*987.77*t)*.35;
            writer.Write((short)(Math.Clamp(tone*fade*4200,-32768,32767)));
        }
        return stream.ToArray();
    }
}
