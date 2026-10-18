using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class SummaryNavigationTests
    {
        static int checks;static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        static void ExpandCommands(AgentTaskPanel panel){foreach(var expander in All<StackPanel>(panel).Single(p=>p.Name=="AgentTaskCommands").Children.OfType<Expander>())expander.IsExpanded=true;panel.UpdateLayout();}
        static void Wait(Func<bool> done){var deadline=DateTime.UtcNow.AddSeconds(8);while(!done()&&DateTime.UtcNow<deadline){Pump();Thread.Sleep(10);}if(!done())throw new Exception("Summary navigation timed out");}
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;AgentTaskPanel panel=null;
            try
            {
                string fixture=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","summary-"+Guid.NewGuid().ToString("N"));string root=Path.Combine(fixture,"project");Directory.CreateDirectory(root);string path=Path.Combine(root,"测试 文件.txt");File.WriteAllText(path,"before\nold");
                var cfg=new PetSettings{AgentWorkspace=root,AutoUpdate=false,AgentEnabled=true,GreetingEnabled=false,ReadReplies=false,ShowPredictedMessages=false,OnboardingState="completed"};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(fixture,"data","settings.json")),null);
                AgentTaskState task;using(var workspace=new AgentWorkspace(root)){workspace.BeginTask(new AgentTaskState{Workspace=root,PersonaId=pet.Skin.Id,Goal="导航回归"});workspace.ExecuteAsync("write_file",new JavaScriptSerializer().Serialize(new{path="测试 文件.txt",content="after\nnew"}),CancellationToken.None).GetAwaiter().GetResult();workspace.FinishTask("complete","");task=workspace.TaskState;}
                task.Commands.Add(new CommandResult{CommandId="build-fixture",CommandText="dotnet build",Workspace=root,IsolationDescription="fixture isolation",Output="构建输出\n第一条完整记录",ExitCode=0});task.Commands.Add(new CommandResult{CommandId="test-fixture",CommandText="dotnet test",Workspace=root,IsolationDescription="fixture isolation",Output="测试输出\n第二条完整记录",ExitCode=3});
                task.Children.Add(new AgentChildResult{Id="reader-child",Status="complete",Input="检查记录",Result="子任务真实完整结果",ToolCalls=2,EstimatedTokens=100});
                panel=new AgentTaskPanel(pet);var tasks=(Dictionary<string,AgentTaskState>)typeof(ConversationSession).GetField("agentTasks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet.Conversation);tasks[pet.Conversation.PersonaId]=task;typeof(AgentTaskPanel).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(panel,null);panel.IsExpanded=true;
                var host=new Window{Content=panel,Width=800,Height=650};host.Show();host.UpdateLayout();Pump();
                Check(All<Button>(panel).Any(b=>b.Name=="OpenAgentChangedFile"),"changed filename is a real clickable summary link");
                string openedFile=null,title=null;FrameworkElement content=null;int details=0;panel.OpenFileRequested=p=>openedFile=p;panel.OpenDetailRequested=(name,element)=>{title=name;content=element;details++;};host.UpdateLayout();
                Check(openedFile==null&&details==0,"assigning navigation callbacks never launches content automatically");
                All<Button>(panel).Single(b=>b.Name=="OpenAgentChangedFile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(openedFile==path,"filename routed click forwards the exact full changed-file path");
                All<Button>(panel).Single(b=>b.Name=="OpenAgentChildTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(title=="子任务 · reader-child"&&content.Parent==null&&All<TextBox>(content).Single().Text.Contains("子任务真实完整结果")&&All<TextBox>(content).Single().IsReadOnly,"child-task details open their own fresh read-only result on explicit click");
                ExpandCommands(panel);var commandButtons=All<Button>(panel).Where(b=>b.Name=="OpenAgentCommandTab").ToArray();Check(commandButtons.Length==2,"each command output has an explicit tab action");
                commandButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var first=content;Check(title.Contains("build-fixture")&&first.Parent==null&&All<TextBox>(first).Single().Text.Contains("第一条完整记录")&&All<TextBox>(first).Single().Text.Contains("dotnet build")&&All<TextBox>(first).Single().IsReadOnly,"first command opens a new unparented read-only view with its real command and complete output");
                var detailHost=new Window{Content=first,Width=500,Height=300};detailHost.Show();detailHost.UpdateLayout();commandButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!ReferenceEquals(first,content)&&content.Parent==null,"reopening an already mounted command creates fresh controls");commandButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(title.Contains("test-fixture")&&All<TextBox>(content).Single().Text.Contains("第二条完整记录")&&!All<TextBox>(content).Single().Text.Contains("第一条完整记录"),"each command link preserves its own corresponding output");detailHost.Close();
                int before=details;All<Button>(panel).Single(b=>b.Name=="ShowAgentDiff").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>details>before);Check(title=="文件差异 · "+Path.GetFileName(path)&&content.Parent==null&&All<FrameworkElement>(content).Any(e=>e.Name=="AgentDiffView"),"diff routed click returns the real standalone diff content to the detail tab callback");Check(!app.Windows.OfType<Window>().Any(w=>w.Name=="AgentChangePreview"&&w.IsVisible),"wired diff navigation does not create a floating preview window");
                var raw=All<Expander>(content).Single();raw.IsExpanded=true;var diffHost=new Window{Content=content,Width=700,Height=450};diffHost.Show();diffHost.UpdateLayout();Check(All<TextBox>(content).Any(t=>t.Text=="before\nold")&&All<TextBox>(content).Any(t=>t.Text=="after\nnew")&&All<TextBox>(content).All(t=>t.IsReadOnly),"tab diff preserves actual before/current source in read-only controls");diffHost.Close();
                panel.OpenFileRequested=null;panel.OpenDetailRequested=null;host.UpdateLayout();ExpandCommands(panel);Check(!All<Button>(panel).Single(b=>b.Name=="OpenAgentChangedFile").IsEnabled&&All<Button>(panel).Where(b=>b.Name=="OpenAgentCommandTab").All(b=>!b.IsEnabled),"unwired navigation actions stay disabled while inline task content remains available");
                All<Button>(panel).Single(b=>b.Name=="ShowAgentDiff").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>app.Windows.OfType<Window>().Any(w=>w.Name=="AgentChangePreview"&&w.IsVisible));var legacy=app.Windows.OfType<Window>().Single(w=>w.Name=="AgentChangePreview"&&w.IsVisible);Check(All<FrameworkElement>(legacy).Any(e=>e.Name=="AgentDiffView"),"null detail callback retains the existing standalone diff behavior");legacy.Close();
                Check(All<Button>(panel).Any(b=>b.Name=="RestoreAgentFile")&&All<Button>(panel).Any(b=>b.Name=="ContinueAgentTask")&&All<Button>(panel).Any(b=>b.Name=="OpenAgentProjectFacts"),"summary navigation preserves restoration continuation and project-fact controls");
                Console.WriteLine("Summary navigation tests: "+checks+" passed");host.Close();return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{if(panel!=null)panel.Dispose();foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
