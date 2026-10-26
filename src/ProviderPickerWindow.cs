using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    public sealed class ProviderPickerWindow : Window
    {
        public ServicePreset SelectedProvider {get;private set;}
        public ImageServicePreset SelectedImageProvider {get;private set;}
        public ProviderPickerWindow(Window owner,string kind="text")
        {
            LocalizedUi.Window(this,()=>(TextResources.Get("providerPickerWindow.providerPickerWindow.window")),520,kind=="text"?550:kind=="image"?440:300);Owner=owner;ShowInTaskbar=false;
            WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
            var layout=new DockPanel{Margin=new Thickness(20)};
            var heading=new StackPanel{Margin=new Thickness(0,0,0,14)};
            var title=LocalizedUi.Text(()=>(TextResources.Get("providerPickerWindow.providerPickerWindow.text")),19);title.FontWeight=FontWeights.SemiBold;heading.Children.Add(title);
            heading.Children.Add(LocalizedUi.Text(()=>(kind=="text"?TextResources.Get("providerPickerWindow.providerPickerWindow.text.alternativeA"):TextResources.Get("providerPickerWindow.providerPickerWindow.text.alternativeB")),12,Ui.Muted));DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
            var cancel=LocalizedUi.Button(()=>(TextResources.Get("common.cancel")));cancel.IsCancel=true;cancel.HorizontalAlignment=HorizontalAlignment.Right;cancel.Margin=new Thickness(0,12,0,0);cancel.Click+=delegate{DialogResult=false;};DockPanel.SetDock(cancel,Dock.Bottom);layout.Children.Add(cancel);
            var choices=new WrapPanel();var seen=new HashSet<string>();
            IList<ServicePreset> catalog=ServicePresets.For(kind);var imagePresets=new Dictionary<ServicePreset,ImageServicePreset>();
            if(kind=="image"){catalog=new List<ServicePreset>();foreach(var image in ImageServicePresets.All){var item=new ServicePreset(image.Name,image.BaseUrl,image.Model,image.Protocol,image.Notes);catalog.Add(item);imagePresets[item]=image;}}
            foreach(var preset in catalog)
            {
                string brand=kind=="image"?imagePresets[preset].Id:ProviderIdentity.Brand(preset.Url);if(!seen.Add(brand))continue;
                var config=new ConnectionSettings{BaseUrl=preset.Url};string name=kind=="image"?preset.Name:ProviderIdentity.DisplayName(ProviderIdentity.Name(config),ProviderIdentity.Brand(config.BaseUrl));
                var tile=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};tile.Children.Add(ProviderLogo.Create(preset.Url,name,29,kind=="image"&&brand=="minimax"?"minimax":null));
                var label=Ui.Text(name,12);label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxWidth=116;label.Margin=new Thickness(0,7,0,0);label.HorizontalAlignment=HorizontalAlignment.Center;label.ToolTip=name;tile.Children.Add(label);
                var choice=Ui.Button("");choice.Name="ChooseProvider_"+(brand.Length==0?"custom":brand);choice.Content=tile;choice.Width=132;choice.Height=76;choice.Margin=new Thickness(4);LocalizedUi.Set(choice,"ToolTip",()=>(preset.Url.Length==0?TextResources.Get("providerPickerWindow.providerPickerWindow.lengthPresetUrl"):preset.Url));
                choice.Click+=delegate{SelectedProvider=preset;if(kind=="image")SelectedImageProvider=imagePresets[preset];DialogResult=true;};choices.Children.Add(choice);
            }
            layout.Children.Add(Ui.Scroll(choices));Content=layout;
        }
    }
}
