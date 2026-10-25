using Avalonia.Platform.Storage;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class CompanionHomePage : ScrollViewer, IDisposable
{
    // Derived controls must opt into the native ScrollViewer theme/template.
    // Without it the page paints its content but has no scroll presenter or extent.
    protected override Type StyleKeyOverride=>typeof(ScrollViewer);
    readonly DesktopController owner;
    readonly RelationshipHomeModel model;
        readonly HolidayKeepsakeAlbumView holidayAlbum;
        bool holidaySelected;
        readonly Avalonia.Threading.DispatcherTimer holidayPulse=new Avalonia.Threading.DispatcherTimer();
        internal Task PendingHoliday {get{return holidayAlbum.Pending;}}
        async void ObserveShownHome(){if(disposed)return;var top=TopLevel.GetTopLevel(this) as Window;await owner.ObserveHolidayEncounter(IsVisible&&top!=null&&top.IsVisible&&top.WindowState!=WindowState.Minimized,HolidayEncounterKind.CompanionHome);}

    readonly SpriteAssets assets=new();
    readonly StackPanel body=Ui.Column();
    StackPanel? settingsControls;Button? retryControl;
    readonly RelationshipStageArtSource scenes=new RelationshipStageArtSource();
    readonly Dictionary<string,HomeCardCapture> captures=new();
    bool introOpen,welcomeOpen,rulesOpen,settingsOpen;string? captureKey;
    string? identity;
    PetSkin? displayedSkin;string? displayedName,displayedSource;
    CompanionPortraitSource? portrait;CompanionPortraitSource? avatarSource;string? artworkKey;
    int revision;
    Action? updateLayout;
    Task refreshTask=Task.CompletedTask;
    bool disposed,saveFailed,saving;
    public RelationshipHomeView? View {get;private set;}
    public Task PendingSettings {get;private set;}=Task.CompletedTask;
    public Task PendingRetry {get;private set;}=Task.CompletedTask;
    public CompanionHomePage(DesktopController owner)
    {
        this.owner=owner;model=new RelationshipHomeModel(owner.Relationships);
            holidayAlbum=new HolidayKeepsakeAlbumView(owner.HolidayKeepsakes,()=>owner.Holidays.Catalog,async delegate{var top=TopLevel.GetTopLevel(this) as Window;await owner.ObserveHolidayEncounter(IsVisible&&top!=null&&top.IsVisible&&top.WindowState!=WindowState.Minimized,HolidayEncounterKind.CompanionHome,true);},()=>!disposed&&identity==owner.RelationshipCompanionId,owner.DataDirectory);
            holidayPulse.Interval=TimeSpan.FromSeconds(1);holidayPulse.Tick+=delegate{ObserveShownHome();};
            AttachedToVisualTree+=delegate{holidayPulse.Start();ObserveShownHome();};DetachedFromVisualTree+=delegate{holidayPulse.Stop();};
Name="CompanionHomePage";
        VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        Background=DesktopTheme.Surface;body.Margin=new Thickness(28,24,28,28);body.MaxWidth=1080;body.HorizontalAlignment=HorizontalAlignment.Stretch;Content=body;
        if(Application.Current!=null)Application.Current.ActualThemeVariantChanged+=ThemeChanged;owner.Relationships.Changed+=SavedChanged;owner.Changed+=IdentityChanged;SizeChanged+=(_,_)=>updateLayout?.Invoke();
        PropertyChanged+=(_,args)=>{if(args.Property==ViewportProperty)updateLayout?.Invoke();};
    }
    void ThemeChanged(object? sender,EventArgs args){if(!disposed&&View!=null){Render();var holidayRefresh=holidayAlbum.RefreshAsync();}}
    async void SavedChanged(){if(!disposed)await RefreshAsync();}
    async void IdentityChanged()
    {
        if(disposed)return;
        if(identity!=owner.RelationshipCompanionId)await RefreshAsync();
        else if(View!=null&&(!ReferenceEquals(displayedSkin,owner.Skin)||displayedName!=owner.Skin.DisplayName||displayedSource!=owner.Skin.SourceKey))Render();
    }
    public Task RefreshAsync()=>refreshTask=LoadAndRenderAsync();
    async Task LoadAndRenderAsync()
    {
        if(disposed)return;string id=owner.RelationshipCompanionId;int version=++revision;
        if(identity!=id){identity=id;scenes.SetIdentity(id);View=null;body.Children.Clear();body.Children.Add(Label(TextResources.Get("relationship.home.loading")));}
        var holiday=holidayAlbum.SetIdentityAsync(id,owner.Skin.OfficialCharacterId,owner.Skin.DisplayName);
            var view=await model.LoadAsync(id,RelationshipTime.Current());
            await holiday;
        if(disposed)return;
        if(version!=revision){await refreshTask;return;}
        if(id!=owner.RelationshipCompanionId){await RefreshAsync();return;}
        scenes.SetIdentity(id);View=view;Render();
    }
    static TextBlock Text(string value,double size=14,string? name=null){var t=Ui.Text(value,size);t.Name=name;return t;}
    static TextBlock Label(string value,double size=14,string? name=null)=>Text(value,size,name);
    static StackPanel Section(string value){var title=Label(value,18);title.FontWeight=FontWeight.SemiBold;return new StackPanel{Spacing=10,Margin=new Thickness(0,0,0,22),Children={title}};}
        void Render()
        {
            displayedSkin=owner.Skin;displayedName=owner.Skin.DisplayName;displayedSource=owner.Skin.SourceKey;
            int version=revision;double scroll=Offset.Y;
            var focused=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            var focusToken=HomeVisual.CaptureFocus(body,focused);
            var expandedStages=captures.Where(x=>x.Value.Expanded).Select(x=>x.Key).ToArray();
            string key=identity+"|"+TextResources.CurrentLanguage+"|"+owner.Skin.DisplayName+"|"+owner.Skin.PickerKey+"|"+owner.Skin.GetHashCode()+"|"+owner.Skin.SourceKey+"|"+owner.Skin.Preview+"|"+owner.Skin.Avatar+"|"+DesktopTheme.IsDark+"|"+RelationshipStageArt.Version+"|"+RelationshipTitleFonts.Version+"|"+RelationshipCardDecoration.Version+"|"+String.Join(",",RelationshipStageArt.All.Where(a=>a.CompanionId==identity&&a.HolidayId==null).Select(a=>a.SceneVersion));
            if(captureKey!=key){bool switched=captureKey==null||!captureKey.StartsWith(identity+"|",StringComparison.Ordinal);foreach(var old in captures.Values)old.Dispose();captures.Clear();captureKey=key;if(switched){introOpen=false;welcomeOpen=false;scroll=0;expandedStages=new string[0];}}
            string nextArtwork=owner.Skin.GetHashCode()+"|"+owner.Skin.SourceKey+"|"+owner.Skin.Preview+"|"+owner.Skin.Avatar;
            if(artworkKey!=nextArtwork){portrait?.Dispose();avatarSource?.Dispose();portrait=CompanionPortraitSource.Load(owner.Skin,assets);avatarSource=new CompanionPortraitSource(CompanionPortraitSource.Avatar(owner.Skin,assets));artworkKey=nextArtwork;}
            body.Children.Clear();var title=Label(TextResources.Get("relationship.home.title"),26);title.FontWeight=FontWeight.SemiBold;body.Children.Add(title);
            var profile=new StackPanel();
            var head=new Grid();head.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(68)});head.ColumnDefinitions.Add(new ColumnDefinition());
            var avatar=new CompanionPortraitVisual(avatarSource!,DesktopTheme.IsDark){Name="CompanionHomeAvatar",Skin=owner.Skin,Width=56,Height=56,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Left};
            head.Children.Add(avatar);
            var name=Text(owner.Skin.DisplayName,27,"CompanionHomeName");name.FontWeight=FontWeight.SemiBold;name.TextTrimming=TextTrimming.CharacterEllipsis;ToolTip.SetTip(name,owner.Skin.DisplayName);
            Grid.SetColumn(name,1);name.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(name);profile.Children.Add(head);
            string description=owner.Skin.DisplayDescription??"";
            if(description.Length>180){var intro=new Expander{Name="CompanionIntroduction",Header=TextResources.Get("relationship.home.introduction"),Content=Text(description),IsExpanded=introOpen,Margin=new Thickness(0,16,0,0),Foreground=DesktopTheme.Brush("#282A2F")};intro.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty)introOpen=intro.IsExpanded;};HomeVisual.Disclosure(intro);profile.Children.Add(intro);}else{var desc=Text(description);desc.Margin=new Thickness(0,16,0,0);profile.Children.Add(desc);}
            if(View!.Snapshot!=null&&View.Snapshot.State!=null){
                string greeting=RelationshipContent.Welcome(View.Snapshot.StageId,owner.RelationshipPack);
                if(!String.IsNullOrWhiteSpace(greeting)){
                    var message=Text(greeting,17,"CompanionCurrentWelcome");message.Margin=new Thickness(0,12,0,4);
                    AutomationProperties.SetName(message,greeting);
                    if(greeting.Length>110){var disclosure=new Expander{Name="CompanionWelcome",Header=TextResources.Get("relationship.home.stageMessage"),Content=message,IsExpanded=welcomeOpen,Margin=new Thickness(0,16,0,0),Foreground=DesktopTheme.Brush("#282A2F")};disclosure.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty)welcomeOpen=disclosure.IsExpanded;};HomeVisual.Disclosure(disclosure);profile.Children.Add(disclosure);}
                    else{var caption=Label(TextResources.Get("relationship.home.stageMessage"),12);caption.Foreground=DesktopTheme.Brush("#7A7E86");caption.Margin=new Thickness(0,20,0,0);profile.Children.Add(caption);profile.Children.Add(message);}
                }
            }
            var profileContent=new Grid();profileContent.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(136)});profileContent.ColumnDefinitions.Add(new ColumnDefinition());
            var portraitView=new CompanionPortraitVisual(portrait!,DesktopTheme.IsDark){Name="CompanionHomePortrait",Height=230,Margin=new Thickness(0,0,18,0)};profileContent.Children.Add(portraitView);Grid.SetColumn(profile,1);profile.VerticalAlignment=VerticalAlignment.Center;profileContent.Children.Add(profile);
            var relation=Section(TextResources.Get("relationship.home.relation"));relation.Margin=new Thickness(0);
            var profileBox=HomeVisual.Box(profileContent,true);var relationBox=HomeVisual.Box(relation,false);relationBox.Background=new SolidColorBrush(DesktopTheme.IsDark?Color.FromRgb(43,53,69):Color.FromRgb(244,247,252));
            var top=new Grid{Margin=new Thickness(0,20,0,0)};top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(55,GridUnitType.Star)});top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(45,GridUnitType.Star)});top.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});top.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});top.Children.Add(profileBox);top.Children.Add(relationBox);body.Children.Add(top);
            UniformGrid? nodes=null;Grid? progressRow=null;StackPanel? stats=null;Grid? bottom=null;WrapPanel? cardsWrap=null;
            if(View!.Snapshot!=null){
                var p=View.Progress;var words=new StackPanel{Margin=new Thickness(18,0,0,0),VerticalAlignment=VerticalAlignment.Center};
                var stage=Text(RelationshipContent.StageName(p.StageId),26,"CompanionRelationshipStage");stage.FontWeight=FontWeight.SemiBold;words.Children.Add(stage);words.Children.Add(Text(p.ScoreText,28,"CompanionRelationshipScore"));var next=Text(p.NextText);next.Foreground=DesktopTheme.Brush("#7A7E86");words.Children.Add(next);
                progressRow=new Grid{Margin=new Thickness(0,14,0,16)};progressRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(112)});progressRow.ColumnDefinitions.Add(new ColumnDefinition());progressRow.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});progressRow.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});progressRow.Children.Add(HomeVisual.Ring(p.Fraction));Grid.SetColumn(words,1);progressRow.Children.Add(words);relation.Children.Add(progressRow);
                nodes=HomeVisual.Nodes(p);relation.Children.Add(nodes);var relationScope=Label(TextResources.Get("relationship.home.globalScope"),12);relationScope.Foreground=DesktopTheme.Brush("#7A7E86");relation.Children.Add(relationScope);
                int statusIndex=0;foreach(string explanation in View.StatusTexts){var state=Text(explanation,13,statusIndex++==0?"RelationshipGrowthStatus":null);state.Margin=new Thickness(0,14,0,0);state.Foreground=DesktopTheme.Brush("#7A7E86");relation.Children.Add(state);}
                stats=new StackPanel{Margin=new Thickness(0,12,0,0)};stats.Children.Add(HomeVisual.Stat("heart",TextResources.Get("relationship.home.todayLabel"),View.TodayPoints+" / 20","CompanionTodayGrowth"));stats.Children.Add(HomeVisual.Stat("sun",TextResources.Get("relationship.home.activeDaysLabel"),TextResources.Format("relationship.home.daysValue",View.ActiveDays),"CompanionActiveDays"));stats.Children.Add(HomeVisual.Stat("calendar",TextResources.Get("relationship.home.acquaintanceDaysLabel"),TextResources.Format("relationship.home.daysValue",View.AcquaintanceDays),"CompanionAcquaintanceDays"));relation.Children.Add(stats);
                var timeline=Section(TextResources.Get("relationship.home.timeline"));var cards=Section(TextResources.Get("relationship.home.cards"));cardsWrap=new WrapPanel();cards.Children.Add(cardsWrap);
                if(View.Snapshot.State==null){timeline.Children.Add(Label(TextResources.Get("relationship.home.empty")));cards.Children.Add(Label(TextResources.Get("relationship.home.emptyCards")));}
                else foreach(var milestone in View.Snapshot.State.Milestones.AsEnumerable().Reverse())timeline.Children.Add(HomeVisual.Milestone(RelationshipContent.StageName(milestone.StageId),RelationshipCardData.FormatDate(milestone.EarnedLocalDate)));
                if(View.Snapshot.State!=null)foreach(var milestone in View.Snapshot.State.Milestones){
                    HomeCardCapture? capture;if(!captures.TryGetValue(milestone.StageId,out capture)||capture.Data.EarnedLocalDate!=milestone.EarnedLocalDate||capture.Data.FirstLocalDate!=View.Snapshot.State.FirstLocalDate){capture?.Dispose();var data=RelationshipCardData.FromSnapshot(View.Snapshot,milestone.StageId,owner.Skin.DisplayName);capture=new HomeCardCapture(data,RelationshipCardRenderer.Render(data,portrait!,DesktopTheme.IsDark,scenes.Get(data))){Expanded=expandedStages.Contains(milestone.StageId)};captures[milestone.StageId]=capture;}
                    var card=new RelationshipCardPreview(capture,RelationshipContent.Welcome(milestone.StageId,owner.RelationshipPack),delegate{return !disposed&&captureKey==key&&identity==capture.Data.CompanionId&&owner.RelationshipCompanionId==capture.Data.CompanionId&&version==revision;},delegate{return !disposed&&owner.RelationshipCompanionId==capture.Data.CompanionId;},delegate{if(updateLayout!=null)updateLayout();});cardsWrap.Children.Add(card);
                }
                bottom=new Grid{Margin=new Thickness(0,4,0,20)};bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(78,GridUnitType.Star)});bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(22,GridUnitType.Star)});bottom.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});bottom.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});bottom.Children.Add(timeline);bottom.Children.Add(cards);body.Children.Add(bottom);
            }
            var albumSelection=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,16)};
            var relationshipTab=Ui.Button(TextResources.Get("holidayKeepsake.relationshipAlbum"),()=>{});var holidayTab=Ui.Button(TextResources.Get("holidayKeepsake.albumTitle"),()=>{});HomeVisual.SecondaryButton(relationshipTab);HomeVisual.SecondaryButton(holidayTab);
            if(bottom!=null)body.Children.Remove(bottom);body.Children.Add(albumSelection);if(bottom!=null)body.Children.Add(bottom);body.Children.Add(holidayAlbum);
            Action selectAlbum=delegate{relationshipTab.Background=holidaySelected?DesktopTheme.Surface:HomeVisual.Accent;holidayTab.Background=holidaySelected?HomeVisual.Accent:DesktopTheme.Surface;relationshipTab.Foreground=holidaySelected?DesktopTheme.Brush("#203754"):Brushes.White;holidayTab.Foreground=holidaySelected?Brushes.White:DesktopTheme.Brush("#203754");relationshipTab.FontWeight=holidaySelected?FontWeight.Normal:FontWeight.SemiBold;holidayTab.FontWeight=holidaySelected?FontWeight.SemiBold:FontWeight.Normal;holidayAlbum.IsVisible=holidaySelected;if(bottom!=null)bottom.IsVisible=!holidaySelected;};selectAlbum();
            relationshipTab.Name="RelationshipAlbumTab";holidayTab.Name="HolidayAlbumTab";albumSelection.Children.Add(relationshipTab);albumSelection.Children.Add(holidayTab);relationshipTab.Click+=delegate{holidaySelected=false;selectAlbum();};holidayTab.Click+=delegate{holidaySelected=true;selectAlbum();};
            bool warning=View.ReadFailed||saveFailed||!String.IsNullOrEmpty(owner.Relationships.Warning);bool restart=owner.Relationships.RequiresRestart;
            var warningPanel=new StackPanel();var warningText=Label(restart?TextResources.Get("relationship.home.restartRequired"):View.ReadFailed?TextResources.Get("relationship.home.readWarning"):saveFailed?TextResources.Get("relationship.home.settingsWarning"):TextResources.Get("relationship.saveWarning"),13,"RelationshipWarning");warningText.IsVisible=warning;warningText.Foreground=DesktopTheme.Brush("#91682E");warningPanel.Children.Add(warningText);
            var actions=new WrapPanel{Margin=new Thickness(0,8,0,0),ItemSpacing=8,LineSpacing=8};var retry=Ui.Button(TextResources.Get("relationship.home.retry"),()=>{});HomeVisual.SecondaryButton(retry);retry.Name="RelationshipRetry";retry.IsVisible=warning&&!restart;retry.IsEnabled=!saving;retryControl=retry;retry.Click+=delegate{if(!BeginSaving())return;PendingRetry=RetryChangesAsync();};actions.Children.Add(retry);
            var open=Ui.Button(TextResources.Get("relationship.home.openDataFolder"),()=>{});HomeVisual.SecondaryButton(open);open.Name="RelationshipOpenDataFolder";open.IsVisible=warning;open.Click+=delegate{try{Directory.CreateDirectory(owner.Relationships.DataDirectory);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(owner.Relationships.DataDirectory){UseShellExecute=true});}catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is System.ComponentModel.Win32Exception))throw;warningText.Text=TextResources.Get("relationship.home.folderFailed");}};actions.Children.Add(open);
            var reboot=Ui.Button(TextResources.Get("relationship.home.restart"),()=>{});HomeVisual.SecondaryButton(reboot);reboot.Name="RelationshipRestart";reboot.IsVisible=warning&&restart;reboot.Click+=delegate{owner.Run(owner.RestartForRelationshipRecoveryAsync);};actions.Children.Add(reboot);warningPanel.Children.Add(actions);if(warning){warningPanel.Margin=new Thickness(0,16,0,0);relation.Children.Add(warningPanel);}else relation.Children.Add(warningPanel);
            var rules=new Expander{HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="CompanionGrowthRules",Header=HomeVisual.Header("diamond",TextResources.Get("relationship.home.rules")),Foreground=DesktopTheme.Brush("#282A2F"),IsExpanded=rulesOpen,Content=HomeVisual.Rules()};rules.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty)rulesOpen=rules.IsExpanded;};
            var settings=new StackPanel();settingsControls=settings;var current=View.Snapshot==null?null:View.Snapshot.Settings;
            var scope=Label(TextResources.Get("relationship.home.globalScope"),13);scope.Foreground=DesktopTheme.Brush("#7A7E86");scope.Margin=new Thickness(0,8,0,12);settings.Children.Add(scope);
            AddSetting(settings,"RelationshipGrowthEnabled",TextResources.Get("relationship.home.growth"),current!=null&&current.GrowthEnabled,0);AddSetting(settings,"RelationshipFeedbackEnabled",TextResources.Get("relationship.home.feedback"),current!=null&&current.FeedbackEnabled,1);AddSetting(settings,"RelationshipAiContextEnabled",TextResources.Get("relationship.home.aiContext"),current!=null&&current.AiContextEnabled,2);settings.IsEnabled=current!=null&&!View.ReadFailed&&!saving;
            var settingsExp=new Expander{HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="CompanionRelationshipSettings",Header=HomeVisual.Header("settings",TextResources.Get("relationship.home.settings")),Foreground=DesktopTheme.Brush("#282A2F"),IsExpanded=settingsOpen,Content=settings};settingsExp.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty)settingsOpen=settingsExp.IsExpanded;};
            HomeVisual.Disclosure(rules);HomeVisual.Disclosure(settingsExp);var groups=new StackPanel();groups.Children.Add(rules);groups.Children.Add(new Border{Height=1,Background=DesktopTheme.Brush("#E9EAED"),Margin=new Thickness(0,10,0,10)});groups.Children.Add(settingsExp);body.Children.Add(HomeVisual.Box(groups,false));
            updateLayout=delegate{
                double viewport=Viewport.Width>0?Viewport.Width:Bounds.Width;double width=Math.Min(1080,Math.Max(0,viewport-(Bounds.Width<600?32:56)));bool wide=width>=760;body.Margin=new Thickness(Bounds.Width<600?16:28,24,Bounds.Width<600?16:28,28);
                Grid.SetColumn(relationBox,wide?1:0);Grid.SetRow(relationBox,wide?0:1);Grid.SetColumnSpan(profileBox,wide?1:2);Grid.SetColumnSpan(relationBox,wide?1:2);relationBox.Margin=new Thickness(wide?22:0,wide?0:16,0,0);
                if(nodes!=null){nodes.Columns=wide?6:3;nodes.Rows=wide?1:2;}
                if(progressRow!=null){bool tiny=width<340;var words=(Control)progressRow.Children[1];Grid.SetColumn(words,tiny?0:1);Grid.SetRow(words,tiny?1:0);Grid.SetColumnSpan(words,tiny?2:1);words.Margin=new Thickness(tiny?0:18,tiny?12:0,0,0);}
                double profileWidth=wide?width*.55-44:width-44;bool compactPortrait=profileWidth<380;profileContent.ColumnDefinitions[0].Width=new GridLength(compactPortrait?112:160);portraitView.Height=compactPortrait?216:230;portraitView.Margin=new Thickness(0,0,compactPortrait?12:20,0);
                if(stats!=null){bool horizontal=wide?width*.45-66>=370:width-44>=370;stats.Orientation=horizontal?Orientation.Horizontal:Orientation.Vertical;foreach(Control stat in stats.Children)stat.Width=horizontal?(wide?width*.45-66:width-44)/3:Double.NaN;}
                if(bottom!=null){var cards=bottom.Children[1] as Control;var timeline=bottom.Children[0] as Control;Grid.SetColumn(cards,0);Grid.SetRow(cards,0);Grid.SetColumn(timeline,wide?1:0);Grid.SetRow(timeline,wide?0:1);Grid.SetColumnSpan(timeline,wide?1:2);Grid.SetColumnSpan(cards,wide?1:2);cards.Margin=new Thickness(0,0,wide?24:0,0);timeline.Margin=new Thickness(wide?16:0,0,0,0);double available=cards.Bounds.Width>0?cards.Bounds.Width:wide?width*.78-24:width;double cardArea=Math.Max(120,available-14);double cardWidth=cardArea>=660?(cardArea-14)/2:cardArea;foreach(Control card in cardsWrap!.Children)card.Width=((HomeCardCapture)card.Tag!).Expanded?cardArea:cardWidth;}
            };updateLayout();InvalidateMeasure();Offset=new Vector(Offset.X,scroll);
            if(focusToken!=null&&TopLevel.GetTopLevel(this) is Window window&&window.IsActive){var replacement=HomeVisual.ResolveFocus(body,focusToken);if(replacement!=null)replacement.Focus();}
        }
    void AddSetting(StackPanel parent,string name,string label,bool value,int kind)
    {
        var check=new ToggleSwitch{Name=name,Content=Label(label),IsChecked=value,OnContent=null,OffContent=null};AutomationProperties.SetName(check,label);
        check.Click+=(_,_)=>{bool desired=check.IsChecked==true;check.IsChecked=value;if(View==null||View.ReadFailed||!parent.IsEnabled||!BeginSaving())return;var current=View.Snapshot.Settings;PendingSettings=SaveAsync(kind==0?desired:current.GrowthEnabled,kind==1?desired:current.FeedbackEnabled,kind==2?desired:current.AiContextEnabled);};
        parent.Children.Add(check);
    }
    bool BeginSaving()
    {
        if(disposed||saving)return false;saving=true;
        if(settingsControls!=null)settingsControls.IsEnabled=false;
        if(retryControl!=null)retryControl.IsEnabled=false;
        return true;
    }
    async Task SaveAsync(bool growth,bool feedback,bool ai)
    {
        try{saveFailed=!(await model.UpdateSettingsAsync(growth,feedback,ai)).Success;}
        catch(Exception){saveFailed=true;}
        finally{saving=false;}
        if(!disposed)await RefreshAsync();
    }
    async Task RetryChangesAsync()
    {
        try{saveFailed=!(await model.RetryAsync()).Success;}
        catch(Exception){saveFailed=true;}
        finally{saving=false;}
        if(!disposed)await RefreshAsync();
    }
    public void Dispose(){if(disposed)return;disposed=true;revision++;holidayPulse.Stop();holidayAlbum.Dispose();if(Application.Current!=null)Application.Current.ActualThemeVariantChanged-=ThemeChanged;owner.Relationships.Changed-=SavedChanged;owner.Changed-=IdentityChanged;foreach(var capture in captures.Values)capture.Dispose();captures.Clear();body.Children.Clear();scenes.Dispose();portrait?.Dispose();avatarSource?.Dispose();assets.Dispose();}
}
