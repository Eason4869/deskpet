using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class CompanionHomePage : ScrollViewer, IDisposable
{
    readonly DesktopController owner;
    readonly RelationshipHomeModel model;
    readonly SpriteAssets assets=new();
    readonly StackPanel body=Ui.Column();
    string? identity;
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
        owner.Relationships.Changed+=SavedChanged;owner.Changed+=IdentityChanged;SizeChanged+=(_,_)=>updateLayout?.Invoke();
    }
    async void SavedChanged(){if(!disposed)await RefreshAsync();}
    async void IdentityChanged(){if(!disposed&&identity!=owner.RelationshipCompanionId)await RefreshAsync();}
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
        bool rulesOpen=body.Children.OfType<Expander>().Any(x=>x.Name=="CompanionGrowthRules"&&x.IsExpanded);
        bool settingsOpen=body.Children.OfType<Expander>().Any(x=>x.Name=="CompanionRelationshipSettings"&&x.IsExpanded);
        body.Children.Clear();body.Children.Add(Label("relationship.home.title",26));
        var profile=Section("relationship.home.profile");profile.Children.Add(new AvatarView{Name="CompanionHomeAvatar",Assets=assets,Skin=owner.Skin,Width=88,Height=88,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,8)});profile.Children.Add(Text(owner.Skin.DisplayName,23));
        var description=owner.Skin.DisplayDescription;
        if(description.Length>180)profile.Children.Add(new Expander{HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Header=TextResources.Get("relationship.home.introduction"),Content=Text(description)});else profile.Children.Add(Text(description));
        var relation=Section("relationship.home.relation");var top=new Grid{ColumnDefinitions=new ColumnDefinitions("*,*"),RowDefinitions=new RowDefinitions("Auto,Auto"),Margin=new Thickness(0,18,0,0)};top.Children.Add(profile);top.Children.Add(relation);
        void Layout(){bool wide=Bounds.Width>=780;Grid.SetColumn(relation,wide?1:0);Grid.SetRow(relation,wide?0:1);Grid.SetColumnSpan(profile,wide?1:2);Grid.SetColumnSpan(relation,wide?1:2);relation.Margin=new Thickness(wide?24:0,0,0,22);}updateLayout=Layout;Layout();body.Children.Add(top);
        var view=View!;
        if(view.Snapshot!=null){
            var p=view.Progress;relation.Children.Add(Text(RelationshipContent.StageName(p.StageId),22,"CompanionRelationshipStage"));relation.Children.Add(Text(p.ScoreText,28,"CompanionRelationshipScore"));
            relation.Children.Add(new ProgressBar{Minimum=0,Maximum=p.NextThreshold??1000,Value=p.Score,Height=7,Margin=new Thickness(0,0,0,8)});
            relation.Children.Add(Text(p.NextText));relation.Children.Add(Text(TextResources.Format("relationship.home.today",view.TodayPoints),14,"CompanionTodayGrowth"));relation.Children.Add(Text(TextResources.Format("relationship.home.activeDays",view.ActiveDays),14,"CompanionActiveDays"));relation.Children.Add(Text(TextResources.Format("relationship.home.acquaintanceDays",view.AcquaintanceDays),14,"CompanionAcquaintanceDays"));
            var timeline=Section("relationship.home.timeline");var cards=Section("relationship.home.cards");
            if(view.Snapshot.State==null){timeline.Children.Add(Label("relationship.home.empty"));cards.Children.Add(Label("relationship.home.emptyCards"));}
            else foreach(var m in view.Snapshot.State.Milestones){timeline.Children.Add(Text(TextResources.Format("relationship.home.achievement",m.EarnedLocalDate,RelationshipContent.StageName(m.StageId))));cards.Children.Add(Ui.Column(Text(RelationshipContent.StageName(m.StageId),16),Text(RelationshipContent.Welcome(m.StageId,owner.RelationshipPack))));}
            body.Children.Add(timeline);body.Children.Add(cards);
        }
        bool warning=view.ReadFailed||saveFailed||!string.IsNullOrEmpty(owner.Relationships.Warning);
        var warningText=Label(view.ReadFailed?"relationship.home.readWarning":saveFailed?"relationship.home.settingsWarning":"relationship.saveWarning",14,"RelationshipWarning");warningText.IsVisible=warning;warningText.Foreground=DesktopTheme.Brush("#8A6500");body.Children.Add(warningText);
        var retry=Ui.AsyncButton(TextResources.Get("relationship.home.retry"),()=>PendingRetry=RetryChangesAsync(),owner.Error);retry.Name="RelationshipRetry";retry.IsVisible=warning;body.Children.Add(retry);
        body.Children.Add(new Expander{HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="CompanionGrowthRules",Header=TextResources.Get("relationship.home.rules"),Margin=new Thickness(0,14,0,0),IsExpanded=rulesOpen,Content=Label("relationship.home.rulesBody")});
        var settings=Ui.Column();var current=view.Snapshot?.Settings;
        AddSetting(settings,"RelationshipGrowthEnabled","relationship.home.growth",current?.GrowthEnabled==true,0);AddSetting(settings,"RelationshipFeedbackEnabled","relationship.home.feedback",current?.FeedbackEnabled==true,1);AddSetting(settings,"RelationshipAiContextEnabled","relationship.home.aiContext",current?.AiContextEnabled==true,2);settings.IsEnabled=current!=null&&!view.ReadFailed&&!saving;
        body.Children.Add(new Expander{HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="CompanionRelationshipSettings",Header=TextResources.Get("relationship.home.settings"),IsExpanded=settingsOpen,Content=settings});
    }
    void AddSetting(StackPanel parent,string name,string key,bool value,int kind)
    {
        var check=new CheckBox{Name=name,Content=Label(key),IsChecked=value};AutomationProperties.SetName(check,TextResources.Get(key));
        check.Click+=(_,_)=>{bool desired=check.IsChecked==true;check.IsChecked=value;parent.IsEnabled=false;saving=true;PendingSettings=SaveAsync();async Task SaveAsync(){var current=View!.Snapshot.Settings;var result=await model.UpdateSettingsAsync(kind==0?desired:current.GrowthEnabled,kind==1?desired:current.FeedbackEnabled,kind==2?desired:current.AiContextEnabled);saving=false;if(disposed)return;saveFailed=!result.Success;await RefreshAsync();}};
        parent.Children.Add(check);
    }
    async Task RetryChangesAsync()
    {
        saving=true;var result=await model.RetryAsync();saving=false;if(disposed)return;saveFailed=!result.Success;await RefreshAsync();
    }
    public void Dispose(){if(disposed)return;disposed=true;revision++;owner.Relationships.Changed-=SavedChanged;owner.Changed-=IdentityChanged;assets.Dispose();}
}
