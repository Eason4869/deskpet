using System;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    public sealed class ConnectionSettingsPanel : Grid
    {
        readonly PetWindow pet;readonly TextProviderEditor text,recognition,synthesis,live;readonly ComboBox voice,liveVoice;
        readonly ImageGenerationSettings images;
        readonly CheckBox cloud;readonly TextBlock notice;readonly ContentControl page=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ClipToBounds=true};readonly UIElement[] sections;
        readonly TextBlock speechAdvice,liveAdvice;
        string speechContext,liveContext;
        internal int SpeechTestRevision {get;private set;}
        internal bool SpeechTestSucceeded {get;private set;}
        int speechTestGeneration;
        readonly System.Collections.Generic.Dictionary<string,string> voiceDrafts=new System.Collections.Generic.Dictionary<string,string>();
        readonly System.Collections.Generic.Dictionary<string,string> voiceSavedValues=new System.Collections.Generic.Dictionary<string,string>();
        public bool HasUnsavedChanges
        {
            get{return images.HasUnsavedChanges || !text.Matches(pet.Settings) || !recognition.Matches(pet.Settings) || !synthesis.Matches(pet.Settings) || !live.Matches(pet.Settings) ||
                (cloud.IsChecked==true)!=pet.Settings.UseCloudVoice ||
                voice.Text.Trim()!=pet.Settings.VoiceName || liveVoice.Text.Trim()!=pet.Settings.LiveVoice || HasVoiceDraftChanges;}
        }
        bool HasVoiceDraftChanges {get{foreach(var pair in voiceDrafts){string saved;if(!voiceSavedValues.TryGetValue(pair.Key,out saved) || saved!=pair.Value.Trim())return true;}return false;}}
        public ConnectionSettingsPanel(PetWindow companion)
        {
            pet=companion;Margin=new Thickness(24,22,24,20);RowDefinitions.Add(new RowDefinition());RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Children.Add(page);
            text=new TextProviderEditor(pet.Settings);text.SaveConnection=SaveTextConnection;recognition=new TextProviderEditor(pet.Settings,"asr");synthesis=new TextProviderEditor(pet.Settings,"speech");live=new TextProviderEditor(pet.Settings,"live");
            cloud=Ui.Toggle("使用云端 TTS",pet.Settings.UseCloudVoice,v=>SetCloud(v));cloud.Name="UseCloudVoice";synthesis.Options.Children.Add(cloud);
            voice=VoiceField(pet.Settings.VoiceName,"SpeechVoice");Ui.Label(synthesis.Extras,"音色（可选择或手动填写）",voice);synthesis.Extras.Children.Add(Ui.Text("音色按宠物和服务模型保存，切换伙伴时同步切换。\n首次推荐仅供试听；关闭云端 TTS 时使用 Windows 系统语音。",11,Ui.Muted));SetCloud(pet.Settings.UseCloudVoice);
            speechAdvice=Ui.Text("",11,Ui.Muted);speechAdvice.Name="SpeechCharacterRecommendation";synthesis.Extras.Children.Add(speechAdvice);
            var sound=new StackPanel{Margin=new Thickness(0,16,0,0)};
            sound.Children.Add(Ui.ReplySpeechToggle(pet));
            var label=Ui.Text("音量 · "+pet.Settings.Volume+"%",12,Ui.Blue);sound.Children.Add(label);
            var volume=new Slider{Name="VoiceVolume",Minimum=0,Maximum=100,TickFrequency=5,IsSnapToTickEnabled=true,Value=pet.Settings.Volume,Margin=new Thickness(0,8,0,12)};
            volume.ValueChanged+=delegate {int value=(int)Math.Round(volume.Value);label.Text="音量 · "+value+"%";pet.Settings.Volume=value;pet.UpdateVoiceVolume(value);pet.SaveSettings();};sound.Children.Add(volume);synthesis.Extras.Children.Add(sound);
            liveVoice=VoiceField(pet.Settings.LiveVoice,"LiveVoice");Ui.Label(live.Extras,"Live 音色（可选择或手动填写）",liveVoice);live.Extras.Children.Add(Ui.Text("例如 Kore。模型与音色以服务账号可用范围为准。\n点击快速或完整对话中的声波图标进入 Live 通话；连接成功才会打开麦克风，挂断后停止收音。",12,Ui.Muted));
            liveAdvice=Ui.Text("",11,Ui.Muted);liveAdvice.Name="LiveCharacterRecommendation";live.Extras.Children.Add(liveAdvice);
            var modes=new ComboBox{Name="VoiceMode",ItemsSource=new[]{"ASR + TTS · 分步语音","Live · 实时语音"},SelectedIndex=pet.Settings.VoiceMode=="live" ? 1 : 0,MinHeight=38,Padding=new Thickness(8)};
            bool refreshingMode=false;
            modes.SelectionChanged+=delegate {if(refreshingMode)return;if(pet.DialogueBusy){refreshingMode=true;modes.SelectedIndex=pet.Settings.VoiceMode=="live"?1:0;refreshingMode=false;if(notice!=null)notice.Text="请先结束当前对话，再切换语音模式。";return;}pet.Settings.VoiceMode=modes.SelectedIndex==1 ? "live" : "pipeline";pet.SaveSettings();pet.RefreshChatAppearance();};Ui.Label(live.Extras,"聊天窗口默认语音模式",modes);
            var testSpeech=Ui.Button("测试朗读",true);testSpeech.Name="PreviewSpeech";testSpeech.Click+=async delegate{
                if(pet.DialogueBusy){notice.Text="请先结束当前对话，再测试朗读。";return;}if(!SaveGuideSection(2))return;testSpeech.IsEnabled=false;
                int generation=++speechTestGeneration;SpeechTestSucceeded=false;
                try{await pet.Conversation.PreviewSpeechAsync(pet.Settings,pet.Skin.Name);notice.Text=pet.Conversation.LastNotice;SpeechTestSucceeded=generation==speechTestGeneration&&pet.Conversation.LastNotice=="朗读测试完成。";}catch(Exception e){ShowError(e);}finally{SpeechTestRevision++;testSpeech.IsEnabled=true;}
            };
            var speechTests=new WrapPanel{Margin=new Thickness(0,12,0,0)};speechTests.Children.Add(testSpeech);var stop=Ui.Button("停止朗读");stop.Name="StopPreviewSpeech";stop.Click+=delegate{speechTestGeneration++;SpeechTestSucceeded=false;pet.StopReplySpeech();};speechTests.Children.Add(stop);synthesis.Extras.Children.Add(speechTests);
            var testLive=Ui.Button("测试 Live 通话",true);testLive.Name="PreviewLive";testLive.HorizontalAlignment=HorizontalAlignment.Left;testLive.Margin=new Thickness(0,12,0,0);
            testLive.Click+=delegate{if(pet.DialogueBusy){notice.Text="请先结束当前对话，再测试 Live。";return;}if(!SaveGuideSection(3))return;var host=Window.GetWindow(this);if(host!=null)pet.ShowLiveCall(host);};live.Extras.Children.Add(testLive);
            live.Extras.Children.Add(Ui.Text("测试使用当前 Live 模型和音色；连接成功后收音，结束通话返回设置。",11,Ui.Muted));
            images=new ImageGenerationSettings(pet);sections=new UIElement[]{text,recognition,synthesis,live,images};ShowSection(0);
            RefreshVoices(true);text.Changed+=delegate {RefreshVoices(false);};synthesis.Changed+=delegate {RefreshVoices(false);};live.Changed+=delegate {RefreshVoices(false);};
            var footer=new StackPanel{Margin=new Thickness(0,14,0,0)};var row=new WrapPanel();var save=Ui.Button("保存配置",true);save.Name="SaveConnections";save.Click+=delegate {Save();};row.Children.Add(save);
            footer.Children.Add(row);
            notice=Ui.Text("连接设置需点击保存。密钥由当前 Windows 用户加密存储。",11,Ui.Muted);notice.Name="ConnectionNotice";notice.Margin=new Thickness(0,10,0,0);footer.Children.Add(notice);Grid.SetRow(footer,1);Children.Add(footer);
        }
        public void ShowSection(int index){var previous=page.Content as ScrollViewer;if(previous!=null)previous.Content=null;page.Content=Ui.Scroll(sections[index]);}
        internal void DisposeImageGeneration(){images.Dispose();}
        static ComboBox VoiceField(string value,string name)
        {return new ComboBox{Name=name,IsEditable=true,IsTextSearchEnabled=true,MinHeight=38,Padding=new Thickness(8),Text=value ?? ""};}
        void RefreshVoices(bool initial)
        {
            var speech=synthesis.Preview();
            FillVoices(voice,"speech",speech,initial,ref speechContext);FillVoices(liveVoice,"live",live.Preview(),initial,ref liveContext);
        }
        public void RefreshTextProviders(){text.RefreshSaved(pet.Settings);}
        public void ExpandTextConnection(){text.ExpandConnection();}
        public void ExpandConnectionFor(string name){if(name.StartsWith("TextService"))text.ExpandConnection();else if(name.StartsWith("Transcription"))recognition.ExpandConnection();else if(name.StartsWith("Speech") && name!="SpeechVoice")synthesis.ExpandConnection();else if(name.StartsWith("LiveService"))live.ExpandConnection();}
        bool SaveTextConnection()
        {
            try{
                var selected=text.ReadConnectionProvider();ValidateAddress(selected.Connection,ApiClient.ChatPath(selected.Connection));
                var saved=pet.Settings.TextProviders.Find(p=>p.Id==selected.Id);
                var provider=saved==null?new TextProviderSettings{Id=selected.Id}:saved.Copy();
                string model=provider.Connection.Model;provider.Name=selected.Name;provider.BrandId=selected.BrandId;provider.Connection=selected.Connection.Copy();provider.Connection.Model=model;
                if(saved==null)pet.Settings.TextProviders.Add(provider);else pet.Settings.TextProviders[pet.Settings.TextProviders.IndexOf(saved)]=provider;
                pet.Settings.ActiveTextProviderId=provider.Id;pet.Settings.Text=provider.Connection.Copy();pet.SaveSettings();text.ConnectionSaved(pet.Settings);pet.RefreshChatAppearance();
                notice.Text=String.IsNullOrEmpty(pet.Notice)?"连接已保存，获取列表后逐个添加需要的模型。":pet.Notice;return String.IsNullOrEmpty(pet.Notice);
            }catch(Exception e){ShowError(e);return false;}
        }
        public void RefreshPersonaVoices()
        {
            voiceDrafts.Clear();voiceSavedValues.Clear();speechContext=null;liveContext=null;
            voice.Text=pet.Settings.VoiceName;liveVoice.Text=pet.Settings.LiveVoice;RefreshVoices(true);
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
                advice.Text=pet.Skin.Name+" · "+(recommended.Length==0?"此模型未提供预置音色，可手动填写。":"首次推荐："+recommended+"，已有选择优先保留。")+
                    (pet.Settings.ActiveVoiceRecommendations==null?"":"\n"+pet.Settings.ActiveVoiceRecommendations.Description);
            }
        }
        void SaveVoiceDrafts(string kind=null)
        {
            foreach(var pair in voiceDrafts){int split=pair.Key.IndexOf(':');string service=pair.Key.Substring(0,split);if(kind==null || service==kind){if(pair.Value.Trim().Length>512)throw new InvalidOperationException("音色 ID 不能超过 512 个字符。");PersonaProfiles.SaveVoiceChoice(pet.Settings,service,pair.Key.Substring(split+1),pair.Value);}}
        }
        void ClearVoiceDrafts(string kind=null){var keys=new System.Collections.Generic.List<string>(voiceDrafts.Keys);foreach(string key in keys)if(kind==null || key.StartsWith(kind+":")){voiceSavedValues[key]=voiceDrafts[key].Trim();voiceDrafts.Remove(key);}if(kind==null || kind=="speech")voiceSavedValues["speech:"+speechContext]=voice.Text.Trim();if(kind==null || kind=="live")voiceSavedValues["live:"+liveContext]=liveVoice.Text.Trim();}
        void SetCloud(bool value){if(voice!=null)voice.IsEnabled=value;}
        public bool Save()
        {
            try
            {
                if(images.HasUnsavedChanges&&!images.Save())return false;
                var nextProviders=text.ReadProviders();var nextAsrProviders=recognition.ReadProviders();var nextSpeechProviders=synthesis.ReadProviders();var nextLiveProviders=live.ReadProviders();var nextText=text.Read();var nextAsr=recognition.Read();var nextSpeech=synthesis.Read();var nextLive=live.Read();
                if(voice.Text.Trim().Length>512 || liveVoice.Text.Trim().Length>512)throw new InvalidOperationException("音色 ID 不能超过 512 个字符。");
                foreach(var provider in nextProviders)ValidateAddress(provider.Connection,ApiClient.ChatPath(provider.Connection));ValidateAddress(nextAsr,ApiClient.AsrPath(nextAsr));ValidateAddress(nextSpeech,ApiClient.SpeechPath(nextSpeech));
                var probe=nextLive.Copy();if(!String.IsNullOrWhiteSpace(probe.BaseUrl)){if(String.IsNullOrWhiteSpace(probe.Model))probe.Model="validation";if(String.IsNullOrEmpty(probe.ApiKey))probe.ApiKey="validation";GeminiLiveSocket.Endpoint(probe);}
                PersonaProfiles.SaveVoices(pet.Settings);
                pet.Settings.TextProviders=nextProviders;pet.Settings.ActiveTextProviderId=text.SelectedProviderId;pet.Settings.Text=nextText;pet.Settings.ApplyProviders("asr",nextAsrProviders,recognition.SelectedProviderId,nextAsr);pet.Settings.ApplyProviders("speech",nextSpeechProviders,synthesis.SelectedProviderId,nextSpeech);pet.Settings.ApplyProviders("live",nextLiveProviders,live.SelectedProviderId,nextLive);pet.Settings.UseCloudVoice=cloud.IsChecked==true;pet.Settings.VoiceName=voice.Text.Trim();pet.Settings.LiveVoice=liveVoice.Text.Trim();SaveVoiceDrafts();PersonaProfiles.SaveVoices(pet.Settings);for(int index=0;index<4;index++)InvalidateGuideTest(index);pet.SaveSettings();ClearVoiceDrafts();text.RefreshSaved(pet.Settings);recognition.RefreshSaved(pet.Settings);synthesis.RefreshSaved(pet.Settings);live.RefreshSaved(pet.Settings);pet.RefreshChatAppearance();notice.Text=String.IsNullOrEmpty(pet.Notice)?"已保存。打开对话开始使用。":pet.Notice;return String.IsNullOrEmpty(pet.Notice);
            }
            catch(Exception e){ShowError(e);return false;}
        }
        public bool ValidateGuideSection(int index,out string error)
        {
            try
            {
                var draft=pet.Settings.Copy();ConnectionSettings service;
                if(index==0){var provider=text.ReadConnectionProvider();service=provider.Connection;ProxyConfiguration.Endpoint(ProxyConfiguration.Normalize(service.ProxyAddress));var probe=service.Copy();probe.Model="guide-validation";ApiClient.ValidateConnection(probe,ApiClient.ChatPath(probe));if(String.IsNullOrWhiteSpace(service.Model) || !provider.Models.Contains(service.Model))throw new InvalidOperationException("请获取列表并逐个添加模型，或手填模型后点击“添加 / 更新”，再选择要使用的模型。");}
                else if(index==1){draft.Transcription=recognition.Read();service=draft.TranscriptionConnection();ApiClient.ValidateConnection(service,ApiClient.AsrPath(service));}
                else if(index==2 && cloud.IsChecked==true){draft.Speech=synthesis.Read();service=draft.SpeechConnection();ApiClient.ValidateConnection(service,ApiClient.SpeechPath(service));if(String.IsNullOrWhiteSpace(voice.Text) || voice.Text.Trim().Length>512)throw new InvalidOperationException("请选择或填写有效音色。");}
                else if(index==3){GeminiLiveSocket.Endpoint(live.Read());if(String.IsNullOrWhiteSpace(liveVoice.Text) || liveVoice.Text.Trim().Length>512)throw new InvalidOperationException("请选择或填写 Live 音色。");}
                error="";return true;
            }
            catch(Exception e){error=e is ApiException || e is InvalidOperationException?e.Message:"请检查当前服务的填写内容。";return false;}
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
                    if(draft.UseCloudVoice){var service=draft.SpeechConnection();ApiClient.ValidateConnection(service,ApiClient.SpeechPath(service));if(draft.VoiceName.Length==0 || draft.VoiceName.Length>512)throw new InvalidOperationException("请选择或填写有效音色。");}
                    pet.Settings.UseCloudVoice=draft.UseCloudVoice;pet.Settings.ApplyProviders("speech",synthesis.ReadProviders(),synthesis.SelectedProviderId,draft.Speech);pet.Settings.VoiceName=draft.VoiceName;PersonaProfiles.SaveVoices(pet.Settings);
                }
                else
                {
                    draft.Live=live.Read();GeminiLiveSocket.Endpoint(draft.Live);draft.LiveVoice=liveVoice.Text.Trim();if(draft.LiveVoice.Length==0 || draft.LiveVoice.Length>512)throw new InvalidOperationException("请选择或填写 Live 音色。");
                    pet.Settings.ApplyProviders("live",live.ReadProviders(),live.SelectedProviderId,draft.Live);pet.Settings.LiveVoice=draft.LiveVoice;PersonaProfiles.SaveVoices(pet.Settings);
                }
                InvalidateGuideTest(index);if(index==2 || index==3){SaveVoiceDrafts(index==2?"speech":"live");PersonaProfiles.SaveVoices(pet.Settings);}pet.SaveSettings();if(index==2 || index==3)ClearVoiceDrafts(index==2?"speech":"live");text.RefreshSaved(pet.Settings);recognition.RefreshSaved(pet.Settings);synthesis.RefreshSaved(pet.Settings);live.RefreshSaved(pet.Settings);pet.RefreshChatAppearance();notice.Text=String.IsNullOrEmpty(pet.Notice)?"当前引导项已保存。":pet.Notice;return String.IsNullOrEmpty(pet.Notice);
            }
            catch(Exception e){ShowError(e);return false;}
        }
        static void ValidateAddress(ConnectionSettings settings,string path)
        {if(String.IsNullOrWhiteSpace(settings.BaseUrl))return;var probe=settings.Copy();probe.Model="validation";ApiClient.Endpoint(probe,path);if(settings.ApiKey.IndexOfAny(new[]{'\r','\n'})>=0)throw new ApiException("密钥中有换行，请重新粘贴。");}
        void ShowError(Exception e){notice.Text=e is OperationCanceledException?"朗读已停止。":e is ApiException || e is InvalidOperationException?e.Message:e is CryptographicException?"当前 Windows 用户无法加密密钥。":"设置或朗读未完成，请检查配置与输出设备。";}
        internal sealed class ConnectionEditor : StackPanel
        {
            readonly TextBox address,model,proxyAddress;readonly PasswordBox key;readonly ComboBox protocol;readonly CheckBox proxy,streamReplies;readonly string kind;readonly TextBlock hint;
            public event Action Changed;public event Action<string> FactorySelected;bool applyingPreset;
            public ConnectionEditor(string title,string prefix,string serviceKind,ConnectionSettings settings)
            {
                Name=prefix+"GuideSection";kind=serviceKind;var heading=Ui.Text(title,17);heading.FontWeight=FontWeights.SemiBold;Children.Add(heading);Children.Add(Ui.Text(kind=="text" ? "选择常用服务，或接入自己的兼容接口。" : kind=="live" ? "边说边听，支持字幕与语音打断。" : kind=="asr" ? "把录音转成文字，再交给当前对话模型。" : "给伙伴选一副声音。",12,Ui.Muted));
                address=Ui.Field(settings.BaseUrl);address.Name=prefix+"Url";model=Ui.Field(settings.Model);model.Name=prefix+"Model";
                key=new PasswordBox{Name=prefix+"Key",Password=settings.ApiKey,MinHeight=38,Padding=new Thickness(10,7,10,7),BorderBrush=Ui.Line,Background=ThemeManager.FieldSurface};
                protocol=new ComboBox{Name=prefix+"Protocol",ItemsSource=kind=="text" ? new[]{"OpenAI Chat Completions","Anthropic Messages"} : kind=="live" ? new[]{"Gemini Live WebSocket"} : new[]{"自动识别（MiMo 模型使用 MiMo 协议）","OpenAI Audio","MiMo Chat Audio"},MinHeight=38,Padding=new Thickness(8)};
                protocol.SelectedIndex=kind=="text" ? (settings.Protocol=="anthropic"?1:0) : kind=="live"?0:settings.Protocol=="mimo"?2:settings.Protocol=="openai"?1:0;
                proxy=Ui.Toggle("使用代理连接",settings.UseProxy,v=>{});proxy.Name=prefix+"Proxy";
                proxyAddress=Ui.Field(ProxyConfiguration.Normalize(settings.ProxyAddress));proxyAddress.Name=prefix+"ProxyAddress";
                var presets=new ComboBox{Name=prefix+"Preset",ItemsSource=ServicePresets.For(kind),SelectedIndex=0,MinHeight=38,Padding=new Thickness(8)};
                hint=Ui.Text(kind=="live"?"支持 WSS；本机测试可使用 ws://127.0.0.1。切换到其他服务时会清空密钥。":"支持 HTTPS，或本机／局域网 IP 的 HTTP 地址。切换到其他服务时会清空密钥。",11,Ui.Muted);hint.Margin=new Thickness(0,7,0,3);
                string keyOrigin=address.Text;
                key.PasswordChanged+=delegate {keyOrigin=address.Text;};
                address.TextChanged+=delegate {if(key.Password.Length>0 && !ServicePresets.SameOrigin(keyOrigin,address.Text)){key.Clear();hint.Text="服务地址已改变，请填写该服务的 API 密钥。";}};
                presets.SelectionChanged+=delegate {var preset=presets.SelectedItem as ServicePreset;if(preset==null)return;if(FactorySelected!=null)FactorySelected(preset.Url.Length==0?"custom":ProviderIdentity.Brand(preset.Url));if(preset.Url.Length==0){if(Changed!=null)Changed();return;}applyingPreset=true;if(!ServicePresets.SameOrigin(address.Text,preset.Url))key.Clear();address.Text=preset.Url;model.Text=preset.Model;protocol.SelectedIndex=kind=="text"?(preset.Protocol=="anthropic"?1:0):kind=="live"?0:preset.Protocol=="mimo"?2:1;hint.Text=preset.Note.Length==0 ? "已填入示例模型，请确认账号可用。" : preset.Note;applyingPreset=false;if(Changed!=null)Changed();};
                streamReplies=Ui.Toggle("流式回复",settings.StreamReplies,v=>{});streamReplies.Name=prefix+"StreamReplies";
                Children.Add(Pair("服务预设",presets,"接口协议",protocol));
                Ui.Label(this,kind=="live"?"完整 WSS 地址":"服务地址（保留 /v1 等前缀）",address);
                if(kind=="text"){Ui.Label(this,"API 密钥",key);model.Visibility=Visibility.Collapsed;Children.Add(model);}else Children.Add(Pair("API 密钥",key,"当前模型 ID",model));
                var toggles=new WrapPanel();proxy.Margin=new Thickness(0,8,24,4);toggles.Children.Add(proxy);
                if(kind=="text"){streamReplies.Margin=new Thickness(0,8,0,4);toggles.Children.Add(streamReplies);}Children.Add(toggles);
                Ui.Label(this,"代理地址（仅开启代理时使用）",proxyAddress);Children.Add(hint);
                address.TextChanged+=delegate {if(!applyingPreset && Changed!=null)Changed();};model.TextChanged+=delegate {if(!applyingPreset && Changed!=null)Changed();};protocol.SelectionChanged+=delegate {if(!applyingPreset && Changed!=null)Changed();};
                key.PasswordChanged+=delegate{if(!applyingPreset && Changed!=null)Changed();};proxy.Checked+=delegate{if(Changed!=null)Changed();};proxy.Unchecked+=delegate{if(Changed!=null)Changed();};proxyAddress.TextChanged+=delegate{if(Changed!=null)Changed();};streamReplies.Checked+=delegate{if(Changed!=null)Changed();};streamReplies.Unchecked+=delegate{if(Changed!=null)Changed();};
            }
            static Grid Pair(string firstLabel,UIElement first,string secondLabel,UIElement second)
            {
                var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());
                var left=new StackPanel{Margin=new Thickness(0,0,6,0)};var right=new StackPanel{Margin=new Thickness(6,0,0,0)};
                Ui.Label(left,firstLabel,first);Ui.Label(right,secondLabel,second);Grid.SetColumn(right,1);row.Children.Add(left);row.Children.Add(right);return row;
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
