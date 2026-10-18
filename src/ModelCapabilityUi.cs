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
            Ui.Label(modelForm,"工具调用能力",toolSupport);Ui.Label(modelForm,"图片理解能力",imageSupport);
            var help=Ui.Text("未知优先采用服务端明确的能力元数据；缺失时沿用旧版兼容判断（未经验证）。手动设置优先。开启能力仍需服务端模型支持，不会放宽项目权限。",11,Ui.Muted);help.TextWrapping=System.Windows.TextWrapping.Wrap;modelForm.Children.Add(help);
        }
        static ComboBox CapabilityField(string name){return new ComboBox{Name=name,ItemsSource=new[]{"自动 / 元数据或旧配置","支持","不支持"},SelectedIndex=0,MinHeight=32};}
        void RefreshCapabilityFields()
        {if(toolSupport==null || selected==null)return;var caps=selected.Capabilities(newModel.Text.Trim());toolSupport.SelectedIndex=(int)caps.Tools;imageSupport.SelectedIndex=(int)caps.Images;}
        void SaveCapabilityFields(string id)
        {if(toolSupport==null)return;selected.ModelCapabilities[id]=new ModelCapabilitySetting{Tools=(CapabilitySupport)Math.Max(0,toolSupport.SelectedIndex),Images=(CapabilitySupport)Math.Max(0,imageSupport.SelectedIndex)};selected.ApplyModelCapabilities(selected.Connection);}
    }
}
