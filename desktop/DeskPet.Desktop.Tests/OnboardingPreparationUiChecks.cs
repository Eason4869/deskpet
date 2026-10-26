using Avalonia;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class OnboardingPreparationUiChecks
{
    private sealed class Source:IOfficialCompanionSource
    {
        private long selection;
        public long BeginSelection()=>++selection;
        public bool OwnsSelection(long token)=>token==selection;
        public OfficialCompanionInstallation? Installed(string id)=>null;
        public Task<OfficialCompanionResult> EnsureAsync(string id,OfficialDownloadRouting routing,OfficialCompanionCheckMode mode,IProgress<OfficialCompanionProgress>? progress,CancellationToken cancel)=>Task.FromCanceled<OfficialCompanionResult>(new CancellationToken(true));
        public Task<IList<OfficialCompanionResult>> EnsureAllAsync(OfficialDownloadRouting routing,IProgress<OfficialCompanionProgress>? progress,CancellationToken cancel)=>throw new Exception("UI fixture must use the explicitly injected preparation operation");
        public void Dispose(){}
    }
    internal static int Run(string directory,string? language=null)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception("Onboarding preparation: "+text);checks++;}
        void Until(Func<bool> ready){var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException("Onboarding preparation");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}
        void Complete(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=language??"zh_CN",OnboardingState="new",AutoUpdate=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(directory),officialSourceFactory:_=>new Source());
        int starts=0;var pending=new TaskCompletionSource<bool>();CancellationToken download=default;IProgress<OfficialCompanionProgress>? reporter=null;
        owner.PreparationDownloadOverride=(progress,token)=>{starts++;reporter=progress;download=token;return pending.Task;};
        GuideWindow? Guide()=>(GuideWindow?)typeof(DesktopController).GetField("guide",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner);
        void Click(Control root,string name){root.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();}
        void Open(){owner.ShowGuide();Until(()=>Guide()?.IsVisible==true);}
        try
        {
            Open();var guide=Guide()!;Check(guide.CurrentLessonId=="language"&&starts==0,"fresh guidance begins with language selection without preparation requests");
            var screen=guide.Screens.ScreenFromWindow(guide)??guide.Screens.Primary;Check(screen!=null&&Math.Abs(guide.Position.X+guide.Width*screen.Scaling/2-screen.WorkingArea.X-screen.WorkingArea.Width/2)<2&&Math.Abs(guide.Position.Y+guide.Height*screen.Scaling/2-screen.WorkingArea.Y-screen.WorkingArea.Height/2)<2,"the guide is centered without a main window");
            var languages=guide.GetLogicalDescendants().OfType<ComboBox>().Single(value=>value.Name=="GuideLanguagePicker");Check(languages.ItemCount==UiLanguages.Choices.Count,"all languages and follow system are available on the homepage");
            languages.SelectedItem=UiLanguages.Choices.Single(choice=>choice.Code=="en_US");
            Check(!guide.GetLogicalDescendants().OfType<Button>().Any(value=>value.Name=="GuideRestart")&&guide.GetLogicalDescendants().OfType<Button>().Single(value=>value.Name=="GuideNext").IsEnabled,"apply and continue replaces the restart action");
            ApplicationRestart.Requested=false;string previousLanguage=owner.Settings.UiLanguage;
            using(var locked=new FileStream(Path.Combine(directory,"settings.json.tmp"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){Click(guide,"GuideNext");Check(!ApplicationRestart.Requested&&guide.CurrentLessonId=="language"&&owner.Settings.UiLanguage==previousLanguage,"a failed settings write preserves the language page and does not restart");}
            Click(guide,"GuideNext");
            var savedLanguage=new SettingsStore(Path.Combine(directory,"settings.json")).Load();Check(!ApplicationRestart.Requested&&TextResources.CurrentLanguage=="en_US"&&savedLanguage.UiLanguage=="en_US"&&savedLanguage.OnboardingLessonId=="welcome"&&savedLanguage.OnboardingState=="deferred","restart persists the selected language and resumes at preparation without claiming completion");
            guide.Close();Open();guide=Guide()!;Check(guide.CurrentLessonId=="welcome"&&guide.GetLogicalDescendants().OfType<TextBlock>().Any(value=>value.Text?.Contains("15–25")==true),"restart returns to before-you-start with a full-guide time estimate");
            Click(guide,"GuideNext");Check(guide.CurrentLessonId=="prepare.network"&&starts==0,"O01 GitHub routing precedes character preparation");
            owner.ShowSettings();var main=owner.GuideMainWindow!;main.Width=600;main.Position=new PixelPoint(50,50);Dispatcher.UIThread.RunJobs();typeof(GuideWindow).GetMethod("PositionGuide",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(guide,null);var anchor=guide.Position;
            Check(screen!=null&&Math.Abs(anchor.X-Math.Min(main.Position.X+main.Bounds.Width*main.RenderScaling+12,screen.WorkingArea.Right-guide.Width*screen.Scaling))<2,"the guide anchors to the right of the open settings window");
            var route=guide.GetLogicalDescendants().OfType<ComboBox>().Single(value=>value.Name=="GitHubRoute");route.SelectedIndex=1;
            Check(new SettingsStore(Path.Combine(directory,"settings.json")).Load().UpdateUseMirror,"O02 choosing the existing mirror route persists immediately");
            Click(guide,"GuideNext");Check(guide.CurrentLessonId=="prepare.channel"&&guide.GetLogicalDescendants().OfType<Button>().Any(value=>value.Name=="GuideReleaseChannels"),"manual-update frontend explains stable and preview packages with a releases-page entry");
            Click(guide,"GuideNext");Check(guide.CurrentLessonId=="prepare.character"&&guide.GetLogicalDescendants().OfType<ComboBox>().Single(value=>value.Name=="GuidePartnerPicker").ItemCount==5,"O03 character selection exposes all official roles before the remaining guide");
            typeof(GuideWindow).GetMethod("PositionGuide",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(guide,null);Check(guide.Position==anchor,"changing guide steps preserves its anchor");
            Click(guide,"GuideNext");Check(guide.CurrentLessonId=="prepare.packs"&&starts==0,"O04 full-pack recommendation remains optional before an explicit click");
            Click(guide,"GuidePreparePacks");Click(guide,"GuidePreparePacks");Check(starts==1&&owner.OfficialPreparation.State==OfficialCompanionPreparationState.Running,"O05 repeated explicit clicks share one nonblocking application batch");
            reporter!.Report(new OfficialCompanionProgress("e-bao","download","fixture",10,100,10));
            string output=Path.GetFullPath(Path.Combine("work","agent-jobs-ui","onboarding",TextResources.CurrentLanguage));Directory.CreateDirectory(output);
            foreach(string theme in new[]{"light","dark"}){
                DesktopTheme.Apply(theme);guide.MinWidth=390;guide.Width=390;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var capture=guide.CaptureRenderedFrame();Check(capture!=null,"narrow "+theme+" recommendation actually renders");var status=guide.GetLogicalDescendants().OfType<TextBlock>().Single(text=>text.Name=="GuidePreparationStatus");var viewport=guide.GetLogicalDescendants().OfType<ScrollViewer>().Single(value=>value.Name=="GuideBody");var position=status.TranslatePoint(new Avalonia.Point(),viewport);
                Check(status.IsEffectivelyVisible&&status.Text!.Contains("10")&&position.HasValue&&position.Value.Y>=0&&position.Value.Y+status.Bounds.Height<=viewport.Bounds.Height,"current preparation progress is actually visible inside the narrow scroll viewport");
                var action=guide.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name=="GuidePreparePacks");Check(action.Bounds.Width<=guide.Bounds.Width-40&&action.Content is TextBlock caption&&caption.TextWrapping==Avalonia.Media.TextWrapping.Wrap,"primary download action wraps within the narrow guide");capture!.Save(Path.Combine(output,"recommendation-"+theme+"-390.png"),new PngBitmapEncoderOptions());
            }
            DesktopTheme.Apply("light");Click(guide,"GuideNext");string later=guide.CurrentLessonId;Check(later.StartsWith("ava.legacy.")&&!pending.Task.IsCompleted,"later guidance proceeds while preparation remains pending");
            guide.Close();Check(!download.IsCancellationRequested&&owner.OfficialPreparation.State==OfficialCompanionPreparationState.Running,"O06 closing guidance never cancels the application transfer");Open();guide=Guide()!;
            Check(guide.CurrentLessonId==later&&starts==1,"O07 stable progress resumes without another download batch");pending.SetResult(true);Until(()=>owner.OfficialPreparation.State==OfficialCompanionPreparationState.Completed);
            guide.Close();owner.Settings.OnboardingState="completed";owner.Save();Check(!OnboardingUpgrade.Prepare(owner.Settings,ReleaseInfo.DisplayVersion).ShowGuide,"O08 the content change never forces an already completed current-series guide");
            owner.Settings.OnboardingState="deferred";owner.Settings.OnboardingLessonId="";owner.Settings.OnboardingStep=8;Open();Check(Guide()!.CurrentLessonId=="ava.legacy.8","legacy numeric progress maps to its prior content instead of an inserted preparation step");var legacy=Guide()!;for(int i=0;i<20&&legacy.CurrentLessonId!="ava.legacy.16";i++)Click(legacy,"GuideNext");
            Check(legacy.CurrentLessonId=="ava.legacy.16","the original final lesson remains reachable after inserted preparation steps");
            Click(legacy,"GuideNext");Check(legacy.CurrentLessonId=="finish.congratulations"&&owner.Settings.OnboardingState!="completed","formal guidance reaches congratulations before completion");
            Click(legacy,"GuideNext");Check(owner.Settings.OnboardingState=="completed"&&!legacy.IsVisible,"only explicit final confirmation completes the expanded guide");
            owner.Settings.UiLanguage=TextResources.CurrentLanguage;owner.Save();Open();guide=Guide()!;Click(guide,"GuideNext");
            Check(guide.CurrentLessonId=="welcome"&&guide.GetLogicalDescendants().OfType<Button>().Any(value=>value.Name=="GuideExplore"&&value.IsVisible),"before-you-start exposes the independent-exploration choice");
            int skippedBefore=owner.Settings.OnboardingSkippedLessons.Count;
            using(var locked=new FileStream(Path.Combine(directory,"settings.json.tmp"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){Click(guide,"GuideExplore");Check(guide.CurrentLessonId=="welcome"&&owner.Settings.OnboardingSkippedLessons.Count==skippedBefore&&guide.GetLogicalDescendants().OfType<TextBlock>().Any(value=>value.Name=="GuideSaveNotice"&&value.IsVisible),"failed exploration persistence restores progress and displays an error");}
            Click(guide,"GuideExplore");Check(guide.CurrentLessonId=="finish.congratulations"&&owner.Settings.OnboardingState=="deferred"&&starts==1,"independent exploration skips formal setup without starting work or completing early");
            using(var locked=new FileStream(Path.Combine(directory,"settings.json.tmp"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){Click(guide,"GuideNext");Check(guide.IsVisible&&owner.Settings.OnboardingState=="deferred"&&guide.GetLogicalDescendants().OfType<TextBlock>().Any(value=>value.Name=="GuideSaveNotice"&&value.IsVisible),"failed final confirmation preserves the incomplete state and keeps its error visible");}
            Click(guide,"GuideNext");Check(owner.Settings.OnboardingState=="completed"&&!guide.IsVisible,"independent exploration also ends through explicit congratulations confirmation");
        }
        finally{Guide()?.Close();owner.GuideMainWindow?.Close();pending.TrySetCanceled();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}
        return checks;
    }
}
