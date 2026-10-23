using Avalonia;
using Avalonia.Controls;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class GuideWindow : Window
{
    private readonly DesktopController owner;
    private readonly TextBlock title = Ui.Text("", 22), detail = Ui.Text("");
    private readonly Button open;
    private readonly Button back, next;
    private int step;
    internal bool AllowsCompanionPreparation {get;private set;}
    internal string CurrentLessonId=>Steps[step].Id;
    private readonly (string Id,string Title,string Detail,string Section)[] Steps;
    private readonly StackPanel bodyContent=new(){Spacing=12};
    private readonly TextBlock preparationStatus=Ui.Text("");
    private bool closed;
    private bool finished;
    private static readonly (string Title, string Detail, string Section)[] LegacySteps =
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
        owner = controller; Title = TextResources.Get("guideWindow.guideWindow.title"); Width = 610; Height = 480; MinWidth = 420; MinHeight = 320; Topmost = true;
        Steps=new[]{("prepare.network",TextResources.Get("onboarding.prepare.network"),TextResources.Get("onboarding.prepare.networkDetail"),"prepare-network"),("prepare.character",TextResources.Get("guideWindow.steps.steps"),TextResources.Get("guideWindow.steps.steps.alternativeA"),"prepare-character"),("prepare.packs",TextResources.Get("onboarding.prepare.packs"),TextResources.Get("onboarding.prepare.packsDetail"),"prepare-packs")}.Concat(LegacySteps.Select((item,index)=>("ava.legacy."+index,item.Title,item.Detail,item.Section))).ToArray();
        int saved=Array.FindIndex(Steps,item=>item.Id==owner.Settings.OnboardingLessonId);
        step=owner.Settings.OnboardingState is "completed" or "skipped"?0:owner.Settings.OnboardingState=="new"?(owner.Settings.OnboardingStep==0?0:Math.Clamp(owner.Settings.OnboardingStep+3,0,Steps.Length-1)):saved>=0?saved:Math.Clamp(owner.Settings.OnboardingStep+3,0,Steps.Length-1);

        AllowsCompanionPreparation=owner.OfficialPreparation.State==OfficialCompanionPreparationState.Running;
        owner.Settings.OnboardingState = "deferred";
        open = Ui.Button(TextResources.Get("guideWindow.guideWindow.text"), OpenStep);
        open.Name="GuideOpenStep";
        back = Ui.Button(TextResources.Get("guideWindow.guideWindow.text.alternativeA"), () => { if (step > 0) { step--; Persist(); Refresh(); } });
        next = Ui.Button(TextResources.Get("guideWindow.guideWindow.text.alternativeB"), () => { if (step == Steps.Length - 1) Finish("completed"); else { step++; Persist(); Refresh(); } });
        back.Name="GuideBack";next.Name="GuideNext";
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 14 };
        layout.Children.Add(title);
        var body = new ScrollViewer { Name="GuideBody",Content = bodyContent, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(body, 1); layout.Children.Add(body);
        var exit = Ui.Button(TextResources.Get("guideWindow.guideWindow.text.alternativeC"), () => Finish("skipped"));
        exit.Name = "GuideSkip"; exit.FontSize = 14; exit.FontWeight = Avalonia.Media.FontWeight.SemiBold; exit.Foreground = DesktopTheme.Brush("#203754");
        var footer = Ui.Column(open, Ui.Row(back, next), Ui.Row(Ui.Button(TextResources.Get("guideWindow.guideWindow.text.alternativeD"), () => Finish("deferred")), exit));
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        Content = new Border { Padding = new Thickness(24), Child = layout };
        Closing += (_, _) => { if (!finished) { owner.Settings.OnboardingState = "deferred"; Persist(); } };
        preparationStatus.Name="GuidePreparationStatus";owner.OfficialPreparation.Changed+=RefreshPreparation;
        Closed+=(_,_)=>{closed=true;owner.OfficialPreparation.Changed-=RefreshPreparation;};
        RefreshPreparation();
        Persist(); Refresh();
    }
    private void Persist() { owner.Settings.OnboardingStep = step; owner.Settings.OnboardingLessonId=Steps[step].Id; owner.Save(); }
    private void Refresh()
    {
        var item=Steps[step];title.Text=$"{step+1} / {Steps.Length} · {item.Title}";detail.Text=item.Detail;detail.IsVisible=true;bodyContent.Children.Clear();bodyContent.Children.Add(detail);back.IsEnabled=step>0;
        next.Content=step==Steps.Length-1?TextResources.Get("guideWindow.refresh.stepStepsLength"):TextResources.Get("guideWindow.refresh.stepStepsLength.alternativeA");open.IsVisible=!item.Section.StartsWith("prepare-");
        if(item.Section=="prepare-network"){var route=new GitHubRoutingView(owner);route.Changed+=()=>next.IsEnabled=route.Saved;bodyContent.Children.Add(route);}
        else next.IsEnabled=true;
        if(item.Section=="prepare-character"){
            var choices=new ComboBox{Name="GuidePartnerPicker",ItemsSource=owner.Skins,SelectedItem=owner.Skin,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch,ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<PetSkin>((skin,_)=>Ui.Text(skin?.DisplayName??""))};
            choices.SelectionChanged+=(_,_)=>{if(choices.SelectedItem is PetSkin selected){AllowsCompanionPreparation=true;owner.Run(()=>owner.SelectSkinAsync(selected,CompanionSelectionOrigin.Onboarding));}};bodyContent.Children.Add(choices);
        }
        if(item.Section=="prepare-packs"){
            var start=Ui.Button(TextResources.Get("onboarding.prepare.start"),()=>{AllowsCompanionPreparation=true;owner.Run(owner.OfficialPreparation.StartAsync);});start.Name="GuidePreparePacks";start.Content=Ui.Text(TextResources.Get("onboarding.prepare.start"));start.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;start.HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Center;start.Background=DesktopTheme.Brush("#357FF5");start.Foreground=Avalonia.Media.Brushes.White;((TextBlock)start.Content).Foreground=Avalonia.Media.Brushes.White;
            bodyContent.Children.Add(preparationStatus);bodyContent.Children.Add(start);var progressButton=Ui.Button(TextResources.Get("onboarding.prepare.progress"),owner.ShowOfficialPreparation);progressButton.Content=Ui.Text(TextResources.Get("onboarding.prepare.progress"));progressButton.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;bodyContent.Children.Add(progressButton);next.Content=TextResources.Get("onboarding.prepare.later");
        }
        RefreshPreparation();
    }
    private void RefreshPreparation()=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>{
        if(closed)return;var state=owner.OfficialPreparation.State;preparationStatus.Text=state==OfficialCompanionPreparationState.Idle?"":owner.OfficialPreparation.StatusText;
        if(Steps[step].Section=="prepare-packs"){detail.IsVisible=state==OfficialCompanionPreparationState.Idle;preparationStatus.IsVisible=state!=OfficialCompanionPreparationState.Idle;next.Content=state==OfficialCompanionPreparationState.Running?TextResources.Get("guideWindow.refresh.stepStepsLength.alternativeA"):TextResources.Get("onboarding.prepare.later");}
    });
    private void OpenStep()
    {
        var section = Steps[step].Section;
        if (section == "chat") owner.ShowChat(true); else if (section == "fullchat") owner.ShowChat(false); else if (section == "about") owner.ShowAbout(); else owner.ShowSettingsSection(section);
    }
    private void Finish(string state) { owner.Settings.OnboardingState = state; Persist(); finished = true; Close(); }
}
