using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PromptixCapture.Helpers;
using PromptixCapture.Models;
using PromptixCapture.Services;

namespace PromptixCapture.Windows;

public sealed class HistoryWindow : Window
{
    public HistoryWindow(HistoryService history)
    {
        Title="ЛовиКадр — история";Width=880;Height=610;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel{Margin=new Thickness(16)};Content=root;
        var heading=Ui.Text("ЛОКАЛЬНАЯ ИСТОРИЯ",22);DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var list=new ListBox{Background=System.Windows.Media.Brushes.Transparent,BorderThickness=new Thickness(0)};
        var actions=new WrapPanel{Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);root.Children.Add(list);
        HistoryItem? Selected()=> (list.SelectedItem as ListBoxItem)?.Tag as HistoryItem;
        void Refresh()
        {
            list.Items.Clear();
            foreach(var item in history.Items)
            {
                var row=new StackPanel{Orientation=Orientation.Horizontal};
                var icon=new System.Windows.Controls.Image{Width=80,Height=50,Margin=new Thickness(4,2,12,2)};
                if(item.Type!=HistoryMediaType.Video && item.Exists)
                {
                    try{using var stream=File.OpenRead(item.Path);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelHeight=50;image.StreamSource=stream;image.EndInit();image.Freeze();icon.Source=image;}catch{ }
                }
                row.Children.Add(icon);var texts=new StackPanel();texts.Children.Add(Ui.Text(item.FileName,15));texts.Children.Add(Ui.Text($"{item.CreatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss} • {item.Details}{(item.Exists?"":" • файл отсутствует")}",12,true));row.Children.Add(texts);
                list.Items.Add(new ListBoxItem{Content=row,Tag=item,Padding=new Thickness(6),HorizontalContentAlignment=HorizontalAlignment.Stretch});
            }
        }
        actions.Children.Add(Ui.Button("Открыть",()=>{if(Selected() is {} i)Ui.Open(i.Path);}));
        actions.Children.Add(Ui.Button("Показать в папке",()=>{if(Selected() is {} i)Ui.Reveal(i.Path);}));
        actions.Children.Add(Ui.AsyncButton("Копировать",async()=>{if(Selected() is {} i && i.Type!=HistoryMediaType.Video)await ImageExportService.CopyAsync(BitmapTools.Load(i.Path));}));
        actions.Children.Add(Ui.Button("Убрать запись",()=>{if(Selected() is {} i){history.Remove(i);Refresh();}}));
        actions.Children.Add(Ui.Button("В корзину",()=>
        {
            if(Selected() is not {} i)return;
            if(MessageBox.Show(this,"Переместить этот файл в корзину?\n"+i.FileName,"Удаление",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            try{if(i.Exists)Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(i.Path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);history.Remove(i);Refresh();}catch(Exception ex){Ui.Error(ex);}
        }));
        actions.Children.Add(Ui.Button("Убрать отсутствующие",()=>{history.Cleanup();Refresh();}));
        list.MouseDoubleClick+=(_,_)=>{if(Selected() is {} i)Ui.Open(i.Path);};Refresh();
    }
}
