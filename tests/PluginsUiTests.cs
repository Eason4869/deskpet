using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class PluginsUiTests
    {
        static int checks;
        static string evidence;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject {if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static T Control<T>(DependencyObject page,string name) where T:FrameworkElement{return All<T>(page).Single(c=>c.Name==name);}
        static void Click(DependencyObject page,string name){Control<Button>(page,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));}
        static void Save(FrameworkElement page,string name){page.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth),(int)Math.Ceiling(page.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(page);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(evidence,name)))encoder.Save(file);}
        static string Skill(string root,string name,string description){string directory=Path.Combine(root,name);Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"SKILL.md"),"---\nname: "+name+"\ndescription: "+description+"\n---\n\n# Local instructions\nRead this fixture when the user asks for its workflow.\n");File.WriteAllText(Path.Combine(directory,"never-run.cmd"),"@echo unexpected>\""+Path.Combine(root,"executed.txt")+"\"");return directory;}
        static void Inside(FrameworkElement page,FrameworkElement child){Point at=child.TranslatePoint(new Point(),page);Check(at.X>=-1&&at.X+child.ActualWidth<=page.ActualWidth+1,child.Name+" stays inside the narrow page");}

        [STAThread] public static int Main(string[] args)
        {
            if(args.Contains("--compile-only")){Console.WriteLine("Plugins UI tests compiled; GUI execution intentionally deferred.");return 0;}
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window host=null;PluginsPage page=null;
            string fixture=Path.Combine(Directory.GetCurrentDirectory(),"work","plugins-fixture-"+Guid.NewGuid().ToString("N"));
            evidence=Path.Combine(Directory.GetCurrentDirectory(),"work","plugins-ui-evidence");Directory.CreateDirectory(evidence);Directory.CreateDirectory(fixture);
            try{
                var settings=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,AgentEnabled=true,OnboardingState="completed"};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(fixture,"data","settings.json")),null);
                page=new PluginsPage(pet);host=new Window{Content=page};Ui.Window(host,"插件 · 本地样本验收",1050,840);host.MinWidth=0;host.MinHeight=0;host.Show();Pump();
                Check(Control<TextBlock>(page,"ManagedSkillsEmpty").IsVisible,"a fresh library has an actionable empty state");
                Check((string)Control<Button>(page,"PluginsSkillsTab").Content=="技能  0"&&(string)Control<Button>(page,"PluginsMcpTab").Content=="MCP  0","tab counts come from the local library and saved MCP configurations");
                Check(!page.HasUnsavedEdits,"opening the page does not create a draft MCP configuration");
                Save(page,"plugins-empty.png");
                string first=Skill(fixture,"writing-helper","写作整理：将笔记整理成清楚的说明"),second=Skill(fixture,"review-helper","检查本地项目的代码与变更记录，帮助完成有依据的审阅");
                Check(page.ImportDirectory(first)&&page.ImportDirectory(second),"explicit import adds valid local skill folders");Pump();
                string directory=Path.Combine(pet.Store.DirectoryPath,"skills");var library=new ManagedSkillLibrary(directory);
                Check(library.List().Count==2&&library.List().All(s=>s.Enabled)&&All<Border>(page).Count(b=>b.Name=="ManagedSkillRow")==2,"imported skills appear as enabled real list rows");
                Check(File.Exists(Path.Combine(first,"SKILL.md"))&&File.Exists(Path.Combine(directory,"writing-helper","SKILL.md")),"import keeps the source and creates the managed copy");
                Check(!page.ImportDirectory(first)&&Control<TextBlock>(page,"PluginsStatus").Text.Contains("同名"),"duplicate import shows a useful error without a phantom row");
                Save(page,"plugins-skills.png");
                var search=Control<TextBox>(page,"PluginsSearch");search.Text="写作整理";Pump();Check(All<Border>(page).Count(b=>b.Name=="ManagedSkillRow")==1,"search filters the actual description");search.Text="REVIEW-HELPER";Pump();Check(All<Border>(page).Single(b=>b.Name=="ManagedSkillRow").Tag as string=="review-helper","search matches names without case sensitivity");
                search.Text="unmatched fixture";Pump();Check(Control<TextBlock>(page,"ManagedSkillsNoResults").IsVisible&&!All<TextBlock>(page).Any(t=>t.Name=="ManagedSkillsEmpty"),"no search results is distinct from an empty library");search.Text="";Pump();
                var toggle=All<CheckBox>(page).Single(c=>c.Name=="ManagedSkillEnabled"&&(string)c.Tag=="writing-helper");toggle.IsChecked=false;Pump();Check(!new ManagedSkillLibrary(directory).List().Single(s=>s.Id=="writing-helper").Enabled,"the visible enable switch persists to the registry");Check((string)Control<Button>(page,"PluginsSkillsTab").Content=="技能  2","disabled skills remain counted as installed");
                var detail=All<Button>(page).Single(b=>b.Name=="ManagedSkillDetails"&&(string)b.Tag=="writing-helper");detail.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();var source=Control<TextBox>(page,"ManagedSkillContent");Check(source.IsReadOnly&&source.Text.Contains("# Local instructions"),"details show the actual SKILL.md as readonly text");Check(Control<TextBlock>(page,"ManagedSkillDirectory").Text==Path.Combine(directory,"writing-helper"),"details identify the managed directory");Save(page,"plugins-details.png");
                foreach(double width in new[]{460d,360d}){host.Width=width;Pump();host.UpdateLayout();Check(page.DesiredSize.Width<=page.ActualWidth+1,"the page measures within a "+width+" DIP host");foreach(string name in new[]{"PluginsSearch","ImportManagedSkill","ManagedSkillContent"})Inside(page,Control<FrameworkElement>(page,name));foreach(var row in All<Border>(page).Where(b=>b.Name=="ManagedSkillRow"))Inside(page,row);var scroll=All<ScrollViewer>(page).First();Check(scroll.ExtentWidth<=scroll.ViewportWidth+1,"skills content does not overflow horizontally at "+width+" DIP");Save(page,"plugins-narrow-"+width+".png");}
                host.Width=1050;Pump();Click(page,"RemoveManagedSkill");Check(new ManagedSkillLibrary(directory).List().Count==2&&Control<TextBlock>(page,"ManagedSkillRemoveConfirmation").IsVisible,"first remove click requires explicit confirmation");Click(page,"CancelRemoveManagedSkill");Check(new ManagedSkillLibrary(directory).List().Count==2,"cancel preserves the managed skill");
                Click(page,"PluginsMcpTab");Check(Control<McpSettingsPanel>(page,"McpSettingsPanel").IsVisible,"MCP tab exposes the retained actual configuration editor");Click(page,"AddMcpServer");var mcpName=Control<TextBox>(page,"McpServerName");mcpName.Text="unsaved plugin fixture";Check(page.HasUnsavedEdits,"an edited MCP draft is reported as unsaved");Click(page,"PluginsSkillsTab");Click(page,"PluginsMcpTab");Check(ReferenceEquals(Control<TextBox>(page,"McpServerName"),mcpName)&&mcpName.Text=="unsaved plugin fixture"&&page.HasUnsavedEdits,"tab switching preserves the original MCP editor and its unsaved draft");
                Check(pet.McpConnections.Status.Length==0&&!File.Exists(Path.Combine(fixture,"executed.txt")),"opening tabs importing and viewing skills never connects MCP or executes bundled scripts");
                Control<TextBox>(page,"McpServerEndpoint").Text="https://localhost.invalid/mcp";Click(page,"SaveMcpServers");Check((string)Control<Button>(page,"PluginsMcpTab").Content=="MCP  1"&&!page.HasUnsavedEdits,"MCP tab count updates only after the actual configuration saves");Save(page,"plugins-mcp.png");Click(page,"PluginsSkillsTab");
                host.Content=null;page.Dispose();page=new PluginsPage(pet);host.Content=page;Pump();Check(!All<CheckBox>(page).Single(c=>c.Name=="ManagedSkillEnabled"&&(string)c.Tag=="writing-helper").IsChecked.Value,"reopening the page retains the disabled skill state");
                All<Button>(page).Single(b=>b.Name=="ManagedSkillDetails"&&(string)b.Tag=="writing-helper").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Click(page,"RemoveManagedSkill");Click(page,"RemoveManagedSkill");Check(new ManagedSkillLibrary(directory).List().Count==1&&File.Exists(Path.Combine(first,"SKILL.md")),"confirmed removal unregisters the managed skill while preserving the source");
                Check(pet.McpConnections.Status.Length==0&&!File.Exists(Path.Combine(fixture,"executed.txt")),"saved configuration and final navigation still perform no automatic connection or script execution");
                Console.WriteLine("Plugins UI tests: "+checks+" passed. Evidence: "+evidence);return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{if(host!=null)host.Close();if(page!=null)page.Dispose();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
