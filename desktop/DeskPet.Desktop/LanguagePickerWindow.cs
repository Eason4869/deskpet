using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Dafeiyu;
namespace DeskPet.Desktop;

internal sealed class LanguagePickerWindow : Window
{
    private readonly ListBox choices;
    internal string SelectedLanguage => (string)((ListBoxItem)choices.SelectedItem!).Tag!;
    internal LanguagePickerWindow(string selected,Action<string> save)
    {
        Name="LanguagePickerWindow";Title=TextResources.Get("menus.language");Width=640;Height=560;MinWidth=320;MinHeight=280;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),Margin=new Thickness(24)};
        body.Children.Add(Ui.Column(Ui.Text(TextResources.Get("menus.language"),24),Ui.Text(TextResources.Get("languages.restartRequired"),12)));
        choices=new ListBox{Name="LanguageChoiceList",Background=Brushes.Transparent,BorderThickness=new Thickness(0),Margin=new Thickness(0,18,0,0)};
        ScrollViewer.SetVerticalScrollBarVisibility(choices,ScrollBarVisibility.Auto);ScrollViewer.SetHorizontalScrollBarVisibility(choices,ScrollBarVisibility.Disabled);
        choices.ItemsPanel=new Avalonia.Controls.Templates.FuncTemplate<Panel?>(()=>new WrapPanel{Orientation=Orientation.Horizontal});
        var items=UiLanguages.Choices.Select(choice=>new ListBoxItem{Name=choice.MenuName,Tag=choice.Code,Content=Ui.Text(choice.NativeName,15),MinHeight=54,Padding=new Thickness(12),Margin=new Thickness(0,0,8,8),HorizontalContentAlignment=HorizontalAlignment.Stretch}).ToArray();
        choices.ItemsSource=items;choices.SelectedItem=items.Single(item=>(string)item.Tag! == UiLanguages.Normalize(selected));
        choices.SizeChanged+=(_,_)=>{int columns=choices.Bounds.Width>=500?2:1;foreach(var item in items)item.Width=Math.Max(100,(choices.Bounds.Width-20)/columns-8);};
        Grid.SetRow(choices,1);body.Children.Add(choices);
        var notice=Ui.Text("",12);notice.Name="LanguageSelectionNotice";
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=10};
        var cancel=Ui.Button(TextResources.Get("common.cancel"),Close);cancel.Name="CancelLanguageSelection";cancel.IsCancel=true;actions.Children.Add(cancel);
        var confirm=Ui.Button(TextResources.Get("common.save"),()=>{try{save(SelectedLanguage);Close();}catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){notice.Text=TextResources.Get("petWindow.saveSettings.notice");}});confirm.Name="SaveLanguageSelection";confirm.IsDefault=true;actions.Children.Add(confirm);
        var footer=Ui.Column(notice,actions);footer.Margin=new Thickness(0,16,0,0);Grid.SetRow(footer,2);body.Children.Add(footer);Content=body;
        confirm.IsEnabled=choices.SelectedItem!=null;choices.SelectionChanged+=(_,_)=>confirm.IsEnabled=choices.SelectedItem!=null;
        Opened+=(_,_)=>{choices.ScrollIntoView(choices.SelectedItem!);choices.Focus();};
    }
}
