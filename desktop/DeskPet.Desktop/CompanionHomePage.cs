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
    readonly SpriteAssets assets=new();
    readonly StackPanel body=Ui.Column();
    StackPanel? settingsControls;Button? retryControl;
    readonly Dictionary<string,HomeCardCapture> captures=new();
    bool introOpen,rulesOpen,settingsOpen;string? captureKey;
    string? identity;
    PetSkin? displayedSkin;string? displayedName;
    int revision;
    Action? updateLayout;
    Task refreshTask=Task.CompletedTask;
    bool disposed,saveFailed,saving;
    public RelationshipHomeView? View {get;private set;}
    public Task PendingSettings {get;private set;}=Task.CompletedTask;
    public Task PendingRetry {get;private set;}=Task.CompletedTask;
    public CompanionHomePage(DesktopController owner)
    {
        this.owner=owner;model=new RelationshipHomeModel(owner.Relationships);Name="CompanionHomePage";
        VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        Background=DesktopTheme.Surface;body.Margin=new Thickness(28,24,28,28);body.MaxWidth=1080;body.HorizontalAlignment=HorizontalAlignment.Stretch;Content=body;
        if(Application.Current!=null)Application.Current.ActualThemeVariantChanged+=ThemeChanged;owner.Relationships.Changed+=SavedChanged;owner.Changed+=IdentityChanged;SizeChanged+=(_,_)=>updateLayout?.Invoke();
        PropertyChanged+=(_,args)=>{if(args.Property==ViewportProperty)updateLayout?.Invoke();};
    }
    void ThemeChanged(object? sender,EventArgs args){if(!disposed&&View!=null)Render();}
    async void SavedChanged(){if(!disposed)await RefreshAsync();}
    async void IdentityChanged()
    {
        if(disposed)return;
        if(identity!=owner.RelationshipCompanionId)await RefreshAsync();
        else if(View!=null&&(!ReferenceEquals(displayedSkin,owner.Skin)||displayedName!=owner.Skin.DisplayName))Render();
    }
    public Task RefreshAsync()=>refreshTask=LoadAndRenderAsync();
    async Task LoadAndRenderAsync()
    {
        if(disposed)return;string id=owner.RelationshipCompanionId;int version=++revision;
        if(identity!=id){identity=id;View=null;body.Children.Clear();body.Children.Add(Label("relationship.home.loading"));}
        var view=await model.LoadAsync(id,RelationshipTime.Current());
        if(disposed)return;
        if(version!=revision){await refreshTask;return;}
        if(id!=owner.RelationshipCompanionId){await RefreshAsync();return;}
        View=view;Render();
    }
    static TextBlock Text(string value,double size=14,string? name=null){var t=Ui.Text(value,size);t.Name=name;return t;}
    static TextBlock Label(string key,double size=14,string? name=null)=>Text(TextResources.Get(key),size,name);
    static StackPanel Section(string key){var title=Label(key,18);title.FontWeight=FontWeight.SemiBold;return new StackPanel{Spacing=10,Margin=new Thickness(0,0,0,22),Children={title}};}
        void Render()
        {
            displayedSkin=owner.Skin;displayedName=owner.Skin.DisplayName;
            int version=revision;double scroll=Offset.Y;
            var focused=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            string? focusName=focused!=null&&this.GetLogicalDescendants().Contains(focused)?focused.Name:null;
            var expandedStages=captures.Where(x=>x.Value.Expanded).Select(x=>x.Key).ToArray();
            string key=identity+"|"+TextResources.CurrentLanguage+"|"+owner.Skin.DisplayName+"|"+owner.Skin.PickerKey+"|"+owner.Skin.GetHashCode()+"|"+DesktopTheme.IsDark;
            if(captureKey!=key){bool switched=captureKey==null||!captureKey.StartsWith(identity+"|",StringComparison.Ordinal);foreach(var old in captures.Values)old.Dispose();captures.Clear();captureKey=key;if(switched){introOpen=false;scroll=0;expandedStages=new string[0];}}
            body.Children.Clear();var title=Label("relationship.home.title",26);title.FontWeight=FontWeight.SemiBold;body.Children.Add(title);
            var profile=new StackPanel();
            var head=new Grid();head.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(112)});head.ColumnDefinitions.Add(new ColumnDefinition());
            var avatar=new AvatarView{Name="CompanionHomeAvatar",Assets=assets,Skin=owner.Skin,Width=96,Height=96};
            avatar.Clip=new EllipseGeometry(new Rect(0,0,96,96));head.Children.Add(new Border{Child=avatar,Background=DesktopTheme.Surface,CornerRadius=new CornerRadius(28),Width=104,Height=104,HorizontalAlignment=HorizontalAlignment.Left});
            var name=Text(owner.Skin.DisplayName,24,"CompanionHomeName");name.FontWeight=FontWeight.SemiBold;name.TextTrimming=TextTrimming.CharacterEllipsis;ToolTip.SetTip(name,owner.Skin.DisplayName);
            Grid.SetColumn(name,1);name.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(name);profile.Children.Add(head);
            string description=owner.Skin.DisplayDescription??"";
            if(description.Length>180){var intro=new Expander{Name="CompanionIntroduction",Header=TextResources.Get("relationship.home.introduction"),Content=Text(description),IsExpanded=introOpen,Margin=new Thickness(0,16,0,0),Foreground=DesktopTheme.Brush("#282A2F")};intro.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty)introOpen=intro.IsExpanded;};HomeVisual.Disclosure(intro);profile.Children.Add(intro);}else{var desc=Text(description);desc.Margin=new Thickness(0,16,0,0);profile.Children.Add(desc);}
            var relation=Section("relationship.home.relation");relation.Margin=new Thickness(0);
            var profileBox=HomeVisual.Box(profile,true);var relationBox=HomeVisual.Box(relation,false);
            var top=new Grid{Margin=new Thickness(0,20,0,0)};top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(36,GridUnitType.Star)});top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(64,GridUnitType.Star)});top.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});top.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});top.Children.Add(profileBox);top.Children.Add(relationBox);body.Children.Add(top);
            UniformGrid? nodes=null;Grid? progressRow=null;StackPanel? stats=null;Grid? bottom=null;WrapPanel? cardsWrap=null;
            if(View!.Snapshot!=null){
                var p=View.Progress;var words=new StackPanel{Margin=new Thickness(18,0,0,0),VerticalAlignment=VerticalAlignment.Center};
                var stage=Text(RelationshipContent.StageName(p.StageId),20,"CompanionRelationshipStage");stage.FontWeight=FontWeight.SemiBold;words.Children.Add(stage);words.Children.Add(Text(p.ScoreText,28,"CompanionRelationshipScore"));var next=Text(p.NextText);next.Foreground=DesktopTheme.Brush("#7A7E86");words.Children.Add(next);
                progressRow=new Grid{Margin=new Thickness(0,14,0,16)};progressRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(112)});progressRow.ColumnDefinitions.Add(new ColumnDefinition());progressRow.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});progressRow.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});progressRow.Children.Add(HomeVisual.Ring(p.Fraction));Grid.SetColumn(words,1);progressRow.Children.Add(words);relation.Children.Add(progressRow);
                nodes=HomeVisual.Nodes(p);relation.Children.Add(nodes);var relationScope=Label("relationship.home.globalScope",12);relationScope.Foreground=DesktopTheme.Brush("#7A7E86");relation.Children.Add(relationScope);
                int statusIndex=0;foreach(string explanation in View.StatusTexts){var state=Text(explanation,13,statusIndex++==0?"RelationshipGrowthStatus":null);state.Margin=new Thickness(0,14,0,0);state.Foreground=DesktopTheme.Brush("#7A7E86");relation.Children.Add(state);}
                stats=new StackPanel{Margin=new Thickness(0,22,0,22)};stats.Children.Add(HomeVisual.Stat("heart",TextResources.Get("relationship.home.todayLabel"),View.TodayPoints+" / 20","CompanionTodayGrowth"));stats.Children.Add(HomeVisual.Stat("sun",TextResources.Get("relationship.home.activeDaysLabel"),TextResources.Format("relationship.home.daysValue",View.ActiveDays),"CompanionActiveDays"));stats.Children.Add(HomeVisual.Stat("calendar",TextResources.Get("relationship.home.acquaintanceDaysLabel"),TextResources.Format("relationship.home.daysValue",View.AcquaintanceDays),"CompanionAcquaintanceDays"));body.Children.Add(stats);
                var timeline=Section("relationship.home.timeline");var cards=Section("relationship.home.cards");cardsWrap=new WrapPanel();cards.Children.Add(cardsWrap);
                if(View.Snapshot.State==null){timeline.Children.Add(Label("relationship.home.empty"));cards.Children.Add(Label("relationship.home.emptyCards"));}
                else foreach(var milestone in View.Snapshot.State.Milestones.AsEnumerable().Reverse())timeline.Children.Add(HomeVisual.Milestone(RelationshipContent.StageName(milestone.StageId),RelationshipCardData.FormatDate(milestone.EarnedLocalDate)));
                if(View.Snapshot.State!=null)foreach(var milestone in View.Snapshot.State.Milestones){
                    HomeCardCapture? capture;if(!captures.TryGetValue(milestone.StageId,out capture)||capture.Data.EarnedLocalDate!=milestone.EarnedLocalDate){capture?.Dispose();var data=RelationshipCardData.FromSnapshot(View.Snapshot,milestone.StageId,owner.Skin.DisplayName);capture=new HomeCardCapture(data,RelationshipCardRenderer.Render(data,owner.Skin,assets)){Expanded=expandedStages.Contains(milestone.StageId)};captures[milestone.StageId]=capture;}
                    var card=new RelationshipCardPreview(capture,RelationshipContent.Welcome(milestone.StageId,owner.RelationshipPack),delegate{return !disposed&&captureKey==key&&identity==capture.Data.CompanionId&&owner.RelationshipCompanionId==capture.Data.CompanionId&&version==revision;},delegate{return !disposed&&owner.RelationshipCompanionId==capture.Data.CompanionId;},delegate{if(updateLayout!=null)updateLayout();});cardsWrap.Children.Add(card);
                }
                bottom=new Grid{Margin=new Thickness(0,4,0,20)};bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32,GridUnitType.Star)});bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(68,GridUnitType.Star)});bottom.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});bottom.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});bottom.Children.Add(timeline);bottom.Children.Add(cards);body.Children.Add(bottom);
            }
            bool warning=View.ReadFailed||saveFailed||!String.IsNullOrEmpty(owner.Relationships.Warning);bool restart=owner.Relationships.RequiresRestart;
            var warningPanel=new StackPanel();var warningText=Label(restart?"relationship.home.restartRequired":View.ReadFailed?"relationship.home.readWarning":saveFailed?"relationship.home.settingsWarning":"relationship.saveWarning",13,"RelationshipWarning");warningText.IsVisible=warning;warningText.Foreground=DesktopTheme.Brush("#91682E");warningPanel.Children.Add(warningText);
            var actions=new WrapPanel{Margin=new Thickness(0,8,0,0)};var retry=Ui.Button(TextResources.Get("relationship.home.retry"),()=>{});retry.Name="RelationshipRetry";retry.IsVisible=warning&&!restart;retry.IsEnabled=!saving;retryControl=retry;retry.Click+=delegate{if(!BeginSaving())return;PendingRetry=RetryChangesAsync();};actions.Children.Add(retry);
            var open=Ui.Button(TextResources.Get("relationship.home.openDataFolder"),()=>{});open.Name="RelationshipOpenDataFolder";open.IsVisible=warning;open.Click+=delegate{try{Directory.CreateDirectory(owner.Relationships.DataDirectory);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(owner.Relationships.DataDirectory){UseShellExecute=true});}catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is System.ComponentModel.Win32Exception))throw;warningText.Text=TextResources.Get("relationship.home.folderFailed");}};actions.Children.Add(open);
            var reboot=Ui.Button(TextResources.Get("relationship.home.restart"),()=>{});reboot.Name="RelationshipRestart";reboot.IsVisible=warning&&restart;reboot.Click+=delegate{owner.Run(owner.RestartForRelationshipRecoveryAsync);};actions.Children.Add(reboot);warningPanel.Children.Add(actions);if(warning){warningPanel.Margin=new Thickness(0,16,0,0);relation.Children.Add(warningPanel);}else relation.Children.Add(warningPanel);
            var rules=new Expander{HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="CompanionGrowthRules",Header=HomeVisual.Header("diamond",TextResources.Get("relationship.home.rules")),Foreground=DesktopTheme.Brush("#282A2F"),IsExpanded=rulesOpen,Content=HomeVisual.Rules()};rules.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty)rulesOpen=rules.IsExpanded;};
            var settings=new StackPanel();settingsControls=settings;var current=View.Snapshot==null?null:View.Snapshot.Settings;
            var scope=Label("relationship.home.globalScope",13);scope.Foreground=DesktopTheme.Brush("#7A7E86");scope.Margin=new Thickness(0,8,0,12);settings.Children.Add(scope);
            AddSetting(settings,"RelationshipGrowthEnabled","relationship.home.growth",current!=null&&current.GrowthEnabled,0);AddSetting(settings,"RelationshipFeedbackEnabled","relationship.home.feedback",current!=null&&current.FeedbackEnabled,1);AddSetting(settings,"RelationshipAiContextEnabled","relationship.home.aiContext",current!=null&&current.AiContextEnabled,2);settings.IsEnabled=current!=null&&!View.ReadFailed&&!saving;
            var settingsExp=new Expander{HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="CompanionRelationshipSettings",Header=HomeVisual.Header("settings",TextResources.Get("relationship.home.settings")),Foreground=DesktopTheme.Brush("#282A2F"),IsExpanded=settingsOpen,Content=settings};settingsExp.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty)settingsOpen=settingsExp.IsExpanded;};
            HomeVisual.Disclosure(rules);HomeVisual.Disclosure(settingsExp);var groups=new StackPanel();groups.Children.Add(rules);groups.Children.Add(new Border{Height=1,Background=DesktopTheme.Brush("#E9EAED"),Margin=new Thickness(0,10,0,10)});groups.Children.Add(settingsExp);body.Children.Add(HomeVisual.Box(groups,false));
            updateLayout=delegate{
                double viewport=Viewport.Width>0?Viewport.Width:Bounds.Width;double width=Math.Min(1080,Math.Max(0,viewport-(Bounds.Width<600?32:56)));bool wide=width>=760;body.Margin=new Thickness(Bounds.Width<600?16:28,24,Bounds.Width<600?16:28,28);
                Grid.SetColumn(relationBox,wide?1:0);Grid.SetRow(relationBox,wide?0:1);Grid.SetColumnSpan(profileBox,wide?1:2);Grid.SetColumnSpan(relationBox,wide?1:2);relationBox.Margin=new Thickness(wide?22:0,wide?0:16,0,0);
                if(nodes!=null){nodes.Columns=wide?6:3;nodes.Rows=wide?1:2;}
                if(progressRow!=null){bool tiny=width<340;var words=(Control)progressRow.Children[1];Grid.SetColumn(words,tiny?0:1);Grid.SetRow(words,tiny?1:0);Grid.SetColumnSpan(words,tiny?2:1);words.Margin=new Thickness(tiny?0:18,tiny?12:0,0,0);}
                if(stats!=null){stats.Orientation=wide?Orientation.Horizontal:Orientation.Vertical;foreach(Control stat in stats.Children)stat.Width=wide?width/3:Double.NaN;}
                if(bottom!=null){var cards=(Control)bottom.Children[1];Grid.SetColumn(cards,wide?1:0);Grid.SetRow(cards,wide?0:1);Grid.SetColumnSpan(bottom.Children[0],wide?1:2);Grid.SetColumnSpan(cards,wide?1:2);cards.Margin=new Thickness(wide?16:0,0,0,0);double cardArea=wide?width*.68-16:width;double cardWidth=wide?cardArea/2-22:cardArea-14;foreach(Control card in cardsWrap!.Children)card.Width=Math.Max(120,((HomeCardCapture)card.Tag!).Expanded?cardArea-14:cardWidth);}
            };updateLayout();InvalidateMeasure();Offset=new Vector(Offset.X,scroll);
            if(!String.IsNullOrEmpty(focusName)&&TopLevel.GetTopLevel(this) is Window window&&window.IsActive){var replacement=HomeVisual.FindNamed(body,focusName);if(replacement!=null)replacement.Focus();}
        }
    void AddSetting(StackPanel parent,string name,string key,bool value,int kind)
    {
        var check=new ToggleSwitch{Name=name,Content=Label(key),IsChecked=value,OnContent=null,OffContent=null};AutomationProperties.SetName(check,TextResources.Get(key));
        check.Click+=(_,_)=>{bool desired=check.IsChecked==true;check.IsChecked=value;if(View==null||View.ReadFailed||!parent.IsEnabled||!BeginSaving())return;PendingSettings=SaveAsync();async Task SaveAsync(){var current=View!.Snapshot.Settings;var result=await model.UpdateSettingsAsync(kind==0?desired:current.GrowthEnabled,kind==1?desired:current.FeedbackEnabled,kind==2?desired:current.AiContextEnabled);saving=false;if(disposed)return;saveFailed=!result.Success;await RefreshAsync();}};
        parent.Children.Add(check);
    }
    bool BeginSaving()
    {
        if(disposed||saving)return false;saving=true;
        if(settingsControls!=null)settingsControls.IsEnabled=false;
        if(retryControl!=null)retryControl.IsEnabled=false;
        return true;
    }
    async Task RetryChangesAsync()
    {
        var result=await model.RetryAsync();saving=false;if(disposed)return;saveFailed=!result.Success;await RefreshAsync();
    }
    public void Dispose(){if(disposed)return;disposed=true;revision++;if(Application.Current!=null)Application.Current.ActualThemeVariantChanged-=ThemeChanged;owner.Relationships.Changed-=SavedChanged;owner.Changed-=IdentityChanged;foreach(var capture in captures.Values)capture.Dispose();captures.Clear();assets.Dispose();}
}
