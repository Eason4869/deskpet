using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class WorkSettingsTests
    {
        static int checks;
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            string scratch=Path.Combine(Directory.GetCurrentDirectory(),"work","work-settings-"+Guid.NewGuid().ToString("N"));
            try
            {
                // Reflection also allows this test to report the missing feature against the preceding application build.
                var type=typeof(PetWindow).Assembly.GetType("Dafeiyu.WorkSettingsPanel");Check(type!=null,"work settings panel is available");
                Directory.CreateDirectory(scratch);string project=Path.Combine(scratch,"project");Directory.CreateDirectory(project);
                var cfg=new PetSettings{SaveChatHistory=false,GreetingEnabled=false,AutoUpdate=false};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                Check(!cfg.PlainTextEditor && cfg.ShowContextUsage && cfg.SendOnEnter && cfg.FollowUpBehavior=="queue" && cfg.ShowPredictedMessages,"new installations default to rich editing, predictions, context usage and Enter send");
                string legacyPath=Path.Combine(scratch,"legacy-settings.json");File.WriteAllText(legacyPath,"{\"ShowPredictedMessages\":false}");var legacy=new SettingsStore(legacyPath).Load();Check(!legacy.PlainTextEditor && legacy.ShowContextUsage && legacy.SendOnEnter && legacy.FollowUpBehavior=="queue" && !legacy.ShowPredictedMessages,"older settings inherit new defaults without overriding an explicit disabled preference");
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(scratch,"settings.json")),null);
                var panel=(UserControl)Activator.CreateInstance(type,new object[]{pet});
                Check(!Dirty(panel) && Access(panel).SelectedIndex==0 && Field(panel,"AgentContextTokenLimit").Text=="32768" && !All<TextBox>(panel).Any(t=>t.Name=="AgentMaxTurns" || t.Name=="AgentTimeoutMinutes"),"default form is clean with automatic compression budget and no task duration or round controls");
                Check(!All<Button>(panel).Any(b=>b.Name.StartsWith("WorkSettingsSection")) && !All<CheckBox>(panel).Any(c=>c.Name=="AgentCommandsEnabled") && !All<TextBox>(panel).Any(t=>t.Name=="AgentCommandTimeoutSeconds"),"single-page work settings omit command and tab controls");
                cfg.AgentCommandsEnabled=true;cfg.AgentCommandRecipes="invalid legacy command data";var runtimeCfg=cfg.Copy();runtimeCfg.AgentWorkspace=project;runtimeCfg.Text.Model="gpt-fixture";runtimeCfg.Text.BaseUrl="http://127.0.0.1";
                using(var runtime=ConversationSession.OpenAgentWorkspace(runtimeCfg)){Check(!new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(runtime.OpenAITools).Contains("run_command"),"application runtime ignores removed legacy command grants");var schema=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(runtime.OpenAITools);Check(schema.Contains("spawn_subagent")&&!schema.Contains("exec_command")&&!schema.Contains("github_create")&&!schema.Contains("github_read_file")&&schema.Contains("fetch_url"),"desktop only advertises connected executable capabilities");bool unsupported=false;try{unsupported=runtime.ExecuteAsync("spawn_subagent","{\"profile\":\"general\",\"input\":\"do work\"}",CancellationToken.None).GetAwaiter().GetResult().StartsWith("错误：");}catch(UnauthorizedAccessException){unsupported=true;}Check(unsupported,"unsupported general worker profile is refused");}
                Check(All<Button>(panel).Any(b=>b.Name=="WorkSettingsPromptTab") && All<TextBox>(panel).Any(t=>t.Name=="AgentSystemPrompt"),"work prompt has its own shared configuration tab");
                Check(new PetSettings().AgentEnabled,"fresh settings default to work mode for the full conversation");
                string defaultPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","agent-workspace");bool existed=Directory.Exists(defaultPath);
                Check(Save(panel) && Directory.Exists(defaultPath)==existed,"saving the default directory does not create it");
                Check(!pet.Conversation.IsBusy && pet.Conversation.History.Messages.Count==0,"saving work settings never starts a conversation");string projectNamePath=Path.Combine(scratch,"named-project");Directory.CreateDirectory(projectNamePath);var namedProject=ProjectCatalogStore.CreateProject(pet.Projects,"old name",projectNamePath);pet.ProjectStore.Save(pet.Projects);typeof(PetWindow).GetField("editingProject",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,namedProject);var projectPanel=(UserControl)Activator.CreateInstance(type,new object[]{pet});Check(!Toggle(projectPanel,"ShowPredictedMessages").IsEnabled && !Toggle(projectPanel,"AgentDetailedProcess").IsEnabled && !Toggle(projectPanel,"PlainTextEditor").IsEnabled && !Toggle(projectPanel,"ShowContextUsage").IsEnabled && !All<ComboBox>(projectPanel).Single(c=>c.Name=="SendShortcut").IsEnabled && !All<ComboBox>(projectPanel).Single(c=>c.Name=="FollowUpBehavior").IsEnabled,"project page identifies editor preferences as global settings");Field(projectPanel,"ProjectName").Text="new project name";Check(Save(projectPanel) && namedProject.Name=="new project name","project name is editable and saved");typeof(PetWindow).GetField("editingProject",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,null);

                Access(panel).SelectedIndex=1;Field(panel,"AgentWorkspace").Text=project;Field(panel,"AgentGoal").Text="  整理项目文档  ";
                Toggle(panel,"AgentPlanMode").IsChecked=true;Toggle(panel,"AgentBackupFiles").IsChecked=false;Toggle(panel,"AgentDetailedProcess").IsChecked=true;
                Field(panel,"AgentContextTokenLimit").Text="131072";Toggle(panel,"ShowPredictedMessages").IsChecked=true;
                Check(Dirty(panel) && cfg.AgentFullAccess && cfg.AgentGoal=="","edits remain local until explicit save");
                Check(Save(panel) && !Dirty(panel),"valid form saves all work fields and clears draft state");
                var saved=pet.Store.Load();Check(!saved.AgentFullAccess && saved.AgentWorkspace==project && saved.AgentPlanMode && saved.AgentGoal=="整理项目文档" && !saved.AgentBackupFiles && saved.AgentDetailedProcess && saved.AgentContextTokenLimit==131072 && saved.ShowPredictedMessages,"all work settings survive real settings persistence");
                Toggle(panel,"PlainTextEditor").IsChecked=true;Toggle(panel,"ShowContextUsage").IsChecked=false;
                All<ComboBox>(panel).Single(c=>c.Name=="SendShortcut").SelectedIndex=1;All<ComboBox>(panel).Single(c=>c.Name=="FollowUpBehavior").SelectedIndex=1;
                Check(Dirty(panel) && Save(panel),"editor preferences participate in dirty state and save");
                var editorSaved=pet.Store.Load();Check(editorSaved.PlainTextEditor && !editorSaved.ShowContextUsage && !editorSaved.SendOnEnter && editorSaved.FollowUpBehavior=="steer","all editor preferences survive persistence");
                var editorCopy=editorSaved.Copy();Check(editorCopy.PlainTextEditor && !editorCopy.ShowContextUsage && !editorCopy.SendOnEnter && editorCopy.FollowUpBehavior=="steer","editor preferences survive settings snapshots");
                var snapshot=cfg.Copy();snapshot.Text.BaseUrl="https://api.xiaomimimo.com/v1";snapshot.Text.Model="mimo-v2.5-flash";cfg.AgentPlanMode=false;cfg.AgentFullAccess=true;cfg.AgentBackupFiles=true;cfg.AgentContextTokenLimit=8192;
                var workspace=ConversationSession.OpenAgentWorkspace(snapshot);
                var policy=workspace.FreezePermissions().AccessPolicy;
                Check(!workspace.RestrictToRoot && workspace.Root==project && workspace.ReadOnly && !workspace.BackupEnabled && workspace.ContextTokenLimit==131072 && policy!=null && policy.Mode==AgentApprovalMode.Auto && policy.Sandbox,"workspace factory preserves saved plan, backup, budget and automatic sandbox policy instead of later changes");
                string outside=Path.Combine(scratch,"outside.txt");File.WriteAllText(outside,"outside-permission-sentinel");
                Check(PolicyDecision(policy,"read_file",outside)==AgentPermissionEffect.Ask && PolicyDecision(policy,"write_file",outside)==AgentPermissionEffect.Ask,"saved automatic policy requires approval for outside reads and writes without a legacy hard root restriction");
                string target=Path.Combine(project,"must-not-exist.txt");var result=workspace.ExecuteAsync("write_file","{\"path\":\"must-not-exist.txt\",\"content\":\"no\"}",CancellationToken.None).GetAwaiter().GetResult();
                Check(result.StartsWith("错误：",StringComparison.Ordinal) && !File.Exists(target) && workspace.OpenAITools.Length>=3 && !new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(workspace.OpenAITools).Contains("write_file"),"planning snapshot rejects actual writes and exposes only read tools");
                workspace.Dispose();
                var ask=snapshot.Copy();ask.AgentPlanMode=false;ask.AgentApprovalMode=AgentApprovalMode.Ask;ask.Text.ToolSupport=CapabilitySupport.Supported;var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
                using(var restricted=ConversationSession.OpenAgentWorkspace(ask)){
                    string inside=Path.Combine(project,"inside.txt");File.WriteAllText(inside,"inside-permission-sentinel");
                    string insideRead=restricted.ExecuteAsync("read_file",serializer.Serialize(new{path=inside}),CancellationToken.None).GetAwaiter().GetResult();
                    string outsideRead=restricted.ExecuteAsync("read_file",serializer.Serialize(new{path=outside}),CancellationToken.None).GetAwaiter().GetResult();
                    string outsideWrite=restricted.ExecuteAsync("write_file",serializer.Serialize(new{path=outside,content="must not overwrite"}),CancellationToken.None).GetAwaiter().GetResult();
                    Check(!restricted.ReadOnly && insideRead.Contains("inside-permission-sentinel") && outsideRead.StartsWith("错误：",StringComparison.Ordinal) && !outsideRead.Contains("outside-permission-sentinel") && outsideWrite.StartsWith("错误：",StringComparison.Ordinal) && File.ReadAllText(outside)=="outside-permission-sentinel","new ask policy permits actual inside reads and rejects outside reads and writes when no approval provider is available");
                }
                var legacySnapshot=ask.Copy();legacySnapshot.AgentApprovalMode=AgentApprovalMode.Legacy;legacySnapshot.AgentSandboxEnabled=false;legacySnapshot.AgentFullAccess=false;
                using(var restricted=ConversationSession.OpenAgentWorkspace(legacySnapshot))Check(restricted.RestrictToRoot,"explicit legacy restricted snapshot retains the original hard workspace boundary");
                legacySnapshot.AgentFullAccess=true;
                using(var complete=ConversationSession.OpenAgentWorkspace(legacySnapshot))Check(!complete.RestrictToRoot && complete.ExecuteAsync("read_file",serializer.Serialize(new{path=outside}),CancellationToken.None).GetAwaiter().GetResult().Contains("outside-permission-sentinel"),"explicit legacy complete-file snapshot retains its original outside read behavior");
                Refresh(panel);Check(!Dirty(panel) && Access(panel).SelectedIndex==0 && !Toggle(panel,"AgentPlanMode").IsChecked.Value && Field(panel,"AgentContextTokenLimit").Text=="8192","clean form refreshes externally changed shared settings");
                Field(panel,"AgentGoal").Text="本地草稿";cfg.AgentGoal="从输入框设置的目标";cfg.AgentPlanMode=true;Refresh(panel);
                Check(Field(panel,"AgentGoal").Text=="本地草稿" && !Toggle(panel,"AgentPlanMode").IsChecked.Value && Dirty(panel),"external changes do not discard a pending local form");
                Check(Save(panel),"local draft can be explicitly committed after an external change");

                foreach(string value in new[]{"0","8191","2000001","1.5","abc","2147483648",""})RejectWithoutMutation(panel,pet,"AgentContextTokenLimit",value);
                RejectWithoutMutation(panel,pet,"AgentGoal",new string('目',4001));
                RejectWithoutMutation(panel,pet,"AgentWorkspace",Path.Combine(scratch,"missing"));
                RejectWithoutMutation(panel,pet,"AgentWorkspace","relative-path");
                Access(panel).SelectedIndex=1;Field(panel,"AgentWorkspace").Text=Path.GetPathRoot(project);var before=State(cfg);
                Check(!Save(panel) && State(cfg)==before,"restricted access rejects a disk root without partial changes");
                Access(panel).SelectedIndex=0;Check(Save(panel),"complete access accepts an existing disk root without elevation");
                using(var unrestricted=ConversationSession.OpenAgentWorkspace(cfg.Copy()))Check(!unrestricted.RestrictToRoot && Directory.Exists(unrestricted.Root) && unrestricted.FreezePermissions().AccessPolicy.Mode==AgentApprovalMode.Auto,"complete file selection preserves the selected automatic approval policy at the workspace factory");
                Field(panel,"AgentWorkspace").Text=project;Field(panel,"AgentContextTokenLimit").Text="8192";Field(panel,"AgentGoal").Text=new string('目',4000);
                Check(Save(panel) && pet.Store.Load().AgentGoal.Length==4000,"minimum context budget and a 4000-character goal save successfully");
                cfg.Text.ContextWindowTokens=262144;Refresh(panel);Check(All<FrameworkElement>(panel).Single(e=>e.Name=="ContextFallbackRow").Visibility==Visibility.Collapsed && All<TextBlock>(panel).Single(t=>t.Name=="WorkContextSummary").Text.Contains("262,144"),"known model hides fallback and shows its actual configured capacity");
                cfg.Text.ContextWindowTokens=0;Refresh(panel);Check(All<FrameworkElement>(panel).Single(e=>e.Name=="ContextFallbackRow").Visibility==Visibility.Visible && Field(panel,"AgentContextTokenLimit").Text=="8192","unknown model restores the saved fallback input");
                Check(!pet.Store.Load().AgentCommandsEnabled && pet.Store.Load().AgentCommandRecipes=="[]","saving removes obsolete command grants without exposing a command editor");
                CaptureEditor(panel,scratch);Console.WriteLine("Work settings tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{if(pet!=null){pet.Close();Pump();}app.Shutdown();}
        }
        static AgentPermissionEffect PolicyDecision(AgentAccessPolicy policy,string tool,string path)
        {
            var call=new AgentToolInvocation(tool,new Dictionary<string,object>{{"path",path}},new AgentToolExecutionContext{CallId=Guid.NewGuid().ToString("N")});
            return policy.Decide(call,AgentPermissionRuntime.DefaultWorkspacePolicy().Decide(call.Action,call.Resource)).Effect;
        }
        static void CaptureEditor(UserControl panel,string folder)
        {
            var window=new Window();Ui.Window(window,"编辑器设置检查",1080,880);window.Content=panel;
            try{window.Show();Pump();var scroll=All<ScrollViewer>(panel).Single(v=>v.Name=="WorkSettingsScroll");scroll.ScrollToEnd();Pump();window.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(folder,"editor-settings.png")))png.Save(stream);Console.WriteLine("Editor settings screenshot: "+Path.Combine(folder,"editor-settings.png"));}finally{window.Close();}
        }
        static void RejectWithoutMutation(UserControl panel,PetWindow pet,string name,string value)
        {
            var field=Field(panel,name);string previous=field.Text;string live=State(pet.Settings),disk=State(pet.Store.Load());field.Text=value;
            Check(!Save(panel) && State(pet.Settings)==live && State(pet.Store.Load())==disk && Dirty(panel) && !String.IsNullOrWhiteSpace(All<TextBlock>(panel).Single(t=>t.Name=="WorkSettingsNotice").Text),"invalid "+name+" input is explained and leaves memory and disk unchanged: "+(value.Length>20?"overlong text":value));field.Text=previous;
        }
        static string State(PetSettings s){return String.Join("|",s.AgentFullAccess,s.AgentWorkspace,s.AgentPlanMode,s.AgentGoal,s.AgentBackupFiles,s.AgentContextTokenLimit,s.ShowPredictedMessages,s.AgentDetailedProcess,s.PlainTextEditor,s.ShowContextUsage,s.SendOnEnter,s.FollowUpBehavior);}
        static bool Save(UserControl panel){return (bool)panel.GetType().GetMethod("Save").Invoke(panel,null);}
        static void Refresh(UserControl panel){panel.GetType().GetMethod("RefreshState").Invoke(panel,null);}
        static bool Dirty(UserControl panel){return (bool)panel.GetType().GetProperty("HasUnsavedChanges").GetValue(panel,null);}
        static ComboBox Access(UserControl panel){return All<ComboBox>(panel).Single(c=>c.Name=="AgentAccessMode");}
        static TextBox Field(UserControl panel,string name){return All<TextBox>(panel).Single(c=>c.Name==name);}
        static CheckBox Toggle(UserControl panel,string name){return All<CheckBox>(panel).Single(c=>c.Name==name);}
        static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject{if(root is T)yield return (T)root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var value in All<T>(child))yield return value;}
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    }
}
