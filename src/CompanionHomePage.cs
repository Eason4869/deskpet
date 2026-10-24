using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class CompanionHomePage : ScrollViewer, IDisposable
    {
        readonly PetWindow owner;
        readonly RelationshipHomeModel model;
        readonly StackPanel body = new StackPanel();
        StackPanel settingsControls;Button retryControl;
        string identity;
        int revision;
        Action updateLayout;
        Task refreshTask;
        bool disposed, saveFailed, saving;
        public Task PendingSettings {get;private set;}
        public Task PendingRetry {get;private set;}
        public RelationshipHomeView View { get; private set; }
        public CompanionHomePage(PetWindow owner)
        {
            this.owner = owner; model = new RelationshipHomeModel(owner.Relationships);
            PendingRetry=PendingSettings=Task.FromResult(true);Name = "CompanionHomePage"; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; Background = ThemeManager.Surface;
            body.Margin = new Thickness(28,24,28,28); body.MaxWidth=1080; body.HorizontalAlignment=HorizontalAlignment.Stretch; Content = body;
            owner.Relationships.Changed += SavedChanged;SizeChanged+=delegate{if(updateLayout!=null)updateLayout();};
        }
        void SavedChanged() { if (!disposed) ObserveRefresh(); }
        async void ObserveRefresh() { await RefreshAsync(); }
        internal void RefreshIdentity() { if (!disposed && identity != owner.RelationshipCompanionId) ObserveRefresh(); }
        public Task RefreshAsync(){return refreshTask=LoadAndRenderAsync();}
        async Task LoadAndRenderAsync()
        {
            if (disposed) return;
            string id=owner.RelationshipCompanionId;int version=++revision;
            if(identity!=id){identity=id;View=null;body.Children.Clear();body.Children.Add(Label("relationship.home.loading"));}
            var view=await model.LoadAsync(id,RelationshipTime.Current());
            if(disposed)return;
            if(version!=revision){if(refreshTask!=null)await refreshTask;return;}
            if(id!=owner.RelationshipCompanionId){await RefreshAsync();return;}
            View=view;Render();
        }
        TextBlock Label(string key,double size=13,string name=null)
        {var text=Ui.Text(TextResources.Get(key),size,AgentUi.Ink);if(name!=null)text.Name=name;return text;}
        TextBlock Text(string value,double size=13,string name=null)
        {var text=Ui.Text(value,size,AgentUi.Ink);if(name!=null)text.Name=name;return text;}
        StackPanel Section(string title){var panel=new StackPanel{Margin=new Thickness(0,0,0,22)};var heading=Label(title,18);heading.FontWeight=FontWeights.SemiBold;panel.Children.Add(heading);return panel;}
        void Render()
        {
            bool rulesOpen=body.Children.OfType<Expander>().Any(x=>x.Name=="CompanionGrowthRules"&&x.IsExpanded);
            bool settingsOpen=body.Children.OfType<Expander>().Any(x=>x.Name=="CompanionRelationshipSettings"&&x.IsExpanded);
            body.Children.Clear();body.Children.Add(Label("relationship.home.title",26));
            var profile=Section("relationship.home.profile");
            profile.Children.Add(new Image{Name="CompanionHomeAvatar",Source=SkinCatalog.GetAvatar(owner.Skin),Width=88,Height=88,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,12)});
            profile.Children.Add(Text(owner.Skin.DisplayName,23));
            var description=owner.Skin.DisplayDescription??"";
            if(description.Length>180)profile.Children.Add(new Expander{HorizontalContentAlignment=HorizontalAlignment.Stretch,Header=TextResources.Get("relationship.home.introduction"),Foreground=AgentUi.Ink,Content=Text(description)});else profile.Children.Add(Text(description));
            var relation=Section("relationship.home.relation");
            var top=new Grid{Margin=new Thickness(0,18,0,0)};top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition());top.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});top.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});top.Children.Add(profile);top.Children.Add(relation);
            Action layout=delegate{bool wide=ActualWidth>=780;Grid.SetColumn(relation,wide?1:0);Grid.SetRow(relation,wide?0:1);Grid.SetColumnSpan(profile,wide?1:2);Grid.SetColumnSpan(relation,wide?1:2);relation.Margin=new Thickness(wide?24:0,0,0,22);};updateLayout=layout;layout();body.Children.Add(top);
            if(View.Snapshot!=null){
                var p=View.Progress;relation.Children.Add(Text(RelationshipContent.StageName(p.StageId),22,"CompanionRelationshipStage"));relation.Children.Add(Text(p.ScoreText,28,"CompanionRelationshipScore"));
                relation.Children.Add(new ProgressBar{Minimum=0,Maximum=p.NextThreshold??1000,Value=p.Score,Height=7,Margin=new Thickness(0,6,0,12),Foreground=ThemeManager.Brush("#357FF5")});
                relation.Children.Add(Text(p.NextText));relation.Children.Add(Text(TextResources.Format("relationship.home.today",View.TodayPoints),13,"CompanionTodayGrowth"));
                relation.Children.Add(Text(TextResources.Format("relationship.home.activeDays",View.ActiveDays),13,"CompanionActiveDays"));relation.Children.Add(Text(TextResources.Format("relationship.home.acquaintanceDays",View.AcquaintanceDays),13,"CompanionAcquaintanceDays"));
                var timeline=Section("relationship.home.timeline");var cards=Section("relationship.home.cards");
                if(View.Snapshot.State==null){timeline.Children.Add(Label("relationship.home.empty"));cards.Children.Add(Label("relationship.home.emptyCards"));}
                else foreach(var m in View.Snapshot.State.Milestones){timeline.Children.Add(Text(TextResources.Format("relationship.home.achievement",m.EarnedLocalDate,RelationshipContent.StageName(m.StageId))));var card=new StackPanel{Margin=new Thickness(0,10,0,12)};card.Children.Add(Text(RelationshipContent.StageName(m.StageId),15));card.Children.Add(Text(RelationshipContent.Welcome(m.StageId,owner.RelationshipPack)));cards.Children.Add(card);}
                body.Children.Add(timeline);body.Children.Add(cards);
            }
            bool warning=View.ReadFailed||saveFailed||!String.IsNullOrEmpty(owner.Relationships.Warning);
            var warningText=Label(View.ReadFailed?"relationship.home.readWarning":saveFailed?"relationship.home.settingsWarning":"relationship.saveWarning",13,"RelationshipWarning");warningText.Visibility=warning?Visibility.Visible:Visibility.Collapsed;warningText.Foreground=ThemeManager.Brush("#97580C","#FFD38A");body.Children.Add(warningText);
            var retry=Ui.Button(TextResources.Get("relationship.home.retry"));retry.Name="RelationshipRetry";retry.Visibility=warning?Visibility.Visible:Visibility.Collapsed;retry.HorizontalAlignment=HorizontalAlignment.Left;retry.IsEnabled=!saving;retryControl=retry;retry.Click+=delegate{if(!BeginSaving())return;PendingRetry=RetryChangesAsync();};body.Children.Add(retry);
            body.Children.Add(new Expander{HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="CompanionGrowthRules",Header=TextResources.Get("relationship.home.rules"),Foreground=AgentUi.Ink,Margin=new Thickness(0,18,0,8),IsExpanded=rulesOpen,Content=Label("relationship.home.rulesBody")});
            var settings=new StackPanel();settingsControls=settings;var snapshot=View.Snapshot;var current=snapshot==null?null:snapshot.Settings;
            AddSetting(settings,"RelationshipGrowthEnabled","relationship.home.growth",current!=null&&current.GrowthEnabled,0);
            AddSetting(settings,"RelationshipFeedbackEnabled","relationship.home.feedback",current!=null&&current.FeedbackEnabled,1);
            AddSetting(settings,"RelationshipAiContextEnabled","relationship.home.aiContext",current!=null&&current.AiContextEnabled,2);
            settings.IsEnabled=current!=null&&!View.ReadFailed&&!saving;
            body.Children.Add(new Expander{HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="CompanionRelationshipSettings",Header=TextResources.Get("relationship.home.settings"),Foreground=AgentUi.Ink,Margin=new Thickness(0,8,0,0),IsExpanded=settingsOpen,Content=settings});
        }
        void AddSetting(StackPanel parent,string name,string label,bool value,int kind)
        {
            var check=new CheckBox{Name=name,Content=Label(label),IsChecked=value,Foreground=AgentUi.Ink,Margin=new Thickness(0,8,0,8)};
            System.Windows.Automation.AutomationProperties.SetName(check,TextResources.Get(label));
            check.Click+=delegate{
                bool desired=check.IsChecked==true;check.IsChecked=value;
                if(View==null||View.ReadFailed||!parent.IsEnabled||!BeginSaving())return;
                var current=View.Snapshot.Settings;
                PendingSettings=SaveAsync(kind==0?desired:current.GrowthEnabled,kind==1?desired:current.FeedbackEnabled,kind==2?desired:current.AiContextEnabled);
            };parent.Children.Add(check);
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
            var result=await model.UpdateSettingsAsync(growth,feedback,ai);saving=false;
            if(disposed)return;saveFailed=!result.Success;await RefreshAsync();
        }
        async Task RetryChangesAsync()
        {
            var result=await model.RetryAsync();saving=false;if(disposed)return;saveFailed=!result.Success;await RefreshAsync();
        }
        public void Dispose(){if(disposed)return;disposed=true;revision++;owner.Relationships.Changed-=SavedChanged;}
    }
}
