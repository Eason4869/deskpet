using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ChatSmoke
    {
        static int passed;
        sealed class FixtureMicrophone:IPcmMicrophone
        {
            public event Action<byte[]> PcmAvailable;public event Action<string> Failed;
            public void Start(){if(PcmAvailable!=null)PcmAvailable(new byte[2048]);}public void Dispose(){}
        }
        static readonly System.Collections.Generic.List<string> regressions=new System.Collections.Generic.List<string>();
        [STAThread] public static int Main()
        {
            PetWindow pet=null;
            try
            {
                var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"chat-smoke",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                pet=new PetWindow(new SpriteAtlas(),new PetSettings {SkinId="dafeiyu",ReadReplies=false,FollowCursor=false,SaveChatHistory=false,GreetingEnabled=false,HideOnFullscreen=false},new SettingsStore(Path.Combine(folder,"settings.json")),null);pet.Show();Pump(.2);pet.ShowChat();Pump(.2);
                var chat=app.Windows.OfType<ChatWindow>().Single();var input=Find<MarkdownEditor>(chat,"MessageInput");var send=Find<Button>(chat,"SendMessage");
                Check(input!=null && send!=null && Find<Button>(chat,"VoiceRecord")!=null && Find<Button>(chat,"StopConversation")==null,"chat exposes text, dictation and one shared action");
                input.Text="你好";send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(.1);Check(Find<TextBlock>(chat,"ChatStatus").Text.Contains("设置"),"missing configuration points to settings");
                using(var server=new ApiTests.Fixture("{\"choices\":[{\"message\":{\"content\":\"你好，我是桌面伙伴。\"}}]}"))
                {
                    pet.Settings.Text=new ConnectionSettings {BaseUrl=server.Url+"/v1",Model="chat-test",ApiKey="dummy-key"};input.Text="你好，大肥鱼";send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Wait(delegate{return !pet.Conversation.IsBusy && pet.Conversation.History.Messages.Count==2;},6);
                    if(!All<MarkdownView>(chat).Any(t=>t.Text=="你好，我是桌面伙伴。"))Console.WriteLine("HTTP UI diagnostic: path="+server.Path+", status="+Find<TextBlock>(chat,"ChatStatus").Text+", message statuses="+String.Join(",",pet.Conversation.History.Messages.Select(m=>m.Status)));
                    Check(All<MarkdownView>(chat).Any(t=>t.Text=="你好，我是桌面伙伴。"),"actual HTTP reply renders in chat");
                    Check(server.Path=="/v1/chat/completions" && server.Body.Contains("大肥鱼"),"chat sends real Unicode model request");
                }
                Save(chat,Path.Combine(folder,"chat-window.png"));
                using(var server=new ApiTests.Fixture("{\"choices\":[{\"message\":{\"content\":\"slow\"}}]}"))
                {
                    server.DelayBodyMilliseconds=1000;pet.Settings.Text.BaseUrl=server.Url;input.Text="停止";send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(.08);
                    Find<Button>(chat,"SendMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(delegate{return !pet.Conversation.IsBusy;},3);
                    Check(pet.Conversation.History.Messages.Count==4 && pet.Conversation.History.Messages.Last().Status=="cancelled","stop retains unfinished display with cancelled status");
                }
                using(var recognition=new ApiTests.Fixture("{\"text\":\"用语音聊设计\"}"))
                using(var model=new ApiTests.Fixture("{\"choices\":[{\"message\":{\"content\":\"语音回复测试。\"}}]}"))
                using(var synthesis=new ApiTests.Fixture(WaveData.EncodePcm(new byte[16000]),"audio/wav"))
                {
                    pet.Settings.Text.BaseUrl=model.Url;pet.Settings.Transcription=new ConnectionSettings {BaseUrl=recognition.Url,Model="stt",ApiKey="voice-key"};
                    pet.Settings.Speech=new ConnectionSettings {BaseUrl=synthesis.Url,Model="tts",ApiKey="speech-key"};pet.Settings.VoiceName="test";pet.Settings.UseCloudVoice=true;pet.Settings.ReadReplies=true;
                    var voice=pet.Conversation.SendVoiceAsync(WaveData.EncodePcm(new byte[16000]),pet.Settings,pet.Skin.Name);try{Wait(delegate{return voice.IsCompleted;},10);}catch{Console.WriteLine("Voice diagnostic: stage="+pet.Conversation.Stage+", ASR="+recognition.Path+", model="+model.Path+", TTS="+synthesis.Path+", status="+Find<TextBlock>(chat,"ChatStatus").Text);throw;}voice.GetAwaiter().GetResult();
                    Check(recognition.Path=="/audio/transcriptions" && model.Path=="/chat/completions" && synthesis.Path=="/audio/speech","three separate configured services execute voice pipeline");
                    Check(pet.Conversation.History.Messages.Count==6 && All<MarkdownView>(chat).Any(t=>t.Text=="语音回复测试。"),"voice reply is retained and displayed");
                    Check(!Directory.GetFiles(Path.Combine(folder,"audio-cache"),"*.wav").Any(),"temporary spoken WAV is deleted");
                }
                chat.Close();Pump(.2);pet.ShowChat();Pump(.2);chat=app.Windows.OfType<ChatWindow>().Single();
                Check(pet.Conversation.History.Messages.Count==6 && All<MarkdownView>(chat).Any(t=>t.Text=="语音回复测试。"),"reopened chat preserves in-memory successful history");
                Check(Find<Button>(chat,"ClearChat")==null,"removed clear button is absent");pet.Conversation.Clear();Check(pet.Conversation.History.Messages.Count==0,"explicit session clearing resets context");
                pet.ShowSettings(true);Pump(.3);var settings=app.Windows.OfType<SettingsWindow>().Single();settings.FocusGuideTarget("TextServiceUrl");Pump(.1);
                var url=Find<TextBox>(settings,"TextServiceUrl");var modelField=Find<TextBox>(settings,"TextServiceModel");var key=Find<PasswordBox>(settings,"TextServiceKey");
                Check(url!=null && key!=null && Find<ComboBox>(settings,"TextServiceProtocol")!=null && Find<CheckBox>(settings,"TextServiceProxy")!=null,"text settings expose password, protocol and proxy fields");
                var textProxy=Find<TextBox>(settings,"TextServiceProxyAddress");Check(textProxy!=null && textProxy.IsEnabled && textProxy.Text==ProxyConfiguration.DefaultAddress,"text proxy address is editable with the default value");textProxy.Text="localhost:17890";
                url.Text="https://example.com/v1";modelField.Text="my-model";key.Password="private-test-key";Find<Button>(settings,"SaveConnections").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(.15);
                Check(pet.Store.Load().Text.ApiKey=="private-test-key" && !File.ReadAllText(Path.Combine(folder,"settings.json")).Contains("private-test-key"),"UI connection save encrypts secret on disk");
                Check(pet.Store.Load().Text.ProxyAddress=="localhost:17890","edited text proxy is saved by the service settings UI");
                textProxy.Text="socks5://localhost:17890";Find<Button>(settings,"SaveConnections").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(pet.Store.Load().Text.ProxyAddress=="localhost:17890" && Find<TextBlock>(settings,"ConnectionNotice").Text.Contains("HTTP 代理"),"invalid service proxy preserves the last saved settings and explains the format");textProxy.Text="localhost:17890";
                url.Text="https://example.com/v2";Regression("same-origin path edits retain the saved key",()=>Check(key.Password=="private-test-key","same-origin path edits retain the saved key"));
                url.Text="https://second-service.invalid/v1";Regression("manual provider domain edits clear the old key",()=>Check(key.Password.Length==0,"manual provider domain edits clear the old key"));
                Save(settings,Path.Combine(folder,"connection-settings.png"));
                settings.SelectPage(3);Pump(.1);Check(Find<TextBox>(settings,"TranscriptionModel")!=null,"ASR has a separate editable service page");settings.SelectPage(4);Pump(.1);Check(Find<ComboBox>(settings,"SpeechVoice")!=null,"TTS has a separate editable voice page");
                Find<Slider>(settings,"VoiceVolume").Value=35;Pump(.1);Check(pet.Settings.Volume==35 && pet.Store.Load().Volume==35,"voice volume applies and persists immediately");Save(settings,Path.Combine(folder,"speech-settings.png"));
                settings.SelectPage(5);Pump(.1);Check(Find<TextBox>(settings,"LiveServiceUrl")!=null && Find<PasswordBox>(settings,"LiveServiceKey")!=null,"Live has its own endpoint, key and voice");Save(settings,Path.Combine(folder,"live-settings.png"));
                Find<TextBox>(settings,"LiveServiceProxyAddress").Text="localhost:17893";settings.SelectPage(3);Pump(.1);Find<TextBox>(settings,"TranscriptionProxyAddress").Text="localhost:17891";settings.SelectPage(4);Pump(.1);Find<TextBox>(settings,"SpeechProxyAddress").Text="localhost:17892";Find<Button>(settings,"SaveConnections").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var proxySaved=pet.Store.Load();Check(proxySaved.Transcription.ProxyAddress=="localhost:17891" && proxySaved.Speech.ProxyAddress=="localhost:17892" && proxySaved.Live.ProxyAddress=="localhost:17893","ASR TTS and Live proxy addresses save independently across pages");
                settings.SelectPage(2);Pump(.1);Find<TextBox>(settings,"TextServiceModel").Text="unsaved-draft";settings.SelectPage(3);Pump(.1);settings.SelectPage(2);Pump(.1);Check(Find<TextBox>(settings,"TextServiceModel").Text=="unsaved-draft","navigation preserves unsaved service drafts");
                var preset=Find<ComboBox>(settings,"TextServicePreset");preset.SelectedIndex=1;Pump(.1);Check(Find<PasswordBox>(settings,"TextServiceKey").Password.Length==0,"switching provider origin clears the previous API key");
                settings.SelectPage(6);Pump(.1);Check(All<TextBlock>(settings).Any(t=>t.Text.Contains("彧晟Eason")) && Find<TextBlock>(settings,"RepositoryUrl").Inlines.OfType<System.Windows.Documents.Hyperlink>().Any(link=>link.NavigateUri.AbsoluteUri==ReleaseInfo.RepositoryUrl),"about page contains author and fixed repository link");Check(Find<TextBox>(settings,"RepositoryUrl")==null && Find<Button>(settings,"SaveRepository")==null && Find<Button>(settings,"CheckUpdates").IsEnabled && Find<Button>(settings,"DownloadUpdate")==null,"official repository is read-only and the update check includes automatic download");Save(settings,Path.Combine(folder,"about.png"));
                pet.SetReplySpeech(false);var sharedQuick=new QuickChatWindow(pet);typeof(PetWindow).GetField("quick",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(pet,sharedQuick);sharedQuick.Closed+=delegate{typeof(PetWindow).GetField("quick",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(pet,null);};sharedQuick.Show();Pump(.2);var quick=app.Windows.OfType<QuickChatWindow>().Single();
                var chatTts=Find<CheckBox>(chat,"TtsOutput");var quickTts=Find<CheckBox>(quick,"TtsOutput");
                Check(chatTts!=null && quickTts!=null && chatTts.IsChecked==false && quickTts.IsChecked==false,"full chat and bubble expose the saved TTS output switch");
                quickTts.IsChecked=true;Pump(.1);Check(chatTts.IsChecked==true && pet.Settings.ReadReplies && pet.Store.Load().ReadReplies,"bubble TTS switch updates full chat and persists");
                settings.SelectPage(4);Pump(.1);var settingsTts=Find<CheckBox>(settings,"TtsOutput");Check(settingsTts!=null && settingsTts.IsChecked==true,"TTS settings synchronize when returning to the voice page");
                chatTts.IsChecked=false;Pump(.1);Check(quickTts.IsChecked==false && settingsTts.IsChecked==false && !pet.Store.Load().ReadReplies,"full chat TTS switch updates bubble and open settings immediately");
                settingsTts.IsChecked=true;Pump(.1);Check(chatTts.IsChecked==true && quickTts.IsChecked==true,"settings TTS switch updates both conversation windows");chatTts.IsChecked=false;
                using(var model=new ApiTests.Fixture("{\"choices\":[{\"message\":{\"content\":\"只显示这段文字。\"}}]}"))
                using(var synthesis=new ApiTests.Fixture(WaveData.EncodePcm(new byte[16000]),"audio/wav"))
                {
                    model.DelayBodyMilliseconds=400;pet.Settings.Text.BaseUrl=model.Url;pet.Settings.Speech=new ConnectionSettings{BaseUrl=synthesis.Url,Model="tts",ApiKey="dummy-key",Protocol="openai"};pet.Settings.UseCloudVoice=true;pet.Settings.SpeechUsesTextService=false;pet.Settings.Volume=35;pet.InputQueue.Resume();
                    chatTts.IsChecked=true;var pending=pet.Conversation.SendTextAsync("等待期间关闭朗读",pet.Settings,pet.Skin.Name);Wait(()=>pet.Conversation.Stage==ConversationStage.Thinking,2);quickTts.IsChecked=false;Wait(()=>pending.IsCompleted,5);pending.GetAwaiter().GetResult();
                    Check(synthesis.Body==null && pet.Conversation.History.Messages.Last().Content=="只显示这段文字。","disabling TTS during model wait prevents synthesis and retains the reply");pet.Conversation.Clear();
                }
                using(var model=new ApiTests.Fixture("{\"choices\":[{\"message\":{\"content\":\"停止声音，保留回复。\"}}]}"))
                using(var synthesis=new ApiTests.Fixture(WaveData.EncodePcm(new byte[16000]),"audio/wav"))
                {
                    synthesis.DelayBodyMilliseconds=800;pet.Settings.Text.BaseUrl=model.Url;pet.Settings.Speech.BaseUrl=synthesis.Url;chatTts.IsChecked=true;
                    var reading=pet.Conversation.SendTextAsync("关闭正在合成的语音",pet.Settings,pet.Skin.Name);Wait(()=>synthesis.Body!=null && pet.Conversation.Stage==ConversationStage.Speaking,5);quickTts.IsChecked=false;Wait(()=>reading.IsCompleted,3);reading.GetAwaiter().GetResult();
                    Check(!pet.Conversation.IsBusy && pet.Conversation.History.Messages.Last().Content=="停止声音，保留回复。" && pet.Conversation.LastNotice.Contains("朗读已停止"),"TTS switch cancels active synthesis without cancelling or deleting the model reply");pet.Conversation.Clear();
                }
                pet.InputQueue.Resume();
                using(var server=new ApiTests.Fixture("{\"choices\":[{\"message\":{\"content\":\"头顶气泡回复。\"}}]}"))
                {pet.Settings.Text.BaseUrl=server.Url;Find<MarkdownEditor>(quick,"QuickMessageInput").Text="快速聊聊";Find<Button>(quick,"SendQuickMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>!pet.Conversation.IsBusy && pet.Conversation.History.Messages.Count==2,5);Check(Find<MarkdownView>(quick,"QuickReply").Text=="头顶气泡回复。" && All<MarkdownView>(chat).Any(t=>t.Text=="头顶气泡回复。"),"quick bubble and full chat share actual model replies");}
                pet.ReturnHome();quick.FollowPet();Pump(.1);Save(quick,Path.Combine(folder,"quick-chat.png"));
                var quickHandle=new System.Windows.Interop.WindowInteropHelper(quick).Handle;var petHandle=new System.Windows.Interop.WindowInteropHelper(pet).Handle;var quickBounds=DesktopInterop.Bounds(quickHandle);Check(DesktopInterop.WorkArea(petHandle).Contains(quickBounds) && quickBounds.Bottom<DesktopInterop.Bounds(petHandle).Bottom,"bubble is clamped above the pet inside the screen");
                Check(!pet.SelectSkin(pet.Catalog.Find("e-bao")) && settings.HasUnsavedEdits,"persona switching protects pending settings edits");Check(settings.SaveAiConfiguration(),"save pending service edits before persona switch");pet.SelectSkin(pet.Catalog.Find("e-bao"));Pump(.2);Check(pet.Settings.SystemPrompt==PersonaProfiles.Ebao && pet.Conversation.History.Messages.Count==0 && !All<MarkdownView>(chat).Any(t=>t.Text=="头顶气泡回复。"),"switching pet updates persona and excludes the other pet history");settings.SelectPage(1);Pump(.1);Save(settings,Path.Combine(folder,"persona-settings.png"));
                pet.SelectSkin(pet.Catalog.Find("dafeiyu"));Pump(.1);Check(pet.Conversation.History.Messages.Count==2 && All<MarkdownView>(chat).Any(t=>t.Text=="头顶气泡回复。"),"returning to a pet restores its own history");
                using(var server=new ApiTests.Fixture("{\"choices\":[{\"message\":{\"content\":\"后台气泡回复。\"}}]}"))
                {server.DelayBodyMilliseconds=350;pet.Settings.Text.BaseUrl=server.Url;Find<MarkdownEditor>(quick,"QuickMessageInput").Text="继续";Find<Button>(quick,"SendQuickMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(.08);Check(!pet.SelectSkin(pet.Catalog.Find("e-bao")),"active conversation refuses persona changes");chat.Close();quick.Close();Wait(()=>!pet.Conversation.IsBusy,5);Check(pet.Conversation.History.Messages.Count==4,"closing either idle full chat or bubble preserves the bubble-owned request");}
                pet.ShowChat();Pump(.1);chat=app.Windows.OfType<ChatWindow>().Single();pet.ShowQuickChat();Pump(.1);quick=app.Windows.OfType<QuickChatWindow>().Single();pet.HidePet();Pump(.1);Check(!quick.IsVisible,"hiding the pet also hides its bubble");pet.RestorePet();quick.Close();
                using(var server=new LiveTests.WebSocketFixture())
                {
                    bool simulated=Environment.GetEnvironmentVariable("DESKPET_TEST_SIMULATED_MIC")=="1";
                    if(simulated)
                    {
                        typeof(GeminiLiveSession).GetField("microphones",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(pet.Live,(Func<IPcmMicrophone>)(()=>new FixtureMicrophone()));
                        Console.WriteLine("INFO Live uses explicit simulated microphone; native capture is NOT verified by this run.");
                    }
                    pet.Settings.Live=new ConnectionSettings{BaseUrl=server.Url,Model="live-test",ApiKey="dummy-key"};pet.Settings.Volume=0;
                    settings.SelectPage(5);Pump(.1);Find<ComboBox>(settings,"VoiceMode").SelectedIndex=1;
                    Find<Button>(chat,"SendMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));try{Wait(()=>pet.LiveActive && pet.Conversation.History.Messages.Any(m=>m.Content=="真实 WS 回复"),7);}catch{var callStatus=Find<TextBlock>(chat,"LiveCallStatus");Console.WriteLine("Live diagnostic: active="+pet.LiveActive+", stage="+pet.Conversation.Stage+", content="+chat.Content.GetType().Name+", status="+(callStatus==null?pet.Live.Status:callStatus.Text));throw;}
                    Check(Find<TextBlock>(chat,"LiveCallTranscript").Text.Contains("真实 WS 回复") && pet.Conversation.History.Messages.Any(t=>t.Content=="真实 WS 回复"),"Live button uses "+(simulated?"simulated":"real")+" microphone and real WebSocket, and renders subtitles and committed history");
                    Check(Find<Button>(chat,"SendMessage")==null && !pet.SelectSkin(pet.Catalog.Find("e-bao")),"Live prevents pipeline requests and persona switching");
                    Find<ComboBox>(settings,"VoiceMode").SelectedIndex=0;Pump(.1);
                    Regression("active Live rejects mode changes from settings",()=>Check(pet.Settings.VoiceMode=="live" && Find<ComboBox>(settings,"VoiceMode").SelectedIndex==1,"active Live rejects mode changes from settings"));
                    pet.Settings.VoiceMode="pipeline";
                    pet.ShowLiveCall(chat);Check(chat.Content is LiveCallView,"active Live entry returns to the embedded call");Find<Button>(chat,"LiveEndCall").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Regression("end call stops actual Live despite saved mode",()=>{Wait(()=>!pet.LiveActive && !(chat.Content is LiveCallView),5);Pump(.1);Check(!pet.LiveActive && pet.Conversation.Stage==ConversationStage.Idle,"end call stops actual Live despite saved mode");});
                    Check(Find<Button>(chat,"SendMessage").IsEnabled,"ending Live closes audio and restores text controls");
                    var cleanup=pet.StopDialogueAsync();Wait(()=>cleanup.IsCompleted,5);cleanup.GetAwaiter().GetResult();
                    pet.Settings.Volume=35;pet.InputQueue.Resume();
                }
                // Capture the default character and current controls for the public README.
                settings.Close();Pump(.1);Check(pet.SelectSkin(pet.Catalog.Find("e-bao")),"public preview uses selected Ebao persona");pet.Conversation.Clear();pet.SetReplySpeech(false);
                Check(Find<TextBlock>(chat,"LiveCallTranscript")==null,"ended Live captions do not leak into another pet or pipeline chat");
                using(var model=new ApiTests.Fixture("{\"choices\":[{\"message\":{\"content\":\"哼，这还不简单？先说清目标，再把问题拆成几步。把你的想法发过来，我陪你一起整理。\"}}]}"))
                {
                    pet.Settings.Text.BaseUrl=model.Url;input=Find<MarkdownEditor>(chat,"MessageInput");input.Text="E包，帮我梳理一下今天的想法。";Find<Button>(chat,"SendMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>!pet.Conversation.IsBusy && pet.Conversation.History.Messages.Count==2,5);pet.SetReplySpeech(true);Pump(.1);Save(chat,Path.Combine(folder,"chat-window.png"));
                    pet.ShowQuickChat();Pump(.1);quick=app.Windows.OfType<QuickChatWindow>().Single();pet.ReturnHome();quick.FollowPet();Pump(.1);Save(quick,Path.Combine(folder,"quick-chat.png"));
                }
                using(var model=new ApiTests.Fixture("{\"choices\":[{\"message\":{\"content\":\"关闭时的声音测试。\"}}]}"))
                using(var synthesis=new ApiTests.Fixture(WaveData.EncodePcm(new byte[160000]),"audio/wav"))
                {
                    pet.Settings.Text.BaseUrl=model.Url;pet.Settings.Speech.BaseUrl=synthesis.Url;pet.Settings.ReadReplies=true;pet.Settings.UseCloudVoice=true;pet.Settings.VoiceName="test";
                    var operation=pet.Conversation.SendTextAsync("退出",pet.Settings,pet.Skin.Name);string cache=Path.Combine(folder,"audio-cache");Wait(delegate{return Directory.Exists(cache) && Directory.GetFiles(cache,"*.wav").Any();},5);
                    bool cleanedAtClose=false;pet.Closed+=delegate {cleanedAtClose=!Directory.GetFiles(cache,"*.wav").Any();};pet.Close();Wait(delegate{return app.Windows.Count==0;},5);
                    Check(cleanedAtClose,"exit waits for active speech cleanup before the application can shut down");Wait(delegate{return operation.IsCompleted;},3);try{operation.GetAwaiter().GetResult();}catch(OperationCanceledException){}
                }
                Check(app.Windows.Count==0,"exit disposes chat and settings windows");
                if(regressions.Count>0)throw new Exception("Regression failures: "+String.Join(", ",regressions));
                Console.WriteLine("Chat smoke: "+passed+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL Chat smoke: "+e);if(pet!=null)pet.Close();return 1;}
        }
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        static void Regression(string name,Action test){try{test();}catch(Exception){regressions.Add(name);Console.WriteLine("REGRESSION FAIL "+name);}}
        static T Find<T>(DependencyObject root,string name) where T:FrameworkElement{return All<T>(root).FirstOrDefault(t=>t.Name==name);}
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(T item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        static void Wait(Func<bool> done,double timeout){var end=DateTime.UtcNow.AddSeconds(timeout);while(!done() && DateTime.UtcNow<end)Pump(.03);if(!done())throw new Exception("UI operation timed out");Pump(.08);}
        static void Pump(double seconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(seconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Save(FrameworkElement element,string file){var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(element);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(file))png.Save(stream);}
    }
}
