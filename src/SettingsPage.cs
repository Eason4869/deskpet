using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Dafeiyu
{
    public sealed class SettingsPage : UserControl, IDisposable
    {
        private readonly PetWindow pet;
        private readonly TextBlock name,notice,sizeLabel;
        private readonly Image avatar;
        private readonly ComboBox skins;
        private readonly Button restoreCompanions;
        private readonly CheckBox follow,wander,top,pause,autoStart,greetings,aiGreetings,stumble,autoSleep;
        private readonly TextBox greetingMin,greetingMax;
        private readonly StartupRegistration startup=new StartupRegistration(Process.GetCurrentProcess().MainModule.FileName);
        private readonly Slider size;
        private readonly ListBox navigation;
        private readonly ContentControl content;
        private readonly PersonaPanel persona;
        private readonly ConnectionSettingsPanel connections;
        private readonly AiConfigurationPanel aiConfiguration;
        private readonly CompanionConfigurationPanel companionPage;
        private readonly AboutPanel about;
        private readonly QuietPanel quiet;
        private readonly WorkSettingsPanel work;
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
        public bool HasUnsavedEdits {get{return (work!=null && work.HasUnsavedChanges) || persona.HasUnsavedChanges || connections.HasUnsavedChanges || about.HasUnsavedChanges || quiet.HasUnsavedChanges || greetingMin.Text!=pet.Settings.GreetingMinMinutes.ToString() || greetingMax.Text!=pet.Settings.GreetingMaxMinutes.ToString();}}
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
            var eyebrow=Ui.Text("当前伙伴",12,Ui.Muted); eyebrow.Margin=new Thickness(0,0,0,3); introduction.Children.Add(eyebrow);
            name=Ui.Text(pet.Skin.Name,24); name.FontWeight=FontWeights.SemiBold; introduction.Children.Add(name);
            introduction.Children.Add(Ui.Text("陪你写代码，也陪你发会儿呆。",12,Ui.Muted));
            Grid.SetColumn(introduction,1); header.Children.Add(introduction); root.Children.Add(header);

            var appearance=new StackPanel(); appearance.Children.Add(Heading("选择伙伴"));
            var appearanceRow=new Grid { Margin=new Thickness(0,8,0,6) };
            appearanceRow.ColumnDefinitions.Add(new ColumnDefinition()); appearanceRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            skins=new ComboBox { Name="SkinPicker",ItemTemplate=SkinCatalog.AvatarTemplate(RemoveCompanion,()=>pet.Catalog.Skins.Count>1),ItemsSource=pet.Catalog.Skins,SelectedItem=pet.Skin,MinHeight=36,Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,10,0),HorizontalContentAlignment=HorizontalAlignment.Stretch };
            skins.SelectionChanged+=delegate
            {
                if(refreshing || skins.SelectedItem==null) return;
                var selected=(SkinDefinition)skins.SelectedItem;
                if(selected==pet.Skin)return;
                if(HasUnsavedEdits){refreshing=true;skins.SelectedItem=pet.Skin;refreshing=false;ShowNotice("请先保存当前修改，再切换伙伴。");return;}
                if(pet.SelectSkin(selected)) RefreshAvatar(); else { RefreshState(); ShowNotice(pet.Notice); }
            };
            appearanceRow.Children.Add(skins);
            var import=Ui.Button("导入 / 更新角色包"); import.Name="ImportSkin"; import.Margin=new Thickness(0); import.Click+=ImportSkin;
            Grid.SetColumn(import,1); appearanceRow.Children.Add(import); appearance.Children.Add(appearanceRow);
            appearance.Children.Add(Ui.Text("支持 ZIP 角色包、PNG 与 pet.json。同 ID 更新素材，保留个人人格、音色与聊天。",11,Ui.Muted));
            restoreCompanions=Ui.Button("恢复已删除伙伴");restoreCompanions.Name="RestoreCompanions";restoreCompanions.HorizontalAlignment=HorizontalAlignment.Left;restoreCompanions.FontSize=11;restoreCompanions.MinHeight=26;restoreCompanions.Padding=new Thickness(0);restoreCompanions.Background=Brushes.Transparent;restoreCompanions.BorderThickness=new Thickness(0);restoreCompanions.Click+=delegate {if(HasUnsavedEdits || pet.DialogueBusy || pet.DictationActive){ShowNotice("请先保存修改并结束当前对话，再恢复伙伴。");return;}pet.Catalog.RestoreRemoved();var current=pet.Catalog.Find(pet.Skin.Id);pet.SelectSkin(current);RefreshSkinChoices();ShowNotice("已恢复已删除伙伴，聊天和配置保持原样。");};appearance.Children.Add(restoreCompanions);
            var guide=new Expander{Name="ImportGuide",Header="如何制作和导入桌宠？",Margin=new Thickness(0,12,0,0)};var tutorial=new StackPanel{Margin=new Thickness(0,8,0,0)};
            tutorial.Children.Add(Ui.Text("1. 打开模板文件夹，选择 v1 原版或 v2 逐帧动作模板。\n2. v1：8 列 × 11 行透明 PNG，可直接导入。\n3. v2：每个动作独立 PNG，8 列、单格 480 × 520；在 pet.json 填写帧数、FPS 和循环方式。视线为 4 列 × 4 行的 16 张原画，不做插值。\n4. 填写角色名称、人格与专属动作名称；保留整个文件夹结构。可直接导入 ZIP（根目录放 pet.json），或选择 pet.json。\n固定 ID 的完整角色包还支持头像、双套动作和音色推荐；同 ID 更新会保留个人配置。导入后会复制素材，原文件保持不变。",12,Ui.Muted));
            var guideButtons=new WrapPanel{Margin=new Thickness(0,10,0,0)};var templates=Ui.Button("下载制作模板");templates.Name="OpenTemplates";templates.Click+=delegate {Process.Start(new ProcessStartInfo(ReleaseInfo.RepositoryUrl+"/releases/tag/v"+ReleaseInfo.DisplayVersion){UseShellExecute=true});};guideButtons.Children.Add(templates);var fullGuide=Ui.Button("查看完整教程");fullGuide.Name="OpenImportGuide";fullGuide.Click+=delegate {OpenGuide(Path.Combine("docs","skin-format.md"));};guideButtons.Children.Add(fullGuide);tutorial.Children.Add(guideButtons);guide.Content=tutorial;appearance.Children.Add(guide);var appearanceCard=GlassSurface.Card(appearance);appearanceCard.Padding=new Thickness(12);appearanceCard.Margin=new Thickness(0,0,0,12);root.Children.Add(appearanceCard);

            var behavior=new StackPanel{Name="GuideBehaviorSection"}; behavior.Children.Add(Heading("陪伴方式"));
            follow=Ui.Toggle("视线跟随鼠标",pet.Settings.FollowCursor,delegate(bool value) { if(!refreshing) { pet.Settings.FollowCursor=value; pet.ApplySettings(); } }); follow.Name="FollowCursor"; behavior.Children.Add(follow);
            wander=Ui.Toggle("偶尔在桌面上散步",pet.Settings.Wander,delegate(bool value) { if(!refreshing) { pet.Settings.Wander=value; pet.ApplySettings(); } }); wander.Name="Wander"; behavior.Children.Add(wander);
            stumble=Ui.Toggle("拖动放下后偶尔踉跄",pet.Settings.StumbleEnabled,delegate(bool value) {if(!refreshing){pet.Settings.StumbleEnabled=value;pet.ApplySettings();}});stumble.Name="StumbleEnabled";behavior.Children.Add(stumble);
            behavior.Children.Add(Ui.Text("实际拖动后有 40% 概率，触发后冷却 10 秒；支持四位内置伙伴。",11,Ui.Muted));
            autoSleep=Ui.Toggle("长时间空闲时自动小憩",pet.Settings.AutoSleepEnabled,delegate(bool value){if(!refreshing){pet.Settings.AutoSleepEnabled=value;pet.ApplySettings();}});autoSleep.Name="AutoSleepEnabled";behavior.Children.Add(autoSleep);
            behavior.Children.Add(Ui.Text("系统空闲 5 分钟或 15 分钟未操作伙伴后，每分钟有 70% 概率小憩；点击互动可唤醒。",11,Ui.Muted));
            top=Ui.Toggle("保持在其他窗口上方",pet.Settings.Topmost,delegate(bool value) { if(!refreshing) { pet.Settings.Topmost=value; pet.ApplySettings(); } }); top.Name="Topmost"; behavior.Children.Add(top);
            autoStart=Ui.Toggle("开启自启",false,SetAutoStart);autoStart.Name="AutoStart";behavior.Children.Add(autoStart);
            behavior.Children.Add(Ui.Text("登录 Windows 时启动桌宠；关闭后立即撤销。",11,Ui.Muted));
            pause=Ui.Toggle("暂停动画",pet.Paused,delegate(bool value) { if(!refreshing) pet.SetPaused(value); }); pause.Name="Pause"; behavior.Children.Add(pause);
            var sizeRow=new DockPanel { Margin=new Thickness(0,10,0,8) };
            sizeLabel=Ui.Text("",12,Ui.Blue); DockPanel.SetDock(sizeLabel,Dock.Right); sizeRow.Children.Add(sizeLabel); sizeRow.Children.Add(Ui.Text("伙伴大小",12)); behavior.Children.Add(sizeRow);
            size=new Slider { Name="SizeSlider",Minimum=.75,Maximum=2.25,Value=pet.Settings.Scale,TickFrequency=.05,IsSnapToTickEnabled=true,SmallChange=.05,LargeChange=.25,Margin=new Thickness(0,0,0,4) };
            size.ValueChanged+=delegate { sizeLabel.Text=Math.Round(size.Value*100)+"%"; if(!refreshing) { pet.Settings.Scale=size.Value; pet.ApplySettings(); } }; behavior.Children.Add(size);
            behavior.Children.Add(Ui.Text("高清原图素材 · 适配 Windows 显示缩放",11,Ui.Muted)); behaviorPage.Children.Add(GlassSurface.Card(behavior));

            var hello=new StackPanel{Name="GuideGreetingSection"};hello.Children.Add(Heading("主动问候"));
            greetings=Ui.Toggle("偶尔主动打个招呼",pet.Settings.GreetingEnabled,delegate(bool value){if(!refreshing){pet.Settings.GreetingEnabled=value;pet.ConfigureGreetings();}});greetings.Name="GreetingEnabled";hello.Children.Add(greetings);
            aiGreetings=Ui.Toggle("使用 AI 生成主动问候",pet.Settings.AiGreetingEnabled,delegate(bool value){if(!refreshing){pet.Settings.AiGreetingEnabled=value;pet.ConfigureGreetings();}});aiGreetings.Name="AiGreetingEnabled";hello.Children.Add(aiGreetings);
            hello.Children.Add(Ui.Text("默认使用角色预设台词。开启 AI 后，使用当前文字模型和当前伙伴最近的聊天记录生成问候，可能消耗模型额度；失败时自动改用预设。",12,Ui.Muted));
            hello.Children.Add(Ui.Text("在伙伴头顶显示一句问候，12 秒后收起。聊天、语音、全屏和暂停时保持安静。",12,Ui.Muted));
            var interval=new WrapPanel{Margin=new Thickness(0,12,0,10),VerticalAlignment=VerticalAlignment.Center};
            var every=Ui.Text("随机间隔",12,Ui.Muted);every.VerticalAlignment=VerticalAlignment.Center;every.Margin=new Thickness(0,0,10,0);interval.Children.Add(every);
            greetingMin=Ui.Field(pet.Settings.GreetingMinMinutes.ToString());greetingMin.Name="GreetingMinMinutes";greetingMin.Width=68;greetingMin.MaxLength=4;interval.Children.Add(greetingMin);
            var between=Ui.Text("—",12,Ui.Muted);between.Margin=new Thickness(8,0,8,0);between.VerticalAlignment=VerticalAlignment.Center;interval.Children.Add(between);
            greetingMax=Ui.Field(pet.Settings.GreetingMaxMinutes.ToString());greetingMax.Name="GreetingMaxMinutes";greetingMax.Width=68;greetingMax.MaxLength=4;interval.Children.Add(greetingMax);
            var minutes=Ui.Text("分钟",12,Ui.Muted);minutes.Margin=new Thickness(8,0,0,0);minutes.VerticalAlignment=VerticalAlignment.Center;interval.Children.Add(minutes);hello.Children.Add(interval);
            greetingMin.LostFocus+=delegate {SaveGreetingInterval();};greetingMax.LostFocus+=delegate {SaveGreetingInterval();};
            var preview=Ui.Button("预览问候气泡");preview.Name="PreviewGreeting";preview.HorizontalAlignment=HorizontalAlignment.Left;preview.Click+=delegate {pet.RestorePet();pet.PreviewGreeting();};hello.Children.Add(preview);
            hello.Children.Add(Ui.Text("可设置 1–1440 分钟；移开焦点后保存并重新计时。问候不会加入聊天记录，也不会自动朗读。",11,Ui.Muted));greetingPage.Children.Add(GlassSurface.Card(hello));

            notice=Ui.Text("",11,Ui.Muted); notice.Margin=new Thickness(0,12,0,0);
            persona=new PersonaPanel(pet,true){Margin=new Thickness(0,4,0,4),Height=310};root.Children.Add(persona);companionPage=new CompanionConfigurationPanel(Ui.Scroll(root),Ui.Scroll(behaviorPage),Ui.Scroll(greetingPage),notice);connections=new ConnectionSettingsPanel(pet);aiConfiguration=new AiConfigurationPanel(connections);about=new AboutPanel(pet);quiet=new QuietPanel(pet);work=new WorkSettingsPanel(pet);workEditingProject=pet.CurrentEditingProject;content=new ContentControl{Name="SettingsContentHost",MaxWidth=Ui.PageContentWidth,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ClipToBounds=true};
            navigation=new ListBox{Name="SettingsNavigation",BorderThickness=new Thickness(0),Background=Brushes.Transparent,Margin=new Thickness(10,0,10,0),Padding=new Thickness(0)};
            ScrollViewer.SetHorizontalScrollBarVisibility(navigation,ScrollBarVisibility.Disabled);
            string[] navigationLabels={"伙伴","模型配置","工作设置","通用","关于"};
            string[] navigationNames={"CompanionNavigation","AiNavigation","WorkNavigation","QuietNavigation","AboutNavigation"};
            string[] navigationIcons={"people","keys","terminal","settings","about"};
            for(int index=0;index<navigationLabels.Length;index++){
                var item=new ListBoxItem{Name=navigationNames[index],Content=navigationLabels[index],Tag=ShellIcons.Image(navigationIcons[index],Ui.PageInk,16),Style=Ui.NavigationItemStyle,Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,0,3),FontSize=13,Foreground=Ui.PageInk,ContentTemplate=NavigationTemplate(false)};
                System.Windows.Automation.AutomationProperties.SetName(item,navigationLabels[index]);item.ToolTip=navigationLabels[index];navigation.Items.Add(item);
            }
            var shell=new Grid();var sidebarColumn=new ColumnDefinition{Width=new GridLength(224)};shell.ColumnDefinitions.Add(sidebarColumn);shell.ColumnDefinitions.Add(new ColumnDefinition());
            var sidebar=new Grid();sidebar.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});sidebar.RowDefinitions.Add(new RowDefinition());sidebar.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var sidebarHeader=new StackPanel{Margin=new Thickness(14,24,14,22)};var settingsHeading=Ui.InlineText("设置",17,Ui.PageInk);settingsHeading.FontWeight=FontWeights.Bold;settingsHeading.Margin=new Thickness(8,0,0,18);sidebarHeader.Children.Add(settingsHeading);
            var searchSurface=new Grid();var categorySearch=Ui.Field();categorySearch.Name="SettingsCategorySearch";categorySearch.FontSize=12;categorySearch.Height=categorySearch.MinHeight=34;categorySearch.Padding=new Thickness(30,0,12,0);categorySearch.Background=Ui.Brush("#ECEFF2");categorySearch.Foreground=Ui.PageInk;
            categorySearch.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><Border Background='{TemplateBinding Background}' CornerRadius='17'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/></Border></ControlTemplate>");
            System.Windows.Automation.AutomationProperties.SetName(categorySearch,"搜索设置分类");searchSurface.Children.Add(categorySearch);var searchGlyph=UiIcons.Image("search",Ui.Muted);searchGlyph.Width=searchGlyph.Height=13;searchGlyph.HorizontalAlignment=HorizontalAlignment.Left;searchGlyph.VerticalAlignment=VerticalAlignment.Center;searchGlyph.Margin=new Thickness(11,0,0,0);searchSurface.Children.Add(searchGlyph);var searchHint=Ui.InlineText("搜索设置",12,Ui.Muted);searchHint.Margin=new Thickness(30,0,10,0);searchHint.IsHitTestVisible=false;searchSurface.Children.Add(searchHint);sidebarHeader.Children.Add(searchSurface);sidebar.Children.Add(sidebarHeader);
            var navScroll=Ui.Scroll(navigation);Grid.SetRow(navScroll,1);sidebar.Children.Add(navScroll);
            categorySearch.TextChanged+=delegate{string query=categorySearch.Text.Trim();searchHint.Visibility=categorySearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;foreach(ListBoxItem item in navigation.Items)item.Visibility=query.Length==0||((string)item.Content).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0?Visibility.Visible:Visibility.Collapsed;};
            var sidebarFooter=new StackPanel{Margin=new Thickness(14,14,14,20)};
            var guideButton=Ui.Button("配置引导");guideButton.Name="OpenOnboarding";guideButton.HorizontalAlignment=HorizontalAlignment.Left;guideButton.Margin=new Thickness(0,0,0,10);guideButton.Click+=delegate {if(GuideRequested!=null)GuideRequested();};System.Windows.Automation.AutomationProperties.SetName(guideButton,"配置引导");guideButton.ToolTip="配置引导";sidebarFooter.Children.Add(guideButton);
            var versionLabel=Ui.InlineText("版本 "+ReleaseInfo.DisplayVersion,11,Ui.Muted);sidebarFooter.Children.Add(versionLabel);Grid.SetRow(sidebarFooter,2);sidebar.Children.Add(sidebarFooter);
            shell.Children.Add(new Border{Child=sidebar,Background=Ui.Brush("#F8F9FB"),BorderBrush=Ui.Brush("#E9ECF0"),BorderThickness=new Thickness(0,0,1,0)});Grid.SetColumn(content,1);shell.Children.Add(content);Content=shell;
            bool? compactNavigation=null;Action updateNavigation=delegate{bool compact=ActualWidth>0&&ActualWidth<700;if(compactNavigation==compact)return;compactNavigation=compact;sidebarColumn.Width=new GridLength(compact?64:224);sidebarHeader.Visibility=compact?Visibility.Collapsed:Visibility.Visible;versionLabel.Visibility=compact?Visibility.Collapsed:Visibility.Visible;sidebarFooter.Margin=compact?new Thickness(10,14,10,20):new Thickness(14,14,14,20);navigation.Margin=compact?new Thickness(6,18,6,0):new Thickness(10,0,10,0);foreach(ListBoxItem item in navigation.Items){item.ContentTemplate=NavigationTemplate(compact);item.Padding=compact?new Thickness(10):new Thickness(10,8,10,8);if(compact)item.Visibility=Visibility.Visible;}if(compact)categorySearch.Text="";guideButton.Content=compact?(object)ShellIcons.Image("book",Ui.PageInk,16):"配置引导";guideButton.Padding=compact?new Thickness(8):new Thickness(12,7,12,7);content.Margin=compact?new Thickness(0):new Thickness(16,18,16,8);};SizeChanged+=delegate{updateNavigation();};Loaded+=delegate{updateNavigation();};
            navigation.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs args) {if(args.Source!=navigation)return;int index=navigation.SelectedIndex;if(index==0)content.Content=companionPage;else if(index==1)content.Content=aiConfiguration;else if(index==2){RefreshWorkState();content.Content=work;}else if(index==3)content.Content=quiet;else if(index==4)content.Content=about;if(NavigationChanged!=null)NavigationChanged(index);};navigation.SelectedIndex=0;
            RefreshState(); ShowNotice(pet.Notice ?? pet.Store.Warning);
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
            if(!Int32.TryParse(greetingMin.Text,out min) || !Int32.TryParse(greetingMax.Text,out max) || min<1 || max>1440 || max<min){ShowNotice("问候间隔需为 1–1440 分钟，最长间隔不能小于最短间隔。");return;}
            if(min==pet.Settings.GreetingMinMinutes && max==pet.Settings.GreetingMaxMinutes)return;
            pet.Settings.GreetingMinMinutes=min;pet.Settings.GreetingMaxMinutes=max;pet.ConfigureGreetings();ShowNotice("问候间隔已保存，将重新等待下一次问候。");
        }
        private void ImportSkin(object sender,RoutedEventArgs args)
        {
            if(HasUnsavedEdits){ShowNotice("请先保存当前修改，再导入并切换伙伴。");return;}
            if(pet.DialogueBusy || pet.DictationActive){ShowNotice("请先停止对话、听写或结束 Live，再导入 / 更新角色包。");return;}
            var dialog=new OpenFileDialog { Title="导入 / 更新角色包",Filter="角色包或外观 (*.zip;*.png;*.json)|*.zip;*.png;*.json|角色资源包 (*.zip)|*.zip|透明 PNG 精灵图|*.png|外观说明 (*.json)|*.json",CheckFileExists=true,Multiselect=false };
            if(dialog.ShowDialog(HostWindow)!=true) return;
            try
            {
                var skin=pet.Catalog.Import(dialog.FileName);
                refreshing=true; skins.ItemsSource=null; skins.ItemsSource=pet.Catalog.Skins; refreshing=false;
                skins.SelectedItem=skin; ShowNotice("已载入「"+skin.Name+"」。同 ID 角色只显示一项，个人设置与聊天记录保留。");
            }
            catch(Exception e)
            {
                refreshing=false;
                if(!(e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is FileFormatException || e is InvalidOperationException || e is NotSupportedException || e is ArgumentException || e is System.Runtime.Serialization.SerializationException)) throw;
                ShowNotice("导入失败："+e.Message+" 请检查 ZIP 角色包或 pet.json 及其素材。");
            }
        }
        internal void RemoveCompanion(SkinDefinition skin)
        {
            skins.IsDropDownOpen=false;
            if(HasUnsavedEdits || pet.DialogueBusy || pet.DictationActive){ShowNotice("请先保存修改并结束当前对话，再删除伙伴。");return;}
            if(pet.Catalog.Skins.Count<=1){ShowNotice("至少保留一位伙伴。");return;}
            var dialog=new DeleteConversationDialog(skin.Name,null,true){Owner=HostWindow};
            if(dialog.ShowDialog()!=true)return;
            if(pet.Skin.Id==skin.Id) {
                SkinDefinition next=null;foreach(var candidate in pet.Catalog.Skins)if(candidate.Id!=skin.Id){next=candidate;break;}
                if(next==null || !pet.SelectSkin(next)){ShowNotice(pet.Notice);return;}
            }
            pet.Catalog.Remove(skin);RefreshSkinChoices();ShowNotice("已删除「"+skin.Name+"」。聊天与配置保留，可恢复或重新导入。");
        }
        void RefreshSkinChoices()
        {refreshing=true;skins.ItemsSource=null;skins.ItemsSource=pet.Catalog.Skins;skins.SelectedItem=pet.Catalog.Find(pet.Skin.Id);refreshing=false;RefreshState();}
        private void RefreshAvatar() { displayedSkin=pet.Skin;name.Text=pet.Skin.Name; avatar.Source=SkinCatalog.GetAvatar(pet.Skin);if(persona!=null)persona.RefreshPersona();if(connections!=null)connections.RefreshPersonaVoices(); }
        public void RefreshTextProviders(){connections.RefreshTextProviders();}
        public void RefreshState()
        {
            if(!ReferenceEquals(displayedSkin,pet.Skin))RefreshAvatar();
            if(restoreCompanions!=null)restoreCompanions.IsEnabled=pet.Catalog.HasRemoved;
            refreshing=true; follow.IsChecked=pet.Settings.FollowCursor; wander.IsChecked=pet.Settings.Wander;stumble.IsChecked=pet.Settings.StumbleEnabled;autoSleep.IsChecked=pet.Settings.AutoSleepEnabled;
            top.IsChecked=pet.Settings.Topmost; pause.IsChecked=pet.Paused;greetings.IsChecked=pet.Settings.GreetingEnabled;aiGreetings.IsChecked=pet.Settings.AiGreetingEnabled; size.Value=pet.Settings.Scale;
            sizeLabel.Text=Math.Round(pet.Settings.Scale*100)+"%"; skins.SelectedItem=pet.Skin; refreshing=false;
            RefreshAutoStart();if(quiet!=null)quiet.RefreshState();if(work!=null)RefreshWorkState();
        }
        private void SetAutoStart(bool enabled)
        {
            if(refreshing)return;
            try{startup.SetEnabled(enabled);ShowNotice(enabled?"已开启自启，登录 Windows 时启动。":"已关闭自启。");}
            catch(Exception e){if(!(e is UnauthorizedAccessException || e is System.Security.SecurityException || e is IOException))throw;ShowNotice("无法修改当前用户的自启设置，请检查 Windows 权限。");}
            RefreshAutoStart();
        }
        private void RefreshAutoStart()
        {
            refreshing=true;
            try{autoStart.IsChecked=startup.IsEnabled;}
            catch(Exception e){if(!(e is UnauthorizedAccessException || e is System.Security.SecurityException || e is IOException))throw;autoStart.IsEnabled=false;ShowNotice("无法读取当前用户的自启设置。");}
            finally{refreshing=false;}
        }
        public void ShowNotice(string text) { if(notice!=null) notice.Text=text ?? ""; }
        public void SelectConnections() { SelectAiSection(0); }
        internal void SelectImageConfiguration(){SelectAiSection(4);}
        // Preserve existing menu, guide and automation routes while the visible navigation is grouped.
        public void SelectPage(int index)
        {
            if(index<0 || index>7)throw new ArgumentOutOfRangeException("index");
            if(index>=2 && index<=5){SelectAiSection(index-2);return;}
            if(index<=1){SelectCompanionSection(0);if(index==1)FocusGuideTarget("PersonaPrompt");return;}
            navigation.SelectedIndex=index==6?4:3;
        }
        public void SelectCompanionSection(int index){companionPage.SelectSection(index);navigation.SelectedIndex=0;}
        public void SelectAiSection(int index){aiConfiguration.SelectSection(index);navigation.SelectedIndex=1;}
        public void SelectGeneralSection(int index){quiet.SelectSection(index);navigation.SelectedIndex=3;}
        internal void SelectMcpSettings(bool edit=false){pet.ShowMcpPage(edit);}
        public bool CanChangeWorkSettingsProject(ProjectDefinition project)
        {
            if(SameProject(workEditingProject,project) || !work.HasUnsavedChanges)return true;
            const string message="工作设置仍有未保存修改，请先保存当前编辑，再切换项目。当前草稿已保留。";
            ShowNotice(message);var label=FindVisual(work.Content as DependencyObject,"WorkSettingsNotice") as TextBlock;if(label!=null)label.Text=message;return false;
        }
        private static bool SameProject(ProjectDefinition first,ProjectDefinition second)
        {return ReferenceEquals(first,second) || (first!=null && second!=null && first.Id==second.Id);}
        private void RefreshWorkState()
        {
            if(!CanChangeWorkSettingsProject(pet.CurrentEditingProject))return;
            if(work.HasUnsavedChanges)return;
            work.RefreshState();workEditingProject=pet.CurrentEditingProject;
        }
        public bool TrySelectWorkSettings()
        {
            if(!CanChangeWorkSettingsProject(pet.CurrentEditingProject))return false;
            RefreshWorkState();navigation.SelectedIndex=2;return true;
        }
        public void SelectWorkSettings(){TrySelectWorkSettings();}
        public void SelectNavigation(int index)
        {
            if(index<0 || index>=navigation.Items.Count)throw new ArgumentOutOfRangeException("index");
            if(index==2){SelectWorkSettings();return;}navigation.SelectedIndex=index;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;connections.DisposeImageGeneration();about.Dispose();
        }
        public bool IsOnAboutPage {get{return navigation.SelectedIndex==4;}}
        public bool ValidateGuideNetwork(out string error){return about.ValidateNetwork(out error);}
        public bool SaveGuideNetwork(){return about.SaveNetwork();}
        public bool SaveAiConfiguration(){return connections.Save();}
        public bool SaveGuideAiSection(int index){return connections.SaveGuideSection(index);}
        internal int GuideSpeechTestRevision {get{return connections.SpeechTestRevision;}}
        internal bool GuideSpeechTestSucceeded {get{return connections.SpeechTestSucceeded;}}
        public bool ValidateGuideAiSection(int index,out string error){return connections.ValidateGuideSection(index,out error);}
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
            connections.ExpandConnectionFor(controlName);
            var target=FindControl(controlName);if(target==null)return null;
            target.BringIntoView();UpdateLayout();if(target.Focusable && target.IsEnabled)target.Focus();return target;
        }
        public FrameworkElement ShowGuideTarget(string controlName){return FocusGuideTarget(controlName);}
        private void OpenGuide(string relative)
        {
            try{string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,relative);if(!File.Exists(path) && !Directory.Exists(path))path=Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)),relative);if(!File.Exists(path) && !Directory.Exists(path))throw new InvalidOperationException();Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
            catch(Exception e){if(!(e is IOException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception))throw;ShowNotice("找不到教程。请完整解压桌宠 ZIP，保留 docs 文件夹。");}
        }
    }
}
