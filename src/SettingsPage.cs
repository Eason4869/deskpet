using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Dafeiyu
{
    public sealed partial class SettingsPage : UserControl, IDisposable
    {
        private readonly PetWindow pet;
        private readonly TextBlock name,notice,sizeLabel;
        private readonly Image avatar;
        private readonly ComboBox skins;
        private readonly HolidayOutfitPanel holidayOutfits;
        private readonly ColumnDefinition wardrobeColumn,wardrobeGap;
        private readonly TextBlock wardrobeHeading;
        private readonly Button restoreCompanions;
        private readonly CheckBox follow,wander,top,pause,autoStart,greetings,aiGreetings,stumble,autoSleep;
        private readonly TextBox greetingMin,greetingMax;
        private readonly StartupRegistration startup=new StartupRegistration(Process.GetCurrentProcess().MainModule.FileName);
        private readonly Slider size;
        private readonly ListBox navigation;
        private readonly ContentControl content;
        private readonly PersonaPanel persona;
        private ConnectionSettingsPanel connections;
        private AiConfigurationPanel aiConfiguration;
        private ImChannelSettingsPanel imChannels;
        private ScrollViewer imChannelsPage;
        private readonly CompanionConfigurationPanel companionPage;
        private AboutPanel about;
        private QuietPanel quiet;
        private WorkSettingsPanel work;
        private bool refreshing;
        private SkinDefinition displayedSkin;
        private bool disposed;
        private ProjectDefinition workEditingProject;
        public Window HostWindow {get{return Window.GetWindow(this);}}
        public int SelectedNavigationIndex {get{return navigation.SelectedIndex;}}
        public ProjectDefinition WorkEditingProject {get{return workEditingProject;}}
        public bool HasUnsavedWorkEdits {get{return work!=null && work.HasUnsavedChanges;}}
        public event Action<int> NavigationChanged;
        public event Action GuideRequested;
        public bool HasUnsavedEdits {get{return (work!=null && work.HasUnsavedChanges) || persona.HasUnsavedChanges || (imChannels!=null && imChannels.HasUnsavedChanges) || (connections!=null && connections.HasUnsavedChanges) || (about!=null && about.HasUnsavedChanges) || (quiet!=null && quiet.HasUnsavedChanges) || greetingMin.Text!=pet.Settings.GreetingMinMinutes.ToString() || greetingMax.Text!=pet.Settings.GreetingMaxMinutes.ToString();}}
        public SettingsPage(PetWindow companion)
        {
            if(companion==null)throw new ArgumentNullException("companion");
            pet=companion;Name="SettingsPage";Background=ThemeManager.Surface;
            var root=new StackPanel();var behaviorPage=new StackPanel();var greetingPage=new StackPanel();
            var header=new Grid { Margin=new Thickness(0,0,0,12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(74) }); header.ColumnDefinitions.Add(new ColumnDefinition());
            avatar=new Image { Source=SkinCatalog.GetAvatar(pet.Skin),Width=54,Height=54,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,54,54),10.8,10.8) };
            RenderOptions.SetBitmapScalingMode(avatar,BitmapScalingMode.HighQuality);
            var avatarCard=new Border { Child=avatar,CornerRadius=new CornerRadius(24),Background=Ui.Pale,Padding=new Thickness(2),Margin=new Thickness(0,0,14,0) };
            header.Children.Add(avatarCard);
            var introduction=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
            var eyebrow=LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text")),12,Ui.Muted); eyebrow.Margin=new Thickness(0,0,0,3); introduction.Children.Add(eyebrow);
            name=Ui.Text(()=>(pet.Skin.DisplayName),24); name.FontWeight=FontWeights.SemiBold; introduction.Children.Add(name);
            introduction.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeA")),12,Ui.Muted));
            Grid.SetColumn(introduction,1); header.Children.Add(introduction); root.Children.Add(header);
            var homeLink=LocalizedUi.Button(()=>(TextResources.Get("relationship.home.open")));homeLink.Name="OpenCompanionHome";homeLink.HorizontalAlignment=HorizontalAlignment.Left;homeLink.Click+=delegate{OpenCompanionFeature("CompanionHomePage");};root.Children.Add(homeLink);

            var appearance=new StackPanel();
            var appearanceRow=new Grid {Name="CompanionWardrobeRow",Margin=new Thickness(0,0,0,6)};
            appearanceRow.ColumnDefinitions.Add(new ColumnDefinition());wardrobeGap=new ColumnDefinition{Width=new GridLength(0)};appearanceRow.ColumnDefinitions.Add(wardrobeGap);wardrobeColumn=new ColumnDefinition{Width=new GridLength(0)};appearanceRow.ColumnDefinitions.Add(wardrobeColumn);
            var actionsGap=new ColumnDefinition{Width=new GridLength(10)};var actionsColumn=new ColumnDefinition{Width=GridLength.Auto};appearanceRow.ColumnDefinitions.Add(actionsGap);appearanceRow.ColumnDefinitions.Add(actionsColumn);
            appearanceRow.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});appearanceRow.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            appearanceRow.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            appearanceRow.Children.Add(Heading(()=>(TextResources.Get("settingsPage.settingsPage.heading"))));wardrobeHeading=Heading(()=>(TextResources.Get("settingsPage.settingsPage.heading.alternativeA")));wardrobeHeading.Name="HolidayWardrobeLabel";Grid.SetColumn(wardrobeHeading,2);appearanceRow.Children.Add(wardrobeHeading);
            skins=new ComboBox { Name="SkinPicker",ItemTemplate=SkinCatalog.AvatarTemplate(RemoveCompanion,()=>pet.Catalog.Skins.Count>1),ItemsSource=pet.Catalog.Skins,SelectedItem=pet.Skin,MinHeight=36,Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,8,0,0),VerticalAlignment=VerticalAlignment.Top,HorizontalContentAlignment=HorizontalAlignment.Stretch };
            skins.SelectionChanged+=async delegate
            {
                if(refreshing || skins.SelectedItem==null) return;
                var selected=(SkinDefinition)skins.SelectedItem;
                if(HasUnsavedEdits){refreshing=true;skins.SelectedItem=pet.Skin;refreshing=false;ShowNotice(()=>(TextResources.Get("settingsPage.settingsPage.showNotice")));return;}
                refreshing=true;skins.SelectedItem=pet.Skin;refreshing=false;
                await pet.RequestSkinAsync(selected,pet.GuideCompanionSelection?CompanionSelectionOrigin.Onboarding:CompanionSelectionOrigin.Settings,HostWindow);RefreshSkinChoices();ShowNotice(pet.Notice);
            };
            skins.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,new System.Windows.Input.MouseButtonEventHandler(delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){var item=FindParentItem(e.OriginalSource as DependencyObject);if(item!=null&&item.IsSelected&&ReferenceEquals(item.DataContext,pet.Skin)){pet.SelectSkin(pet.Skin);ShowNotice(pet.Notice);}}),true);
            Grid.SetRow(skins,1);appearanceRow.Children.Add(skins);
            holidayOutfits=new HolidayOutfitPanel(pet.Holidays,()=>pet.Skin){Margin=new Thickness(0,8,0,0)};Grid.SetColumn(holidayOutfits,2);Grid.SetRow(holidayOutfits,1);appearanceRow.Children.Add(holidayOutfits);
            var import=LocalizedUi.Button(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeB"))); import.Name="ImportSkin"; import.Margin=new Thickness(0,8,0,0);import.VerticalAlignment=VerticalAlignment.Top;import.SetBinding(HeightProperty,new System.Windows.Data.Binding("ActualHeight"){Source=skins}); import.Click+=ImportSkin;
            appearance.Children.Add(appearanceRow);var packageActions=new WrapPanel{Name="CompanionPackageActions",Margin=new Thickness(0,8,0,0),VerticalAlignment=VerticalAlignment.Top};import.Margin=new Thickness(0);packageActions.Children.Add(import);
            var downloadAll=LocalizedUi.Button(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeC")));downloadAll.Name="DownloadAllOfficialCompanions";downloadAll.Margin=new Thickness(8,0,0,0);downloadAll.SetBinding(HeightProperty,new System.Windows.Data.Binding("ActualHeight"){Source=skins});downloadAll.Click+=async delegate{await pet.DownloadAllOfficialAsync(HostWindow);RefreshSkinChoices();};packageActions.Children.Add(downloadAll);Grid.SetColumn(packageActions,4);Grid.SetRow(packageActions,1);appearanceRow.Children.Add(packageActions);
            bool? compactPackages=null;Action refreshPackages=delegate{bool compact=appearanceRow.ActualWidth>0&&appearanceRow.ActualWidth<720;if(compactPackages==compact)return;compactPackages=compact;Grid.SetColumn(packageActions,compact?0:4);Grid.SetRow(packageActions,compact?2:1);Grid.SetColumnSpan(packageActions,compact?5:1);actionsGap.Width=new GridLength(compact?0:10);actionsColumn.Width=compact?new GridLength(0):GridLength.Auto;};appearanceRow.SizeChanged+=delegate{refreshPackages();};appearanceRow.Loaded+=delegate{refreshPackages();};
            appearance.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeD")),11,Ui.Muted));
            pet.Holidays.Changed+=HolidayWardrobeChanged;RefreshHolidayLayout();
            restoreCompanions=LocalizedUi.Button(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeE")));restoreCompanions.Name="RestoreCompanions";restoreCompanions.HorizontalAlignment=HorizontalAlignment.Left;restoreCompanions.FontSize=11;restoreCompanions.MinHeight=26;restoreCompanions.Padding=new Thickness(0);restoreCompanions.Background=Brushes.Transparent;restoreCompanions.BorderThickness=new Thickness(0);restoreCompanions.Click+=delegate {if(pet.NoticeOnlyCompanionGuide()){ShowNotice(pet.Notice);return;}if(HasUnsavedEdits || pet.DialogueBusy || pet.DictationActive){ShowNotice(()=>(TextResources.Get("settingsPage.settingsPage.showNotice.alternativeA")));return;}pet.Catalog.RestoreRemoved();pet.Catalog.PreserveActive(pet.Skin);RefreshSkinChoices();ShowNotice(()=>(TextResources.Get("settingsPage.settingsPage.showNotice.alternativeB")));};appearance.Children.Add(restoreCompanions);
            var guide=LocalizedUi.With(new Expander{Name="ImportGuide",Header=TextResources.Get("settingsPage.settingsPage.header"),Margin=new Thickness(0,12,0,0)},"Header",()=>(TextResources.Get("settingsPage.settingsPage.header")));var tutorial=new StackPanel{Margin=new Thickness(0,8,0,0)};
            tutorial.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeF")),12,Ui.Muted));
            var guideButtons=new WrapPanel{Margin=new Thickness(0,10,0,0)};var templates=LocalizedUi.Button(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeG")));templates.Name="OpenTemplates";templates.Click+=delegate {Process.Start(new ProcessStartInfo(ReleaseInfo.RepositoryUrl+"/releases/tag/v"+ReleaseInfo.DisplayVersion){UseShellExecute=true});};guideButtons.Children.Add(templates);var fullGuide=LocalizedUi.Button(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeH")));fullGuide.Name="OpenImportGuide";fullGuide.Click+=delegate {OpenGuide(Path.Combine("docs","skin-format.md"));};guideButtons.Children.Add(fullGuide);tutorial.Children.Add(guideButtons);guide.Content=tutorial;appearance.Children.Add(guide);var appearanceCard=GlassSurface.Card(appearance);appearanceCard.Padding=new Thickness(12);appearanceCard.Margin=new Thickness(0,0,0,12);root.Children.Add(appearanceCard);

            var behavior=new StackPanel{Name="GuideBehaviorSection"}; behavior.Children.Add(Heading(()=>(TextResources.Get("settingsPage.settingsPage.heading.alternativeB"))));
            follow=LocalizedUi.Toggle(()=>(TextResources.Get("settingsPage.settingsPage.toggle")),pet.Settings.FollowCursor,delegate(bool value) { if(!refreshing) { pet.Settings.FollowCursor=value; pet.ApplySettings(); } }); follow.Name="FollowCursor"; behavior.Children.Add(follow);
            wander=LocalizedUi.Toggle(()=>(TextResources.Get("settingsPage.settingsPage.toggle.alternativeA")),pet.Settings.Wander,delegate(bool value) { if(!refreshing) { pet.Settings.Wander=value; pet.ApplySettings(); } }); wander.Name="Wander"; behavior.Children.Add(wander);
            stumble=LocalizedUi.Toggle(()=>(TextResources.Get("settingsPage.settingsPage.toggle.alternativeB")),pet.Settings.StumbleEnabled,delegate(bool value) {if(!refreshing){pet.Settings.StumbleEnabled=value;pet.ApplySettings();}});stumble.Name="StumbleEnabled";behavior.Children.Add(stumble);
            behavior.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("companions.behavior.stumbleHint")),11,Ui.Muted));
            autoSleep=LocalizedUi.Toggle(()=>(TextResources.Get("settingsPage.settingsPage.toggle.alternativeC")),pet.Settings.AutoSleepEnabled,delegate(bool value){if(!refreshing){pet.Settings.AutoSleepEnabled=value;pet.ApplySettings();}});autoSleep.Name="AutoSleepEnabled";behavior.Children.Add(autoSleep);
            behavior.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeJ")),11,Ui.Muted));
            top=LocalizedUi.Toggle(()=>(TextResources.Get("settingsPage.settingsPage.toggle.alternativeD")),pet.Settings.Topmost,delegate(bool value) { if(!refreshing) { pet.Settings.Topmost=value; pet.ApplySettings(); } }); top.Name="Topmost"; behavior.Children.Add(top);
            autoStart=LocalizedUi.Toggle(()=>(TextResources.Get("settingsPage.settingsPage.toggle.alternativeE")),false,SetAutoStart);autoStart.Name="AutoStart";behavior.Children.Add(autoStart);
            behavior.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeK")),11,Ui.Muted));
            pause=LocalizedUi.Toggle(()=>(TextResources.Get("settingsPage.settingsPage.toggle.alternativeF")),pet.Paused,delegate(bool value) { if(!refreshing) pet.SetPaused(value); }); pause.Name="Pause"; behavior.Children.Add(pause);
            var sizeRow=new DockPanel { Margin=new Thickness(0,10,0,8) };
            sizeLabel=Ui.Text("",12,Ui.Blue); DockPanel.SetDock(sizeLabel,Dock.Right); sizeRow.Children.Add(sizeLabel); sizeRow.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeL")),12)); behavior.Children.Add(sizeRow);
            size=new Slider { Name="SizeSlider",Minimum=.75,Maximum=2.25,Value=pet.Settings.Scale,TickFrequency=.05,IsSnapToTickEnabled=true,SmallChange=.05,LargeChange=.25,Margin=new Thickness(0,0,0,4) };
            bool draggingSize=false;
            size.ValueChanged+=delegate { sizeLabel.Text=Math.Round(size.Value*100)+"%"; if(!refreshing) { pet.PreviewScale(size.Value);if(!draggingSize)pet.RequestPreferenceSave(); } }; behavior.Children.Add(size);
            size.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,new System.Windows.Controls.Primitives.DragStartedEventHandler(delegate{if(!draggingSize){pet.BeginPreferenceEdit();draggingSize=true;}}));
            size.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,new System.Windows.Controls.Primitives.DragCompletedEventHandler(delegate{if(draggingSize){draggingSize=false;pet.EndPreferenceEdit();}if(!refreshing)pet.CommitScale();}));
            size.LostKeyboardFocus+=delegate{if(!draggingSize)pet.FlushPreferenceSaves();};
            behavior.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeM")),11,Ui.Muted)); behaviorPage.Children.Add(GlassSurface.Card(behavior));

            var hello=new StackPanel{Name="GuideGreetingSection"};hello.Children.Add(Heading(()=>(TextResources.Get("settingsPage.settingsPage.heading.alternativeC"))));
            greetings=LocalizedUi.Toggle(()=>(TextResources.Get("settingsPage.settingsPage.toggle.alternativeG")),pet.Settings.GreetingEnabled,delegate(bool value){if(!refreshing){pet.Settings.GreetingEnabled=value;pet.ConfigureGreetings();}});greetings.Name="GreetingEnabled";hello.Children.Add(greetings);
            aiGreetings=LocalizedUi.Toggle(()=>(TextResources.Get("settingsPage.settingsPage.toggle.alternativeH")),pet.Settings.AiGreetingEnabled,delegate(bool value){if(!refreshing){pet.Settings.AiGreetingEnabled=value;pet.ConfigureGreetings();}});aiGreetings.Name="AiGreetingEnabled";hello.Children.Add(aiGreetings);
            hello.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeN")),12,Ui.Muted));
            hello.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeO")),12,Ui.Muted));
            var interval=new WrapPanel{Margin=new Thickness(0,12,0,10),VerticalAlignment=VerticalAlignment.Center};
            var every=LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeP")),12,Ui.Muted);every.VerticalAlignment=VerticalAlignment.Center;every.Margin=new Thickness(0,0,10,0);interval.Children.Add(every);
            greetingMin=Ui.Field(pet.Settings.GreetingMinMinutes.ToString());greetingMin.Name="GreetingMinMinutes";greetingMin.Width=68;greetingMin.MaxLength=4;interval.Children.Add(greetingMin);
            var between=Ui.Text("—",12,Ui.Muted);between.Margin=new Thickness(8,0,8,0);between.VerticalAlignment=VerticalAlignment.Center;interval.Children.Add(between);
            greetingMax=Ui.Field(pet.Settings.GreetingMaxMinutes.ToString());greetingMax.Name="GreetingMaxMinutes";greetingMax.Width=68;greetingMax.MaxLength=4;interval.Children.Add(greetingMax);
            var minutes=LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeQ")),12,Ui.Muted);minutes.Margin=new Thickness(8,0,0,0);minutes.VerticalAlignment=VerticalAlignment.Center;interval.Children.Add(minutes);hello.Children.Add(interval);
            greetingMin.LostFocus+=delegate {SaveGreetingInterval();};greetingMax.LostFocus+=delegate {SaveGreetingInterval();};
            var preview=LocalizedUi.Button(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeR")));preview.Name="PreviewGreeting";preview.HorizontalAlignment=HorizontalAlignment.Left;preview.Click+=delegate {pet.RestorePet();pet.PreviewGreeting();};hello.Children.Add(preview);
            hello.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeS")),11,Ui.Muted));greetingPage.Children.Add(GlassSurface.Card(hello));

            notice=Ui.Text("",11,Ui.Muted); notice.Margin=new Thickness(0,12,0,0);
            persona=new PersonaPanel(pet,true){Margin=new Thickness(0,4,0,4),Height=310};root.Children.Add(persona);companionPage=new CompanionConfigurationPanel(Ui.Scroll(root),Ui.Scroll(behaviorPage),Ui.Scroll(greetingPage),notice);workEditingProject=pet.CurrentEditingProject;content=new ContentControl{Name="SettingsContentHost",MaxWidth=Ui.PageContentWidth,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ClipToBounds=true};
            navigation=new ListBox{Name="SettingsNavigation",BorderThickness=new Thickness(0),Background=Brushes.Transparent,Margin=new Thickness(10,0,10,0),Padding=new Thickness(0)};
            ScrollViewer.SetHorizontalScrollBarVisibility(navigation,ScrollBarVisibility.Disabled);
            Func<string[]> navigationLabels=()=>new[]{TextResources.Get("settingsPage.settingsPage.navigationLabels"),TextResources.Get("settingsPage.settingsPage.navigationLabels.alternativeA"),TextResources.Get("settingsPage.settingsPage.navigationLabels.alternativeB"),TextResources.Get("settingsPage.settingsPage.navigationLabels.alternativeC"),TextResources.Get("settingsPage.settingsPage.navigationLabels.alternativeD"),TextResources.Get("im.title")};
            string[] navigationNames={"CompanionNavigation","AiNavigation","WorkNavigation","QuietNavigation","AboutNavigation","ImChannelsNavigation"};
            string[] navigationIcons={"people","keys","terminal","settings","about","keys"};
            for(int index=0;index<navigationLabels().Length;index++){
                int labelIndex=index;var item=new ListBoxItem{Name=navigationNames[index],Content=navigationLabels()[index],Tag=ShellIcons.Image(navigationIcons[index],Ui.PageInk,16),Style=Ui.NavigationItemStyle,Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,0,3),FontSize=13,Foreground=Ui.PageInk,ContentTemplate=NavigationTemplate(false)};
                LocalizedUi.Set(item,"Content",()=>navigationLabels()[labelIndex]);LocalizedUi.Set(item,"ToolTip",()=>navigationLabels()[labelIndex]);LiveTextBindings.Bind(item,"AutomationName",()=>navigationLabels()[labelIndex],value=>System.Windows.Automation.AutomationProperties.GetName((DependencyObject)value),(value,text)=>System.Windows.Automation.AutomationProperties.SetName((DependencyObject)value,(string)text));navigation.Items.Add(item);
            }
            var shell=new Grid();var sidebarColumn=new ColumnDefinition{Width=new GridLength(224)};shell.ColumnDefinitions.Add(sidebarColumn);shell.ColumnDefinitions.Add(new ColumnDefinition());
            var sidebar=new Grid();sidebar.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});sidebar.RowDefinitions.Add(new RowDefinition());sidebar.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var sidebarHeader=new StackPanel{Margin=new Thickness(14,24,14,22)};var settingsHeading=LocalizedUi.InlineText(()=>(TextResources.Get("settingsPage.settingsPage.inlineText.alternativeA")),17,Ui.PageInk);settingsHeading.FontWeight=FontWeights.Bold;settingsHeading.Margin=new Thickness(8,0,0,18);sidebarHeader.Children.Add(settingsHeading);
            var searchSurface=new Grid();var categorySearch=Ui.Field();categorySearch.Name="SettingsCategorySearch";categorySearch.FontSize=12;categorySearch.Height=categorySearch.MinHeight=34;categorySearch.Padding=new Thickness(30,0,12,0);categorySearch.Background=Ui.Brush("#ECEFF2");categorySearch.Foreground=Ui.PageInk;
            categorySearch.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><Border Background='{TemplateBinding Background}' CornerRadius='17'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/></Border></ControlTemplate>");
            LocalizedUi.Automation(categorySearch,()=>(TextResources.Get("settingsPage.settingsPage.automationAutomationPropertiesSetName")));searchSurface.Children.Add(categorySearch);var searchGlyph=UiIcons.Image("search",Ui.Muted);searchGlyph.Width=searchGlyph.Height=13;searchGlyph.HorizontalAlignment=HorizontalAlignment.Left;searchGlyph.VerticalAlignment=VerticalAlignment.Center;searchGlyph.Margin=new Thickness(11,0,0,0);searchSurface.Children.Add(searchGlyph);var searchHint=LocalizedUi.InlineText(()=>(TextResources.Get("settingsPage.settingsPage.inlineText.alternativeB")),12,Ui.Muted);searchHint.Margin=new Thickness(30,0,10,0);searchHint.IsHitTestVisible=false;searchSurface.Children.Add(searchHint);sidebarHeader.Children.Add(searchSurface);sidebar.Children.Add(sidebarHeader);
            var navScroll=Ui.Scroll(navigation);Grid.SetRow(navScroll,1);sidebar.Children.Add(navScroll);
            InitializeFeatureNavigation(categorySearch,searchHint,sidebarHeader);
            var sidebarFooter=new StackPanel{Margin=new Thickness(14,14,14,20)};
            var guideButton=LocalizedUi.Button(()=>(TextResources.Get("settingsPage.settingsPage.text.alternativeT")));guideButton.Name="OpenOnboarding";guideButton.HorizontalAlignment=HorizontalAlignment.Left;guideButton.Margin=new Thickness(0,0,0,10);guideButton.Click+=delegate {if(GuideRequested!=null)GuideRequested();};LocalizedUi.Automation(guideButton,()=>(TextResources.Get("settingsPage.settingsPage.automationAutomationPropertiesSetName.alternativeA")));LocalizedUi.Set(guideButton,"ToolTip",()=>(TextResources.Get("settingsPage.settingsPage.guideButtonToolTip")));sidebarFooter.Children.Add(guideButton);
            var versionLabel=LocalizedUi.InlineText(()=>(TextResources.Format("settingsPage.settingsPage.inlineText",ReleaseInfo.DisplayVersion)),11,Ui.Muted);sidebarFooter.Children.Add(versionLabel);Grid.SetRow(sidebarFooter,2);sidebar.Children.Add(sidebarFooter);
            shell.Children.Add(new Border{Child=sidebar,Background=Ui.Brush("#F8F9FB"),BorderBrush=Ui.Brush("#E9ECF0"),BorderThickness=new Thickness(0,0,1,0)});Grid.SetColumn(content,1);shell.Children.Add(content);Content=shell;
            bool? compactNavigation=null;Action updateNavigation=delegate{bool compact=ActualWidth>0&&ActualWidth<700;if(compactNavigation==compact)return;compactNavigation=compact;sidebarColumn.Width=new GridLength(compact?64:224);sidebarHeader.Visibility=compact?Visibility.Collapsed:Visibility.Visible;versionLabel.Visibility=compact?Visibility.Collapsed:Visibility.Visible;sidebarFooter.Margin=compact?new Thickness(10,14,10,20):new Thickness(14,14,14,20);navigation.Margin=compact?new Thickness(6,18,6,0):new Thickness(10,0,10,0);foreach(ListBoxItem item in navigation.Items){item.ContentTemplate=NavigationTemplate(compact);item.Padding=compact?new Thickness(10):new Thickness(10,8,10,8);if(compact)item.Visibility=Visibility.Visible;}if(compact)categorySearch.Text="";guideButton.Content=compact?(object)ShellIcons.Image("book",Ui.PageInk,16):TextResources.Get("settingsPage.settingsPage.guideButtonContentCompact");guideButton.Padding=compact?new Thickness(8):new Thickness(12,7,12,7);content.Margin=compact?new Thickness(0):new Thickness(16,18,16,8);};SizeChanged+=delegate{updateNavigation();};Loaded+=delegate{updateNavigation();};
            navigation.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs args) {if(args.Source!=navigation)return;int index=navigation.SelectedIndex;ActivateCategory(index);if(changingRoute)return;if(ActivateSearchCategory(index))return;featureAnchor=null;NotifyNavigation();};navigation.SelectedIndex=0;
            IsVisibleChanged+=delegate{if(IsVisible)RefreshActiveCategory();};
            RefreshState(); ShowNotice(pet.Notice ?? pet.Store.Warning);
        }
        // Only routes and explicit guide actions materialize secondary pages.
        // State/dirty queries and background refreshes must never call this method.
        private void EnsureCategory(int category,int section=-1)
        {
            if(disposed)throw new ObjectDisposedException("SettingsPage");
            if(category==1 && aiConfiguration==null){int initial=Math.Max(0,section);connections=new ConnectionSettingsPanel(pet,initial);aiConfiguration=new AiConfigurationPanel(connections,initial);aiConfiguration.SelectionChanged+=delegate{SecondaryChanged(1);};}
            else if(category==2 && work==null){work=new WorkSettingsPanel(pet);workEditingProject=pet.CurrentEditingProject;}
            else if(category==3 && quiet==null){quiet=new QuietPanel(pet,RefreshRestoredPreferences);quiet.SelectionChanged+=delegate{SecondaryChanged(3);};}
            else if(category==4 && about==null)about=new AboutPanel(pet);
            else if(category==5 && imChannels==null){imChannels=new ImChannelSettingsPanel(pet);imChannelsPage=Ui.Scroll(imChannels);}
        }
        private void ActivateCategory(int category)
        {
            if(category<0)return;EnsureCategory(category);
            switch(category){case 0:content.Content=companionPage;break;case 1:content.Content=aiConfiguration;break;case 2:content.Content=work;break;case 3:content.Content=quiet;break;case 4:content.Content=about;break;case 5:content.Content=imChannelsPage;break;}
            RefreshActiveCategory();
        }
        private void RefreshActiveCategory()
        {
            if(disposed)return;
            if(navigation.SelectedIndex==2 && work!=null)RefreshWorkState();
            else if(navigation.SelectedIndex==3 && quiet!=null)quiet.RefreshState();
        }
        private static DataTemplate NavigationTemplate(bool compact)
        {
            var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);row.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
            var icon=new FrameworkElementFactory(typeof(ContentPresenter));icon.SetBinding(ContentPresenter.ContentProperty,new System.Windows.Data.Binding("Tag"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1)});icon.SetValue(FrameworkElement.WidthProperty,16d);icon.SetValue(FrameworkElement.HeightProperty,16d);icon.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(icon);
            if(!compact){var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding());text.SetValue(FrameworkElement.MarginProperty,new Thickness(10,0,0,0));text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);text.SetValue(TextBlock.FontSizeProperty,13d);row.AppendChild(text);}
            return new DataTemplate{VisualTree=row};
        }
        private static TextBlock Heading(string text) { var label=Ui.Text(text,14); label.FontWeight=FontWeights.SemiBold; return label; }
        private void SaveGreetingInterval()
        {
            if(refreshing)return;int min,max;
            if(!Int32.TryParse(greetingMin.Text,out min) || !Int32.TryParse(greetingMax.Text,out max) || min<1 || max>1440 || max<min){ShowNotice(()=>(TextResources.Get("settingsPage.saveGreetingInterval.showNotice")));return;}
            if(min==pet.Settings.GreetingMinMinutes && max==pet.Settings.GreetingMaxMinutes)return;
            pet.Settings.GreetingMinMinutes=min;pet.Settings.GreetingMaxMinutes=max;pet.ConfigureGreetings();ShowNotice(()=>(TextResources.Get("settingsPage.saveGreetingInterval.showNotice.alternativeA")));
        }
        private async void ImportSkin(object sender,RoutedEventArgs args)
        {
            if(pet.NoticeOnlyCompanionGuide()){ShowNotice(pet.Notice);return;}
            if(HasUnsavedEdits){ShowNotice(()=>(TextResources.Get("settingsPage.importSkin.showNotice.alternativeB")));return;}
            if(pet.DialogueBusy || pet.DictationActive){ShowNotice(()=>(TextResources.Get("settingsPage.importSkin.showNotice.alternativeC")));return;}
            var dialog=LocalizedUi.With(new OpenFileDialog { Title=TextResources.Get("settingsPage.importSkin.title"),Filter=TextResources.Get("settingsPage.importSkin.filter"),CheckFileExists=true,Multiselect=false },"Title",()=>(TextResources.Get("settingsPage.importSkin.title")));
            if(dialog.ShowDialog(HostWindow)!=true) return;
            if(pet.NoticeOnlyCompanionGuide()){ShowNotice(pet.Notice);return;}
            try
            {
                var skin=await LocalCompanionImportWindow.RunAsync(HostWindow,cancel=>pet.Catalog.Import(dialog.FileName,cancel));
                refreshing=true; skins.ItemsSource=null; skins.ItemsSource=pet.Catalog.Skins; refreshing=false;
                skins.SelectedItem=skin; ShowNotice(()=>(TextResources.Format("settingsPage.importSkin.showNotice",skin.DisplayName)));
            }
            catch(OperationCanceledException){refreshing=false;ShowNotice(()=>TextResources.Get("companionImport.cancelled"));}
            catch(Exception e)
            {
                refreshing=false;
                if(!(e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is FileFormatException || e is InvalidOperationException || e is NotSupportedException || e is ArgumentException || e is System.Runtime.Serialization.SerializationException)) throw;
                ShowNotice(()=>(TextResources.Format("settingsPage.importSkin.showNotice.alternativeA",e.Message)));
            }
        }
        internal void RemoveCompanion(SkinDefinition skin)
        {
            if(pet.NoticeOnlyCompanionGuide()){ShowNotice(pet.Notice);return;}
            if(skin!=null&&skin.IsOfficial&&skin.BuiltinCharacterId=="e-bao"){ShowNotice(()=>(TextResources.Get("settingsPage.removeCompanion.showNotice.alternativeA")));return;}
            skins.IsDropDownOpen=false;
            if(HasUnsavedEdits || pet.DialogueBusy || pet.DictationActive){ShowNotice(()=>(TextResources.Get("settingsPage.removeCompanion.showNotice.alternativeB")));return;}
            if(pet.Catalog.Skins.Count<=1){ShowNotice(()=>(TextResources.Get("settingsPage.removeCompanion.showNotice.alternativeC")));return;}
            var dialog=new DeleteConversationDialog(skin.DisplayName,null,true){Owner=HostWindow};
            if(dialog.ShowDialog()!=true)return;
            if(pet.NoticeOnlyCompanionGuide()){ShowNotice(pet.Notice);return;}
            if(pet.Skin.PickerKey==skin.PickerKey) {
                SkinDefinition next=null;foreach(var candidate in pet.Catalog.Skins)if(candidate.PickerKey!=skin.PickerKey&&!candidate.IsPlaceholder){next=candidate;break;}
                if(next==null || !pet.SelectSkin(next)){ShowNotice(pet.Notice);return;}
            }
            pet.Catalog.Remove(skin);RefreshSkinChoices();ShowNotice(()=>(TextResources.Format("settingsPage.removeCompanion.showNotice",skin.DisplayName)));
        }
        internal void RefreshSkinChoices()
        {refreshing=true;skins.ItemsSource=null;skins.ItemsSource=pet.Catalog.Skins;skins.SelectedItem=pet.Skin;refreshing=false;RefreshState();}
        static ComboBoxItem FindParentItem(DependencyObject source)
        {while(source!=null){var item=source as ComboBoxItem;if(item!=null)return item;source=source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);}return null;}
        private void RefreshAvatar() { displayedSkin=pet.Skin;LocalizedUi.Set(name,"Text",()=>(pet.Skin.DisplayName)); avatar.Source=SkinCatalog.GetAvatar(pet.Skin);if(persona!=null)persona.RefreshPersona();if(connections!=null)connections.RefreshPersonaVoices(); }
        public void RefreshTextProviders(){if(connections!=null)connections.RefreshTextProviders();}
        void RefreshRestoredPreferences()
        {
            greetingMin.Text=pet.Settings.GreetingMinMinutes.ToString();greetingMax.Text=pet.Settings.GreetingMaxMinutes.ToString();if(connections!=null)connections.RefreshPreferenceVolume();if(work!=null)work.RefreshRestoredPreferences();RefreshState();
        }
        public void RefreshState()
        {
            if(holidayOutfits!=null)holidayOutfits.RefreshState();
            RefreshHolidayLayout();
            if(!ReferenceEquals(displayedSkin,pet.Skin))RefreshAvatar();
            if(restoreCompanions!=null)restoreCompanions.IsEnabled=pet.Catalog.HasRemoved;
            refreshing=true; follow.IsChecked=pet.Settings.FollowCursor; wander.IsChecked=pet.Settings.Wander;stumble.IsChecked=pet.Settings.StumbleEnabled;autoSleep.IsChecked=pet.Settings.AutoSleepEnabled;
            top.IsChecked=pet.Settings.Topmost; pause.IsChecked=pet.Paused;greetings.IsChecked=pet.Settings.GreetingEnabled;aiGreetings.IsChecked=pet.Settings.AiGreetingEnabled; size.Value=pet.Settings.Scale;
            sizeLabel.Text=Math.Round(pet.Settings.Scale*100)+"%"; skins.SelectedItem=pet.Skin; refreshing=false;
            RefreshAutoStart();if(IsVisible)RefreshActiveCategory();
        }
        private void HolidayWardrobeChanged(object sender,EventArgs args)
        {if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshHolidayLayout));return;}RefreshHolidayLayout();}
        private void RefreshHolidayLayout()
        {if(wardrobeColumn==null)return;bool visible=pet.Holidays.WardrobeVisible;wardrobeColumn.Width=visible?new GridLength(1,GridUnitType.Star):new GridLength(0);wardrobeGap.Width=new GridLength(visible?10:0);wardrobeHeading.Visibility=visible?Visibility.Visible:Visibility.Collapsed;}
        private void SetAutoStart(bool enabled)
        {
            if(refreshing)return;
            try{startup.SetEnabled(enabled);ShowNotice(()=>(enabled?TextResources.Get("settingsPage.setAutoStart.showNotice"):TextResources.Get("settingsPage.setAutoStart.showNotice.alternativeA")));}
            catch(Exception e){if(!(e is UnauthorizedAccessException || e is System.Security.SecurityException || e is IOException))throw;ShowNotice(()=>(TextResources.Get("settingsPage.setAutoStart.showNotice.alternativeB")));}
            RefreshAutoStart();
        }
        private void RefreshAutoStart()
        {
            refreshing=true;
            try{autoStart.IsChecked=startup.IsEnabled;}
            catch(Exception e){if(!(e is UnauthorizedAccessException || e is System.Security.SecurityException || e is IOException))throw;autoStart.IsEnabled=false;ShowNotice(()=>(TextResources.Get("settingsPage.refreshAutoStart.showNotice")));}
            finally{refreshing=false;}
        }
        public void ShowNotice(Func<string> text){if(notice!=null)LocalizedUi.Set(notice,"Text",text);}
        public void ShowNotice(string text) { if(notice!=null) notice.Text=text ?? ""; }
        public void SelectConnections() { SelectAiSection(0); }
        internal void SelectImageConfiguration(){SelectAiSection(4);}
        // Preserve existing menu, guide and automation routes while the visible navigation is grouped.
        public void SelectPage(int index)
        {
            if(index<0 || index>7)throw new ArgumentOutOfRangeException("index");
            if(index>=2 && index<=5){SelectAiSection(index-2);return;}
            if(index<=1){SelectCompanionSection(0);if(index==1)FocusGuideTarget("PersonaPrompt");return;}
            RestoreNavigation(index==6?4:3,-1,null);
        }
        public void SelectCompanionSection(int index){RestoreNavigation(0,index,null);}
        public void SelectAiSection(int index){RestoreNavigation(1,index,null);}
        public void SelectGeneralSection(int index){RestoreNavigation(3,index,null);}
        internal void SelectMcpSettings(bool edit=false){pet.ShowMcpPage(edit);}
        public bool CanChangeWorkSettingsProject(ProjectDefinition project)
        {
            if(work==null || SameProject(workEditingProject,project) || !work.HasUnsavedChanges)return true;
            string message=TextResources.Get("settingsPage.canChangeWorkSettingsProject.message");
            ShowNotice(message);var label=FindVisual(work.Content as DependencyObject,"WorkSettingsNotice") as TextBlock;if(label!=null)label.Text=message;return false;
        }
        private static bool SameProject(ProjectDefinition first,ProjectDefinition second)
        {return ReferenceEquals(first,second) || (first!=null && second!=null && first.Id==second.Id);}
        private void RefreshWorkState()
        {
            if(work==null || !CanChangeWorkSettingsProject(pet.CurrentEditingProject))return;
            if(work.HasUnsavedChanges)return;
            work.RefreshState();workEditingProject=pet.CurrentEditingProject;
        }
        public bool TrySelectWorkSettings()
        {
            if(!CanChangeWorkSettingsProject(pet.CurrentEditingProject))return false;
            RestoreNavigation(2,-1,null);return true;
        }
        public void SelectWorkSettings(){TrySelectWorkSettings();}
        public void SelectNavigation(int index)
        {
            if(index<0 || index>=navigation.Items.Count)throw new ArgumentOutOfRangeException("index");
            if(index==2){SelectWorkSettings();return;}RestoreNavigation(index,-1,null);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;pet.Holidays.Changed-=HolidayWardrobeChanged;holidayOutfits.Dispose();if(connections!=null)connections.DisposeImageGeneration();if(about!=null)about.Dispose();if(imChannels!=null)imChannels.Dispose();
        }
        public bool IsOnAboutPage {get{return navigation.SelectedIndex==4;}}
        public bool ValidateGuideNetwork(out string error){EnsureCategory(4);return about.ValidateNetwork(out error);}
        public bool SaveGuideNetwork(){EnsureCategory(4);return about.SaveNetwork();}
        public bool SaveAiConfiguration(){EnsureCategory(1);return connections.Save();}
        public bool SaveGuideAiSection(int index){EnsureCategory(1,index);return connections.SaveGuideSection(index);}
        internal int GuideSpeechTestRevision {get{return connections==null?0:connections.SpeechTestRevision;}}
        internal bool GuideSpeechTestSucceeded {get{return connections!=null && connections.SpeechTestSucceeded;}}
        public bool ValidateGuideAiSection(int index,out string error){EnsureCategory(1,index);return connections.ValidateGuideSection(index,out error);}
        public FrameworkElement FindControl(string controlName)
        {
            UpdateLayout();var match=FindVisual(this,controlName);return match??(HostWindow==null?null:FindVisual(HostWindow,controlName));
        }
        static FrameworkElement FindVisual(DependencyObject parent,string controlName)
        {
            var element=parent as FrameworkElement;if(element!=null && element.Name==controlName)return element;
            for(int index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
            {var match=FindVisual(VisualTreeHelper.GetChild(parent,index),controlName);if(match!=null)return match;}
            return null;
        }
        public FrameworkElement FocusGuideTarget(string controlName)
        {
            if(navigation.SelectedIndex==3)quiet.ShowTarget(controlName);
            if(navigation.SelectedIndex==1){EnsureCategory(1);connections.ExpandConnectionFor(controlName);}
            var target=FindControl(controlName);if(target==null)return null;
            target.BringIntoView();UpdateLayout();if(target.Focusable && target.IsEnabled)target.Focus();return target;
        }
        public FrameworkElement ShowGuideTarget(string controlName){return FocusGuideTarget(controlName);}
        private void OpenGuide(string relative)
        {
            try{string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,relative);if(!File.Exists(path) && !Directory.Exists(path))path=Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)),relative);if(!File.Exists(path) && !Directory.Exists(path))throw new InvalidOperationException();Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
            catch(Exception e){if(!(e is IOException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception))throw;ShowNotice(()=>(TextResources.Get("settingsPage.openGuide.showNotice")));}
        }
private static TextBlock Heading(System.Func<string> text){return LocalizedUi.With(Heading(text()),"Text",text);}    }
}
