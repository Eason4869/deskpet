using System;
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
            var header=new StackPanel();title=Ui.Text("",compact?18:25);title.FontWeight=FontWeights.SemiBold;header.Children.Add(title);description=Ui.Text("",12,Ui.Muted);header.Children.Add(description);if(compact)description.Visibility=Visibility.Collapsed;header.Children.Add(Ui.Text(TextResources.Get("personaPanel.personaPanel.text"),12,Ui.Muted));header.Margin=new Thickness(0,0,0,compact?10:18);Children.Add(header);
            prompt=Ui.Field("",true);prompt.Name="PersonaPrompt";prompt.MaxLength=8000;prompt.MinHeight=120;Grid.SetRow(prompt,1);Children.Add(prompt);
            var footer=new StackPanel{Margin=new Thickness(0,16,0,0)};var row=new WrapPanel();var save=Ui.Button(TextResources.Get("personaPanel.personaPanel.text.alternativeA"),true);save.Name="SavePersona";save.Click+=delegate {Change(false);};row.Children.Add(save);var reset=Ui.Button(TextResources.Get("personaPanel.personaPanel.text.alternativeB"));reset.Name="ResetPersona";reset.Click+=delegate {Change(true);};row.Children.Add(reset);footer.Children.Add(row);notice=Ui.Text(TextResources.Get("personaPanel.personaPanel.text.alternativeC"),11,Ui.Muted);notice.Margin=new Thickness(0,12,0,0);footer.Children.Add(notice);Grid.SetRow(footer,2);Children.Add(footer);RefreshPersona();
        }
        public void RefreshPersona(){title.Text=TextResources.Format("personaPanel.refreshPersona.petSkinName",pet.Skin.DisplayName);description.Text=pet.Skin.DisplayDescription;prompt.Text=pet.Settings.SystemPrompt;}
        void Change(bool reset)
        {
            try{if(pet.DialogueBusy)throw new InvalidOperationException(TextResources.Get("personaPanel.change.text"));if(reset)PersonaProfiles.Reset(pet.Settings,pet.Skin);else PersonaProfiles.Save(pet.Settings,pet.Skin,prompt.Text);pet.Conversation.Clear();pet.SaveSettings();pet.ReloadConversation();RefreshPersona();notice.Text=String.IsNullOrEmpty(pet.Notice)?TextResources.Get("personaPanel.change.noticeStringIsNullOrEmpty"):pet.Notice;}
            catch(InvalidOperationException e){notice.Text=e.Message;}
        }
    }
}
