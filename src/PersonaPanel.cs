using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    public sealed class PersonaPanel : Grid
    {
        readonly PetWindow pet;readonly TextBox prompt;readonly TextBlock title,description,notice;
        public bool HasUnsavedChanges {get{return !String.Equals(prompt.Text,pet.Settings.SystemPrompt,StringComparison.Ordinal);}}
        public PersonaPanel(PetWindow companion,bool compact=false)
        {
            Name="GuidePersonaSection";pet=companion;Margin=new Thickness(24,22,24,20);RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new StackPanel();title=Ui.Text("",compact?18:25);title.FontWeight=FontWeights.SemiBold;header.Children.Add(title);description=Ui.Text("",12,Ui.Muted);header.Children.Add(description);if(compact)description.Visibility=Visibility.Collapsed;header.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("personaPanel.personaPanel.text")),12,Ui.Muted));header.Margin=new Thickness(0,0,0,compact?10:18);Children.Add(header);
            prompt=Ui.Field("",true);prompt.Name="PersonaPrompt";prompt.MaxLength=8000;prompt.MinHeight=120;Grid.SetRow(prompt,1);Children.Add(prompt);
            var footer=new StackPanel{Margin=new Thickness(0,16,0,0)};var row=new WrapPanel();var save=LocalizedUi.Button(()=>(TextResources.Get("personaPanel.personaPanel.text.alternativeA")),true);save.Name="SavePersona";save.Click+=async delegate {await ChangeAsync(false);};row.Children.Add(save);var reset=LocalizedUi.Button(()=>(TextResources.Get("personaPanel.personaPanel.text.alternativeB")));reset.Name="ResetPersona";reset.Click+=async delegate {await ChangeAsync(true);};row.Children.Add(reset);footer.Children.Add(row);notice=LocalizedUi.Text(()=>(TextResources.Get("personaPanel.personaPanel.text.alternativeC")),11,Ui.Muted);notice.Margin=new Thickness(0,12,0,0);footer.Children.Add(notice);Grid.SetRow(footer,2);Children.Add(footer);RefreshPersona();
        }
        public void RefreshPersona(){LocalizedUi.Set(title,"Text",()=>(TextResources.Format("personaPanel.refreshPersona.petSkinName",pet.Skin.DisplayName)));LocalizedUi.Set(description,"Text",()=>(pet.Skin.DisplayDescription));prompt.Text=pet.Settings.SystemPrompt;}
        internal Task<bool> ChangeAsync(bool reset)
        {
            return pet.RunSettingsEditAsync(this,async delegate{
                try{
                    if(pet.DialogueBusy)throw new InvalidOperationException(TextResources.Get("personaPanel.change.text"));
                    var skin=pet.Skin;var session=pet.Conversation;string sessionPersona=session.PersonaId;string submitted=prompt.Text;var draft=pet.Settings.Copy();
                    if(reset)PersonaProfiles.Reset(draft,skin);else PersonaProfiles.Save(draft,skin,submitted);
                    string persona;bool has=draft.PersonaOverrides.TryGetValue(skin.Id,out persona);
                    bool saved=await pet.PersistSettingsAsync(target=>{if(has)target.PersonaOverrides[skin.Id]=persona;else target.PersonaOverrides.Remove(skin.Id);target.PersonaProfileVersion=draft.PersonaProfileVersion;if(target.SkinId==skin.Id)target.SystemPrompt=draft.SystemPrompt;});
                    if(!saved){notice.Text=pet.Notice;return false;}
                    if(ReferenceEquals(pet.Conversation,session)&&session.PersonaId==sessionPersona&&pet.Skin.Id==skin.Id&&!pet.DialogueBusy){session.Clear();pet.ReloadConversation();}
                    if(prompt.Text==submitted)RefreshPersona();notice.Text=TextResources.Get("personaPanel.change.noticeStringIsNullOrEmpty");return true;
                }catch(InvalidOperationException e){notice.Text=e.Message;return false;}
            });
        }
    }
}
