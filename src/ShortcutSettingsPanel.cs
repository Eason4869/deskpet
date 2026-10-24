using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    public sealed class ShortcutSettingsPanel : ScrollViewer
    {
        readonly PetWindow pet;
        readonly Dictionary<string,TextBox> fields=new Dictionary<string,TextBox>();
        readonly Dictionary<string,string> baselines=new Dictionary<string,string>();
        readonly TextBlock notice;
        public bool HasUnsavedChanges {get{return ShortcutCatalog.All.Any(d=>fields[d.Id].Text!=baselines[d.Id]&&!SameValue(fields[d.Id].Text,ShortcutCatalog.Value(pet.Settings,d)));}}
        static bool SameValue(string left,string right){return left==right||(String.IsNullOrWhiteSpace(left)&&String.IsNullOrWhiteSpace(right))||ShortcutCatalog.Equal(left,right);}
        public ShortcutSettingsPanel(PetWindow owner)
        {
            pet=owner;VerticalScrollBarVisibility=ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
            var root=new StackPanel{Margin=new Thickness(0,16,0,0)};
            root.Children.Add(Ui.Text(TextResources.Get("shortcutSettingsPanel.shortcutSettingsPanel.text"),17));
            root.Children.Add(Ui.Text(TextResources.Get("shortcutSettingsPanel.shortcutSettingsPanel.text.alternativeA"),12,Ui.Muted));
            notice=Ui.Text(pet.HotkeyWarning??"",12,Ui.Muted);notice.Name="ShortcutNotice";notice.Margin=new Thickness(0,12,0,0);root.Children.Add(notice);
            foreach(var group in ShortcutCatalog.All.GroupBy(d=>d.Group))
            {
                var panel=new StackPanel();panel.Children.Add(Ui.Text(group.Key=="全局"?TextResources.Get("shortcutSettingsPanel.shortcutSettingsPanel.text.alternativeB"):TextResources.Format("shortcutSettingsPanel.shortcutSettingsPanel.template",ShortcutCatalog.GroupTitle(group.Key)),15));
                foreach(var definition in group)AddShortcut(panel,definition);
                root.Children.Add(GlassSurface.Card(panel));
            }
            var reference=new StackPanel();reference.Children.Add(Ui.Text(TextResources.Get("shortcutSettingsPanel.shortcutSettingsPanel.text.alternativeC"),15));
            reference.Children.Add(Ui.Text(TextResources.Get("shortcutSettingsPanel.shortcutSettingsPanel.text.alternativeD"),12,Ui.Muted));
            reference.Children.Add(Ui.Text(TextResources.Get("shortcutSettingsPanel.shortcutSettingsPanel.text.alternativeE"),12,Ui.Muted));
            reference.Children.Add(Ui.Text(TextResources.Get("shortcutSettingsPanel.shortcutSettingsPanel.text.alternativeF"),12,Ui.Muted));
            root.Children.Add(GlassSurface.Card(reference));Content=root;
        }
        void AddShortcut(Panel panel,ShortcutDefinition definition)
        {
            var row=new Grid{Name="GuideShortcut"+definition.Id,Margin=new Thickness(0,12,0,0)};
            row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var title=Ui.Text(definition.Title,12);title.Margin=new Thickness(0,0,0,6);Grid.SetColumnSpan(title,3);row.Children.Add(title);
            var field=Ui.Field(ShortcutCatalog.Label(ShortcutCatalog.Value(pet.Settings,definition)));field.Name="Hotkey"+definition.Id;field.MaxLength=64;field.MinWidth=120;fields[definition.Id]=field;baselines[definition.Id]=field.Text;Grid.SetRow(field,1);row.Children.Add(field);
            System.Windows.Automation.AutomationProperties.SetName(field,TextResources.Format("shortcutSettingsPanel.addShortcut.automationAutomationPropertiesSetName",definition.Title));
            var save=Ui.Button(TextResources.Get("common.save"));save.Name="SaveShortcut"+definition.Id;save.Margin=new Thickness(8,0,0,0);Grid.SetRow(save,1);Grid.SetColumn(save,1);row.Children.Add(save);
            var reset=Ui.Button(TextResources.Get("shortcutSettingsPanel.addShortcut.text"));reset.Name="ResetShortcut"+definition.Id;reset.Margin=new Thickness(6,0,0,0);Grid.SetRow(reset,1);Grid.SetColumn(reset,2);row.Children.Add(reset);
            save.Click+=delegate{Save(definition,field.Text);};reset.Click+=delegate{Save(definition,definition.Default);};panel.Children.Add(row);
        }
        void Save(ShortcutDefinition definition,string value)
        {
            string error;if(!pet.UpdateHotkey(definition.Id,value,out error)){notice.Text=error;notice.BringIntoView();return;}
            fields[definition.Id].Text=ShortcutCatalog.Label(ShortcutCatalog.Value(pet.Settings,definition));baselines[definition.Id]=fields[definition.Id].Text;notice.Text=definition.Title+"："+(String.IsNullOrWhiteSpace(value)?TextResources.Get("shortcutSettingsPanel.save.noticeDefinitionTitle"):TextResources.Get("shortcutSettingsPanel.save.noticeDefinitionTitle.alternativeA"));
        }
        public void RefreshState()
        {
            foreach(var definition in ShortcutCatalog.All){string saved=ShortcutCatalog.Label(ShortcutCatalog.Value(pet.Settings,definition));if(fields[definition.Id].Text==baselines[definition.Id]||SameValue(fields[definition.Id].Text,saved)){fields[definition.Id].Text=saved;baselines[definition.Id]=saved;}}
            notice.Text=pet.HotkeyWarning??"";
        }
        internal void DiscardPreferenceDrafts()
        {
            foreach(var definition in ShortcutCatalog.All){string saved=ShortcutCatalog.Label(ShortcutCatalog.Value(pet.Settings,definition));fields[definition.Id].Text=baselines[definition.Id]=saved;}
            notice.Text=pet.HotkeyWarning??"";
        }
    }
}
