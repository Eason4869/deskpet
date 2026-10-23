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
            Ui.Label(modelForm,TextResources.Get("modelCapabilityUi.addCapabilityFields.text"),toolSupport);Ui.Label(modelForm,TextResources.Get("modelCapabilityUi.addCapabilityFields.text.alternativeA"),imageSupport);
            var help=Ui.Text(TextResources.Get("modelCapabilityUi.addCapabilityFields.text.alternativeB"),11,Ui.Muted);help.TextWrapping=System.Windows.TextWrapping.Wrap;modelForm.Children.Add(help);
        }
        static ComboBox CapabilityField(string name){return new ComboBox{Name=name,ItemsSource=new[]{TextResources.Get("modelCapabilityUi.capabilityField.itemsSource"),TextResources.Get("modelCapabilityUi.capabilityField.itemsSource.alternativeA"),TextResources.Get("modelCapabilityUi.capabilityField.itemsSource.alternativeB")},SelectedIndex=0,MinHeight=32};}
        void RefreshCapabilityFields()
        {if(toolSupport==null || selected==null)return;var caps=selected.Capabilities(newModel.Text.Trim());toolSupport.SelectedIndex=(int)caps.Tools;imageSupport.SelectedIndex=(int)caps.Images;}
        void SaveCapabilityFields(string id)
        {if(toolSupport==null)return;selected.ModelCapabilities[id]=new ModelCapabilitySetting{Tools=(CapabilitySupport)Math.Max(0,toolSupport.SelectedIndex),Images=(CapabilitySupport)Math.Max(0,imageSupport.SelectedIndex)};selected.ApplyModelCapabilities(selected.Connection);}
    }
}
