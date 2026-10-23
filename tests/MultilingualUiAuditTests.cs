using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    // One locale per process: resource-backed static fields deliberately retain startup language.
    public static class MultilingualUiAuditTests
    {
        static Application app;
        static PetWindow pet;
        static string output;
        static int checks,captures;
        static readonly List<string> manifest=new List<string>();
        static readonly Regex Missing=new Regex(@"^\[[a-zA-Z][a-zA-Z0-9]*\.[a-zA-Z0-9_.]+\]$");
        static void Check(bool value,string label){if(!value)throw new Exception("Multilingual WPF audit: "+label);checks++;}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject
        {
            if(root==null)yield break;
            if(root is T)yield return(T)root;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;
        }
        static Button Button(DependencyObject root,string name){return All<Button>(root).Single(b=>b.Name==name);}
        static void Click(DependencyObject root,string name){Button(root,name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));}
        static async Task Settle(){await app.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(30);}
        static string Plain(string text){return(text??"").Replace("\r"," ").Replace("\n"," ").Replace("\t"," ");}
        static bool VisibleIn(FrameworkElement element,FrameworkElement root,out Rect bounds,bool templateRendered=false)
        {
            bounds=Rect.Empty;
            if((!templateRendered&&!element.IsVisible)||element.Visibility!=Visibility.Visible||element.ActualWidth<=0||element.ActualHeight<=0)return false;
            if(ReferenceEquals(element,root)){bounds=new Rect(element.RenderSize);return true;}
            try{bounds=element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));}
            catch(InvalidOperationException){return false;}
            if(bounds.IsEmpty||!bounds.IntersectsWith(new Rect(0,0,root.ActualWidth,root.ActualHeight)))return false;
            // Respect scroll viewports and parent clips: offscreen labels are recorded at the other scroll position.
            for(DependencyObject parent=VisualTreeHelper.GetParent(element);parent!=null&&parent!=root;parent=VisualTreeHelper.GetParent(parent))
            {
                var framework=parent as FrameworkElement;
                if(templateRendered&&framework!=null&&framework.Visibility!=Visibility.Visible)return false;
                if(framework!=null&&(framework.ClipToBounds||parent is ScrollContentPresenter))
                {
                    var clip=framework.TransformToAncestor(root).TransformBounds(new Rect(framework.RenderSize));
                    if(!clip.IntersectsWith(bounds))return false;
                }
            }
            return true;
        }
        static void Capture(FrameworkElement root,string name,bool templateRendered=false)
        {
            File.AppendAllText(Path.Combine(output,"trace.log"),DateTime.UtcNow.ToString("O")+" capture begin "+name+Environment.NewLine);
            root.UpdateLayout();
            Check((templateRendered?root.Visibility==Visibility.Visible:root.IsVisible)&&root.IsArrangeValid&&root.IsMeasureValid&&root.ActualWidth>40&&root.ActualHeight>30,"arranged capture "+name+"; visibility="+root.Visibility+" arrange="+root.IsArrangeValid+" measure="+root.IsMeasureValid+" actual="+root.ActualWidth+"x"+root.ActualHeight);
            int width=(int)Math.Ceiling(root.ActualWidth),height=(int)Math.Ceiling(root.ActualHeight);
            Check(width<=4096&&height<=4096,"bounded capture "+name);
            // Capture this application's control only. No desktop/screen capture API is used.
            var bitmap=UiControlSnapshot.Capture(root,templateRendered);
            if(templateRendered)
            {
                var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);int painted=0,varied=0;
                for(int i=0;i<pixels.Length;i+=4){if(pixels[i+3]!=0)painted++;if(pixels[i]!=pixels[0]||pixels[i+1]!=pixels[1]||pixels[i+2]!=pixels[2]||pixels[i+3]!=pixels[3])varied++;}
                Check(painted>width*height/4&&varied>100,"template surface has painted and varied pixels "+name);
            }
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
            File.AppendAllText(Path.Combine(output,"trace.log"),DateTime.UtcNow.ToString("O")+" bitmap saved "+name+Environment.NewLine);
            var lines=new List<string>{"type\tname\tbounds\thasHan\ttext"};
            var window=root as Window;if(window!=null)lines.Add("WindowTitle\t\t\t"+Regex.IsMatch(window.Title??"",@"[\u3400-\u9fff]")+"\t"+Plain(window.Title));
            foreach(var element in All<FrameworkElement>(root))
            {
                Rect bounds;if(!VisibleIn(element,root,out bounds,templateRendered))continue;
                string text=null;
                var block=element as TextBlock;if(block!=null)text=block.Text;
                var content=element as ContentControl;if(content!=null&&content.Content is string)text=(string)content.Content;
                var header=element as HeaderedItemsControl;if(header!=null&&header.Header is string)text=(string)header.Header;
                if(!String.IsNullOrWhiteSpace(text))
                {
                    Check(!Missing.IsMatch(text.Trim()),"no missing resource marker "+name+" "+text);
                    lines.Add(element.GetType().Name+"\t"+element.Name+"\t"+bounds.ToString(CultureInfo.InvariantCulture)+"\t"+Regex.IsMatch(text,@"[\u3400-\u9fff]")+"\t"+Plain(text));
                }
                if(element.ToolTip is string)lines.Add("ToolTip\t"+element.Name+"\t\t\t"+Plain((string)element.ToolTip));
                string accessible=System.Windows.Automation.AutomationProperties.GetName(element);
                if(!String.IsNullOrWhiteSpace(accessible))lines.Add("AutomationName\t"+element.Name+"\t\t\t"+Plain(accessible));
            }
            Check(lines.Count>1,"visible text exists "+name);
            File.WriteAllLines(Path.Combine(output,name+".txt"),lines,Encoding.UTF8);
            manifest.Add(name+"\t"+width+"x"+height+"\t"+(lines.Count-1)+"\t"+(templateRendered?"template-rendered":"actual-window"));captures++;
        }
        static async Task ScrollCaptures(FrameworkElement host,string name)
        {
            await Settle();
            var scrolls=All<ScrollViewer>(host).Where(v=>v.IsVisible).ToArray();
            foreach(var scroll in scrolls)scroll.ScrollToTop();await Settle();
            var shortcuts=All<ShortcutSettingsPanel>(host).SingleOrDefault(panel=>panel.IsVisible);
            var seenShortcutTitles=new HashSet<string>();
            Action observe=delegate
            {
                if(shortcuts==null)return;
                foreach(var definition in ShortcutCatalog.All)
                {
                    var row=All<Grid>(shortcuts).Single(grid=>grid.Name=="GuideShortcut"+definition.Id);
                    var title=row.Children.OfType<TextBlock>().Single();
                    Check(title.Text==definition.Title,"shortcut title follows selected catalog "+definition.Id);
                    if(definition.Id=="quick")Check(title.Text==TextResources.Get("shortcutDefinitions.all.allShortcutDefinition"),"shortcut startup title resolves active catalog independently");
                    Rect bounds;if(!VisibleIn(title,host,out bounds)||!new Rect(host.RenderSize).Contains(bounds))continue;
                    bool fullyVisible=true;
                    for(DependencyObject parent=VisualTreeHelper.GetParent(title);parent!=null&&parent!=host;parent=VisualTreeHelper.GetParent(parent))
                    {
                        var framework=parent as FrameworkElement;
                        if(framework!=null&&(framework.ClipToBounds||parent is ScrollContentPresenter))
                        {
                            var clip=framework.TransformToAncestor(host).TransformBounds(new Rect(framework.RenderSize));
                            if(!clip.Contains(bounds)){fullyVisible=false;break;}
                        }
                    }
                    if(fullyVisible)seenShortcutTitles.Add(definition.Id);
                }
            };
            Capture(host,name+"-top");observe();
            // Tile each outer scrollable branch independently. Nested text-box scroll hosts
            // remain at the top; mixing their offsets would not describe the settings page.
            var branches=scrolls.Where(scroll=>scroll.ScrollableHeight>1&&!Ancestors(scroll,host).OfType<ScrollViewer>().Any(parent=>parent.IsVisible&&parent.ScrollableHeight>1)).ToArray();
            for(int branch=0;branch<branches.Length;branch++)
            {
                var scroll=branches[branch];int page=0;
                while(scroll.VerticalOffset<scroll.ScrollableHeight-.5)
                {
                    double previous=scroll.VerticalOffset,next=Math.Min(scroll.ScrollableHeight,previous+Math.Max(1,scroll.ViewportHeight*.75));
                    scroll.ScrollToVerticalOffset(next);await Settle();
                    Check(scroll.VerticalOffset>previous+.01,"scroll viewport advances "+name);
                    Check(page<256,"bounded scroll tiling "+name);
                    Capture(host,name+"-scroll-"+branch+"-"+(++page).ToString("D2",CultureInfo.InvariantCulture));observe();
                }
                scroll.ScrollToTop();await Settle();
            }
            foreach(var scroll in scrolls)scroll.ScrollToBottom();await Settle();Capture(host,name+"-bottom");observe();
            if(shortcuts!=null)Check(seenShortcutTitles.SetEquals(ShortcutCatalog.All.Select(definition=>definition.Id)),"all shortcut operation titles fully captured: "+name+"; missing="+String.Join(",",ShortcutCatalog.All.Where(definition=>!seenShortcutTitles.Contains(definition.Id)).Select(definition=>definition.Id)));
        }
        static IEnumerable<DependencyObject> Ancestors(DependencyObject element,DependencyObject root)
        {for(var parent=VisualTreeHelper.GetParent(element);parent!=null&&parent!=root;parent=VisualTreeHelper.GetParent(parent))yield return parent;}
        static async Task Dialog(Window dialog,Window owner,string name)
        {
            dialog.Owner=owner;dialog.Show();try{await Settle();await ScrollCaptures(dialog,name);}finally{dialog.Close();}await Settle();
        }
        static async Task Menu(ContextMenu menu,FrameworkElement target,string name)
        {
            Check(menu!=null,"production menu exists "+name);menu.IsOpen=false;await Settle();
            // Refresh exactly the production display-state handlers, with no native popup open.
            // The pet's deferred ForegroundMenu handler observes IsOpen=false and does nothing.
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent,menu));await Settle();
            // ContextMenu cannot be detached from the private Popup created on first open.
            // Rehost its actual MenuItem/Separator controls in a never-opened presentation menu
            // with the identical template/resources; headers, icons and states are not copied.
            var surface=ChatUi.Menu();surface.Template=menu.Template;surface.Resources=menu.Resources;surface.MinWidth=menu.MinWidth;
            surface.Foreground=menu.Foreground;surface.Background=menu.Background;surface.FontFamily=menu.FontFamily;surface.FontSize=menu.FontSize;surface.Padding=menu.Padding;surface.FlowDirection=menu.FlowDirection;
            var actualItems=menu.Items.Cast<object>().ToArray();menu.Items.Clear();foreach(var item in actualItems)surface.Items.Add(item);
            try{LayoutTemplateSurface(surface,name);Capture(surface,name,true);CheckMenuItems(surface.Items,surface,name);await Submenus(surface.Items,name);Check(!menu.IsOpen&&!surface.IsOpen,"template rendering did not open native menu "+name);}
            finally{surface.Items.Clear();foreach(var item in actualItems)menu.Items.Add(item);}
        }
        static void LayoutTemplateSurface(FrameworkElement surface,string name)
        {
            surface.Visibility=Visibility.Visible;surface.ApplyTemplate();surface.Measure(new Size(1024,1024));
            Check(surface.DesiredSize.Width>40&&surface.DesiredSize.Height>30,"template desired geometry "+name);
            surface.Arrange(new Rect(new Point(),surface.DesiredSize));surface.UpdateLayout();
        }
        static void CheckMenuItems(ItemCollection items,FrameworkElement surface,string name)
        {
            int arranged=0;
            foreach(var item in items.OfType<MenuItem>().Where(item=>item.Visibility==Visibility.Visible))
            {
                Rect bounds;Check(VisibleIn(item,surface,out bounds,true)&&item.IsArrangeValid,"real menu item arranged "+name+" / "+item.Header);arranged++;
            }
            Check(arranged>0,"template has real arranged menu items "+name);
        }
        static async Task Submenus(ItemCollection items,string name)
        {
            int index=0;
            foreach(var item in items.OfType<MenuItem>().Where(item=>item.HasItems&&item.Visibility==Visibility.Visible))
            {
                item.ApplyTemplate();var popup=item.Template.FindName("PART_Popup",item) as System.Windows.Controls.Primitives.Popup;
                Check(popup!=null&&popup.Child is FrameworkElement,"submenu exposes actual template surface "+name+" / "+item.Header);
                Check(!popup.IsOpen,"submenu remains native-closed "+name);var surface=(FrameworkElement)popup.Child;
                string capture=name+"-submenu-"+(index++);LayoutTemplateSurface(surface,capture);await Settle();
                Capture(surface,capture,true);CheckMenuItems(item.Items,surface,capture);await Submenus(item.Items,capture);
            }
        }
        static void Inside(FrameworkElement root,string name)
        {
            var element=All<FrameworkElement>(root).Single(e=>e.Name==name);Rect bounds;
            Check(VisibleIn(element,root,out bounds)&&bounds.Left>=-1&&bounds.Top>=-1&&bounds.Right<=root.ActualWidth+1&&bounds.Bottom<=root.ActualHeight+1,"control in capture: "+name);
        }
        static async Task Run(string language)
        {
            Directory.CreateDirectory(output);string data=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
            var cfg=new PetSettings{UiLanguage=language,AutoUpdate=false,GreetingEnabled=false,AiGreetingEnabled=false,ReadReplies=false,Wander=false,FollowCursor=false,HideOnFullscreen=false,OnboardingState="completed",ShowPredictedMessages=false,AgentWorkspace=Path.Combine(data,"workspace")};
            Directory.CreateDirectory(cfg.AgentWorkspace);foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
            var store=new SettingsStore(Path.Combine(data,"settings.json"));store.Save(cfg);cfg=store.Load();TextResources.ConfigureStartupLanguage(cfg.UiLanguage);
            Check(TextResources.CurrentLanguage==language,"saved startup locale");
            pet=new PetWindow(null,cfg,store,null);pet.ShowChat();await Settle();var chat=app.Windows.OfType<ChatWindow>().Single(w=>w.IsVisible);chat.Width=1000;chat.Height=780;
            var chatSession=(ConversationSession)typeof(ChatWindow).GetField("session",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chat);
            foreach(string theme in new[]{"light","dark"})
            {
                File.AppendAllText(Path.Combine(output,"trace.log"),DateTime.UtcNow.ToString("O")+" theme begin "+theme+Environment.NewLine);
                chatSession.Clear();if(!ReferenceEquals(chatSession,pet.Conversation))pet.Conversation.Clear();
                ThemeManager.Apply(theme);chat.Width=1000;await Settle();
                foreach(int width in new[]{1000,520}){chat.Width=width;await Settle();Inside(chat,"ShellAvatar");Inside(chat,"MessageInput");Capture(chat,"chat-empty-"+theme+"-"+width);}
                chat.Width=1000;await Settle();Click(chat,"ShellAvatar");var avatar=Button(chat,"ShellAvatar");avatar.ContextMenu.IsOpen=false;await Menu(avatar.ContextMenu,avatar,"avatar-"+theme);
                await Dialog(new LanguagePickerWindow(pet.Settings.UiLanguage,code=>{}),chat,"language-dialog-"+theme);
                foreach(string menuName in new[]{"AppMenuFile","AppMenuEdit","AppMenuView","AppMenuHelp"}){Click(chat,menuName);var button=Button(chat,menuName);Check(button.ContextMenu!=null,"production title menu exists "+menuName);button.ContextMenu.IsOpen=false;await Menu(button.ContextMenu,button,"shell-"+menuName+"-"+theme);}
                if(pet.ContextMenu!=null)await Menu(pet.ContextMenu,avatar,"pet-menu-"+theme);
                Click(chat,"ShellSettings");await Settle();var settings=All<SettingsPage>(chat).Single();
                foreach(int width in new[]{1000,700})
                {
                    chat.Width=width;
                    for(int section=0;section<3;section++){settings.SelectCompanionSection(section);await ScrollCaptures(chat,"settings-companion-"+section+"-"+theme+"-"+width);}
                    for(int section=0;section<5;section++){settings.SelectAiSection(section);await ScrollCaptures(chat,"settings-model-"+section+"-"+theme+"-"+width);}
                    settings.SelectWorkSettings();await ScrollCaptures(chat,"settings-work-"+theme+"-"+width);
                    for(int section=0;section<3;section++){settings.SelectGeneralSection(section);await ScrollCaptures(chat,"settings-general-"+section+"-"+theme+"-"+width);}
                    settings.SelectNavigation(4);await ScrollCaptures(chat,"settings-about-"+theme+"-"+width);
                }
                chat.Width=1000;
                foreach(string route in new[]{"ShellUsage","ShellMcp"}){Click(chat,route);await ScrollCaptures(chat,route+"-"+theme);}
                foreach(string tab in new[]{"PluginsSkillsTab","PluginsMcpTab"}){Click(chat,tab);await ScrollCaptures(chat,tab+"-"+theme);}
                Click(chat,"AddMcpServer");await ScrollCaptures(chat,"mcp-editor-"+theme);
                chat.ShowImagesPage();await Settle();var images=All<ImageWorkspacePage>(chat).Single();
                foreach(string tab in new[]{"ImageGenerateTab","ImageGalleryTab"})
                {
                    Click(images,tab);
                    await ScrollCaptures(chat,tab+"-"+theme);
                }
                Click(images,"ImageTasksTab");await ScrollCaptures(chat,"image-tasks-"+theme);
                Click(chat,"ShellHome");await Settle();chatSession.AddLiveTurn(chatSession.PersonaId,"UI fixture question","UI fixture reply");
                if(!ReferenceEquals(chatSession,pet.Conversation))pet.Conversation.AddLiveTurn(pet.Skin.Id,"UI fixture question","UI fixture reply");
                await Settle();Check(All<MarkdownView>(chat).Any(view=>view.Text=="UI fixture reply"),"seeded chat history visible");Capture(chat,"chat-history-"+theme);
                await Dialog(new QuickChatWindow(pet),chat,"quick-chat-"+theme);
                // Both palette tabs are text-only until an action is explicitly selected.
                // Its constructor assigns Owner=pet. Supply that window's HWND without showing it:
                // Show would start the pet's production background/update/official-download timers.
                new System.Windows.Interop.WindowInteropHelper(pet).EnsureHandle();
                var palette=new ActionPaletteWindow(pet);palette.Owner=chat;palette.Show();
                try{await Settle();Capture(palette,"palette-interactions-"+theme);Click(palette,"PaletteBasicTab");await Settle();Capture(palette,"palette-basic-"+theme);}finally{palette.Close();}await Settle();
                await Dialog(new ConversationRenameDialog("UI fixture"),chat,"dialog-rename-"+theme);
                await Dialog(new ConversationRenameDialog("UI fixture",null,null,true),chat,"dialog-rename-section-"+theme);
                await Dialog(new NewProjectDialog(),chat,"dialog-project-"+theme);
                await Dialog(new DeleteConversationDialog("UI fixture"),chat,"dialog-delete-"+theme);
                await Dialog(new DeleteConversationDialog("UI fixture",null,true),chat,"dialog-remove-companion-"+theme);
                await Dialog(new PermissionChangeDialog(TextResources.Get("composerPermissions.confirmFullAccess.permissionChangeDialog"),TextResources.Get("composerPermissions.confirmFullAccess.permissionChangeDialog.alternativeA"),true),chat,"dialog-full-access-"+theme);
                foreach(string kind in new[]{"text","asr","speech","live","image"})await Dialog(new ProviderPickerWindow(chat,kind),chat,"dialog-provider-"+kind+"-"+theme);
                var commands=(IEnumerable<ShellCommand>)typeof(ChatWindow).GetField("shellCommands",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chat);
                await Dialog(new ShortcutHelpDialog(chat,commands),chat,"dialog-shortcuts-"+theme);
                Click(chat,"ShellSettings");await Settle();
                // Direct saved-lesson fixtures show instructional text without pressing action/test controls.
                foreach(string lesson in new[]{"welcome","pet.select","persona.read","behavior.size","text.provider","asr.intro","finish.summary","finish.congratulations"})
                {
                    cfg.OnboardingState="deferred";cfg.OnboardingLessonId=lesson;
                    var guide=new OnboardingWindow(pet,settings,chat);guide.Show();try{await Settle();Check(guide.CurrentLessonId==lesson,"requested guide lesson "+lesson);await ScrollCaptures(guide,"guide-"+lesson+"-"+theme);}finally{guide.Close();}await Settle();
                }
                cfg.OnboardingState="completed";Click(chat,"ShellHome");await Settle();
            }
            File.WriteAllLines(Path.Combine(output,"manifest.tsv"),new[]{"capture\tpixels\tvisible_text_records\tcapture_mode"}.Concat(manifest),Encoding.UTF8);
            File.WriteAllLines(Path.Combine(output,"exclusions.txt"),new[]{
                "Control-only screenshots; no screen/desktop capture. One startup locale per process.",
                "Manifest capture_mode=actual-window captures a shown application control. capture_mode=template-rendered lays out the actual menu or PART_Popup.Child while native popup IsOpen=false; it verifies menu copy/templates, not OS placement, activation, mouse capture or native menu interaction.",
                "Fixtures do not activate network, microphone, speech playback, downloads, browser, file/folder pickers or destructive actions.",
                "Not captured: live audio/recording, API responses/errors/model discovery, update/download/install/uninstall dialogs, OS permission dialogs, native file pickers, WebView/browser pages; running-task/approval/question fixture states and project-hover details.",
                "Image workspace covers empty generation/gallery/task states; populated image/error/queue/reference/editor dynamic states require separate injected fixtures.",
                "Onboarding captures the eight named safe lessons only; gesture verification, remaining lesson cards and live test execution are excluded.",
                "hasHan in text dumps is a review hint, not a failure: Japanese, Chinese native names, user data and brand names may legitimately contain Han characters.",
                "Capture and layout assertions establish visibility and resource resolution; human screenshot/text review is still required for translation quality and truncation."
            },Encoding.UTF8);
            Console.WriteLine("Multilingual WPF audit: "+language+", "+captures+" captures, "+checks+" checks; "+output);
        }
        [STAThread]public static int Main(string[] args)
        {
            if(args.Length<1||args.Length>2||!UiLanguages.Choices.Any(c=>c.Code==args[0]))return 2;
            output=Path.GetFullPath(args.Length==2?args[1]:Path.Combine("work","language-ui","full","wpf",args[0]));
            app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;
            app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run(args[0]);result=0;}catch(Exception error){Console.Error.WriteLine(error);}finally{foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();if(pet!=null)pet.Close();app.Shutdown();}}));app.Run();return result;
        }
    }
}
