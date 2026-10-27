using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed class LanguagePickerWindow : Window
    {
        readonly ListBox choices;
        readonly TextBlock notice;
        readonly Action<string> save;
        internal string SelectedLanguage {get{return (string)((ListBoxItem)choices.SelectedItem).Tag;}}
        internal LanguagePickerWindow(string selected,Action<string> saveSelection)
        {
            Name="LanguagePickerWindow";save=saveSelection;
            LocalizedUi.Window(this,()=>(TextResources.Get("menus.language")),640,560);
            MinWidth=320;MinHeight=280;
            Width=Math.Max(320,Math.Min(640,SystemParameters.WorkArea.Width-32));Height=Math.Max(280,Math.Min(560,SystemParameters.WorkArea.Height-32));
            var body=new Grid{Margin=new Thickness(24)};
            body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition());body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var heading=new StackPanel{Margin=new Thickness(0,0,0,18)};var title=LocalizedUi.Text(()=>(TextResources.Get("menus.language")),24);title.FontWeight=FontWeights.SemiBold;heading.Children.Add(title);body.Children.Add(heading);
            choices=new ListBox{Name="LanguageChoiceList",BorderThickness=new Thickness(0),Background=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(0)};
            ScrollViewer.SetVerticalScrollBarVisibility(choices,ScrollBarVisibility.Auto);ScrollViewer.SetHorizontalScrollBarVisibility(choices,ScrollBarVisibility.Disabled);
            var panel=new FrameworkElementFactory(typeof(WrapPanel));panel.SetValue(WrapPanel.OrientationProperty,Orientation.Horizontal);choices.ItemsPanel=new ItemsPanelTemplate(panel);
            foreach(var choice in UiLanguages.Choices)
            {
                var label=Ui.Text(()=>(choice.NativeName),15);label.Margin=new Thickness(10,6,10,6);label.TextWrapping=TextWrapping.Wrap;
                var item=new ListBoxItem{Name=choice.MenuName,Tag=choice.Code,Content=label,MinHeight=54,Margin=new Thickness(0,0,8,8),Padding=new Thickness(4),HorizontalContentAlignment=HorizontalAlignment.Stretch,Style=Ui.NavigationItemStyle};
                System.Windows.Automation.AutomationProperties.SetName(item,choice.NativeName);choices.Items.Add(item);
            }
            choices.SelectedItem=choices.Items.OfType<ListBoxItem>().Single(item=>(string)item.Tag==UiLanguages.Normalize(selected));
            choices.SizeChanged+=delegate{int columns=choices.ActualWidth>=500?2:1;foreach(ListBoxItem item in choices.Items)item.Width=Math.Max(100,(choices.ActualWidth-20)/columns-8);};
            Grid.SetRow(choices,1);body.Children.Add(choices);
            var footer=new StackPanel{Margin=new Thickness(0,16,0,0)};notice=Ui.Text("",12,Ui.Muted);notice.Name="LanguageSelectionNotice";footer.Children.Add(notice);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            var cancel=LocalizedUi.Button(()=>(TextResources.Get("common.cancel")));cancel.Name="CancelLanguageSelection";cancel.IsCancel=true;cancel.Click+=delegate{Close();};actions.Children.Add(cancel);
            var confirm=LocalizedUi.Button(()=>(TextResources.Get("common.save")));confirm.Name="SaveLanguageSelection";confirm.IsDefault=true;confirm.Margin=new Thickness(10,0,0,0);confirm.Click+=delegate{try{save(SelectedLanguage);Close();}catch(Exception error){if(!(error is System.IO.IOException||error is UnauthorizedAccessException||error is System.Security.SecurityException))throw;LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("petWindow.saveSettings.notice")));}};actions.Children.Add(confirm);footer.Children.Add(actions);Grid.SetRow(footer,2);body.Children.Add(footer);
            confirm.IsEnabled=choices.SelectedItem!=null;choices.SelectionChanged+=delegate{confirm.IsEnabled=choices.SelectedItem!=null;};
            DialogUi.Apply(this,body);Loaded+=delegate{choices.ScrollIntoView(choices.SelectedItem);choices.Focus();};
        }
    }
}
