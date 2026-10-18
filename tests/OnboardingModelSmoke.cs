using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class OnboardingModelSmoke
    {
        static void AddDiscovered(FrameworkElement root)
        {
            var list=Find<ListBox>(root,"DiscoveredTextModels");var item=(ListBoxItem)list.Items[0];
            root.Dispatcher.BeginInvoke(new Action(delegate{var dialog=Application.Current.Windows.OfType<Window>().First(w=>w.Title=="添加模型");Find<Button>(dialog,"AddTextModel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}),DispatcherPriority.Background);
            Find<Button>(item,"AddDiscoveredModel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        }
        static int passed;
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"onboarding-models-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try
            {
                var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,FollowCursor=false,Wander=false,SaveChatHistory=false,OnboardingState="deferred",OnboardingStep=8};
                foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);pet.Show();pet.ShowOnboarding();Pump();
                var settings=(SettingsPage)Field(pet,"preferences");var guide=(OnboardingWindow)Field(pet,"guide");
                Check(typeof(OnboardingWindow).GetProperty("CurrentLessonId")!=null,"guide has stable detailed lesson IDs");
                Check(guide.Step==8 && Find<Button>(settings,"AddTextProvider").IsEnabled,"text guide unlocks the manufacturer picker instead of only the old connection fields");
                Check(!Find<FrameworkElement>(settings,"SettingsNavigation").IsEnabled && !Find<Button>(settings,"SaveConnections").IsEnabled,"guide keeps other pages and global save locked");
                int before=Find<ListBox>(settings,"TextProviderList").Items.Count;Choose(settings,"AddTextProvider","",true);
                Check(Find<ListBox>(settings,"TextProviderList").Items.Count==before && Find<Button>(settings,"AddTextProvider").IsEnabled,"cancelling the manufacturer picker preserves drafts and restores guide interaction");
                using(var server=new ApiTests.Fixture("{\"data\":[{\"id\":\"guide-model-a\"},{\"id\":\"guide-model-b\"}]}"))
                {
                    Choose(settings,"AddTextProvider","deepseek");
                    Check(Find<PasswordBox>(settings,"TextServiceKey").IsEnabled && guide.HighlightVisible,"new provider fields remain editable while the guide highlight survives");
                    Find<TextBox>(settings,"TextServiceUrl").Text=server.Url+"/v1";Find<PasswordBox>(settings,"TextServiceKey").Password="guide-text-key";
                    Go(guide,"text.save");Check(!Find<Button>(guide,"GuideNext").IsEnabled,"a saved connection without an added model cannot finish the text step");
                    Click(settings,"SaveAndFetchTextModels");Until(()=>Find<ListBox>(settings,"DiscoveredTextModels").Items.Count==2,delegate{Console.WriteLine("Catalog fixture: "+server.Done.Status+" path="+server.Path);foreach(string name in new[]{"ConnectionNotice","TextProviderNotice"}){var notice=settings.FindControl(name) as TextBlock;if(notice!=null)Console.WriteLine(name+": "+notice.Text);}});
                    Check(Find<ListBox>(settings,"DiscoveredTextModels").IsEnabled && settings.FindControl("SelectAllDiscoveredModels")==null,"guide permits selecting models after fetching collapses the connection area");
                    AddDiscovered(settings);AddDiscovered(settings);
                    Check(Find<ListBox>(settings,"TextProviderModels").Items.Count==2 && Find<ListBox>(settings,"TextProviderModels").IsEnabled,"both selected models can be added and changed inside the guide");
                    Until(()=>Find<Button>(guide,"GuideNext").IsEnabled);Capture(settings,Path.Combine(folder,"guide-models-added.png"));Capture(guide,Path.Combine(folder,"guide-models-card.png"));
                    Click(guide,"GuideNext");
                    var saved=pet.Store.Load();var active=saved.TextProviders.Single(p=>p.Id==saved.ActiveTextProviderId);
                    Check(guide.CurrentLessonId=="text.test" && active.Models.Count==2 && active.Models.Contains(saved.Text.Model),"next saves configured text models and advances only to ASR");
                }
                Go(guide,"asr.provider");Choose(settings,"AddAsrProvider","xiaomimimo");
                Check(Find<TextBox>(settings,"TranscriptionModel").IsEnabled && Find<TextBox>(settings,"TranscriptionModel").Text=="mimo-v2.5-asr" && settings.FindControl("FetchAsrModels")==null,"ASR guide allows manufacturer selection and its editable prefilled model without discovery");
                Find<PasswordBox>(settings,"TranscriptionKey").Password="guide-asr-key";Go(guide,"asr.save");Until(()=>Find<Button>(guide,"GuideNext").IsEnabled);Click(guide,"GuideNext");
                Check(guide.CurrentLessonId=="asr.operate" && pet.Settings.Transcription.ApiKey=="guide-asr-key" && pet.Settings.Text.ApiKey=="guide-text-key","ASR next saves its independent key without changing the text key");
                Go(guide,"speech.intro");Check(Find<CheckBox>(settings,"UseCloudVoice").IsEnabled,"TTS guide unlocks the moved cloud toggle");
                Choose(settings,"AddSpeechProvider","xiaomimimo");Find<CheckBox>(settings,"UseCloudVoice").IsChecked=true;Go(guide,"speech.key");Find<PasswordBox>(settings,"SpeechKey").Password="guide-speech-key";
                Check(Find<ComboBox>(settings,"SpeechVoice").IsEnabled && Find<Slider>(settings,"VoiceVolume").IsEnabled,"TTS guide permits editing voice and volume outside connection fields");
                Check(!Find<Button>(settings,"PreviewSpeech").IsEnabled && Find<Button>(settings,"StopPreviewSpeech").IsEnabled,"guide blocks starting speech tests before the dedicated lesson while retaining stop");Go(guide,"speech.save");
                Until(()=>Find<Button>(guide,"GuideNext").IsEnabled);Click(guide,"GuideNext");
                Go(guide,"live.provider");Choose(settings,"AddLiveProvider","gemini");Find<PasswordBox>(settings,"LiveServiceKey").Password="guide-live-key";
                Check(Find<ComboBox>(settings,"LiveVoice").IsEnabled && Find<TextBox>(settings,"LiveServiceModel").Text=="gemini-3.8-live","Live guide enables the manufacturer, prefilled model and independent voice");
                Check(!Find<Button>(settings,"PreviewLive").IsEnabled && !pet.LiveActive,"guide blocks the Live test and never connects automatically");
                Go(guide,"live.save");Until(()=>Find<Button>(guide,"GuideNext").IsEnabled);Click(guide,"GuideNext");
                Check(guide.CurrentLessonId=="live.test" && pet.Settings.Live.ApiKey=="guide-live-key" && !pet.LiveActive,"Live next saves only Live configuration without a call");
                Click(guide,"GuideBack");Check(!Find<Button>(settings,"PreviewLive").IsEnabled,"returning to Live keeps its test guarded during the guide");
                Click(guide,"GuideSkip");Check(Find<Button>(settings,"PreviewLive").IsEnabled && Find<Button>(settings,"SaveConnections").IsEnabled,"leaving the guide restores page tests and global save");
                CheckManualFallback(pet,folder);
                Console.WriteLine("Onboarding model smoke: "+passed+" passed; previews: "+folder);return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL onboarding models: "+e);return 1;}
            finally{if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
        static void Go(OnboardingWindow guide,string id){typeof(OnboardingWindow).GetField("index",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(guide,Array.FindIndex(OnboardingLessons.All,l=>l.Id==id));typeof(OnboardingWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(guide,null);Pump();}
        static void CheckManualFallback(PetWindow pet,string folder)
        {
            pet.Settings.OnboardingState="deferred";pet.Settings.OnboardingStep=8;pet.Settings.OnboardingLessonId="text.save";pet.ShowOnboarding();Pump();
            var settings=(SettingsPage)Field(pet,"preferences");var guide=(OnboardingWindow)Field(pet,"guide");
            using(var server=new ApiTests.Fixture("private catalog error",401)){
                Choose(settings,"AddTextProvider","custom");Find<TextBox>(settings,"TextServiceUrl").Text=server.Url+"/v1";Find<PasswordBox>(settings,"TextServiceKey").Password="manual-guide-key";Click(settings,"SaveAndFetchTextModels");
                Until(()=>Find<TextBlock>(settings,"TextProviderNotice").Text.Contains("401"));
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{var dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="添加模型");Find<TextBox>(dialog,"TextProviderNewModel").Text="manual-guide-model";Pump();Check(!Find<Button>(guide,"GuideNext").IsEnabled,"typing an unadded model does not silently complete the guide");Click(dialog,"AddTextModel");}));
                Check(Find<Button>(settings,"CustomTextModel").IsEnabled,"failed discovery leaves manual model addition usable in the guide");Click(settings,"CustomTextModel");Until(()=>Find<Button>(guide,"GuideNext").IsEnabled);
                Find<TextBox>(settings,"TextServiceProxyAddress").Text="socks5://127.0.0.1:7890";Until(()=>!Find<Button>(guide,"GuideNext").IsEnabled);Check(!Find<Button>(guide,"GuideNext").IsEnabled,"invalid proxy settings cannot mark the text guide ready before save");
                Find<TextBox>(settings,"TextServiceProxyAddress").Text=ProxyConfiguration.DefaultAddress;Until(()=>Find<Button>(guide,"GuideNext").IsEnabled);Click(guide,"GuideNext");
                Check(pet.Settings.Text.Model=="manual-guide-model" && pet.Settings.TextProviders.Single(p=>p.Id==pet.Settings.ActiveTextProviderId).Models.Count==1,"manually added model survives guide save after a discovery error");
            }
            Click(guide,"GuideBack");settings.HostWindow.Width=700;settings.HostWindow.Height=430;Pump();Capture(settings,Path.Combine(folder,"guide-models-minimum.png"));Capture(guide,Path.Combine(folder,"guide-models-minimum-card.png"));Click(guide,"GuideSkip");
        }
        static object Field(object value,string name){return value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);}
        static T Find<T>(DependencyObject root,string name) where T:FrameworkElement {var settings=root as SettingsPage;var found=settings==null?FindTree<T>(root,name):settings.FindControl(name) as T;if(found==null)throw new Exception("Missing "+name);return found;}
        static T FindTree<T>(DependencyObject root,string name) where T:FrameworkElement {var element=root as T;if(element!=null && element.Name==name)return element;foreach(object item in LogicalTreeHelper.GetChildren(root)){var child=item as DependencyObject;if(child!=null){var result=FindTree<T>(child,name);if(result!=null)return result;}}return null;}
        static void Click(DependencyObject root,string name){var button=Find<Button>(root,name);Check(button.IsEnabled,"button is enabled: "+name);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        static void Choose(SettingsPage settings,string add,string brand,bool cancel=false){Exception failure=null;Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{try{var picker=Application.Current.Windows.OfType<ProviderPickerWindow>().Single();if(cancel)picker.DialogResult=false;else{var button=Find<Button>(picker,"ChooseProvider_"+brand);Check(button.IsEnabled,"manufacturer picker is interactive during guide");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}}catch(Exception e){failure=e;}}));Click(settings,add);if(failure!=null)throw failure;Pump();}
        static void Until(Func<bool> done,Action diagnostic=null){var end=DateTime.UtcNow.AddSeconds(18);while(!done() && DateTime.UtcNow<end)Pump();if(!done()&&diagnostic!=null)diagnostic();Check(done(),"guide state becomes ready");}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
        static void Capture(FrameworkElement window,string path){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);using(var stream=File.Create(path)){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));png.Save(stream);}}
    }
}
