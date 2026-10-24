using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
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
        readonly Dictionary<string,HomeCardCapture> captures=new Dictionary<string,HomeCardCapture>();
        bool introOpen,rulesOpen,settingsOpen;string captureKey;
        string identity;
        SkinDefinition displayedSkin;string displayedName;
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
            ThemeManager.Changed+=ThemeChanged;owner.Relationships.Changed += SavedChanged;SizeChanged+=delegate{if(updateLayout!=null)updateLayout();};
            ScrollChanged+=delegate(object sender,ScrollChangedEventArgs args){if(args.ViewportWidthChange!=0&&updateLayout!=null)updateLayout();};
        }
        void ThemeChanged(){if(!disposed&&View!=null)Render();}
        void SavedChanged() { if (!disposed) ObserveRefresh(); }
        async void ObserveRefresh() { await RefreshAsync(); }
        internal void RefreshIdentity()
        {
            if(disposed)return;
            if(identity!=owner.RelationshipCompanionId)ObserveRefresh();
            else if(View!=null&&(!Object.ReferenceEquals(displayedSkin,owner.Skin)||displayedName!=owner.Skin.DisplayName))Render();
        }
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
            displayedSkin=owner.Skin;displayedName=owner.Skin.DisplayName;
            int version=revision;double scroll=VerticalOffset;
            var focused=Keyboard.FocusedElement as FrameworkElement;
            string focusName=focused!=null&&IsAncestorOf(focused)?focused.Name:null;
            var expandedStages=captures.Where(x=>x.Value.Expanded).Select(x=>x.Key).ToArray();
            string key=identity+"|"+TextResources.CurrentLanguage+"|"+owner.Skin.DisplayName+"|"+owner.Skin.PickerKey+"|"+owner.Skin.GetHashCode()+"|"+ThemeManager.IsDark;
            if(captureKey!=key){bool switched=captureKey==null||!captureKey.StartsWith(identity+"|",StringComparison.Ordinal);captures.Clear();captureKey=key;if(switched){introOpen=false;scroll=0;expandedStages=new string[0];}}
            body.Children.Clear();var title=Label("relationship.home.title",26);title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
            var profile=new StackPanel();
            var head=new Grid();head.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(112)});head.ColumnDefinitions.Add(new ColumnDefinition());
            var avatar=new Image{Name="CompanionHomeAvatar",Source=SkinCatalog.GetAvatar(owner.Skin),Width=96,Height=96,Stretch=Stretch.Uniform};
            avatar.Clip=new EllipseGeometry(new Point(48,48),48,48);head.Children.Add(new Border{Child=avatar,Background=ThemeManager.Surface,CornerRadius=new CornerRadius(28),Width=104,Height=104,HorizontalAlignment=HorizontalAlignment.Left});
            var name=Text(owner.Skin.DisplayName,24,"CompanionHomeName");name.FontWeight=FontWeights.SemiBold;name.TextTrimming=TextTrimming.CharacterEllipsis;name.ToolTip=owner.Skin.DisplayName;
            Grid.SetColumn(name,1);name.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(name);profile.Children.Add(head);
            string description=owner.Skin.DisplayDescription??"";
            if(description.Length>180){var intro=new Expander{Name="CompanionIntroduction",Header=TextResources.Get("relationship.home.introduction"),Content=Text(description),IsExpanded=introOpen,Margin=new Thickness(0,16,0,0),Foreground=AgentUi.Ink};intro.Expanded+=delegate{introOpen=true;};intro.Collapsed+=delegate{introOpen=false;};profile.Children.Add(intro);}else{var desc=Text(description);desc.Margin=new Thickness(0,16,0,0);profile.Children.Add(desc);}
            var relation=Section("relationship.home.relation");relation.Margin=new Thickness(0);
            var profileBox=HomeVisual.Box(profile,true);var relationBox=HomeVisual.Box(relation,false);
            var top=new Grid{Margin=new Thickness(0,20,0,0)};top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(36,GridUnitType.Star)});top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(64,GridUnitType.Star)});top.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});top.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});top.Children.Add(profileBox);top.Children.Add(relationBox);body.Children.Add(top);
            UniformGrid nodes=null;Grid progressRow=null;StackPanel stats=null;Grid bottom=null;WrapPanel cardsWrap=null;
            if(View.Snapshot!=null){
                var p=View.Progress;var words=new StackPanel{Margin=new Thickness(18,0,0,0),VerticalAlignment=VerticalAlignment.Center};
                var stage=Text(RelationshipContent.StageName(p.StageId),20,"CompanionRelationshipStage");stage.FontWeight=FontWeights.SemiBold;words.Children.Add(stage);words.Children.Add(Text(p.ScoreText,28,"CompanionRelationshipScore"));var next=Text(p.NextText);next.Foreground=AgentUi.Muted;words.Children.Add(next);
                progressRow=new Grid{Margin=new Thickness(0,14,0,16)};progressRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(112)});progressRow.ColumnDefinitions.Add(new ColumnDefinition());progressRow.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});progressRow.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});progressRow.Children.Add(HomeVisual.Ring(p.Fraction));Grid.SetColumn(words,1);progressRow.Children.Add(words);relation.Children.Add(progressRow);
                nodes=HomeVisual.Nodes(p);relation.Children.Add(nodes);var relationScope=Label("relationship.home.globalScope",12);relationScope.Foreground=AgentUi.Muted;relation.Children.Add(relationScope);
                int statusIndex=0;foreach(string explanation in View.StatusTexts){var state=Text(explanation,13,statusIndex++==0?"RelationshipGrowthStatus":null);state.Margin=new Thickness(0,14,0,0);state.Foreground=AgentUi.Muted;relation.Children.Add(state);}
                stats=new StackPanel{Margin=new Thickness(0,22,0,22)};stats.Children.Add(HomeVisual.Stat("heart",TextResources.Get("relationship.home.todayLabel"),View.TodayPoints+" / 20","CompanionTodayGrowth"));stats.Children.Add(HomeVisual.Stat("sun",TextResources.Get("relationship.home.activeDaysLabel"),TextResources.Format("relationship.home.daysValue",View.ActiveDays),"CompanionActiveDays"));stats.Children.Add(HomeVisual.Stat("calendar",TextResources.Get("relationship.home.acquaintanceDaysLabel"),TextResources.Format("relationship.home.daysValue",View.AcquaintanceDays),"CompanionAcquaintanceDays"));body.Children.Add(stats);
                var timeline=Section("relationship.home.timeline");var cards=Section("relationship.home.cards");cardsWrap=new WrapPanel();cards.Children.Add(cardsWrap);
                if(View.Snapshot.State==null){timeline.Children.Add(Label("relationship.home.empty"));cards.Children.Add(Label("relationship.home.emptyCards"));}
                else foreach(var milestone in View.Snapshot.State.Milestones.AsEnumerable().Reverse())timeline.Children.Add(HomeVisual.Milestone(RelationshipContent.StageName(milestone.StageId),RelationshipCardData.FormatDate(milestone.EarnedLocalDate)));
                if(View.Snapshot.State!=null)foreach(var milestone in View.Snapshot.State.Milestones){
                    HomeCardCapture capture;if(!captures.TryGetValue(milestone.StageId,out capture)||capture.Data.EarnedLocalDate!=milestone.EarnedLocalDate){var data=RelationshipCardData.FromSnapshot(View.Snapshot,milestone.StageId,owner.Skin.DisplayName);capture=new HomeCardCapture(data,RelationshipCardRenderer.Render(data,SkinCatalog.GetAvatar(owner.Skin))){Expanded=expandedStages.Contains(milestone.StageId)};captures[milestone.StageId]=capture;}
                    var card=new RelationshipCardPreview(capture,RelationshipContent.Welcome(milestone.StageId,owner.RelationshipPack),delegate{return !disposed&&captureKey==key&&identity==capture.Data.CompanionId&&owner.RelationshipCompanionId==capture.Data.CompanionId&&version==revision;},delegate{return !disposed&&owner.RelationshipCompanionId==capture.Data.CompanionId;},delegate{if(updateLayout!=null)updateLayout();});cardsWrap.Children.Add(card);
                }
                bottom=new Grid{Margin=new Thickness(0,4,0,20)};bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32,GridUnitType.Star)});bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(68,GridUnitType.Star)});bottom.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});bottom.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});bottom.Children.Add(timeline);bottom.Children.Add(cards);body.Children.Add(bottom);
            }
            bool warning=View.ReadFailed||saveFailed||!String.IsNullOrEmpty(owner.Relationships.Warning);bool restart=owner.Relationships.RequiresRestart;
            var warningPanel=new StackPanel();var warningText=Label(restart?"relationship.home.restartRequired":View.ReadFailed?"relationship.home.readWarning":saveFailed?"relationship.home.settingsWarning":"relationship.saveWarning",13,"RelationshipWarning");warningText.Visibility=warning?Visibility.Visible:Visibility.Collapsed;warningText.Foreground=ThemeManager.Brush("#97580C","#FFD38A");warningPanel.Children.Add(warningText);
            var actions=new WrapPanel{Margin=new Thickness(0,8,0,0)};var retry=Ui.Button(TextResources.Get("relationship.home.retry"));retry.Name="RelationshipRetry";retry.Visibility=warning&&!restart?Visibility.Visible:Visibility.Collapsed;retry.IsEnabled=!saving;retryControl=retry;retry.Click+=delegate{if(!BeginSaving())return;PendingRetry=RetryChangesAsync();};actions.Children.Add(retry);
            var open=Ui.Button(TextResources.Get("relationship.home.openDataFolder"));open.Name="RelationshipOpenDataFolder";open.Visibility=warning?Visibility.Visible:Visibility.Collapsed;open.Click+=delegate{try{Directory.CreateDirectory(owner.Relationships.DataDirectory);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(owner.Relationships.DataDirectory){UseShellExecute=true});}catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is System.ComponentModel.Win32Exception))throw;warningText.Text=TextResources.Get("relationship.home.folderFailed");}};actions.Children.Add(open);
            var reboot=Ui.Button(TextResources.Get("relationship.home.restart"));reboot.Name="RelationshipRestart";reboot.Visibility=warning&&restart?Visibility.Visible:Visibility.Collapsed;reboot.Click+=delegate{ApplicationRestart.Request(false);owner.Close();};actions.Children.Add(reboot);warningPanel.Children.Add(actions);if(warning){warningPanel.Margin=new Thickness(0,16,0,0);relation.Children.Add(warningPanel);}else relation.Children.Add(warningPanel);
            var rules=new Expander{HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="CompanionGrowthRules",Header=HomeVisual.Header("diamond",TextResources.Get("relationship.home.rules")),Foreground=AgentUi.Ink,IsExpanded=rulesOpen,Content=HomeVisual.Rules()};rules.Expanded+=delegate{rulesOpen=true;};rules.Collapsed+=delegate{rulesOpen=false;};
            var settings=new StackPanel();settingsControls=settings;var current=View.Snapshot==null?null:View.Snapshot.Settings;
            var scope=Label("relationship.home.globalScope",13);scope.Foreground=AgentUi.Muted;scope.Margin=new Thickness(0,8,0,12);settings.Children.Add(scope);
            AddSetting(settings,"RelationshipGrowthEnabled","relationship.home.growth",current!=null&&current.GrowthEnabled,0);AddSetting(settings,"RelationshipFeedbackEnabled","relationship.home.feedback",current!=null&&current.FeedbackEnabled,1);AddSetting(settings,"RelationshipAiContextEnabled","relationship.home.aiContext",current!=null&&current.AiContextEnabled,2);settings.IsEnabled=current!=null&&!View.ReadFailed&&!saving;
            var settingsExp=new Expander{HorizontalContentAlignment=HorizontalAlignment.Stretch,Name="CompanionRelationshipSettings",Header=HomeVisual.Header("settings",TextResources.Get("relationship.home.settings")),Foreground=AgentUi.Ink,IsExpanded=settingsOpen,Content=settings};settingsExp.Expanded+=delegate{settingsOpen=true;};settingsExp.Collapsed+=delegate{settingsOpen=false;};
            var groups=new StackPanel();groups.Children.Add(rules);groups.Children.Add(new Border{Height=1,Background=AgentUi.Line,Margin=new Thickness(0,10,0,10)});groups.Children.Add(settingsExp);body.Children.Add(HomeVisual.Box(groups,false));
            updateLayout=delegate{
                double viewport=ViewportWidth>0?ViewportWidth:ActualWidth;double width=Math.Min(1080,Math.Max(0,viewport-(ActualWidth<600?32:56)));bool wide=width>=760;body.Margin=new Thickness(ActualWidth<600?16:28,24,ActualWidth<600?16:28,28);
                Grid.SetColumn(relationBox,wide?1:0);Grid.SetRow(relationBox,wide?0:1);Grid.SetColumnSpan(profileBox,wide?1:2);Grid.SetColumnSpan(relationBox,wide?1:2);relationBox.Margin=new Thickness(wide?22:0,wide?0:16,0,0);
                if(nodes!=null){nodes.Columns=wide?6:3;nodes.Rows=wide?1:2;}
                if(progressRow!=null){bool tiny=width<340;var words=progressRow.Children[1] as FrameworkElement;Grid.SetColumn(words,tiny?0:1);Grid.SetRow(words,tiny?1:0);Grid.SetColumnSpan(words,tiny?2:1);words.Margin=new Thickness(tiny?0:18,tiny?12:0,0,0);}
                if(stats!=null){stats.Orientation=wide?Orientation.Horizontal:Orientation.Vertical;foreach(FrameworkElement stat in stats.Children)stat.Width=wide?width/3:Double.NaN;}
                if(bottom!=null){var cards=bottom.Children[1] as FrameworkElement;Grid.SetColumn(cards,wide?1:0);Grid.SetRow(cards,wide?0:1);Grid.SetColumnSpan(bottom.Children[0],wide?1:2);Grid.SetColumnSpan(cards,wide?1:2);cards.Margin=new Thickness(wide?16:0,0,0,0);double cardArea=wide?width*.68-16:width;double cardWidth=wide?cardArea/2-22:cardArea-14;foreach(FrameworkElement card in cardsWrap.Children)card.Width=Math.Max(120,((HomeCardCapture)card.Tag).Expanded?cardArea-14:cardWidth);}
            };updateLayout();UpdateLayout();ScrollToVerticalOffset(scroll);
            if(!String.IsNullOrEmpty(focusName)&&Window.GetWindow(this)!=null&&Window.GetWindow(this).IsActive){var replacement=HomeVisual.FindNamed(body,focusName);if(replacement!=null)replacement.Focus();}
        }
        void AddSetting(StackPanel parent,string name,string label,bool value,int kind)
        {
            var check=Ui.Toggle(TextResources.Get(label),value,delegate{});check.Name=name;check.Content=Label(label);
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
        public void Dispose(){if(disposed)return;disposed=true;revision++;ThemeManager.Changed-=ThemeChanged;owner.Relationships.Changed-=SavedChanged;captures.Clear();}
    }
}
