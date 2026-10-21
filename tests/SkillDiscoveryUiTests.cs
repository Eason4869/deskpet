using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class SkillDiscoveryUiTests
    {
        static int checks;
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static T Control<T>(DependencyObject page,string name) where T:FrameworkElement{return All<T>(page).Single(c=>c.Name==name);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));}
        static void Click(DependencyObject page,string name){Control<Button>(page,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        static void Wait(SkillDiscoveryPanel panel,int seconds=30){var until=DateTime.UtcNow.AddSeconds(seconds);do{Pump();Thread.Sleep(10);}while(panel.IsBusy&&DateTime.UtcNow<until);Pump();Check(!panel.IsBusy,"asynchronous discovery operation completes");}
        static void Save(FrameworkElement page,string directory,string name){page.UpdateLayout();var bmp=new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth),(int)Math.Ceiling(page.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(page);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var output=File.Create(Path.Combine(directory,name)))encoder.Save(output);}
        [STAThread] public static int Main(string[] args)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window host=null;PluginsPage page=null;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            AppDomain.CurrentDomain.UnhandledException+=delegate(object sender,UnhandledExceptionEventArgs eventArgs){var error=eventArgs.ExceptionObject as Exception;if(error!=null){Console.Error.WriteLine("UNHANDLED "+error.GetType().FullName+" "+error.Message);Console.Error.WriteLine(error.StackTrace);}};
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","discovery-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string evidence=Path.Combine(Directory.GetCurrentDirectory(),"work","skill-discovery-evidence");Directory.CreateDirectory(evidence);
            try{
                var settings=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,AgentEnabled=true,OnboardingState="completed"};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"data","settings.json")),null);
                var handler=new SkillRepositoryTests.Fixture();page=new PluginsPage(pet,args.Contains("--live")?new SkillRepositoryClient():new SkillRepositoryClient(handler));host=new Window{Content=page};Ui.Window(host,"技能发现 · 流程验收",1050,900);host.MinWidth=0;host.MinHeight=0;host.Show();Pump();
                Click(page,"ImportManagedSkill");var menu=Control<Button>(page,"ImportManagedSkill").ContextMenu;
                Check(menu.Items.OfType<MenuItem>().Select(m=>m.Header as string).SequenceEqual(new[]{"在发现里浏览","从 ZIP 安装","导入本机已有"}),"add menu exposes browse ZIP and existing directory");
                menu.IsOpen=false;((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();var panel=Control<SkillDiscoveryPanel>(page,"SkillDiscoveryPanel");
                if(args.Contains("--live")){
                    Control<TextBox>(panel,"SkillRepositoryInput").Text="anthropics/skills";Click(panel,"AddSkillRepository");Wait(panel,180);Console.WriteLine("LIVE "+Control<TextBlock>(panel,"SkillDiscoveryStatus").Text);
                    Check(All<Border>(panel).Any(c=>c.Name=="DiscoveredSkillRow"),"live public repository displays discovered skills");Save(page,evidence,"live-repository-results.png");
                    Control<TextBox>(panel,"SkillDiscoverySearch").Text="brand-guidelines";Pump();Click(panel,"PreviewDiscoveredSkill");Save(page,evidence,"live-skill-preview.png");Console.WriteLine("LIVE ORIGIN "+Control<TextBlock>(panel,"SkillPreviewOrigin").Text);
                    Click(panel,"InstallDiscoveredSkill");Wait(panel,180);Console.WriteLine("LIVE INSTALL "+Control<TextBlock>(panel,"SkillDiscoveryStatus").Text);Check((string)Control<Button>(page,"PluginsSkillsTab").Content=="技能  1","live UI installation registers the selected skill");Save(page,evidence,"live-skill-installed.png");
                    ManagedSkillLibrary.ActiveDirectory=Path.Combine(pet.Store.DirectoryPath,"skills");using(var workspace=new AgentWorkspace(root)){workspace.ConfigureInstructions(false);Check(workspace.ExecuteAsync("load_skill","{\"name\":\"brand-guidelines\"}",CancellationToken.None).Result.Contains("brand-guidelines"),"real load_skill reads the live GitHub installation");}ManagedSkillLibrary.ActiveDirectory=null;
                    Console.WriteLine("Live skill discovery UI tests: "+checks+" passed. Data: "+root+" Evidence: "+evidence);return 0;
                }
                Check(handler.Requests.Count==0,"opening discovery never makes automatic network requests");
                Control<TextBox>(panel,"SkillRepositoryInput").Text="demo/skills";Click(panel,"AddSkillRepository");Wait(panel);
                Check(Control<TextBlock>(panel,"SkillDiscoveryStatus").Text.Contains("刷新完成"),"queued progress never replaces final scan status");
                Save(page,evidence,"discovery-repositories-light.png");ThemeManager.Apply("dark");Pump();Save(page,evidence,"discovery-repositories-dark.png");ThemeManager.Apply("light");Pump();
                Check(All<Border>(panel).Count(c=>c.Name=="DiscoveredSkillRow")==3,"adding a source scans and renders root and nested skills");
                var search=Control<TextBox>(panel,"SkillDiscoverySearch");search.Text="REVIEW-HELPER";Pump();Check(All<Border>(panel).Count(c=>c.Name=="DiscoveredSkillRow")==1,"discovery search matches actual names ignoring case");
                Click(panel,"PreviewDiscoveredSkill");Check(Control<TextBox>(panel,"SkillPreviewContent").IsReadOnly&&Control<TextBox>(panel,"SkillPreviewContent").Text.Contains("# review-helper"),"preview shows actual readonly SKILL.md");
                Check(Control<TextBlock>(panel,"SkillPreviewOrigin").Text.Contains(SkillRepositoryTests.Fixture.Commit),"preview identifies exact immutable commit");Save(page,evidence,"discovery-preview-light.png");
                ThemeManager.Apply("dark");Pump();Save(page,evidence,"discovery-preview-dark.png");ThemeManager.Apply("light");Pump();
                Click(panel,"InstallDiscoveredSkill");Wait(panel);Check((string)Control<Button>(page,"PluginsSkillsTab").Content=="技能  1","repository install updates actual installed count");
                Check(!Control<Button>(panel,"InstallDiscoveredSkill").IsEnabled&&Control<TextBlock>(panel,"SkillDiscoveryStatus").Text.Contains("下一次"),"successful install reports availability and prevents duplicate installation");
                search.Text="demo/skills";Pump();Check(All<Border>(panel).Count(c=>c.Name=="DiscoveredSkillRow")==3,"search also matches repository origin");search.Text="";
                var toggle=Control<CheckBox>(panel,"SkillRepositoryEnabled");toggle.IsChecked=false;Pump();Check(All<Border>(panel).Count(c=>c.Name=="DiscoveredSkillRow")==0,"unchecked source is excluded from results");toggle=Control<CheckBox>(panel,"SkillRepositoryEnabled");toggle.IsChecked=true;Pump();
                handler.Fail=true;Click(panel,"RefreshSkillRepositories");Wait(panel);Check(All<Border>(panel).Count(c=>c.Name=="DiscoveredSkillRow")==3&&Control<TextBlock>(panel,"SkillDiscoveryStatus").Text.Contains("限流"),"failed refresh keeps successful cache with actionable status");handler.Fail=false;
                handler.Block=true;Click(panel,"RefreshSkillRepositories");Pump();Click(panel,"CancelSkillDiscovery");Wait(panel);Check(Control<TextBlock>(panel,"SkillDiscoveryStatus").Text.Contains("取消")&&All<Border>(panel).Count(c=>c.Name=="DiscoveredSkillRow")==3,"cancel restores actionable cached results");handler.Block=false;
                search.Text="review-helper";Pump();Click(panel,"PreviewDiscoveredSkill");
                foreach(double width in new[]{460d,360d}){host.Width=width;Pump();host.UpdateLayout();Check(page.DesiredSize.Width<=page.ActualWidth+1,"discovery page measures within "+width+" DIP");foreach(string name in new[]{"SkillDiscoverySearch","SkillPreviewContent","InstallDiscoveredSkill"}){var control=Control<FrameworkElement>(panel,name);Point at=control.TranslatePoint(new Point(),page);Check(at.X>=-1&&at.X+control.ActualWidth<=page.ActualWidth+1,name+" remains inside narrow page");}Save(page,evidence,"discovery-narrow-"+width+".png");}
                host.Width=1050;Pump();Click(panel,"CloseSkillDiscovery");Check(All<Border>(page).Any(c=>c.Name=="ManagedSkillRow"),"back returns to the newly installed real skill");
                var details=All<Button>(page).Single(c=>c.Name=="ManagedSkillDetails");details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(Control<TextBlock>(page,"ManagedSkillOrigin").Text.Contains(SkillRepositoryTests.Fixture.Commit),"installed details persist provenance");
                host.Content=null;page.Dispose();page=new PluginsPage(pet,new SkillRepositoryClient(handler));host.Content=page;page.OpenDiscovery();Pump();panel=Control<SkillDiscoveryPanel>(page,"SkillDiscoveryPanel");Check(All<Border>(panel).Count(c=>c.Name=="DiscoveredSkillRow")==3,"reopening restores cached results without a refresh");
                Control<Expander>(panel,"SkillRepositoryManager").IsExpanded=true;Pump();Click(panel,"RemoveSkillRepository");Check(All<Border>(panel).Count(c=>c.Name=="DiscoveredSkillRow")==0&&(string)Control<Button>(page,"PluginsSkillsTab").Content=="技能  1","removing source removes discovery results and preserves installation");
                string zipPath=Path.Combine(root,"local-skills.zip");using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create)){using(var writer=new StreamWriter(zip.CreateEntry("bundle/SKILL.md").Open()))writer.Write(SkillRepositoryTests.Body("zip-ui-helper"));}
                panel.OpenZip(zipPath);Wait(panel);Check(All<Border>(panel).Single(c=>c.Name=="DiscoveredSkillRow").Tag as string=="zip-ui-helper","ZIP entry opens actual previewable results");Click(panel,"PreviewDiscoveredSkill");Click(panel,"InstallDiscoveredSkill");Wait(panel);Check((string)Control<Button>(page,"PluginsSkillsTab").Content=="技能  2","ZIP UI installs into the same managed library");Click(panel,"CloseSkillArchive");Check(All<Border>(panel).Count(c=>c.Name=="DiscoveredSkillRow")==0,"closing ZIP returns to repository search");
                Click(page,"PluginsMcpTab");Click(page,"AddMcpServer");Check(Control<TextBox>(page,"McpServerName").IsVisible,"MCP add action still opens retained editor");
                Console.WriteLine("Skill discovery UI tests: "+checks+" passed. Evidence: "+evidence);return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{if(host!=null)host.Close();if(page!=null)page.Dispose();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
