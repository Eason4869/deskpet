using Avalonia;
using Avalonia.Controls;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class GuideWindow : Window
{
    private readonly DesktopController owner;
    private readonly TextBlock title = Ui.Text("", 22), detail = Ui.Text("");
    private readonly Button open;
    private readonly Button back, next, explore, exit, defer;
    private int step;
    internal bool AllowsCompanionPreparation {get;private set;}
    internal string CurrentLessonId=>Steps[step].Id;
    private (string Id,string Title,string Detail,string Section)[]? cachedSteps;
    private string? cachedLanguage;
    private (string Id,string Title,string Detail,string Section)[] Steps {get{if(cachedSteps==null||cachedLanguage!=TextResources.CurrentLanguage){cachedSteps=BuildSteps();cachedLanguage=TextResources.CurrentLanguage;}return cachedSteps;}}
    private readonly StackPanel bodyContent=new(){Spacing=12};
    private readonly TextBlock preparationStatus=Ui.Text("");
    private readonly TextBlock saveNotice=new(){Name="GuideSaveNotice",TextWrapping=Avalonia.Media.TextWrapping.Wrap,IsVisible=false};
    private bool closed;
    private bool finished;
    private readonly Avalonia.Threading.DispatcherTimer placement=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private ComboBox? languagePicker;
    private static (string Title, string Detail, string Section)[] LegacySteps => new (string Title, string Detail, string Section)[]
    {
        (TextResources.Get("guideWindow.steps.steps"), TextResources.Get("guideWindow.steps.steps.alternativeA"), "partner"),
        (TextResources.Get("guideWindow.steps.steps.alternativeB"), TextResources.Get("guideWindow.steps.steps.alternativeC"), "partner"),
        (TextResources.Get("guideWindow.steps.steps.alternativeD"), TextResources.Get("guideWindow.steps.steps.alternativeE"), "behavior"),
        (TextResources.Get("guideWindow.steps.steps.alternativeF"), TextResources.Get("guideWindow.steps.steps.alternativeG"), "behavior"),
        (TextResources.Get("guideWindow.steps.steps.alternativeH"), TextResources.Get("guideWindow.steps.steps.alternativeI"), "text"),
        (TextResources.Get("guideWindow.steps.steps.alternativeJ"), TextResources.Get("guideWindow.steps.steps.alternativeK"), "text"),
        (TextResources.Get("guideWindow.steps.steps.alternativeL"), TextResources.Get("guideWindow.steps.steps.alternativeM"), "partner"),
        (TextResources.Get("guideWindow.steps.steps.alternativeN"), TextResources.Get("guideWindow.steps.steps.alternativeO"), "chat"),
        (TextResources.Get("guideWindow.steps.steps.alternativeP"), TextResources.Get("guideWindow.steps.steps.alternativeQ"), "fullchat"),
        (TextResources.Get("guideWindow.steps.steps.alternativeR"), TextResources.Get("guideWindow.steps.steps.alternativeS"), "asr"),
        (TextResources.Get("guideWindow.steps.steps.alternativeT"), TextResources.Get("guideWindow.steps.steps.alternativeU"), "speech"),
        (TextResources.Get("guideWindow.steps.steps.alternativeV"), TextResources.Get("guideWindow.steps.steps.alternativeW"), "live"),
        (TextResources.Get("guideWindow.steps.steps.alternativeX"), TextResources.Get("guideWindow.steps.steps.alternativeY"), "behavior"),
        (TextResources.Get("guideWindow.steps.steps.alternativeZ"), TextResources.Get("guideWindow.steps.steps.alternativeAA"), "storage"),
        (TextResources.Get("guideWindow.steps.steps.alternativeAB"), TextResources.Get("guideWindow.steps.steps.alternativeAC"), "chat"),
        (TextResources.Get("guideWindow.steps.steps.alternativeAD"), TextResources.Get("guideWindow.steps.steps.alternativeAE"), "work"),
        (TextResources.Get("guideWindow.steps.steps.alternativeAF"), TextResources.Get("guideWindow.steps.steps.alternativeAG"), "about")
    };
    public GuideWindow(DesktopController controller)
    {
        owner = controller; LocalizedUi.Set(this,"Title",()=>(TextResources.Get("guideWindow.guideWindow.title"))); Width = 390; Height = 540; MinWidth = 320; MinHeight = 280; Topmost = true;WindowStartupLocation=WindowStartupLocation.Manual;

        int saved=Array.FindIndex(Steps,item=>item.Id==owner.Settings.OnboardingLessonId);
        step=owner.Settings.OnboardingState is "completed" or "skipped"?0:owner.Settings.OnboardingState=="new"?(owner.Settings.OnboardingStep==0?0:Math.Clamp(owner.Settings.OnboardingStep+6,0,Steps.Length-1)):saved>=0?saved:Math.Clamp(owner.Settings.OnboardingStep+6,0,Steps.Length-1);

        AllowsCompanionPreparation=owner.OfficialPreparation.State==OfficialCompanionPreparationState.Running;
        owner.Settings.OnboardingState = "deferred";
        open = LocalizedUi.Button(()=>(TextResources.Get("guideWindow.guideWindow.text")), OpenStep);
        open.Name="GuideOpenStep";
        back = LocalizedUi.Button(()=>(TextResources.Get("guideWindow.guideWindow.text.alternativeA")), () => { if (step > 0&&SaveProgress(()=>step--)) Refresh(); });
        next = LocalizedUi.Button(()=>(TextResources.Get("guideWindow.guideWindow.text.alternativeB")), Advance);
        back.Name="GuideBack";next.Name="GuideNext";
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 14 };
        layout.Children.Add(title);
        var body = new ScrollViewer { Name="GuideBody",Content = bodyContent, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(body, 1); layout.Children.Add(body);
        exit = LocalizedUi.Button(()=>(TextResources.Get("guideWindow.guideWindow.text.alternativeC")), () => Finish("skipped"));
        exit.Name = "GuideSkip"; exit.FontSize = 14; exit.FontWeight = Avalonia.Media.FontWeight.SemiBold; exit.Foreground = DesktopTheme.Brush("#203754");
        explore=LocalizedUi.Button(()=>(TextResources.Get("onboarding.explore")),ExploreIndependently);explore.Name="GuideExplore";explore.Background=DesktopTheme.Brush("#E9ECF0");explore.Foreground=DesktopTheme.Brush("#203754");explore.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;
        next.Background=DesktopTheme.Brush("#456DE6");next.Foreground=Avalonia.Media.Brushes.White;next.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;
        defer=LocalizedUi.Button(()=>(TextResources.Get("guideWindow.guideWindow.text.alternativeD")),()=>Finish("deferred"));defer.Name="GuideDefer";
        var footer = Ui.Column(open, next, explore, saveNotice, Ui.Row(back, defer, exit));
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        Content = new Border { Padding = new Thickness(24), Child = layout };
        Closing += (_, args) => { if (!finished&&!SaveProgress(()=>owner.Settings.OnboardingState="deferred")) args.Cancel=true; };
        preparationStatus.Name="GuidePreparationStatus";owner.OfficialPreparation.Changed+=RefreshPreparation;
        Closed+=(_,_)=>{closed=true;placement.Stop();owner.OfficialPreparation.Changed-=RefreshPreparation;};
        Opened+=(_,_)=>{PositionGuide();placement.Start();};placement.Tick+=(_,_)=>PositionGuide();
        RefreshPreparation();
        Persist(); Refresh();
    }
    private void Persist() { owner.Settings.OnboardingStep = step; owner.Settings.OnboardingLessonId=Steps[step].Id; owner.Save(); }
    private bool SaveProgress(Action change)
    {
        int previous=step,savedStep=owner.Settings.OnboardingStep;
        string language=owner.Settings.UiLanguage,state=owner.Settings.OnboardingState,lesson=owner.Settings.OnboardingLessonId;
        var skipped=owner.Settings.OnboardingSkippedLessons.ToArray();
        try{change();Persist();saveNotice.IsVisible=false;return true;}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){
            step=previous;owner.Settings.UiLanguage=language;owner.Settings.OnboardingState=state;owner.Settings.OnboardingStep=savedStep;owner.Settings.OnboardingLessonId=lesson;
            owner.Settings.OnboardingSkippedLessons.Clear();owner.Settings.OnboardingSkippedLessons.AddRange(skipped);
            LocalizedUi.Set(saveNotice,"Text",()=>(TextResources.Get("petWindow.saveSettings.notice")));saveNotice.IsVisible=true;return false;
        }
    }
    private static (string Id,string Title,string Detail,string Section)[] BuildSteps(){return new[]{("language",TextResources.Get("onboarding.language.title"),TextResources.Get("onboarding.language.liveDetail"),"language"),("welcome",TextResources.Get("onboardingLessons.build.add"),TextResources.Get("onboarding.timeEstimate")+"\n\n"+TextResources.Get("onboardingLessons.build.add.alternativeA"),"intro"),("prepare.network",TextResources.Get("onboarding.prepare.network"),TextResources.Get("onboarding.prepare.networkDetail"),"prepare-network"),("prepare.channel",TextResources.Get("onboarding.channel.title"),TextResources.Get("onboarding.channel.detail")+"\n\n"+TextResources.Get("onboarding.channel.manual"),"prepare-channel"),("prepare.character",TextResources.Get("guideWindow.steps.steps"),TextResources.Get("guideWindow.steps.steps.alternativeA"),"prepare-character"),("prepare.packs",TextResources.Get("onboarding.prepare.packs"),TextResources.Get("onboarding.prepare.packsDetail"),"prepare-packs")}.Concat(LegacySteps.Select((item,index)=>("ava.legacy."+index,item.Title,item.Detail,item.Section))).Concat(new[]{("finish.congratulations",TextResources.Get("onboardingLessons.build.add.alternativeIZ"),TextResources.Get("onboardingLessons.build.add.alternativeJA"),"complete")}).ToArray();}
    private void Advance()
    {
        if(step==0&&languagePicker?.SelectedItem is UiLanguageChoice selectedLanguage){
            try{owner.ApplyGuideLanguage(selectedLanguage.Code);step=1;Refresh();}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){saveNotice.Text=TextResources.Get("petWindow.saveSettings.notice");saveNotice.IsVisible=true;}
            return;
        }
        bool complete=step==Steps.Length-1;
        if(!SaveProgress(()=>{if(step==0&&languagePicker?.SelectedItem is UiLanguageChoice selected)owner.Settings.UiLanguage=selected.Code;if(complete)owner.Settings.OnboardingState="completed";else step++;}))return;
        if(complete){finished=true;Close();}else Refresh();
    }
    private void ExploreIndependently()
    {
        if(Steps[step].Section!="intro")return;
        if(SaveProgress(()=>{
            foreach(var item in Steps.Where(item=>item.Section!="language"&&item.Section!="intro"&&item.Section!="complete"))
                if(!owner.Settings.OnboardingCompletedLessons.Contains(item.Id)&&!owner.Settings.OnboardingSkippedLessons.Contains(item.Id))owner.Settings.OnboardingSkippedLessons.Add(item.Id);
            step=Steps.Length-1;
        }))Refresh();
    }
    private void Refresh()
    {
        var item=Steps[step];LocalizedUi.Set(title,"Text",()=>$"{step+1} / {Steps.Length} · {Steps[step].Title}");LocalizedUi.Set(detail,"Text",()=>Steps[step].Detail);detail.IsVisible=true;bodyContent.Children.Clear();bodyContent.Children.Add(detail);back.IsEnabled=step>0;
        LocalizedUi.Set(next,"Content",()=>(item.Section=="intro"?TextResources.Get("onboardingWindow.navigate.currentActionStartsWith.alternativeA"):step==Steps.Length-1?TextResources.Get("guideWindow.refresh.stepStepsLength"):TextResources.Get("guideWindow.refresh.stepStepsLength.alternativeA")));open.IsVisible=!item.Section.StartsWith("prepare-")&&item.Section!="language"&&item.Section!="intro"&&item.Section!="complete";
        explore.IsVisible=item.Section=="intro";exit.IsVisible=defer.IsVisible=item.Section!="language"&&item.Section!="intro"&&item.Section!="complete";
        title.TextAlignment=detail.TextAlignment=item.Section=="complete"?Avalonia.Media.TextAlignment.Center:Avalonia.Media.TextAlignment.Left;
        if(item.Section=="prepare-network"){var route=new GitHubRoutingView(owner);route.Changed+=()=>next.IsEnabled=route.Saved;bodyContent.Children.Add(route);}
        else next.IsEnabled=true;
        if(item.Section=="language")BuildLanguagePage();
        if(item.Section=="prepare-channel"){var releases=LocalizedUi.Button(()=>(TextResources.Get("aboutPanel.aboutPanel.text.alternativeF")),()=>owner.Run(()=>Launcher.LaunchUriAsync(new Uri(ReleaseInfo.RepositoryUrl+"/releases"))));releases.Name="GuideReleaseChannels";bodyContent.Children.Add(releases);}
        if(item.Section=="prepare-character"){
            var choices=new ComboBox{Name="GuidePartnerPicker",ItemsSource=owner.Skins,SelectedItem=owner.Skin,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch,ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<PetSkin>((skin,_)=>Ui.Text(skin?.DisplayName??""))};
            choices.SelectionChanged+=(_,_)=>{if(choices.SelectedItem is PetSkin selected){AllowsCompanionPreparation=true;owner.Run(()=>owner.SelectSkinAsync(selected,CompanionSelectionOrigin.Onboarding));}};bodyContent.Children.Add(choices);
        }
        if(item.Section=="prepare-packs"){
            var start=LocalizedUi.Button(()=>(TextResources.Get("onboarding.prepare.start")),()=>{AllowsCompanionPreparation=true;owner.Run(owner.OfficialPreparation.StartAsync);});start.Name="GuidePreparePacks";start.Content=LocalizedUi.Text(()=>(TextResources.Get("onboarding.prepare.start")));start.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;start.HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Center;start.Background=DesktopTheme.Brush("#357FF5");start.Foreground=Avalonia.Media.Brushes.White;((TextBlock)start.Content).Foreground=Avalonia.Media.Brushes.White;
            bodyContent.Children.Add(preparationStatus);bodyContent.Children.Add(start);var progressButton=LocalizedUi.Button(()=>(TextResources.Get("onboarding.prepare.progress")),owner.ShowOfficialPreparation);progressButton.Content=LocalizedUi.Text(()=>(TextResources.Get("onboarding.prepare.progress")));progressButton.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;bodyContent.Children.Add(progressButton);LocalizedUi.Set(next,"Content",()=>(TextResources.Get("onboarding.prepare.later")));
        }
        RefreshPreparation();
    }
    private void BuildLanguagePage()
    {
        var choices=new ComboBox{Name="GuideLanguagePicker",ItemsSource=UiLanguages.Choices,SelectedItem=UiLanguages.Choices.Single(choice=>choice.Code==UiLanguages.Normalize(owner.Settings.UiLanguage)),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch,ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<UiLanguageChoice>((choice,_)=>Ui.Text(choice?.NativeName??""))};
        languagePicker=choices;string Selected()=>((UiLanguageChoice)choices.SelectedItem!).Code;
        choices.SelectionChanged+=(_,_)=>{next.IsEnabled=choices.SelectedItem!=null;};
        next.Content=TextResources.Get("onboarding.language.applyContinue");next.IsEnabled=choices.SelectedItem!=null;
        bodyContent.Children.Add(choices);bodyContent.Children.Add(preparationStatus);
    }
    private void PositionGuide()
    {
        if(!IsVisible)return;var main=owner.GuideMainWindow;var screen=Screens.ScreenFromWindow(main??this)??Screens.Primary;if(screen==null)return;
        var area=screen.WorkingArea;double scale=screen.Scaling;double width=Width*scale,height=Height*scale;
        double availableWidth=Math.Max(1,area.Width/scale-24),availableHeight=Math.Max(1,area.Height/scale-24);
        MinWidth=Math.Min(320,availableWidth);MinHeight=Math.Min(280,availableHeight);Width=Math.Min(390,availableWidth);Height=Math.Min(540,availableHeight);width=Width*scale;height=Height*scale;
        int x=main==null?area.X+(int)((area.Width-width)/2):main.Position.X+(int)(main.Bounds.Width*main.RenderScaling)+12;
        int y=main==null?area.Y+(int)((area.Height-height)/2):main.Position.Y;
        var desired=new PixelPoint((int)Math.Max(area.X,Math.Min(x,area.Right-width)),(int)Math.Max(area.Y,Math.Min(y,area.Bottom-height)));
        if(Position!=desired)Position=desired;
    }
    private void RefreshPreparation()=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>{
        if(closed)return;var state=owner.OfficialPreparation.State;preparationStatus.Text=state==OfficialCompanionPreparationState.Idle?"":owner.OfficialPreparation.StatusText;
        if(Steps[step].Section=="prepare-packs"){detail.IsVisible=state==OfficialCompanionPreparationState.Idle;preparationStatus.IsVisible=state!=OfficialCompanionPreparationState.Idle;LocalizedUi.Set(next,"Content",()=>(state==OfficialCompanionPreparationState.Running?TextResources.Get("guideWindow.refresh.stepStepsLength.alternativeA"):TextResources.Get("onboarding.prepare.later")));}
    });
    private void OpenStep()
    {
        var section = Steps[step].Section;
        if (section == "chat") owner.ShowChat(true); else if (section == "fullchat") owner.ShowChat(false); else if (section == "about") owner.ShowAbout(); else owner.ShowSettingsSection(section);
    }
    private void Finish(string state) { if(!SaveProgress(()=>owner.Settings.OnboardingState=state))return;finished=true;Close(); }
}
