using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
 public static class ProviderUiSmoke
 {
        static void AddDiscovered(FrameworkElement root)
        {
            var list=FindIn<ListBox>(root,"DiscoveredTextModels");var item=(ListBoxItem)list.Items[0];
            root.Dispatcher.BeginInvoke(new Action(delegate{var dialog=Application.Current.Windows.OfType<Window>().First(w=>w.Title=="添加模型");FindIn<Button>(dialog,"AddTextModel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}),DispatcherPriority.Background);
            FindIn<Button>(item,"AddDiscoveredModel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        }
  static int passed;
  [STAThread] public static int Main()
  {
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
   string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"provider-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
   PetWindow pet=null;SettingsWindow window=null;
   try {
    CheckAudioPages(folder);
    CheckSeparateVoiceTests(folder);
    CheckCatalog(folder);
    CheckCatalogLifecycle();
    CheckCatalogInvalidation();
    CheckNewProviderFlow(folder);
    var cfg=new PetSettings{GreetingEnabled=false,AutoUpdate=false,FollowCursor=false,HideOnFullscreen=false,SaveChatHistory=false};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
    cfg.Text=new ConnectionSettings{BaseUrl="https://first.example/v1",Model="first-a",ApiKey="first-secret"};
    var store=new SettingsStore(Path.Combine(folder,"settings.json"));pet=new PetWindow(null,cfg,store,null);pet.Show();window=new SettingsWindow(pet);window.Show();window.SelectAiSection(0);Pump();
    Check(Find<ListBox>(window,"TextProviderList").Items.Count==1,"legacy provider shown");
    Find<TextBox>(window,"TextProviderName").Text="主要服务";Find<TextBox>(window,"TextProviderNewModel").Text="first-b";Find<TextBox>(window,"TextModelDisplayName").Text="深度思考";Click(window,"AddTextModel");
    Check(Find<ListBox>(window,"TextProviderModels").Items.Count==2,"second model added to first provider");
    Click(window,"AddTextProvider");Find<TextBox>(window,"TextProviderName").Text="备用服务";Find<TextBox>(window,"TextServiceUrl").Text="https://second.example/v1";Find<PasswordBox>(window,"TextServiceKey").Password="second-secret";Find<TextBox>(window,"TextProviderNewModel").Text="second-a";Click(window,"AddTextModel");
    Find<TextBox>(window,"TextServiceProxyAddress").Text="unfinished proxy";
    Find<ListBox>(window,"TextProviderList").SelectedIndex=0;Pump();
    Check(Find<TextBox>(window,"TextServiceModel").Text=="first-b" && Find<PasswordBox>(window,"TextServiceKey").Password=="first-secret","provider switch preserves separate model and key drafts");
    Find<ListBox>(window,"TextProviderList").SelectedIndex=1;Pump();Check(Find<TextBox>(window,"TextServiceProxyAddress").Text=="unfinished proxy","invalid unfinished draft survives navigation");
    Find<TextBox>(window,"TextServiceProxyAddress").Text="127.0.0.1:7890";Click(window,"SaveConnections");
    Check(pet.Settings.TextProviders.Count==2 && pet.Settings.Text.Model=="second-a" && pet.Settings.Text.ApiKey=="second-secret","save activates selected provider connection");
    Check(!window.HasUnsavedEdits,"saved providers are clean");
    Find<TextBox>(window,"TextProviderNewModel").Text="second-pending";Find<TextBox>(window,"TextModelDisplayName").Text="待保存模型";
    pet.Settings.SelectTextModel(pet.Settings.TextProviders[0].Id,"first-b");window.RefreshTextProviders();Pump();
    Check(Find<TextBox>(window,"TextProviderNewModel").Text=="second-pending" && Find<TextBox>(window,"TextModelDisplayName").Text=="待保存模型" && window.HasUnsavedEdits,"external selection preserves pending new model ID and alias before Add");
    Find<ListBox>(window,"TextProviderList").SelectedIndex=0;Find<ListBox>(window,"TextProviderList").SelectedIndex=1;Pump();
    Check(Find<TextBox>(window,"TextProviderNewModel").Text=="second-pending" && Find<TextBox>(window,"TextModelDisplayName").Text=="待保存模型","provider navigation preserves staged model ID and alias");
    Find<ListBox>(window,"TextProviderModels").SelectedValue="second-pending";Click(window,"RemoveTextModel");Click(window,"SaveConnections");
    Check(pet.Settings.TextProviders[0].DisplayName("first-b")=="深度思考","Chinese model alias saved independently from ID");
    pet.Settings.SelectTextModel(pet.Settings.TextProviders[0].Id,"first-b");window.RefreshTextProviders();Pump();Check(Find<TextBox>(window,"TextProviderName").Text=="主要服务" && !window.HasUnsavedEdits,"external model selection refreshes a clean settings draft");
    Find<TextBox>(window,"TextModelDisplayName").Text="长文思考";pet.Settings.SelectTextModel(pet.Settings.TextProviders[1].Id,"second-a");window.RefreshTextProviders();Pump();Check(Find<TextBox>(window,"TextModelDisplayName").Text=="长文思考" && window.HasUnsavedEdits,"external model selection preserves an edited display-name draft");
    Click(window,"SaveConnections");Check(pet.Settings.TextProviders[0].DisplayName("first-b")=="长文思考","editing existing model alias saves without changing ID");
    Find<ListBox>(window,"TextProviderList").SelectedIndex=1;Click(window,"SaveConnections");
    var saved=store.Load();Check(saved.TextProviders.Count==2 && saved.TextProviders[0].Models.Count==2 && saved.TextProviders[1].Connection.ApiKey=="second-secret","provider drafts persisted on disk");
    window.Close();window=new SettingsWindow(pet);window.Show();window.SelectAiSection(0);Pump();Check(Find<TextBox>(window,"TextProviderName").Text=="备用服务","reopen restores active provider");
    Capture(window,Path.Combine(folder,"providers-normal.png"));
    window.Width=700;window.Height=430;Pump();Check(Inside(Find<Button>(window,"SaveConnections"),window),"save visible at minimum dimensions");Check(Inside(Find<Button>(window,"AiLiveTab"),window),"all service categories remain reachable");
    Check(window.FocusGuideTarget("TextServiceModel")!=null,"existing guide locates editable model field");Pump();Capture(window,Path.Combine(folder,"providers-minimum.png"));
    window.Width=960;window.Height=790;window.SelectAiSection(0);Pump();
    Find<ListBox>(window,"TextProviderList").SelectedIndex=0;Pump();Find<ListBox>(window,"TextProviderModels").SelectedValue="first-b";Click(window,"RemoveTextModel");Click(window,"SaveConnections");
    Check(pet.Settings.Text.Model=="first-a" && pet.Settings.TextProviders[0].Models.Count==1,"remove active model falls back within provider");
    Find<ListBox>(window,"TextProviderList").SelectedIndex=1;Click(window,"RemoveTextProvider");Click(window,"SaveConnections");Check(pet.Settings.TextProviders.Count==1 && pet.Settings.Text.ApiKey=="first-secret","remove provider retains valid remaining provider");
    Check(!Find<Button>(window,"RemoveTextProvider").IsEnabled,"last provider cannot be removed");
    Console.WriteLine("ProviderUiSmoke: "+passed+" passed; previews: "+folder);return 0;
   }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
   finally{if(window!=null)window.Close();if(pet!=null)pet.Close();Pump();app.Shutdown();}
  }
  static T Find<T>(SettingsWindow w,string name) where T:FrameworkElement {if(name.StartsWith("TextService")){var details=w.FindControl("TextConnectionDetails") as Expander;if(details!=null)details.IsExpanded=true;w.UpdateLayout();}var result=w.FindControl(name) as T;if(result==null)throw new Exception("Missing "+name);return result;}
  static void CheckCatalog(string folder)
  {
   using(var server=new ApiTests.Fixture("{\"data\":[{\"id\":\"remote-b\"},{\"id\":\" remote-a \"},{\"id\":\"remote-a\"}]}"))
   {
    var cfg=new PetSettings{Text=new ConnectionSettings{BaseUrl=server.Url+"/v1/chat/completions",Model="manual-model",ApiKey="catalog-test-key"}};
    var editor=new TextProviderEditor(cfg);var host=new Window();Ui.Window(host,"模型列表测试",740,800);host.Content=Ui.Scroll(editor);
    try {
     host.Show();Pump();var search=FindIn<TextBox>(editor,"TextModelSearch");Check(search!=null,"model list exposes a search field");
     Until(()=>FindIn<ListBox>(editor,"DiscoveredTextModels")!=null && FindIn<ListBox>(editor,"DiscoveredTextModels").Items.Count==2);
     Check(server.Headers.StartsWith("GET /v1/models ") && server.Authorization=="Bearer catalog-test-key","configured provider automatically retrieves its model list without a selected remote model");
     var list=FindIn<ListBox>(editor,"TextProviderModels");Check(list.Items.Count==1,"fetching lists candidates without automatically adding models");
     Check(FindCheckBox(FindIn<ListBox>(editor,"DiscoveredTextModels"))==null && FindIn<Button>(editor,"AddSelectedTextModels")==null,"discovery has no checkbox or bulk-add control");
     AddDiscovered(editor);Check(list.Items.Count==2,"one plus click adds exactly one model");AddDiscovered(editor);
     Check(list.Items.Count==3,"multiple fetched models can be explicitly selected and added while the manual model survives");
     list.SelectedValue="remote-b";FindIn<TextBox>(editor,"TextModelDisplayName").Text="深度思考";
     search.Text="深度";Pump();Check(list.Items.Count==1 && (string)((ListBoxItem)list.Items[0]).Tag=="remote-b","model search matches a custom Chinese alias");
     search.Text="remote-a";Pump();Check(list.Items.Count==1 && editor.Preview().Model=="remote-b","filtering never changes the active model");
     search.Text="nothing-matches";Pump();Check(list.Items.Count==0 && FindIn<TextBlock>(editor,"TextModelsEmpty").IsVisible,"empty search has a visible recovery hint");
     search.Text="";Pump();Capture(host,Path.Combine(folder,"models-auto-fetched.png"));
     var providers=editor.ReadProviders();Check(providers[0].Models.Count==3 && providers[0].DisplayName("remote-b")=="深度思考","fetched models and personal aliases remain saveable");
     FindIn<TextBox>(editor,"TextProviderNewModel").Text="manual-model";Pump();
     var activeRow=list.Items.Cast<ListBoxItem>().Single(i=>(string)i.Tag=="manual-model");
     Check(((DockPanel)activeRow.Content).Children.OfType<Border>().Any(),"typing a model ID moves the current-selection badge immediately");
     ChooseProvider(editor,"deepseek");
     var presets=FindIn<ComboBox>(editor,"TextServicePreset");presets.SelectedItem=presets.Items.Cast<ServicePreset>().Single(p=>p.Name=="DeepSeek");Pump();
     Check(FindIn<TextBox>(editor,"TextProviderName").Text=="DeepSeek" && FindIn<Image>(editor,"ProviderLogo_deepseek")!=null,"selecting a provider preset names the service and displays its brand logo");
     FindIn<TextBox>(editor,"TextServiceUrl").Text="https://my-gateway.example/v1";Pump();
     Check(FindIn<Image>(editor,"ProviderLogo_deepseek")!=null && FindIn<TextBox>(editor,"TextProviderName").Text=="DeepSeek","a chosen manufacturer's logo and default name survive a custom gateway address");
     FindIn<TextBox>(editor,"TextProviderName").Text="自定义服务 企业网关";FindIn<ListBox>(editor,"TextProviderList").SelectedIndex=0;FindIn<ListBox>(editor,"TextProviderList").SelectedIndex=1;
     presets=FindIn<ComboBox>(editor,"TextServicePreset");presets.SelectedItem=presets.Items.Cast<ServicePreset>().Single(p=>p.Name=="MiniMax");Pump();
     Check(FindIn<TextBox>(editor,"TextProviderName").Text=="自定义服务 企业网关" && FindIn<Image>(editor,"ProviderLogo_minimax")!=null,"changing endpoints after navigation preserves a custom provider name and updates its logo");
    } finally{host.Close();Pump();}
   }
  }
  static T FindIn<T>(DependencyObject root,string name) where T:FrameworkElement {var element=root as T;if(element!=null && element.Name==name)return element;foreach(object child in LogicalTreeHelper.GetChildren(root)){var node=child as DependencyObject;if(node==null)continue;var result=FindIn<T>(node,name);if(result!=null)return result;}return null;}
  static void CheckCatalogLifecycle()
  {
   foreach(bool delayed in new[]{false,true})
   using(var server=new ApiTests.Fixture(delayed?"{\"data\":[{\"id\":\"late-model\"}]}":"private-body-secret",delayed?200:401))
   {
    server.DelayMilliseconds=delayed?1800:0;
    var cfg=new PetSettings{Text=new ConnectionSettings{BaseUrl=server.Url+"/v1",Model="manual-model",ApiKey="local-test-key"}};
    var editor=new TextProviderEditor(cfg);var host=new Window();Ui.Window(host,"模型列表生命周期测试",740,760);host.Content=Ui.Scroll(editor);
    try {
     host.Show();Until(()=>server.Path!=null);
     if(delayed)
     {
      ChooseProvider(editor,"custom");
      var end=DateTime.UtcNow.AddSeconds(2.2);while(DateTime.UtcNow<end)Pump();
      Check(FindIn<ListBox>(editor,"TextProviderModels").Items.Count==0 && editor.ReadProviders()[0].Models.Count==1,"late model response cannot mutate a newly selected provider or its previous draft");
      FindIn<ListBox>(editor,"TextProviderList").SelectedIndex=0;
      Until(()=>FindIn<ProgressBar>(editor,"TextModelsLoading").Visibility==Visibility.Visible);
      Check(true,"returning to the cancelled provider automatically retries its model request");
     }
     else
     {
      Until(()=>FindIn<TextBlock>(editor,"TextProviderNotice").Text.Contains("401"));
      Check(FindIn<ListBox>(editor,"TextProviderModels").Items.Count==1 && !FindIn<TextBlock>(editor,"TextProviderNotice").Text.Contains("private-body-secret") && FindIn<Button>(editor,"FetchTextModels").IsEnabled,"fetch failure preserves manual models and offers retry without exposing the response body");
     }
    }finally{host.Close();Pump();}
   }
  }
  static CheckBox FindCheckBox(DependencyObject root){if(root is CheckBox)return (CheckBox)root;foreach(object child in LogicalTreeHelper.GetChildren(root)){var node=child as DependencyObject;if(node!=null){var found=FindCheckBox(node);if(found!=null)return found;}}return null;}
  static void Until(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(6);while(!done() && DateTime.UtcNow<end)Pump();Check(done(),"automatic model request completes");}
  static void CheckCatalogInvalidation()
  {
   using(var first=new ApiTests.Fixture("{\"data\":[{\"id\":\"old-candidate\"}]}"))
   using(var second=new ApiTests.Fixture("private-error",401))
   {
    var cfg=new PetSettings{Text=new ConnectionSettings{BaseUrl=first.Url+"/v1",Model="manual-model",ApiKey="test-key"}};
    var editor=new TextProviderEditor(cfg);var host=new Window();Ui.Window(host,"候选列表切换测试",740,760);host.Content=Ui.Scroll(editor);
    try {
     host.Show();Until(()=>FindIn<ListBox>(editor,"DiscoveredTextModels").Items.Count==1);
     Check(FindIn<Button>((ListBoxItem)FindIn<ListBox>(editor,"DiscoveredTextModels").Items[0],"AddDiscoveredModel")!=null,"candidate has an individual add action");
     FindIn<TextBox>(editor,"TextServiceUrl").Text=second.Url+"/v1";Pump();
     Check(FindIn<ListBox>(editor,"DiscoveredTextModels").Items.Count==0,"changing a connection immediately clears old candidates and checked selections");
     Until(()=>FindIn<TextBlock>(editor,"TextProviderNotice").Text.Contains("401"));
     Check(editor.ReadProviders()[0].Models.Count==1,"a failed replacement endpoint cannot add stale candidates");
    }finally{host.Close();Pump();}
   }
  }
  static void CheckNewProviderFlow(string folder)
  {
   using(var server=new ApiTests.Fixture("{\"data\":[{\"id\":\"model-alpha\"},{\"id\":\"model-beta\"}]}"))
   {
    var cfg=new PetSettings{GreetingEnabled=false,AutoUpdate=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,SaveChatHistory=false,ReadReplies=false};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
    cfg.Text=new ConnectionSettings{BaseUrl="https://first.example/v1",Model="manual-first",ApiKey="first-secret"};
    var store=new SettingsStore(Path.Combine(folder,"new-provider-settings.json"));var pet=new PetWindow(null,cfg,store,null);var window=new SettingsWindow(pet);
    try
    {
     pet.Show();window.Show();window.SelectAiSection(0);Pump();ChooseProvider(window,"deepseek",Path.Combine(folder,"provider-picker.png"));
     Find<TextBox>(window,"TextServiceUrl").Text=server.Url+"/v1";Find<PasswordBox>(window,"TextServiceKey").Password="new-provider-secret";Pump();
     Check(server.Path==null,"a new provider waits for save before requesting models");
     Find<ListBox>(window,"TextProviderList").SelectedIndex=0;Find<TextBox>(window,"TextProviderNewModel").Text="other-provider-draft";Click(window,"AddTextModel");Find<ListBox>(window,"TextProviderList").SelectedIndex=1;
     Click(window,"SaveAndFetchTextModels");Until(()=>Find<ListBox>(window,"DiscoveredTextModels").Items.Count==2);
     var saved=store.Load();var active=saved.TextProviders.Single(p=>p.Id==saved.ActiveTextProviderId);
     Check(active.BrandId=="deepseek" && active.Name=="DeepSeek" && active.Connection.ApiKey=="new-provider-secret" && active.Models.Count==0 && !File.ReadAllText(Path.Combine(folder,"new-provider-settings.json")).Contains("new-provider-secret"),"save and fetch persists encrypted connection before any model is added");
     Check(saved.TextProviders[0].Models.SequenceEqual(new[]{"manual-first"}),"saving a connection does not commit another provider's unfinished model draft");
     Capture(window,Path.Combine(folder,"models-select.png"));
     AddDiscovered(window);AddDiscovered(window);Check(Find<ListBox>(window,"TextProviderModels").Items.Count==2,"chosen multiple models enter the configured list");
     Check(store.Load().TextProviders.Single(p=>p.Id==saved.ActiveTextProviderId).Models.Count==0,"adding models remains a draft until final save");
     Click(window,"SaveConnections");saved=store.Load();active=saved.TextProviders.Single(p=>p.Id==saved.ActiveTextProviderId);
     Check(active.Models.Count==2 && active.Models.Contains(saved.Text.Model),"final save persists the selected models and a usable current model");Capture(window,Path.Combine(folder,"models-configured.png"));
    }
    finally{window.Close();pet.Close();Pump();}
   }
  }
  static void CheckAudioPages(string folder)
  {
   var cfg=new PetSettings{GreetingEnabled=false,AutoUpdate=false,SaveChatHistory=false};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
   var store=new SettingsStore(Path.Combine(folder,"audio-settings.json"));var pet=new PetWindow(null,cfg,store,null);var window=new SettingsWindow(pet);
   try{
    window.Show();window.SelectAiSection(0);Pump();Check(window.FindControl("PreviewSpeech")==null && window.FindControl("PreviewLive")==null,"text page has no permanent voice test controls");
    for(int index=1;index<=3;index++){
     string role=index==1?"Asr":index==2?"Speech":"Live",kind=index==1?"asr":index==2?"speech":"live",prefix=index==1?"Transcription":index==2?"Speech":"LiveService";
     string model=index==1?"mimo-v2.5-asr":index==2?"mimo-v2.5-tts":"gemini-3.8-live";
     using(var server=new ApiTests.Fixture("{\"data\":[]}")){
      window.SelectAiSection(index);Pump();Check(Find<ListBox>(window,role+"ProviderList").Items.Count==1,role+" uses the shared provider rail");
      ChooseProvider(window,index==3?"gemini":"xiaomimimo",Path.Combine(folder,kind+"-picker.png"),"Add"+role+"Provider");
      Check(Find<TextBox>(window,prefix+"Model").Text==model,role+" pre-fills the correct manufacturer model");
      Check(window.FindControl("Fetch"+role+"Models")==null && window.FindControl("SaveAndFetch"+role+"Models")==null && window.FindControl(role+"ModelSearch")==null && window.FindControl("ReuseTextForAsr")==null && window.FindControl("ReuseTextForSpeech")==null,role+" has no model discovery or shared-key controls");
      if(index==2){Find<CheckBox>(window,"UseCloudVoice").IsChecked=true;var voices=Find<ComboBox>(window,"SpeechVoice");Check(voices.IsEditable && voices.Items.Cast<VoicePreset>().Any(v=>v.Id=="Mia"),"speech retains actual MiMo voices");Check(Find<Button>(window,"PreviewSpeech").Parent!=Find<Button>(window,"SaveConnections").Parent && window.FindControl("PreviewLive")==null,"speech test belongs only to the speech page body");}
      if(index==3)Check(Find<Button>(window,"PreviewLive")!=null && window.FindControl("PreviewSpeech")==null && !pet.LiveActive,"Live has its own test button without automatically connecting");
      if(index==1)Check(window.FindControl("PreviewSpeech")==null && window.FindControl("PreviewLive")==null,"ASR page has no TTS or Live test button");
      Find<TextBox>(window,prefix+"Url").Text=index==3?server.Url.Replace("http://","ws://")+"/ws/test":server.Url+"/v1";Find<PasswordBox>(window,prefix+"Key").Password="audio-provider-secret";
      Find<TextBox>(window,prefix+"Model").Text=model+"-custom";
      var end=DateTime.UtcNow.AddSeconds(1.2);while(DateTime.UtcNow<end)Pump();Check(server.Path==null,role+" never requests a model catalog");
      Click(window,"SaveConnections");var saved=store.Load().ProviderView(kind);
      Check(saved.TextProviders.Count==2 && saved.Text.Model==model+"-custom" && saved.Text.ApiKey=="audio-provider-secret",role+" saves an editable model and independent manufacturer configuration");
      Check(!File.ReadAllText(Path.Combine(folder,"audio-settings.json")).Contains("audio-provider-secret"),role+" encrypts provider keys");
      Find<TextBox>(window,prefix+"Model").Text=model;Click(window,"SaveConnections");Capture(window,Path.Combine(folder,kind+"-configured.png"));
      if(index==2){Find<ListBox>(window,"SpeechProviderList").SelectedIndex=0;Find<ComboBox>(window,"SpeechVoice").Text="voice-draft-a";Find<ListBox>(window,"SpeechProviderList").SelectedIndex=1;Find<ComboBox>(window,"SpeechVoice").Text="voice-draft-b";Click(window,"SaveConnections");window.Close();window=new SettingsWindow(pet);window.Show();window.SelectAiSection(2);Find<ListBox>(window,"SpeechProviderList").SelectedIndex=0;Check(Find<ComboBox>(window,"SpeechVoice").Text=="voice-draft-a","saving multiple providers retains each edited voice after reopening");Find<ListBox>(window,"SpeechProviderList").SelectedIndex=1;Check(Find<ComboBox>(window,"SpeechVoice").Text=="voice-draft-b","the second provider also restores its own edited voice");Check(!window.HasUnsavedEdits,"switching saved voice providers and returning without editing does not mark settings dirty");}
     }
    }
    window.Close();window=new SettingsWindow(pet);window.Show();
    for(int index=1;index<=3;index++){window.SelectAiSection(index);Pump();Check(Find<ListBox>(window,(index==1?"Asr":index==2?"Speech":"Live")+"ProviderList").Items.Count==2,"audio providers restore after reopening: "+index);}
   }finally{window.Close();pet.Close();Pump();}
  }
  static void CheckSeparateVoiceTests(string folder)
  {
   using(var tts=new ApiTests.Fixture(WaveData.EncodePcm(new byte[3200]),"audio/wav"))
   using(var liveServer=new LiveTests.WebSocketFixture(false)){
    var cfg=new PetSettings{GreetingEnabled=false,AutoUpdate=false,SaveChatHistory=false,UseCloudVoice=true,VoiceName="alloy",LiveVoice="Kore",Text=new ConnectionSettings{BaseUrl="invalid-text-draft"},Speech=new ConnectionSettings{BaseUrl=tts.Url+"/v1",Model="tts-1",ApiKey="tts-only-key",Protocol="openai"},Live=new ConnectionSettings{BaseUrl=liveServer.Url,Model="live-fixture-model",ApiKey="live-only-key",Protocol="gemini-live"}};
    foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
    var pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"voice-test-settings.json")),null);var window=new SettingsWindow(pet);var mic=new TestLiveMic();var output=new TestLiveOutput();
    var session=new GeminiLiveSession(()=>new GeminiLiveSocket(),()=>mic,()=>output);
    typeof(PetWindow).GetField("live",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(pet,session);
    try{
     window.Show();window.SelectAiSection(2);Pump();Click(window,"PreviewSpeech");Until(()=>Find<TextBlock>(window,"ConnectionNotice").Text.Contains("朗读测试完成"));
     Check(tts.Path=="/v1/audio/speech" && tts.Authorization=="Bearer tts-only-key" && tts.Body.Contains("tts-1") && liveServer.Setup==null && !mic.Started,"speech test performs its own HTTP TTS request and playback without opening Live or a microphone");
     window.SelectAiSection(3);Pump();Click(window,"PreviewLive");Until(()=>mic.Started);
     Check(window.Content is LiveCallView && liveServer.Setup.Contains("models/live-fixture-model") && liveServer.Setup.Contains("Kore"),"Live test uses the actual WebSocket setup with its own model and voice, even with an invalid text draft");
     mic.Emit(new byte[]{1,2,3,4});Until(()=>output.Chunks==1);
     Check(liveServer.Audio.Contains("audio/pcm;rate=16000"),"Live test sends PCM and receives Live audio separately from TTS");
     FindIn<Button>(window.Content as DependencyObject,"LiveEndCall").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>!(window.Content is LiveCallView) && !session.IsActive);
     Check(mic.Disposed && output.Disposed && window.FindControl("PreviewLive")!=null,"ending the Live test releases audio and restores its settings page");
    }finally{window.Close();pet.Close();Pump();}
   }
  }
  sealed class TestLiveMic:IPcmMicrophone{public event Action<byte[]> PcmAvailable;public event Action<string> Failed;public bool Started,Disposed;public void Start(){Started=true;}public void Emit(byte[] data){if(PcmAvailable!=null)PcmAvailable(data);}public void Dispose(){Disposed=true;}}
  sealed class TestLiveOutput:ILiveAudioOutput{public int Chunks;public bool Disposed;public void Enqueue(byte[] data,int rate){Chunks++;}public void Interrupt(){}public void SetVolume(int volume){}public void Dispose(){Disposed=true;}}
  static void ChooseProvider(DependencyObject editor,string brand,string capture=null,string addName="AddTextProvider")
  {
   Exception failure=null;
   Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{try{var picker=Application.Current.Windows.Cast<Window>().Single(w=>w.Title=="选择服务商");if(capture!=null){picker.UpdateLayout();Capture(picker,capture);}FindIn<Button>(picker,"ChooseProvider_"+brand).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}catch(Exception e){failure=e;}}));
   FindIn<Button>(editor,addName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();if(failure!=null)throw new Exception("adding a provider must open the manufacturer picker",failure);
  }
  static void Click(SettingsWindow w,string name){if(name=="AddTextProvider"){ChooseProvider(w,"custom");return;}Find<Button>(w,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
  static bool Inside(FrameworkElement e,Window w){Rect r=e.TransformToAncestor(w).TransformBounds(new Rect(e.RenderSize));return e.IsVisible && r.Left>=0 && r.Top>=0 && r.Right<=w.ActualWidth && r.Bottom<=w.ActualHeight;}
  static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
  static void Capture(Window w,string path){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(w);using(var stream=File.Create(path)){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));png.Save(stream);}}
 }
}
