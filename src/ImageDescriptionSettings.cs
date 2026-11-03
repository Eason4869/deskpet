using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    // Both settings pages edit the same global preference; neither owns a detached draft.
    internal sealed class ImageDescriptionSettings : UserControl
    {
        readonly PetWindow pet;readonly ComboBox picker;readonly TextBlock notice;bool refreshing,saving;
        internal ImageDescriptionSettings(PetWindow pet,string controlName,bool emphasizeTitle=true)
        {
            this.pet=pet;picker=new ComboBox{Name=controlName,MinHeight=36,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            var body=new StackPanel();body.Children.Add(Ui.SettingRow(()=>TextResources.Get("imageDescription.settings.title"),()=>TextResources.Get("imageDescription.settings.help"),picker,emphasizeTitle));notice=Ui.Text("",11,Ui.Muted);notice.Visibility=Visibility.Collapsed;body.Children.Add(notice);Content=body;
            picker.SelectionChanged+=async delegate{
                if(refreshing || saving)return;var item=picker.SelectedItem as ComboBoxItem;var selected=item==null?null:item.Tag as ModelReference;
                if(Same(selected,pet.Settings.ImageDescriptionModel))return;
                var submitted=selected==null?null:selected.Copy();saving=true;
                try{bool saved=await pet.RunSettingsEditAsync(this,()=>pet.PersistSettingsAsync(s=>s.ImageDescriptionModel=submitted==null?null:submitted.Copy()),"image-description");notice.Text=saved?"":TextResources.Get("desktopObservation.saveFailed");notice.Visibility=saved?Visibility.Collapsed:Visibility.Visible;}
                finally{saving=false;Refresh();}
            };
            Loaded+=delegate{pet.ModelSettingsChanged+=ModelSettingsChanged;Refresh();};Unloaded+=delegate{pet.ModelSettingsChanged-=ModelSettingsChanged;};IsVisibleChanged+=delegate{if(IsVisible)Refresh();};Refresh();
        }
        void ModelSettingsChanged(object sender,EventArgs args){Refresh();}
        internal void Refresh()
        {
            refreshing=true;
            try{
                picker.Items.Clear();var empty=LocalizedUi.With(new ComboBoxItem{Tag=null},"Content",()=>TextResources.Get("imageDescription.none"));picker.Items.Add(empty);ComboBoxItem selected=pet.Settings.ImageDescriptionModel==null?empty:null;
                foreach(var choice in ImageModelResolver.Candidates(pet.Settings)){
                    var provider=pet.Settings.TextProviders.FirstOrDefault(p=>p.Id==choice.ProviderId);if(provider==null)continue;
                    string label=provider.Name+" · "+provider.DisplayName(choice.ModelId);var item=new ComboBoxItem{Tag=choice,Content=Ui.InlineText(label,12),ToolTip=label};picker.Items.Add(item);if(Same(choice,pet.Settings.ImageDescriptionModel))selected=item;
                }
                if(selected==null){selected=LocalizedUi.With(new ComboBoxItem{Tag=pet.Settings.ImageDescriptionModel.Copy()},"Content",()=>TextResources.Get("imageDescription.invalid"));picker.Items.Add(selected);}picker.SelectedItem=selected;
            }finally{refreshing=false;}
        }
        static bool Same(ModelReference a,ModelReference b){return a==null?b==null:b!=null && a.ProviderId==b.ProviderId && a.ModelId==b.ModelId;}
    }
}
