using System;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    public sealed class ConnectionSettingsPanel : Grid
    {
        readonly PetWindow pet;TextProviderEditor text,recognition,synthesis,live;ComboBox voice,liveVoice;
        ImageGenerationSettings images;
        CheckBox cloud;readonly TextBlock notice;readonly ContentControl page=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ClipToBounds=true};readonly UIElement[] sections;
        TextBlock speechAdvice,liveAdvice;
        Slider volume;TextBlock volumeLabel;bool refreshingVolume;
        string speechContext,liveContext;
        internal int SpeechTestRevision {get;private set;}
        internal bool SpeechTestSucceeded {get;private set;}
        int speechTestGeneration;
        readonly System.Collections.Generic.Dictionary<string,string> voiceDrafts=new System.Collections.Generic.Dictionary<string,string>();
        readonly System.Collections.Generic.Dictionary<string,string> voiceSavedValues=new System.Collections.Generic.Dictionary<string,string>();
        public bool HasUnsavedChanges
        {
            get{return (images!=null && images.HasUnsavedChanges) || (text!=null && !text.Matches(pet.Settings)) || (recognition!=null && !recognition.Matches(pet.Settings)) || (synthesis!=null && !synthesis.Matches(pet.Settings)) || (live!=null && !live.Matches(pet.Settings)) ||
                (cloud!=null && (cloud.IsChecked==true)!=pet.Settings.UseCloudVoice) ||
                (voice!=null && voice.Text.Trim()!=pet.Settings.VoiceName) || (liveVoice!=null && liveVoice.Text.Trim()!=pet.Settings.LiveVoice) || HasVoiceDraftChanges;}
        }
        bool HasVoiceDraftChanges {get{foreach(var pair in voiceDrafts){string saved;if(!voiceSavedValues.TryGetValue(pair.Key,out saved) || saved!=pair.Value.Trim())return true;}return false;}}
        public ConnectionSettingsPanel(PetWindow companion,int initialSection=0)
        {
            pet=companion;Margin=new Thickness(24,22,24,20);RowDefinitions.Add(new RowDefinition());RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Children.Add(page);
            sections=new UIElement[5];ShowSection(initialSection);
            var footer=new StackPanel{Margin=new Thickness(0,14,0,0)};var row=new WrapPanel();var save=LocalizedUi.Button(()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text.alternativeI")),true);save.Name="SaveConnections";save.Click+=delegate {Save();};row.Children.Add(save);
            footer.Children.Add(row);
            notice=LocalizedUi.Text(()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text.alternativeJ")),11,Ui.Muted);notice.Name="ConnectionNotice";notice.Margin=new Thickness(0,10,0,0);footer.Children.Add(notice);Grid.SetRow(footer,1);Children.Add(footer);
        }
        void EnsureSection(int index)
        {
            if(index<0 || index>=sections.Length)throw new ArgumentOutOfRangeException("index");
            if(sections[index]!=null)return;
            if(index==0){text=new TextProviderEditor(pet.Settings);text.SaveConnection=SaveTextConnection;sections[index]=text;}
            else if(index==1){recognition=new TextProviderEditor(pet.Settings,"asr");sections[index]=recognition;}
            else if(index==2){CreateSpeechEditor();sections[index]=synthesis;}
            else if(index==3){CreateLiveEditor();sections[index]=live;}
            else{images=new ImageGenerationSettings(pet);sections[index]=images;}
        }
        void CreateSpeechEditor()
        {
            synthesis=new TextProviderEditor(pet.Settings,"speech");
            cloud=LocalizedUi.Toggle(()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.toggle")),pet.Settings.UseCloudVoice,v=>SetCloud(v));cloud.Name="UseCloudVoice";synthesis.Options.Children.Add(cloud);
            voice=VoiceField(pet.Settings.VoiceName,"SpeechVoice");LocalizedUi.Label(synthesis.Extras,()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text")),voice);synthesis.Extras.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text.alternativeA")),11,Ui.Muted));SetCloud(pet.Settings.UseCloudVoice);
            speechAdvice=Ui.Text("",11,Ui.Muted);speechAdvice.Name="SpeechCharacterRecommendation";synthesis.Extras.Children.Add(speechAdvice);
            var sound=new StackPanel{Margin=new Thickness(0,16,0,0)};
            sound.Children.Add(Ui.ReplySpeechToggle(pet));
            volumeLabel=LocalizedUi.Text(()=>(TextResources.Format("connectionSettingsPanel.connectionSettingsPanel.template",pet.Settings.Volume)),12,Ui.Blue);sound.Children.Add(volumeLabel);
            volume=new Slider{Name="VoiceVolume",Minimum=0,Maximum=100,TickFrequency=5,IsSnapToTickEnabled=true,Value=pet.Settings.Volume,Margin=new Thickness(0,8,0,12)};
            volume.ValueChanged+=delegate {if(refreshingVolume)return;int value=(int)Math.Round(volume.Value);LocalizedUi.Set(volumeLabel,"Text",()=>(TextResources.Format("connectionSettingsPanel.connectionSettingsPanel.labelValue",value)));pet.Settings.Volume=value;pet.UpdateVoiceVolume(value);pet.SaveSettings();};sound.Children.Add(volume);synthesis.Extras.Children.Add(sound);
            var testSpeech=LocalizedUi.Button(()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text.alternativeE")),true);testSpeech.Name="PreviewSpeech";testSpeech.Click+=async delegate{
                if(pet.DialogueBusy){LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.notice.alternativeA")));return;}if(!SaveGuideSection(2))return;testSpeech.IsEnabled=false;
                int generation=++speechTestGeneration;SpeechTestSucceeded=false;
                try{bool succeeded=await pet.Conversation.PreviewSpeechAsync(pet.Settings,pet.Skin.Name);notice.Text=pet.Conversation.LastNotice;SpeechTestSucceeded=generation==speechTestGeneration&&succeeded;}catch(Exception e){ShowError(e);}finally{SpeechTestRevision++;testSpeech.IsEnabled=true;}
            };
            var speechTests=new WrapPanel{Margin=new Thickness(0,12,0,0)};speechTests.Children.Add(testSpeech);var stop=LocalizedUi.Button(()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text.alternativeF")));stop.Name="StopPreviewSpeech";stop.Click+=delegate{speechTestGeneration++;SpeechTestSucceeded=false;pet.StopReplySpeech();};speechTests.Children.Add(stop);synthesis.Extras.Children.Add(speechTests);
            RefreshVoices(true);synthesis.Changed+=delegate{RefreshVoices(false);};
        }
        void CreateLiveEditor()
        {
            live=new TextProviderEditor(pet.Settings,"live");
            liveVoice=VoiceField(pet.Settings.LiveVoice,"LiveVoice");LocalizedUi.Label(live.Extras,()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text.alternativeB")),liveVoice);live.Extras.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text.alternativeC")),12,Ui.Muted));
            liveAdvice=Ui.Text("",11,Ui.Muted);liveAdvice.Name="LiveCharacterRecommendation";live.Extras.Children.Add(liveAdvice);
            var modes=LocalizedUi.Options(new ComboBox{Name="VoiceMode",ItemsSource=new[]{TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.itemsSource"),TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.itemsSource.alternativeA")},SelectedIndex=pet.Settings.VoiceMode=="live" ? 1 : 0,MinHeight=38,Padding=new Thickness(8)},()=>(new[]{TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.itemsSource"),TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.itemsSource.alternativeA")}));
            bool refreshingMode=false;
            modes.SelectionChanged+=delegate {if(refreshingMode)return;if(pet.DialogueBusy){refreshingMode=true;modes.SelectedIndex=pet.Settings.VoiceMode=="live"?1:0;refreshingMode=false;if(notice!=null)LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.notice")));return;}pet.Settings.VoiceMode=modes.SelectedIndex==1 ? "live" : "pipeline";pet.SaveSettings();pet.RefreshChatAppearance();};LocalizedUi.Label(live.Extras,()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text.alternativeD")),modes);
            var testLive=LocalizedUi.Button(()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text.alternativeG")),true);testLive.Name="PreviewLive";testLive.HorizontalAlignment=HorizontalAlignment.Left;testLive.Margin=new Thickness(0,12,0,0);
            testLive.Click+=delegate{if(pet.DialogueBusy){LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.notice.alternativeB")));return;}if(!SaveGuideSection(3))return;var host=Window.GetWindow(this);if(host!=null)pet.ShowLiveCall(host);};live.Extras.Children.Add(testLive);
            live.Extras.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("connectionSettingsPanel.connectionSettingsPanel.text.alternativeH")),11,Ui.Muted));
            RefreshVoices(true);live.Changed+=delegate{RefreshVoices(false);};
        }
        readonly ScrollViewer[] sectionScrolls=new ScrollViewer[5];
        public void ShowSection(int index)
        {
            EnsureSection(index);
            if(sectionScrolls[index]==null)sectionScrolls[index]=Ui.Scroll(sections[index]);
            if(!ReferenceEquals(page.Content,sectionScrolls[index]))page.Content=sectionScrolls[index];
        }
        internal void DisposeImageGeneration(){if(images!=null)images.Dispose();}
        static ComboBox VoiceField(string value,string name)
        {return new ComboBox{Name=name,IsEditable=true,IsTextSearchEnabled=true,MinHeight=38,Padding=new Thickness(8),Text=value ?? ""};}
        void RefreshVoices(bool initial)
        {
            if(synthesis!=null)FillVoices(voice,"speech",synthesis.Preview(),initial,ref speechContext);
            if(live!=null)FillVoices(liveVoice,"live",live.Preview(),initial,ref liveContext);
        }
        public void RefreshTextProviders(){if(text!=null)text.RefreshSaved(pet.Settings);}
        internal void RefreshPreferenceVolume()
        {
            if(volume==null)return;refreshingVolume=true;
            try{volume.Value=pet.Settings.Volume;LocalizedUi.Set(volumeLabel,"Text",()=>(TextResources.Format("connectionSettingsPanel.connectionSettingsPanel.labelValue",pet.Settings.Volume)));}
            finally{refreshingVolume=false;}
        }
        public void ExpandTextConnection(){EnsureSection(0);text.ExpandConnection();}
        public void ExpandConnectionFor(string name){if(name.StartsWith("TextService")){EnsureSection(0);text.ExpandConnection();}else if(name.StartsWith("Transcription")){EnsureSection(1);recognition.ExpandConnection();}else if(name.StartsWith("Speech") && name!="SpeechVoice"){EnsureSection(2);synthesis.ExpandConnection();}else if(name.StartsWith("LiveService")){EnsureSection(3);live.ExpandConnection();}}
        bool SaveTextConnection()
        {
            try{
                var selected=text.ReadConnectionProvider();ValidateAddress(selected.Connection,ApiClient.ChatPath(selected.Connection));
                var saved=pet.Settings.TextProviders.Find(p=>p.Id==selected.Id);
                var provider=saved==null?new TextProviderSettings{Id=selected.Id}:saved.Copy();
                string model=provider.Connection.Model;provider.Name=selected.Name;provider.BrandId=selected.BrandId;provider.Connection=selected.Connection.Copy();provider.Connection.Model=model;
                if(saved==null)pet.Settings.TextProviders.Add(provider);else pet.Settings.TextProviders[pet.Settings.TextProviders.IndexOf(saved)]=provider;
                pet.Settings.ActiveTextProviderId=provider.Id;pet.Settings.Text=provider.Connection.Copy();pet.SaveSettings();text.ConnectionSaved(pet.Settings);pet.RefreshChatAppearance();
                notice.Text=String.IsNullOrEmpty(pet.Notice)?TextResources.Get("connectionSettingsPanel.saveTextConnection.noticeStringIsNullOrEmpty"):pet.Notice;return String.IsNullOrEmpty(pet.Notice);
            }catch(Exception e){ShowError(e);return false;}
        }
        public void RefreshPersonaVoices()
        {
            voiceDrafts.Clear();voiceSavedValues.Clear();speechContext=null;liveContext=null;
            if(voice!=null)voice.Text=pet.Settings.VoiceName;if(liveVoice!=null)liveVoice.Text=pet.Settings.LiveVoice;RefreshVoices(true);
        }
        void FillVoices(ComboBox field,string kind,ConnectionSettings settings,bool initial,ref string previousContext)
        {
            string current=field.Text;string context=PersonaProfiles.VoiceContext(kind,settings);
            if(!initial && previousContext!=null && previousContext!=context) {
                voiceDrafts[kind+":"+previousContext]=current;
                if(!voiceDrafts.TryGetValue(kind+":"+context,out current))current=PersonaProfiles.VoiceFor(pet.Settings,pet.Skin.Id,kind,settings);
            }
            var next=VoicePresets.For(kind,settings);
            TextSearch.SetTextPath(field,"Id");field.ItemsSource=next;field.SelectedIndex=-1;
            field.Text=String.IsNullOrWhiteSpace(current)?PersonaProfiles.RecommendedVoice(pet.Skin.Id,kind,settings,pet.Settings.ActiveVoiceRecommendations):current;
            if(!voiceSavedValues.ContainsKey(kind+":"+context))voiceSavedValues[kind+":"+context]=field.Text.Trim();
            previousContext=context;
            var advice=kind=="live"?liveAdvice:speechAdvice;
            if(advice!=null) {
                string recommended=PersonaProfiles.RecommendedVoice(pet.Skin.Id,kind,settings,pet.Settings.ActiveVoiceRecommendations);
                LocalizedUi.Set(advice,"Text",()=>(pet.Skin.DisplayName+" · "+(recommended.Length==0?TextResources.Get("connectionSettingsPanel.fillVoices.petSkinName.alternativeA"):TextResources.Format("connectionSettingsPanel.fillVoices.petSkinName",recommended))+
                    (pet.Settings.ActiveVoiceRecommendations==null?"":"\n"+pet.Settings.ActiveVoiceRecommendations.Description)));
            }
        }
        void SaveVoiceDrafts(string kind=null)
        {
            foreach(var pair in voiceDrafts){int split=pair.Key.IndexOf(':');string service=pair.Key.Substring(0,split);if(kind==null || service==kind){if(pair.Value.Trim().Length>512)throw new InvalidOperationException(TextResources.Get("connectionSettingsPanel.saveVoiceDrafts.text"));PersonaProfiles.SaveVoiceChoice(pet.Settings,service,pair.Key.Substring(split+1),pair.Value);}}
        }
        void ClearVoiceDrafts(string kind=null){var keys=new System.Collections.Generic.List<string>(voiceDrafts.Keys);foreach(string key in keys)if(kind==null || key.StartsWith(kind+":")){voiceSavedValues[key]=voiceDrafts[key].Trim();voiceDrafts.Remove(key);}if(voice!=null && (kind==null || kind=="speech"))voiceSavedValues["speech:"+speechContext]=voice.Text.Trim();if(liveVoice!=null && (kind==null || kind=="live"))voiceSavedValues["live:"+liveContext]=liveVoice.Text.Trim();}
        void SetCloud(bool value){if(voice!=null)voice.IsEnabled=value;}
        public bool Save()
        {
            try
            {
                if(images!=null&&images.HasUnsavedChanges&&!images.Save())return false;
                var nextProviders=text==null?pet.Settings.TextProviders:text.ReadProviders();
                var nextText=text==null?pet.Settings.Text:text.Read();var nextAsr=recognition==null?pet.Settings.Transcription:recognition.Read();var nextSpeech=synthesis==null?pet.Settings.Speech:synthesis.Read();var nextLive=live==null?pet.Settings.Live:live.Read();
                string nextVoice=voice==null?pet.Settings.VoiceName:voice.Text.Trim(),nextLiveVoice=liveVoice==null?pet.Settings.LiveVoice:liveVoice.Text.Trim();
                if(nextVoice.Length>512 || nextLiveVoice.Length>512)throw new InvalidOperationException(TextResources.Get("connectionSettingsPanel.save.text"));
                foreach(var provider in nextProviders)ValidateAddress(provider.Connection,ApiClient.ChatPath(provider.Connection));ValidateAddress(nextAsr,ApiClient.AsrPath(nextAsr));ValidateAddress(nextSpeech,ApiClient.SpeechPath(nextSpeech));
                var probe=nextLive.Copy();if(!String.IsNullOrWhiteSpace(probe.BaseUrl)){if(String.IsNullOrWhiteSpace(probe.Model))probe.Model="validation";if(String.IsNullOrEmpty(probe.ApiKey))probe.ApiKey="validation";GeminiLiveSocket.Endpoint(probe);}
                PersonaProfiles.SaveVoices(pet.Settings);
                if(text!=null){pet.Settings.TextProviders=nextProviders;pet.Settings.ActiveTextProviderId=text.SelectedProviderId;pet.Settings.Text=nextText;}
                if(recognition!=null)pet.Settings.ApplyProviders("asr",recognition.ReadProviders(),recognition.SelectedProviderId,nextAsr);
                if(synthesis!=null){pet.Settings.ApplyProviders("speech",synthesis.ReadProviders(),synthesis.SelectedProviderId,nextSpeech);pet.Settings.UseCloudVoice=cloud.IsChecked==true;pet.Settings.VoiceName=nextVoice;}
                if(live!=null){pet.Settings.ApplyProviders("live",live.ReadProviders(),live.SelectedProviderId,nextLive);pet.Settings.LiveVoice=nextLiveVoice;}SaveVoiceDrafts();PersonaProfiles.SaveVoices(pet.Settings);for(int index=0;index<4;index++)InvalidateGuideTest(index);pet.SaveSettings();ClearVoiceDrafts();RefreshCreatedEditors();pet.RefreshChatAppearance();notice.Text=String.IsNullOrEmpty(pet.Notice)?TextResources.Get("connectionSettingsPanel.save.noticeStringIsNullOrEmpty"):pet.Notice;return String.IsNullOrEmpty(pet.Notice);
            }
            catch(Exception e){ShowError(e);return false;}
        }
        void RefreshCreatedEditors(){if(text!=null)text.RefreshSaved(pet.Settings);if(recognition!=null)recognition.RefreshSaved(pet.Settings);if(synthesis!=null)synthesis.RefreshSaved(pet.Settings);if(live!=null)live.RefreshSaved(pet.Settings);}
        public bool ValidateGuideSection(int index,out string error)
        {
            try
            {
                EnsureSection(index);var draft=pet.Settings.Copy();ConnectionSettings service;
                if(index==0){var provider=text.ReadConnectionProvider();service=provider.Connection;ProxyConfiguration.Endpoint(ProxyConfiguration.Normalize(service.ProxyAddress));var probe=service.Copy();probe.Model="guide-validation";ApiClient.ValidateConnection(probe,ApiClient.ChatPath(probe));if(String.IsNullOrWhiteSpace(service.Model) || !provider.Models.Contains(service.Model))throw new InvalidOperationException(TextResources.Get("connectionSettingsPanel.validateGuideSection.text"));}
                else if(index==1){draft.Transcription=recognition.Read();service=draft.TranscriptionConnection();ApiClient.ValidateConnection(service,ApiClient.AsrPath(service));}
                else if(index==2 && cloud.IsChecked==true){draft.Speech=synthesis.Read();service=draft.SpeechConnection();ApiClient.ValidateConnection(service,ApiClient.SpeechPath(service));if(String.IsNullOrWhiteSpace(voice.Text) || voice.Text.Trim().Length>512)throw new InvalidOperationException(TextResources.Get("connectionSettingsPanel.validateGuideSection.text.alternativeA"));}
                else if(index==3){GeminiLiveSocket.Endpoint(live.Read());if(String.IsNullOrWhiteSpace(liveVoice.Text) || liveVoice.Text.Trim().Length>512)throw new InvalidOperationException(TextResources.Get("connectionSettingsPanel.validateGuideSection.text.alternativeB"));}
                error="";return true;
            }
            catch(Exception e){error=e is ApiException || e is InvalidOperationException?e.Message:TextResources.Get("connectionSettingsPanel.validateGuideSection.invalidOperationExceptionEMessage");return false;}
        }
        void InvalidateGuideTest(int index)
        {
            string id=new[]{"text.test","chat.dictation","speech.test","live.test"}[index];
            pet.Settings.OnboardingVerifiedLessons.Remove(id);pet.Settings.OnboardingCompletedLessons.Remove(id);
        }
        public bool SaveGuideSection(int index)
        {
            string error;if(!ValidateGuideSection(index,out error)){notice.Text=error;return false;}
            try
            {
                var draft=pet.Settings.Copy();
                if(index==0){draft.Text=text.Read();ApiClient.ValidateConnection(draft.Text,ApiClient.ChatPath(draft.Text));pet.Settings.TextProviders=text.ReadProviders();pet.Settings.ActiveTextProviderId=text.SelectedProviderId;pet.Settings.Text=draft.Text;}
                else if(index==1)
                {
                    draft.Transcription=recognition.Read();var service=draft.TranscriptionConnection();ApiClient.ValidateConnection(service,ApiClient.AsrPath(service));
                    pet.Settings.ApplyProviders("asr",recognition.ReadProviders(),recognition.SelectedProviderId,draft.Transcription);
                }
                else if(index==2)
                {
                    draft.UseCloudVoice=cloud.IsChecked==true;draft.Speech=synthesis.Read();draft.VoiceName=voice.Text.Trim();
                    if(draft.UseCloudVoice){var service=draft.SpeechConnection();ApiClient.ValidateConnection(service,ApiClient.SpeechPath(service));if(draft.VoiceName.Length==0 || draft.VoiceName.Length>512)throw new InvalidOperationException(TextResources.Get("connectionSettingsPanel.saveGuideSection.text"));}
                    pet.Settings.UseCloudVoice=draft.UseCloudVoice;pet.Settings.ApplyProviders("speech",synthesis.ReadProviders(),synthesis.SelectedProviderId,draft.Speech);pet.Settings.VoiceName=draft.VoiceName;PersonaProfiles.SaveVoices(pet.Settings);
                }
                else
                {
                    draft.Live=live.Read();GeminiLiveSocket.Endpoint(draft.Live);draft.LiveVoice=liveVoice.Text.Trim();if(draft.LiveVoice.Length==0 || draft.LiveVoice.Length>512)throw new InvalidOperationException(TextResources.Get("connectionSettingsPanel.saveGuideSection.text.alternativeA"));
                    pet.Settings.ApplyProviders("live",live.ReadProviders(),live.SelectedProviderId,draft.Live);pet.Settings.LiveVoice=draft.LiveVoice;PersonaProfiles.SaveVoices(pet.Settings);
                }
                InvalidateGuideTest(index);if(index==2 || index==3){SaveVoiceDrafts(index==2?"speech":"live");PersonaProfiles.SaveVoices(pet.Settings);}pet.SaveSettings();if(index==2 || index==3)ClearVoiceDrafts(index==2?"speech":"live");RefreshCreatedEditors();pet.RefreshChatAppearance();notice.Text=String.IsNullOrEmpty(pet.Notice)?TextResources.Get("connectionSettingsPanel.saveGuideSection.noticeStringIsNullOrEmpty"):pet.Notice;return String.IsNullOrEmpty(pet.Notice);
            }
            catch(Exception e){ShowError(e);return false;}
        }
        static void ValidateAddress(ConnectionSettings settings,string path)
        {if(String.IsNullOrWhiteSpace(settings.BaseUrl))return;var probe=settings.Copy();probe.Model="validation";ApiClient.Endpoint(probe,path);if(settings.ApiKey.IndexOfAny(new[]{'\r','\n'})>=0)throw new ApiException(TextResources.Get("connectionSettingsPanel.validateAddress.text"));}
        void ShowError(Exception e){LocalizedUi.Set(notice,"Text",()=>(e is OperationCanceledException?TextResources.Get("connectionSettingsPanel.showError.eIsCryptographicException"):e is ApiException || e is InvalidOperationException?e.Message:e is CryptographicException?TextResources.Get("connectionSettingsPanel.showError.eIsCryptographicException.alternativeA"):TextResources.Get("connectionSettingsPanel.showError.eIsCryptographicException.alternativeB")));}
        internal sealed class ConnectionEditor : StackPanel
        {
            readonly TextBox address,model,proxyAddress;readonly PasswordBox key;readonly ComboBox protocol;readonly CheckBox proxy,streamReplies;readonly string kind;readonly TextBlock hint;
            public event Action Changed;public event Action<string> FactorySelected;bool applyingPreset;
            public ConnectionEditor(string title,string prefix,string serviceKind,ConnectionSettings settings)
            {
                Name=prefix+"GuideSection";kind=serviceKind;var heading=Ui.Text(title,17);heading.FontWeight=FontWeights.SemiBold;Children.Add(heading);Children.Add(LocalizedUi.Text(()=>(kind=="text" ? TextResources.Get("connectionSettingsPanel.connectionEditor.text") : kind=="live" ? TextResources.Get("connectionSettingsPanel.connectionEditor.text.alternativeA") : kind=="asr" ? TextResources.Get("connectionSettingsPanel.connectionEditor.text.alternativeB") : TextResources.Get("connectionSettingsPanel.connectionEditor.text.alternativeC")),12,Ui.Muted));
                address=Ui.Field(settings.BaseUrl);address.Name=prefix+"Url";model=Ui.Field(settings.Model);model.Name=prefix+"Model";
                key=new PasswordBox{Name=prefix+"Key",Password=settings.ApiKey,MinHeight=38,Padding=new Thickness(10,7,10,7),BorderBrush=Ui.Line,Background=ThemeManager.FieldSurface};
                protocol=LocalizedUi.Options(new ComboBox{Name=prefix+"Protocol",ItemsSource=kind=="text" ? new[]{"OpenAI Chat Completions","Anthropic Messages"} : kind=="live" ? new[]{"Gemini Live WebSocket"} : new[]{TextResources.Get("connectionSettingsPanel.connectionEditor.itemsSourceKindKind"),"OpenAI Audio","MiMo Chat Audio"},MinHeight=38,Padding=new Thickness(8)},()=>(kind=="text" ? new[]{"OpenAI Chat Completions","Anthropic Messages"} : kind=="live" ? new[]{"Gemini Live WebSocket"} : new[]{TextResources.Get("connectionSettingsPanel.connectionEditor.itemsSourceKindKind"),"OpenAI Audio","MiMo Chat Audio"}));
                protocol.SelectedIndex=kind=="text" ? (settings.Protocol=="anthropic"?1:0) : kind=="live"?0:settings.Protocol=="mimo"?2:settings.Protocol=="openai"?1:0;
                proxy=LocalizedUi.Toggle(()=>(TextResources.Get("connectionSettingsPanel.connectionEditor.toggle")),settings.UseProxy,v=>{});proxy.Name=prefix+"Proxy";
                proxyAddress=Ui.Field(ProxyConfiguration.Normalize(settings.ProxyAddress));proxyAddress.Name=prefix+"ProxyAddress";
                var presets=new ComboBox{Name=prefix+"Preset",ItemsSource=ServicePresets.For(kind),SelectedIndex=0,MinHeight=38,Padding=new Thickness(8)};
                hint=LocalizedUi.Text(()=>(kind=="live"?TextResources.Get("connectionSettingsPanel.connectionEditor.text.alternativeD"):TextResources.Get("connectionSettingsPanel.connectionEditor.text.alternativeE")),11,Ui.Muted);hint.Margin=new Thickness(0,7,0,3);
                string keyOrigin=address.Text;
                key.PasswordChanged+=delegate {keyOrigin=address.Text;};
                address.TextChanged+=delegate {if(key.Password.Length>0 && !ServicePresets.SameOrigin(keyOrigin,address.Text)){key.Clear();LocalizedUi.Set(hint,"Text",()=>(TextResources.Get("connectionSettingsPanel.connectionEditor.hint")));}};
                presets.SelectionChanged+=delegate {var preset=presets.SelectedItem as ServicePreset;if(preset==null)return;if(FactorySelected!=null)FactorySelected(preset.Url.Length==0?"custom":ProviderIdentity.Brand(preset.Url));if(preset.Url.Length==0){if(Changed!=null)Changed();return;}applyingPreset=true;if(!ServicePresets.SameOrigin(address.Text,preset.Url))key.Clear();address.Text=preset.Url;model.Text=preset.Model;protocol.SelectedIndex=kind=="text"?(preset.Protocol=="anthropic"?1:0):kind=="live"?0:preset.Protocol=="mimo"?2:1;LocalizedUi.Set(hint,"Text",()=>(preset.Note.Length==0 ? TextResources.Get("connectionSettingsPanel.connectionEditor.lengthPresetNote") : preset.Note));applyingPreset=false;if(Changed!=null)Changed();};
                streamReplies=LocalizedUi.Toggle(()=>(TextResources.Get("connectionSettingsPanel.connectionEditor.toggle.alternativeA")),settings.StreamReplies,v=>{});streamReplies.Name=prefix+"StreamReplies";
                Children.Add(Pair(()=> (TextResources.Get("connectionSettingsPanel.connectionEditor.pair")),presets,()=> (TextResources.Get("connectionSettingsPanel.connectionEditor.pair.alternativeA")),protocol));
                LocalizedUi.Label(this,()=>(kind=="live"?TextResources.Get("connectionSettingsPanel.connectionEditor.text.alternativeF"):TextResources.Get("connectionSettingsPanel.connectionEditor.text.alternativeG")),address);
                if(kind=="text"){LocalizedUi.Label(this,()=>(TextResources.Get("connectionSettingsPanel.connectionEditor.text.alternativeH")),key);model.Visibility=Visibility.Collapsed;Children.Add(model);}else Children.Add(Pair(()=> (TextResources.Get("connectionSettingsPanel.connectionEditor.pair.alternativeB")),key,()=> (TextResources.Get("connectionSettingsPanel.connectionEditor.pair.alternativeC")),model));
                var toggles=new WrapPanel();proxy.Margin=new Thickness(0,8,24,4);toggles.Children.Add(proxy);
                if(kind=="text"){streamReplies.Margin=new Thickness(0,8,0,4);toggles.Children.Add(streamReplies);}Children.Add(toggles);
                LocalizedUi.Label(this,()=>(TextResources.Get("connectionSettingsPanel.connectionEditor.text.alternativeI")),proxyAddress);Children.Add(hint);
                address.TextChanged+=delegate {if(!applyingPreset && Changed!=null)Changed();};model.TextChanged+=delegate {if(!applyingPreset && Changed!=null)Changed();};protocol.SelectionChanged+=delegate {if(!applyingPreset && Changed!=null)Changed();};
                key.PasswordChanged+=delegate{if(!applyingPreset && Changed!=null)Changed();};proxy.Checked+=delegate{if(Changed!=null)Changed();};proxy.Unchecked+=delegate{if(Changed!=null)Changed();};proxyAddress.TextChanged+=delegate{if(Changed!=null)Changed();};streamReplies.Checked+=delegate{if(Changed!=null)Changed();};streamReplies.Unchecked+=delegate{if(Changed!=null)Changed();};
            }
            static Grid Pair(System.Func<string> firstLabel,UIElement first,System.Func<string> secondLabel,UIElement second)
            {
                var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());
                var left=new StackPanel{Margin=new Thickness(0,0,6,0)};var right=new StackPanel{Margin=new Thickness(6,0,0,0)};
LocalizedUi.Label(left,firstLabel,first);LocalizedUi.Label(right,secondLabel,second);Grid.SetColumn(right,1);row.Children.Add(left);row.Children.Add(right);return row;
            }
            public ConnectionSettings Preview(){return new ConnectionSettings{BaseUrl=address.Text.Trim(),Model=model.Text.Trim(),Protocol=kind=="text"?(protocol.SelectedIndex==1?"anthropic":"openai"):kind=="live"?"gemini-live":protocol.SelectedIndex==2?"mimo":protocol.SelectedIndex==1?"openai":"auto"};}
            public ConnectionSettings Read(){string selectedProxy=ProxyConfiguration.Normalize(proxyAddress.Text);if(proxyAddress.IsEnabled)ProxyConfiguration.Endpoint(selectedProxy);return Draft();}
            public void SetModel(string value){model.Text=value ?? "";}
            public ConnectionSettings Draft(){string selectedProxy=ProxyConfiguration.Normalize(proxyAddress.Text);return new ConnectionSettings{BaseUrl=address.Text.Trim(),Model=model.Text.Trim(),ApiKey=key.Password.Trim(),Protocol=kind=="text"?(protocol.SelectedIndex==1?"anthropic":"openai"):kind=="live"?"gemini-live":protocol.SelectedIndex==2?"mimo":protocol.SelectedIndex==1?"openai":"auto",UseProxy=proxy.IsChecked==true,ProxyAddress=selectedProxy,StreamReplies=streamReplies.IsChecked==true};}
            public bool Matches(ConnectionSettings saved)
            {
                string selected=kind=="text"?(protocol.SelectedIndex==1?"anthropic":"openai"):kind=="live"?"gemini-live":protocol.SelectedIndex==2?"mimo":protocol.SelectedIndex==1?"openai":"auto";
                string expected=kind=="text"?(saved.Protocol=="anthropic"?"anthropic":"openai"):kind=="live"?"gemini-live":saved.Protocol=="mimo"?"mimo":saved.Protocol=="openai"?"openai":"auto";
                return address.Text.Trim()==saved.BaseUrl && model.Text.Trim()==saved.Model && key.Password.Trim()==saved.ApiKey && selected==expected &&
                    (kind!="text" || (streamReplies.IsChecked==true)==saved.StreamReplies) && (proxy.IsChecked==true)==saved.UseProxy && ProxyConfiguration.Normalize(proxyAddress.Text)==ProxyConfiguration.Normalize(saved.ProxyAddress);
            }
        }
    }
}
