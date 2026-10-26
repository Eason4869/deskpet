using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Dafeiyu
{
    // One visual contract for the four settings destinations, including live
    // theme brushes. Page bodies keep their existing controls and drafts.
    internal sealed class SettingsPageHeader:StackPanel
    {
        internal readonly TextBlock Title,Description;
        internal readonly Button[] Tabs;
        internal int SelectedIndex{get;private set;}
        internal event Action<int> SelectionChanged;
        internal SettingsPageHeader(string title,string description,string[] labels,string[] names)
        {
            if(labels==null||names==null||labels.Length==0||labels.Length!=names.Length)throw new ArgumentException(TextResources.Get("settingsPageHeader.settingsPageHeader.text"));
            Name="SettingsPageHeader";Title=Ui.PageHeading(title);Title.Name="SettingsPageTitle";Children.Add(Title);
            Description=Ui.Text(description??"",12,Ui.Muted);Description.Name="SettingsPageDescription";Description.MinHeight=20;Description.Margin=new Thickness(0,4,0,18);Children.Add(Description);
            Tabs=new Button[labels.Length];var strip=new UniformGrid{Rows=1,Columns=labels.Length};
            for(int i=0;i<labels.Length;i++)
            {
                int index=i;var button=Ui.Button("");button.Name=names[i];button.Content=new TextBlock{Text=labels[i],TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};button.FontSize=12;button.MinHeight=38;button.Padding=new Thickness(5,7,5,7);button.Margin=new Thickness(2);
                System.Windows.Automation.AutomationProperties.SetName(button,labels[i]);button.Click+=delegate{Select(index);};Tabs[i]=button;strip.Children.Add(button);
            }
            Children.Add(new Border{Name="SettingsPageTabs",Child=strip,Background=Ui.Pale,BorderBrush=Ui.Brush("#E0E9FA"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(4)});Select(0);
        }
        internal SettingsPageHeader(Func<string> title,Func<string> description,Func<string[]> labels,string[] names):this(title(),description(),labels(),names)
        {
            LocalizedUi.Set(Title,"Text",title);LocalizedUi.Set(Description,"Text",description);
            for(int i=0;i<Tabs.Length;i++){int index=i;LocalizedUi.Set((TextBlock)Tabs[i].Content,"Text",()=>labels()[index]);LocalizedUi.Automation(Tabs[i],()=>labels()[index]);}
        }
        internal void Select(int index)
        {
            if(index<0||index>=Tabs.Length)throw new ArgumentOutOfRangeException("index");SelectedIndex=index;
            for(int i=0;i<Tabs.Length;i++)
            {
                bool selected=i==index;Tabs[i].Background=selected?ThemeManager.Surface:Brushes.Transparent;Tabs[i].BorderBrush=selected?Ui.Line:Brushes.Transparent;Tabs[i].Foreground=selected?ThemeManager.Brush("#456DE6","#91B5FF"):Ui.Muted;Tabs[i].FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;
                LocalizedUi.HelpText(Tabs[i],()=>selected?TextResources.Get("settingsPageHeader.select.automationAutomationPropertiesSetHelpText"):TextResources.Get("settingsPageHeader.select.automationAutomationPropertiesSetHelpText.alternativeA"));
            }
            if(SelectionChanged!=null)SelectionChanged(index);
        }
    }
}
