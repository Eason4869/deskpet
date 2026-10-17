using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu
{
    // The guide points at the live editor. It never owns a second copy of settings.
    public sealed class OnboardingWindow : Window
    {
        private readonly PetWindow pet;
        private readonly SettingsWindow settings;
        private readonly Image avatar;
        private readonly TextBlock introduction,title,instruction,status;
        private readonly StackPanel actions;
        private readonly Button next,back;
        private readonly Border[] chips=new Border[PetSettings.OnboardingLastStep+1];
        private Expander networkPreview;private bool networkWasExpanded;
        private bool PetLesson {get{return (Step>=3 && Step<=7) || Step==14;}}
        private FrameworkElement target;
        private AdornerLayer layer;
        private GuideAdorner highlight;
        private bool finished,navigating,positioning,ownerClosing;
        GuideFocusScope focusScope,petScope;bool gestureDone;
        readonly List<GuideFocusScope> lessonRestrictions=new List<GuideFocusScope>();
        readonly DispatcherTimer readiness=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};readonly List<GuideFocusScope> otherScopes=new List<GuideFocusScope>();
        public int Step {get;private set;}
        public string TargetName {get;private set;}
        public bool HighlightVisible {get{return highlight!=null && target!=null && target.IsVisible;}}

        public OnboardingWindow(PetWindow companion,SettingsWindow editor)
        {
            pet=companion;settings=editor;
            Step=pet.Settings.OnboardingState=="completed" || pet.Settings.OnboardingState=="skipped"?0:Math.Max(0,Math.Min(PetSettings.OnboardingLastStep,pet.Settings.OnboardingStep));
            if(pet.Settings.OnboardingState=="completed" || pet.Settings.OnboardingState=="skipped")pet.Settings.OnboardingState="deferred";
            Ui.Window(this,"配置引导 · 桌面伙伴",360,430);
            Owner=settings;ShowInTaskbar=false;WindowStyle=WindowStyle.ToolWindow;ResizeMode=ResizeMode.NoResize;
            MinWidth=280;MinHeight=240;WindowStartupLocation=WindowStartupLocation.Manual;
            var root=new Grid {Margin=new Thickness(20,16,20,18)};
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            var greeting=new DockPanel {Margin=new Thickness(0,0,0,15)};
            avatar=new Image {Source=SkinCatalog.GetAvatar(pet.Skin),Width=56,Height=56,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,56,56),11.2,11.2),Margin=new Thickness(0,0,13,0)};
            RenderOptions.SetBitmapScalingMode(avatar,BitmapScalingMode.HighQuality);DockPanel.SetDock(avatar,Dock.Left);greeting.Children.Add(avatar);
            var hello=new StackPanel {VerticalAlignment=VerticalAlignment.Center};introduction=Ui.Text("和"+pet.Skin.Name+"一起开始",17);introduction.FontWeight=FontWeights.SemiBold;hello.Children.Add(introduction);
            hello.Children.Add(Ui.Text("当前项可操作，其余区域暂时锁定。",11,Ui.Muted));greeting.Children.Add(hello);root.Children.Add(greeting);
            var progress=new UniformGrid {Rows=2,Columns=9,Margin=new Thickness(0,0,0,17)};

            for(int i=0;i<chips.Length;i++)
            {
                var label=Ui.Text((i+1).ToString(),10);label.HorizontalAlignment=HorizontalAlignment.Center;
                chips[i]=new Border {Child=label,CornerRadius=new CornerRadius(7),Padding=new Thickness(2,3,2,3),Margin=new Thickness(0,0,i%9==8?0:3,3)};progress.Children.Add(chips[i]);
            }
            Grid.SetRow(progress,1);root.Children.Add(progress);
            var body=new StackPanel();title=Ui.Text("",20);title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,9);body.Children.Add(title);
            instruction=Ui.Text("",12,Ui.Muted);instruction.Margin=new Thickness(0,0,0,12);body.Children.Add(instruction);
            actions=new StackPanel();body.Children.Add(actions);
            status=Ui.Text("",11,Ui.Blue);status.Name="GuideNotice";status.Margin=new Thickness(0,9,0,0);body.Children.Add(status);
            var returnTarget=Ui.Button("回到当前步骤");returnTarget.Name="GuideReturnTarget";returnTarget.FontSize=11;returnTarget.MinHeight=28;returnTarget.Padding=new Thickness(0,3,0,3);returnTarget.Background=Brushes.Transparent;returnTarget.BorderBrush=Brushes.Transparent;returnTarget.Foreground=Ui.Blue;returnTarget.HorizontalAlignment=HorizontalAlignment.Left;returnTarget.Click+=delegate {Navigate();};body.Children.Add(returnTarget);
            var scroll=Ui.Scroll(body);Grid.SetRow(scroll,2);root.Children.Add(scroll);
            var footer=new StackPanel {Margin=new Thickness(0,14,0,0)};
            var navigation=new Grid();navigation.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});navigation.ColumnDefinitions.Add(new ColumnDefinition());
            back=Ui.Button("上一步");back.Name="GuideBack";back.Click+=delegate {if(Step>0){Step--;Navigate();}};navigation.Children.Add(back);
            next=Ui.Button("下一步",true);next.Name="GuideNext";next.IsDefault=true;next.Margin=new Thickness(0);next.Click+=delegate {Advance();};Grid.SetColumn(next,1);navigation.Children.Add(next);footer.Children.Add(navigation);
            var exits=new Grid {Margin=new Thickness(0,9,0,0)};exits.ColumnDefinitions.Add(new ColumnDefinition());exits.ColumnDefinitions.Add(new ColumnDefinition());
            var skip=SmallButton("跳过引导","GuideSkip");skip.Click+=delegate {Finish("skipped");};exits.Children.Add(skip);
            var later=SmallButton("跳过这一步","GuideLater");later.ToolTip="跳过当前项，进入下一步；未保存的填写内容保留在设置页。";later.Click+=delegate {if(Step==PetSettings.OnboardingLastStep)Finish("completed");else{Step++;Navigate();}};Grid.SetColumn(later,1);exits.Children.Add(later);footer.Children.Add(exits);
            Grid.SetRow(footer,3);root.Children.Add(footer);Content=root;
            Loaded+=delegate {pet.SetGuideLock(true);foreach(Window window in Application.Current.Windows.Cast<Window>().ToArray())if(window!=this && window!=settings && window!=pet && !(window is QuickChatWindow) && window.Content is UIElement)otherScopes.Add(new GuideFocusScope((UIElement)window.Content));Navigate();readiness.Start();};
            readiness.Tick+=delegate{RefreshReadiness();};pet.GuideInteractionCompleted+=InteractionCompleted;
            settings.Closing+=OwnerClosing;settings.LocationChanged+=OwnerMoved;settings.SizeChanged+=OwnerSized;
            Closing+=ClosingGuide;Closed+=delegate {readiness.Stop();RestoreNetworkPreview();RestoreLessonRestrictions();pet.SetGuideLock(false);if(!ownerClosing && !settings.IsVisible && settings.IsLoaded)settings.Show();pet.GuideInteractionCompleted-=InteractionCompleted;pet.SetGuideInteraction("");if(petScope!=null)petScope.Dispose();if(focusScope!=null)focusScope.Dispose();foreach(var scope in otherScopes)scope.Dispose();RemoveHighlight();settings.Closing-=OwnerClosing;settings.LocationChanged-=OwnerMoved;settings.SizeChanged-=OwnerSized;};
        }

        private static Button SmallButton(string text,string name)
        {
            var button=Ui.Button(text);button.Name=name;button.FontSize=11;button.MinHeight=26;button.Padding=new Thickness(2,3,2,3);button.Margin=new Thickness(0);button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;button.Foreground=Ui.Muted;return button;
        }
        private void AddAction(string text,string name,Action action)
        {
            var button=Ui.Button(text);button.Name=name;button.FontSize=12;button.Margin=new Thickness(0,0,0,6);button.HorizontalAlignment=HorizontalAlignment.Left;button.Click+=delegate {action();};actions.Children.Add(button);
        }
        private void Advance()
        {
            RefreshReadiness();if(!next.IsEnabled)return;
            try
            {
                if(Step==1)
                {
                    var prompt=(TextBox)settings.FindControl("PersonaPrompt");if(String.IsNullOrWhiteSpace(prompt.Text))throw new InvalidOperationException("请填写人格，或恢复预设后继续。");
                    ((Button)settings.FindControl("SavePersona")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(prompt.Text!=pet.Settings.SystemPrompt)throw new InvalidOperationException("人格尚未保存，请查看设置提示。");
                }
                if(Step>=8 && Step<=11 && !settings.SaveGuideAiSection(Step-8))throw new InvalidOperationException("当前项尚未填完整。请查看设置页提示；暂时不配置可点“跳过这一步”。");
                if(Step==12)
                {
                    int min,max;if(!Int32.TryParse(((TextBox)settings.FindControl("GreetingMinMinutes")).Text,out min) || !Int32.TryParse(((TextBox)settings.FindControl("GreetingMaxMinutes")).Text,out max) || min<1 || max>1440 || max<min)throw new InvalidOperationException("问候间隔需为 1–1440 分钟，最长不能小于最短。");
                    pet.Settings.GreetingMinMinutes=min;pet.Settings.GreetingMaxMinutes=max;pet.ConfigureGreetings();
                }
                if(Step==13)
                {
                    string error;if(!pet.UpdateHotkey("quick",((TextBox)settings.FindControl("Hotkeyquick")).Text,out error))throw new InvalidOperationException(error);
                    }
                if(Step==15 && !((WorkSettingsPanel)settings.FindControl("WorkSettingsPanel")).Save())throw new InvalidOperationException("工作设置尚未保存，请查看设置页提示；也可跳过本项。");
                if(Step==16){if(!settings.SaveGuideNetwork())throw new InvalidOperationException("请检查网络地址，或跳过这一步。");Finish("completed");return;}
                Step++;Navigate();
            }
            catch(InvalidOperationException e){status.Text=e.Message;}
        }
        void InteractionCompleted(string action){gestureDone=true;RefreshReadiness();}
        void RefreshReadiness()
        {
            if(finished || navigating)return;string error="";bool ready=true;
            if(Step>=3 && Step<=7){ready=gestureDone;if(!ready)error="请完成上方实操，或跳过本项。";}
            else if(Step>=8 && Step<=11)ready=settings.ValidateGuideAiSection(Step-8,out error);
            else if(Step==1){var prompt=settings.FindControl("PersonaPrompt") as TextBox;ready=prompt!=null && !String.IsNullOrWhiteSpace(prompt.Text);if(!ready)error="请填写人格或恢复预设。";}
            else if(Step==12){int min,max;ready=Int32.TryParse(((TextBox)settings.FindControl("GreetingMinMinutes")).Text,out min) && Int32.TryParse(((TextBox)settings.FindControl("GreetingMaxMinutes")).Text,out max) && min>=1 && max<=1440 && min<=max;if(!ready)error="请输入有效的问候间隔。";}
            else if(Step==13){string value=((TextBox)settings.FindControl("Hotkeyquick")).Text;HotkeyCombination combination;ready=String.IsNullOrWhiteSpace(value) || HotkeyCombination.TryParse(value,out combination);if(!ready)error="请输入有效快捷键，或留空停用。";}
            if(Step==16)ready=settings.ValidateGuideNetwork(out error);
            if(!ready || !next.IsEnabled)status.Text=error;next.IsEnabled=ready;
        }
        private void Navigate()
        {
            if(finished || navigating)return;navigating=true;
            try
            {
                RestoreNetworkPreview();RestoreLessonRestrictions();if(focusScope!=null){focusScope.Dispose();focusScope=null;}if(petScope!=null){petScope.Dispose();petScope=null;}pet.SetGuideInteraction("");gestureDone=false;RemoveHighlight();
                if(PetLesson){Owner=pet;settings.Hide();}else{if(!settings.IsVisible)settings.Show();Owner=settings;}status.Text="";actions.Children.Clear();
                introduction.Text="和"+pet.Skin.Name+"一起开始";avatar.Source=SkinCatalog.GetAvatar(pet.Skin);
                for(int i=0;i<chips.Length;i++){chips[i].Background=i==Step?Ui.Blue:i<Step?Ui.Mint:Ui.Pale;((TextBlock)chips[i].Child).Foreground=i==Step?Brushes.White:Ui.Muted;}
                back.IsEnabled=Step>0;next.Content=Step==PetSettings.OnboardingLastStep?"完成引导":"下一步";
                string scopeName;
                if(Step==0)
                {
                    title.Text="1 · 选择桌面伙伴";instruction.Text="在高亮处选择喜欢的角色；保留当前角色也可以。各自的人格、声音与聊天记录独立保存。";
                    settings.SelectCompanionSection(0);TargetName=scopeName="SkinPicker";
                }
                else if(Step==1)
                {
                    title.Text="2 · 确认伙伴人格";instruction.Text="阅读或修改人格，也可恢复预设。填写内容不能为空；下一步会保存当前人格。";
                    settings.SelectCompanionSection(0);TargetName="PersonaPrompt";scopeName="GuidePersonaSection";
                }
                else if(Step==2)
                {
                    title.Text="3 · 调整陪伴方式";instruction.Text="调整大小、跟随、散步与置顶，也可开关拖放踉跄和空闲小憩。悬停时随机播放跳跃、打招呼或伙伴的具名招牌动作，一次移入只播放一次；移开后恢复视线跟随。引导期间暂停悬停随机动作和自动小憩。保留当前值也可以；自启只在你主动开启时生效。";
                    settings.SelectCompanionSection(1);TargetName="SizeSlider";scopeName="GuideBehaviorSection";
                    AddAction("试试互动","GuideTryInteraction",delegate {pet.PlayInteraction("petting");});
                }
                else if(Step>=3 && Step<=7)
                {
                    string[] gestures={"drag","menu","wheel","click","double-click"};string[] captions={"4 · 拖动桌面伙伴","5 · 打开右键菜单","6 · 使用中键轮盘","7 · 点击伙伴互动","8 · 双击打开快速对话"};
                    string[] tips={"在角色不透明的身体上按住左键，拖到另一处再松开。实际移动后才能继续。", "在角色身体上点击鼠标右键，查看菜单。松开右键后菜单会保持打开，单击菜单外空白处收起。当前仅演示菜单；完成引导后可使用其中所有功能。", "在角色身体上按鼠标中键，轮盘会在伙伴正上方展开（屏幕边缘自动调整），再选择一个技能或动作。选中并播放后才能继续。", "在角色头部或身体单击一次，等待摸头或轻戳动画出现。双击打开聊天不计入本步。", "在角色身体上快速双击左键，实际打开快速对话后才能继续。本步仅预览，不会发送消息或收音。正常使用时右上角可展开完整对话；完成服务配置后，第 15 步会说明发送、听写和排队操作。"};
                    title.Text=captions[Step-3];instruction.Text=tips[Step-3];TargetName="GuidePet";scopeName="";pet.SetGuideInteraction(gestures[Step-3]);
                }
                else if(Step==8)
                {
                    title.Text="9 · 模型配置";instruction.Text="在“模型配置”的文字页点击“新增”选厂商，填写 Key 后“保存并获取模型列表”。勾选需要的模型后点“添加所选模型”，选择当前模型，再点引导的“下一步”保存本页。\n\n列表不可用时，手填模型 ID 后点“添加 / 更新”。可搜索模型或填写中文名称；没有账号可跳过本项。\n\n底部“保存配置”在引导中暂时锁定。已保存的有效连接可自动刷新列表，仅查询模型信息，不发送对话或录音。";
                    settings.SelectAiSection(0);TargetName="AddTextProvider";scopeName="GuideTextProviderSection";
                }
                else if(Step==9)
                {
                    title.Text="10 · 配置语音输入";instruction.Text="点击“新增”选择识别厂商，核对自动填入的模型名；模型可编辑，填写该服务独立的 Key，按需设置代理。语音页不获取模型列表，也不复用文字 Key。点击“下一步”保存本页。\n\n麦克风用于听写：停止后识别并保留草稿，发送则识别后发送或排队；Esc 取消。本步不录音。";
                    settings.SelectAiSection(1);TargetName="AddAsrProvider";scopeName="GuideAsrProviderSection";
                }
                else if(Step==10)
                {
                    title.Text="11 · 选择回复声音";instruction.Text="可保留 Windows 系统朗读。使用云端 TTS 时，点击“新增”选择厂商，核对可编辑的预填模型，填写独立 Key，再选择或手填音色；各厂商分别保存声音。这里还可调整朗读开关与音量。\n\n点击“下一步”保存本页。引导中不试听；完成后可回到朗读页点击“测试朗读”，它只使用当前 TTS 或系统语音。";
                    settings.SelectAiSection(2);TargetName="UseCloudVoice";scopeName="GuideSpeechProviderSection";
                }
                else if(Step==11)
                {
                    title.Text="12 · 配置 Live 通话";instruction.Text="点击“新增”选择 Gemini，核对完整 WSS 地址和可编辑的预填模型，填写独立 Key，选择或手填 Live 音色。不获取模型列表，也不使用 TTS 服务。\n\n点击“下一步”只保存配置，不连接或收音。完成引导后，可在本页点击“测试 Live 通话”，或从对话框声波按钮开始；连接成功才开启麦克风，挂断返回。暂时不用可跳过。";
                    settings.SelectAiSection(3);TargetName="AddLiveProvider";scopeName="GuideLiveProviderSection";
                }
                else if(Step==12)
                {
                    title.Text="13 · 设置主动问候";instruction.Text="选择是否主动打招呼，并设置随机间隔。默认使用本地台词；开启 AI 问候后会使用当前模型和最近聊天，可能消耗额度。";
                    settings.SelectCompanionSection(2);TargetName="GreetingEnabled";scopeName="GuideGreetingSection";
                }
                else if(Step==13)
                {
                    title.Text="14 · 确认快速对话快捷键";instruction.Text="填写快捷键，留空可停用；下一步保存，冲突时保留原快捷键。\n\n找不到伙伴时：右键任务栏托盘图标，选择“显示 / 隐藏伙伴”；开启鼠标穿透后，也可从托盘取消“鼠标穿透”。此页还可设置免打扰、全屏隐藏、锁定位置及聊天记录保存。记录按伙伴在本机加密保存，完整对话可导出或清空。";
                    settings.SelectPage(7);TargetName="Hotkeyquick";scopeName="GuideShortcutquick";
                }
                else if(Step==14)
                {
                    title.Text="15 · 对话模式与附件";instruction.Text="快聊与完整对话共用会话，本步仅预览，不会发送或收音。\n\n快速对话始终为纯聊天；完整对话默认工作模式，可在其输入框左下角或按 Tab 切换对话模式，从下一次请求生效。权限在下一步“工作设置”中调整。\n\n粘贴、拖放或 + 可添加图片与文件；+ 也可附加文件夹路径或最近应用窗口的快照。目录附件只引用路径，不读取目录，也不改变权限；发送前可移除附件。图片识别需要模型支持。\n\nEnter 发送，Shift + Enter 换行；忙碌时排队，Ctrl + Enter 或队列“引导”在当前操作完成后接入，不打断正在运行的命令；发送菜单可选择“停止并立即发送”。停止或出错后，点“继续队列”恢复。\n\n麦克风用于听写：停止后保留草稿，发送箭头识别后发送；Esc 取消。回复支持 Markdown；思考/处理时间持续更新，正文出现后过程自动收起，可从回复的更多菜单查看。";
                    TargetName="GuideConversationPreview";scopeName="";pet.SetGuideInteraction("double-click");pet.ShowQuickChat();
                }
                else if(Step==15)
                {
                    title.Text="16 · 工作设置";instruction.Text="工作模式使用原生文件工具，可列目录、搜索、读取、创建与修改文件，创建文件夹并打开目录。需要所选模型支持工具调用。\n\n默认“完整访问”沿用当前 Windows 账户的文件权限，包括桌面等本机目录；不会提权。“仅工作目录”可限制访问范围。默认目录是相对路径的起点；计划模式只读，不写文件、不打开资源管理器。\n\n可设置持续目标、上下文预算和详细过程。默认开启修改前备份；完整访问的备份位于原文件旁的 .deskpet-agent-backups。停止任务会保留已完成的修改。\n\n点击“下一步”保存；保存不会执行任务，从下一次请求生效。多轮请求和附件可能消耗模型额度。";
                    settings.SelectWorkSettings();TargetName=scopeName="WorkSettingsPanel";
                }
                else
                {
                    title.Text="17 · 更新与网络加速";instruction.Text="关于页的“网络加速”平时默认收起，本步临时展开。三个选项互斥：不使用 GitHub 加速、使用 GitHub 加速、使用网络代理。\n\n先点击“测试连通性”，查看各地址的可用状态和耗时，再选择可用镜像。自定义地址也需先填写并测试；仅加速更新包下载，失败自动回退 GitHub 直连，版本与校验值仍从官方获取。代理默认 http://127.0.0.1:7890，可用于检查版本和下载更新。此处不影响 AI 服务的代理设置。\n\n通过测试后选中的镜像即时保存；代理地址填写后在移开焦点或完成本步时保存。“测试连通性”需手动点击，不会安装；本引导不会自动测试、更新或卸载。卸载入口在关于页固定底栏。\n\n完成仅表示走完引导，不表示跳过的服务已配置或已验证连接；之后可从设置左下角重新打开。";
                    settings.SelectPage(6);networkPreview=(Expander)settings.FindControl("UpdateNetwork");networkWasExpanded=networkPreview.IsExpanded;networkPreview.IsExpanded=true;TargetName=scopeName="UpdateNetwork";
                }
                settings.UpdateLayout();focusScope=new GuideFocusScope((UIElement)settings.Content,scopeName.Length==0?null:settings.FindControl(scopeName),scopeName.Length==0?null:settings.FindControl("ConnectionNotice"));
                if(Step>=8 && Step<=11)foreach(string name in new[]{"PreviewSpeech","StopPreviewSpeech","PreviewLive"}){var control=settings.FindControl(name);if(control!=null)lessonRestrictions.Add(new GuideFocusScope(control));}
                settings.UpdateLayout();target=Step==14?(FrameworkElement)Application.Current.Windows.OfType<QuickChatWindow>().First().Content:PetLesson?(FrameworkElement)pet.Content:settings.FocusGuideTarget(TargetName);if(!PetLesson || Step==14)petScope=new GuideFocusScope((UIElement)pet.Content);
                if(target!=null)
                {
                    target.IsVisibleChanged+=TargetVisibilityChanged;
                    layer=AdornerLayer.GetAdornerLayer(target);
                    if(layer!=null){highlight=new GuideAdorner(target);layer.Add(highlight);}
                }
                pet.Settings.OnboardingStep=Step;pet.SaveSettings();RefreshReadiness();
                UpdateLayout();PositionNearTarget();
                // A posted pass only updates geometry after BringIntoView scrolls.
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate {if(!finished){if(highlight!=null)highlight.InvalidateVisual();PositionNearTarget();}}));
            }
            finally{navigating=false;RefreshReadiness();}
        }
        private void RestoreNetworkPreview(){if(networkPreview!=null){networkPreview.IsExpanded=networkWasExpanded;networkPreview=null;}}
        private void RestoreLessonRestrictions(){foreach(var scope in lessonRestrictions)scope.Dispose();lessonRestrictions.Clear();}
        private void TargetVisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)
        {
            if(!navigating && target!=null && !target.IsVisible){RemoveHighlight();status.Text="你切换了设置页，可点下方按钮回到当前步骤。";}
        }
        private void RemoveHighlight()
        {
            if(target!=null)target.IsVisibleChanged-=TargetVisibilityChanged;
            if(layer!=null && highlight!=null)layer.Remove(highlight);target=null;layer=null;highlight=null;
        }
        private void OwnerClosing(object sender,CancelEventArgs args){ownerClosing=true;Close();}
        private void OwnerMoved(object sender,EventArgs args){PositionNearTarget();}
        private void OwnerSized(object sender,SizeChangedEventArgs args){if(highlight!=null)highlight.InvalidateVisual();PositionNearTarget();}
        private void PositionNearTarget()
        {
            if(!IsVisible || positioning)return;positioning=true;
            try
            {
                IntPtr mine=new WindowInteropHelper(this).Handle,owner=new WindowInteropHelper(Step==14 && target!=null?Window.GetWindow(target):settings).Handle;
                if(mine==IntPtr.Zero || owner==IntPtr.Zero)return;
                Rect area=DesktopInterop.WorkArea(owner),editor=DesktopInterop.Bounds(owner),bounds=DesktopInterop.Bounds(mine);
                var source=PresentationSource.FromVisual(this);Matrix toDevice=source==null?Matrix.Identity:source.CompositionTarget.TransformToDevice;
                Width=Math.Max(280,Math.Min(360,(area.Width-24)/toDevice.M11));Height=Math.Max(240,Math.Min(430,(area.Height-24)/toDevice.M22));
                double width=Width*toDevice.M11,height=Height*toDevice.M22;
                Rect focus=new Rect(editor.Left+editor.Width*.5,editor.Top+editor.Height*.5,1,1);
                if(target!=null && target.IsVisible)
                {
                    Point a=target.PointToScreen(new Point()),b=target.PointToScreen(new Point(target.ActualWidth,target.ActualHeight));focus=new Rect(a,b);
                }
                Point[] candidates={new Point(editor.Right+12,focus.Top),new Point(editor.Left-width-12,focus.Top),new Point(focus.Right+16,focus.Top),new Point(focus.Left-width-16,focus.Top),new Point(focus.Left,focus.Bottom+16),new Point(focus.Left,focus.Top-height-16),area.TopLeft,new Point(area.Right-width,area.Top),new Point(area.Left,area.Bottom-height),new Point(area.Right-width,area.Bottom-height)};
                double best=Double.MaxValue;Point chosen=bounds.TopLeft;
                foreach(Point candidate in candidates)
                {
                    Point clamped=PetMath.ClampPosition(candidate.X,candidate.Y,width,height,area);Rect rectangle=new Rect(clamped,new Size(width,height)),overlap=Rect.Intersect(rectangle,focus),editorOverlap=Rect.Intersect(rectangle,editor);
                    double score=(overlap.IsEmpty?0:overlap.Width*overlap.Height)*10000+(editorOverlap.IsEmpty?0:editorOverlap.Width*editorOverlap.Height)+Math.Abs(clamped.Y-focus.Y)*.01;
                    if(score<best){best=score;chosen=clamped;}
                }
                DesktopInterop.Move(mine,chosen);
            }
            finally{positioning=false;}
        }
        private void Finish(string state)
        {
            if(finished)return;pet.Settings.OnboardingState=state;pet.Settings.OnboardingStep=Step;pet.SaveSettings();finished=true;Close();
        }
        private void ClosingGuide(object sender,CancelEventArgs args)
        {
            if(!finished){pet.Settings.OnboardingState="skipped";pet.Settings.OnboardingStep=Step;pet.SaveSettings();finished=true;}
        }
        private sealed class GuideAdorner : Adorner
        {
            public GuideAdorner(UIElement element):base(element){IsHitTestVisible=false;}
            protected override void OnRender(DrawingContext drawing)
            {
                Rect rect=new Rect(AdornedElement.RenderSize);rect.Inflate(4,4);
                drawing.DrawRoundedRectangle(null,new Pen(Ui.Blue,2),rect,10,10);
            }
        }
    }
}
