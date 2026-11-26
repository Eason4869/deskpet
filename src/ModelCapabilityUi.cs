using System;
using System.Windows.Controls;
namespace Dafeiyu
{
    public sealed partial class TextProviderEditor
    {
        ComboBox toolSupport,imageSupport;
        void AddCapabilityFields()
        {
            toolSupport=CapabilityField("TextModelToolSupport");imageSupport=CapabilityField("TextModelImageSupport");
            modelForm.Children.Add(Ui.SettingRow(()=>TextResources.Get("modelCapabilityUi.addCapabilityFields.text"),null,toolSupport,alignRight:true,stackBelow:350));
            modelForm.Children.Add(Ui.SettingRow(()=>TextResources.Get("modelCapabilityUi.addCapabilityFields.text.alternativeA"),null,imageSupport,alignRight:true,stackBelow:350));
            var help=LocalizedUi.Text(()=>(TextResources.Get("modelCapabilityUi.addCapabilityFields.text.alternativeB")),11,Ui.Muted);help.TextWrapping=System.Windows.TextWrapping.Wrap;modelForm.Children.Add(help);
        }
        static ComboBox CapabilityField(string name){return LocalizedUi.Options(new ComboBox{Name=name,Width=180,ItemsSource=new[]{TextResources.Get("modelCapabilityUi.capabilityField.itemsSource"),TextResources.Get("modelCapabilityUi.capabilityField.itemsSource.alternativeA"),TextResources.Get("modelCapabilityUi.capabilityField.itemsSource.alternativeB")},SelectedIndex=0,MinHeight=32},()=>(new[]{TextResources.Get("modelCapabilityUi.capabilityField.itemsSource"),TextResources.Get("modelCapabilityUi.capabilityField.itemsSource.alternativeA"),TextResources.Get("modelCapabilityUi.capabilityField.itemsSource.alternativeB")}));}
        void RefreshCapabilityFields()
        {if(toolSupport==null || selected==null)return;var caps=selected.Capabilities(newModel.Text.Trim());toolSupport.SelectedIndex=(int)caps.Tools;imageSupport.SelectedIndex=(int)caps.Images;}
        void SaveCapabilityFields(string id)
        {if(toolSupport==null)return;selected.ModelCapabilities[id]=new ModelCapabilitySetting{Tools=(CapabilitySupport)Math.Max(0,toolSupport.SelectedIndex),Images=(CapabilitySupport)Math.Max(0,imageSupport.SelectedIndex)};selected.ApplyModelCapabilities(selected.Connection);}
    }
}
