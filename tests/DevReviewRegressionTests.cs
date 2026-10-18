using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class DevReviewRegressionTests
    {
        static string root;static int passed,failed;
        static readonly BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
        static void Run(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        static PetSettings Config(){var s=new PetSettings{AgentEnabled=false,ReadReplies=false,AutoUpdate=false,GreetingEnabled=false,OnboardingState="completed"};s.Text.BaseUrl="http://127.0.0.1:1";s.Text.Model="fixture";foreach(var key in s.Hotkeys.Keys.ToArray())s.Hotkeys[key]="";return s;}
        static ConversationWorkspace Open(ProjectDefinition p,ConversationDefinition c,bool save=true){return ConversationWorkspace.Create(p,c,root,"pet",()=>new Api(),()=>new Silent(),()=>null,Config,()=>save);}
        static ProjectDefinition Project(string id){string path=Path.Combine(root,id);Directory.CreateDirectory(path);return new ProjectDefinition{Id=id,Name=id,Workspace=path};}
        static string Call(AgentWorkspace w,string tool,object args){return w.ExecuteAsync(tool,new JavaScriptSerializer().Serialize(args),CancellationToken.None).GetAwaiter().GetResult();}
        [STAThread] public static int Main()
        {
            root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dev-review-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            Run("moving an open conversation preserves history and paused queue after reopening",MoveHistory);
            Run("kernel session resumes durable state after reopening",KernelRecovery);
            Run("kernel input is encrypted on disk",KernelEncryption);
            Run("disabling history prevents kernel transcript persistence",NoHistory);
            Run("clearing history also clears kernel transcript",ClearHistory);
            Run("kernel storage failure releases busy state and allows another send",StorageFailure);
            Run("legacy project directory and plaintext kernel migrate without losing messages",LegacyMigration);
            Run("unreadable kernel is preserved until explicitly cleared",CorruptKernel);
            Run("disabling history removes previously saved kernel transcript",DisableExistingHistory);
            Run("moving a busy conversation is refused before changing project ownership",BusyMove);
            Run("interrupting a child fills the free slot and releases its claim",Interrupt);
            Run("completing a child cannot overfill a different parent queue",ParentIsolation);
            Run("project plan menu affects actual tool permissions and moved request settings",ProjectSettings);
            Run("create project footer stays visible before and after folder selection",DialogLayout);
            app.Shutdown();Console.WriteLine("Dev review regression tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
        static void MoveHistory(){
            var a=Project("move-a");var b=Project("move-b");var c=new ConversationDefinition{Id="move-chat",Name="chat"};
            using(var w=Open(a,c)){
                w.Session.SendTextAsync("saved input",Config(),"pet").GetAwaiter().GetResult();w.Session.FlushHistoryAsync().GetAwaiter().GetResult();
                // Populate the real durable queue while the session is busy, then pause it.
                var active=w.Session.SendTextAsync("hold",Config(),"pet");w.Queue.Enqueue("pending input");w.Queue.PauseAsync().GetAwaiter().GetResult();try{active.GetAwaiter().GetResult();}catch(OperationCanceledException){}w.FollowProject(b);
            }
            using(var w=Open(b,c)){Check(w.Session.History.Messages.Any(m=>m.Content=="saved input"),"history vanished after move");Check(w.Queue.Items.Any(m=>m.Text=="pending input")&&w.Queue.IsPaused,"paused queue vanished after move");Check(w.KernelSession.ProjectId==b.Id,"kernel identity still belongs to original project");}
        }
        static void KernelRecovery(){var p=Project("recover");var c=new ConversationDefinition{Id="recover",Name="chat"};using(var w=Open(p,c)){w.Session.SendTextAsync("recover me",Config(),"pet").GetAwaiter().GetResult();}using(var w=Open(p,c)){Check(w.KernelSession.Step==1&&w.KernelSession.Delivered.Any(i=>i.Text=="recover me"),"persisted step/inbox not loaded");}}
        static void KernelEncryption(){using(var w=Open(Project("encrypted"),new ConversationDefinition{Id="encrypted",Name="chat"})){w.Session.SendTextAsync("SENSITIVE-INPUT",Config(),"pet").GetAwaiter().GetResult();Check(!Directory.GetFiles(w.DataDirectory,"*.session",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p).Contains("SENSITIVE-INPUT")),"user input is plaintext");}}
        static void NoHistory(){using(var w=Open(Project("private"),new ConversationDefinition{Id="private",Name="chat"},false)){var cfg=Config();cfg.SaveChatHistory=false;w.Session.SendTextAsync("DO-NOT-PERSIST",cfg,"pet").GetAwaiter().GetResult();Check(Directory.GetFiles(w.DataDirectory,"*.session",SearchOption.AllDirectories).Length==0,"history disabled but kernel file was saved");}}
        static void ClearHistory(){using(var w=Open(Project("clear"),new ConversationDefinition{Id="clear",Name="chat"})){w.Session.SendTextAsync("erase me",Config(),"pet").GetAwaiter().GetResult();w.Session.Clear();w.Session.FlushHistoryAsync().GetAwaiter().GetResult();Check(w.Session.KernelSession.Delivered.Count==0 && Directory.GetFiles(w.DataDirectory,"*.session",SearchOption.AllDirectories).Length==0,"kernel transcript survived Clear");}}
        static void StorageFailure(){using(var w=Open(Project("storage"),new ConversationDefinition{Id="storage",Name="chat"})){
            File.WriteAllText(Path.Combine(w.DataDirectory,"kernel-sessions"),"blocked");try{w.Session.SendTextAsync("first",Config(),"pet").GetAwaiter().GetResult();}catch(IOException){}
            Check(!w.Session.IsBusy,"session stuck busy after storage failure");Check(w.Session.HistoryWarning.Length>0,"storage error was not reported");
            w.Session.SendTextAsync("second",Config(),"pet").GetAwaiter().GetResult();Check(w.Session.History.Messages.Count(m=>m.Role=="user")==2,"second send did not complete");
            File.Delete(Path.Combine(w.DataDirectory,"kernel-sessions"));w.Session.SendTextAsync("third",Config(),"pet").GetAwaiter().GetResult();Check(w.Session.HistoryWarning.Length==0&&!w.Session.IsBusy,"warning did not recover after storage was fixed");
        }}
        static void LegacyMigration(){
            var project=Project("legacy-new-owner");var c=new ConversationDefinition{Id="legacy-chat",Name="chat",Mode=null};string legacy=Path.Combine(root,"projects","legacy-old-owner","chats",c.Id);Directory.CreateDirectory(legacy);
            var log=new ConversationLog();ChatMessage user;log.BeginTurn("legacy history","text",out user);var history=new HistoryStore(Path.Combine(legacy,"history"));history.SaveAsync("pet",log.Messages).GetAwaiter().GetResult();
            string kernel=Path.Combine(legacy,"kernel-sessions");Directory.CreateDirectory(kernel);var model=new AgentSessionModel(c.Id,"legacy-old-owner",c.Id);model.Enqueue(new AgentSessionInput{IdempotencyId="legacy-input",Text="LEGACY-PRIVATE"});model.Step=3;model.RecoveryState="running";
            using(var stream=File.Create(Path.Combine(kernel,"old.session")))new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AgentSessionModel)).WriteObject(stream,model);
            using(var w=Open(project,c)){Check(!Directory.Exists(legacy)&&w.Session.History.Messages.Any(m=>m.Content=="legacy history"),"legacy history migration failed");Check(w.KernelSession.Step==3&&w.KernelSession.Pending.Count==1&&w.KernelSession.ProjectId==project.Id&&w.KernelSession.RecoveryState=="interrupted","legacy kernel did not recover");Check(!Directory.GetFiles(w.DataDirectory,"*.session",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p).Contains("LEGACY-PRIVATE")),"legacy plaintext was retained");}
        }
        static void CorruptKernel(){var p=Project("corrupt");var c=new ConversationDefinition{Id="corrupt",Name="chat"};string file;using(var w=Open(p,c)){w.Session.SendTextAsync("before corruption",Config(),"pet").GetAwaiter().GetResult();file=Directory.GetFiles(w.DataDirectory,"*.session",SearchOption.AllDirectories).Single();}byte[] broken={1,2,3,4};File.WriteAllBytes(file,broken);using(var w=Open(p,c)){Check(w.Session.HistoryWarning.Length>0,"unreadable kernel was silently discarded");w.Session.SendTextAsync("after corruption",Config(),"pet").GetAwaiter().GetResult();Check(File.ReadAllBytes(file).SequenceEqual(broken)&&!w.Session.IsBusy,"unreadable kernel was overwritten");w.Session.Clear();w.Session.SendTextAsync("after clear",Config(),"pet").GetAwaiter().GetResult();Check(w.Session.HistoryWarning.Length==0,"clear did not unblock kernel persistence");}}
        static void DisableExistingHistory(){var p=Project("disable-existing");var c=new ConversationDefinition{Id="disable-existing",Name="chat"};using(var w=Open(p,c))w.Session.SendTextAsync("old input",Config(),"pet").GetAwaiter().GetResult();using(var w=Open(p,c,false)){Check(w.KernelSession.Delivered.Count==0&&Directory.GetFiles(w.DataDirectory,"*.session",SearchOption.AllDirectories).Length==0,"previous kernel persisted with history disabled");}}
        static void BusyMove(){var p=Project("busy-a");var q=Project("busy-b");using(var w=Open(p,new ConversationDefinition{Id="busy",Name="chat"})){var active=w.Session.SendTextAsync("hold",Config(),"pet");bool refused=false;try{w.FollowProject(q);}catch(InvalidOperationException){refused=true;}Check(refused&&w.ProjectId==p.Id,"busy move changed project before rejecting");w.Session.StopAsync().GetAwaiter().GetResult();try{active.GetAwaiter().GetResult();}catch(OperationCanceledException){}}}
        static void Interrupt(){var claims=new ExecutionClaimStore(Path.Combine(root,"interrupt-claims"));var s=new AgentSubagentScheduler(new AgentSubagentManager(claims),1);var profile=new AgentProfileRegistry().Get("general");var a=s.Schedule("p",profile,"a");var b=s.Schedule("p",profile,"b");s.Interrupt(a.Id);Check(b.Status=="running","queued child not promoted");Check(!claims.Interrupted.Contains(a.Id),"interrupted child claim still running");}
        static void ParentIsolation(){var s=new AgentSubagentScheduler(new AgentSubagentManager(new ExecutionClaimStore(Path.Combine(root,"parent-claims"))),1);var p=new AgentProfileRegistry().Get("general");var a=s.Schedule("a",p,"a");var b=s.Schedule("b",p,"b");var queued=s.Schedule("b",p,"queued");s.Complete(a.Id,"done");Check(queued.Status=="queued"&&s.List("b").Count(x=>x.Status=="running")==1,"completion overfilled another parent");s.Interrupt(b.Id);Check(queued.Status=="running","parent queue did not resume");}
        static void ProjectSettings(){var pet=new PetWindow(null,Config(),new SettingsStore(Path.Combine(root,"ui","settings.json")),null);try{
            var a=ProjectCatalogStore.CreateProject(pet.Projects,"A",Project("ui-a").Workspace);var b=ProjectCatalogStore.CreateProject(pet.Projects,"B",Project("ui-b").Workspace);b.FullAccess=false;b.PlanMode=true;b.Goal="B goal";
            pet.OpenProjectConversation(a,a.Conversations[0]);var chat=Application.Current.Windows.OfType<ChatWindow>().Single();var composer=typeof(ChatWindow).GetField("composer",hidden).GetValue(chat);composer.GetType().GetMethod("BuildSlashItems",hidden).Invoke(composer,null);
            foreach(var item in (System.Collections.IEnumerable)composer.GetType().GetField("triggerItems",hidden).GetValue(composer))if((string)item.GetType().GetField("Title").GetValue(item)=="计划模式")((Action)item.GetType().GetField("Action").GetValue(item))();
            var w=pet.Workspaces.OpenWorkspaces.Single();var settings=(Func<PetSettings>)typeof(ConversationQueue).GetField("settings",hidden).GetValue(w.Queue);var cfg=settings();Check(cfg.AgentPlanMode,"menu did not enable project plan mode");
            using(var tools=new AgentWorkspace(cfg.AgentWorkspace){ReadOnly=cfg.AgentPlanMode})Check(Call(tools,"write_file",new{path="forbidden.txt",content="x"}).StartsWith("错误：")&&!File.Exists(Path.Combine(cfg.AgentWorkspace,"forbidden.txt")),"plan mode wrote a file");
            Check(pet.MoveConversation(a,b,a.Conversations[0]),"move failed");cfg=settings();Check(cfg.AgentWorkspace==b.Workspace&&!cfg.AgentFullAccess&&cfg.AgentPlanMode&&cfg.AgentGoal=="B goal","moved queue uses old project settings");chat.Close();
        }finally{pet.Close();}}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var x in All<T>(VisualTreeHelper.GetChild(root,i)))yield return x;}
        static void DialogLayout(){var dialog=new NewProjectDialog();try{dialog.Show();dialog.UpdateLayout();var content=(FrameworkElement)dialog.Content;var create=All<Button>(content).Single(b=>b.Name=="CreateProject");var cancel=All<Button>(content).Single(b=>Object.Equals(b.Content,"取消"));Action check=()=>{foreach(var button in new[]{create,cancel}){var point=button.TransformToAncestor(content).Transform(new Point());Check(button.ActualHeight>=28&&point.Y>=0&&point.Y+button.ActualHeight<=content.ActualHeight+1 && button.TransformToAncestor(dialog).Transform(new Point()).Y+button.ActualHeight<=dialog.ActualHeight-6,"footer button outside visible content");}};check();create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));dialog.UpdateLayout();check();foreach(double scale in new[]{1.0,1.25,1.5}){var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*scale),(int)Math.Ceiling(content.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(dialog.Background,null,new Rect(0,0,content.ActualWidth,content.ActualHeight));drawing.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));}bitmap.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(root,"new-project-"+(int)(scale*100)+".png")))encoder.Save(output);}typeof(NewProjectDialog).GetField("workspacePath",hidden).SetValue(dialog,Path.Combine(root,new string('x',120)));typeof(NewProjectDialog).GetMethod("RefreshFolder",hidden).Invoke(dialog,null);dialog.UpdateLayout();check();}finally{dialog.Close();}}
        sealed class Api:IConversationApi{public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken t){if(m.Last().Content=="hold"){var pending=new TaskCompletionSource<string>();t.Register(()=>pending.TrySetCanceled());return pending.Task;}return Task.FromResult("reply");}public Task<string> TranscribeAsync(ConnectionSettings s,byte[] b,CancellationToken t){return Task.FromResult("");}public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string text,CancellationToken t){return Task.FromResult(new byte[0]);}public void Dispose(){}}
        sealed class Silent:IReplySpeech{public Task SpeakAsync(string s,PetSettings p,CancellationToken t){return Task.FromResult(0);}public void Stop(){}public void SetVolume(int n){}public void Dispose(){}}
    }
}
