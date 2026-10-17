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
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(scratch,"settings.json")),null);
                var panel=(UserControl)Activator.CreateInstance(type,new object[]{pet});
                Check(!Dirty(panel) && Access(panel).SelectedIndex==0 && Field(panel,"AgentContextTokenLimit").Text=="32768" && !All<TextBox>(panel).Any(t=>t.Name=="AgentMaxTurns" || t.Name=="AgentTimeoutMinutes"),"default form is clean with automatic compression budget and no task duration or round controls");
                Check(new PetSettings().AgentEnabled,"fresh settings default to work mode for the full conversation");
                string defaultPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","agent-workspace");bool existed=Directory.Exists(defaultPath);
                Check(Save(panel) && Directory.Exists(defaultPath)==existed,"saving the default directory does not create it");
                Check(!pet.Conversation.IsBusy && pet.Conversation.History.Messages.Count==0,"saving work settings never starts a conversation");string projectNamePath=Path.Combine(scratch,"named-project");Directory.CreateDirectory(projectNamePath);var namedProject=ProjectCatalogStore.CreateProject(pet.Projects,"old name",projectNamePath);pet.ProjectStore.Save(pet.Projects);typeof(PetWindow).GetField("editingProject",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,namedProject);var projectPanel=(UserControl)Activator.CreateInstance(type,new object[]{pet});Field(projectPanel,"ProjectName").Text="new project name";Check(Save(projectPanel) && namedProject.Name=="new project name","project name is editable and saved");typeof(PetWindow).GetField("editingProject",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,null);

                Access(panel).SelectedIndex=1;Field(panel,"AgentWorkspace").Text=project;Field(panel,"AgentGoal").Text="  整理项目文档  ";
                Toggle(panel,"AgentPlanMode").IsChecked=true;Toggle(panel,"AgentBackupFiles").IsChecked=false;Toggle(panel,"AgentDetailedProcess").IsChecked=true;
                Field(panel,"AgentContextTokenLimit").Text="131072";Toggle(panel,"ShowPredictedMessages").IsChecked=true;
                Check(Dirty(panel) && cfg.AgentFullAccess && cfg.AgentGoal=="","edits remain local until explicit save");
                Check(Save(panel) && !Dirty(panel),"valid form saves all work fields and clears draft state");
                var saved=pet.Store.Load();Check(!saved.AgentFullAccess && saved.AgentWorkspace==project && saved.AgentPlanMode && saved.AgentGoal=="整理项目文档" && !saved.AgentBackupFiles && saved.AgentDetailedProcess && saved.AgentContextTokenLimit==131072 && saved.ShowPredictedMessages,"all work settings survive real settings persistence");
                var snapshot=cfg.Copy();snapshot.Text.BaseUrl="https://api.xiaomimimo.com/v1";snapshot.Text.Model="mimo-v2.5-flash";cfg.AgentPlanMode=false;cfg.AgentFullAccess=true;cfg.AgentBackupFiles=true;cfg.AgentContextTokenLimit=8192;
                var workspace=ConversationSession.OpenAgentWorkspace(snapshot);
                Check(workspace.RestrictToRoot && workspace.ReadOnly && !workspace.BackupEnabled && workspace.ContextTokenLimit==131072,"workspace factory uses the saved settings snapshot instead of later changes");
                string target=Path.Combine(project,"must-not-exist.txt");var result=workspace.ExecuteAsync("write_file","{\"path\":\"must-not-exist.txt\",\"content\":\"no\"}",CancellationToken.None).GetAwaiter().GetResult();
                Check(result.StartsWith("错误：",StringComparison.Ordinal) && !File.Exists(target) && workspace.OpenAITools.Length>=3 && !new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(workspace.OpenAITools).Contains("write_file"),"planning snapshot rejects actual writes and exposes only read tools");
                Refresh(panel);Check(!Dirty(panel) && Access(panel).SelectedIndex==0 && !Toggle(panel,"AgentPlanMode").IsChecked.Value && Field(panel,"AgentContextTokenLimit").Text=="8192","clean form refreshes externally changed shared settings");
                Field(panel,"AgentGoal").Text="本地草稿";cfg.AgentGoal="从输入框设置的目标";cfg.AgentPlanMode=true;Refresh(panel);
                Check(Field(panel,"AgentGoal").Text=="本地草稿" && !Toggle(panel,"AgentPlanMode").IsChecked.Value && Dirty(panel),"external changes do not discard a pending local form");
                Check(Save(panel),"local draft can be explicitly committed after an external change");

                foreach(string value in new[]{"0","8191","131073","1.5","abc","2147483648",""})RejectWithoutMutation(panel,pet,"AgentContextTokenLimit",value);
                RejectWithoutMutation(panel,pet,"AgentGoal",new string('目',4001));
                RejectWithoutMutation(panel,pet,"AgentWorkspace",Path.Combine(scratch,"missing"));
                RejectWithoutMutation(panel,pet,"AgentWorkspace","relative-path");
                Access(panel).SelectedIndex=1;Field(panel,"AgentWorkspace").Text=Path.GetPathRoot(project);var before=State(cfg);
                Check(!Save(panel) && State(cfg)==before,"restricted access rejects a disk root without partial changes");
                Access(panel).SelectedIndex=0;Check(Save(panel),"complete access accepts an existing disk root without elevation");
                var unrestricted=ConversationSession.OpenAgentWorkspace(cfg.Copy());Check(!unrestricted.RestrictToRoot && Directory.Exists(unrestricted.Root),"complete access selection reaches the workspace factory");
                Field(panel,"AgentWorkspace").Text=project;Field(panel,"AgentContextTokenLimit").Text="8192";Field(panel,"AgentGoal").Text=new string('目',4000);
                Check(Save(panel) && pet.Store.Load().AgentGoal.Length==4000,"minimum context budget and a 4000-character goal save successfully");
                Field(panel,"CommandRecipeId").Text="build";Field(panel,"CommandRecipeExecutable").Text=typeof(object).Assembly.Location;
                Check(Dirty(panel),"unsaved command text marks work settings dirty");
                Check(Save(panel) && pet.Store.Load().AgentCommandRecipes.Contains("build"),"saving work settings commits the pending command editor fields");
                All<ListBox>(panel).Single().SelectedIndex=0;string otherProject=Path.Combine(scratch,"other-project");Directory.CreateDirectory(otherProject);Field(panel,"AgentWorkspace").Text=otherProject;Field(panel,"CommandRecipeId").Text="renamed-build";
                Check(Save(panel) && new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<AgentCommand[]>(pet.Store.Load().AgentCommandRecipes).Single().Workspace==project,"editing a command after switching projects preserves its original project binding");
                All<Button>(panel).Single(b=>b.Name=="NewCommandRecipe").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Field(panel,"CommandRecipeId").Text="second";Field(panel,"CommandRecipeExecutable").Text=typeof(object).Assembly.Location;Check(Save(panel),"explicit new command action creates a separate recipe");
                var commandList=All<ListBox>(panel).Single();Field(panel,"CommandRecipeArguments").Text="keep draft";var selected=commandList.SelectedItem;commandList.SelectedIndex=0;
                Check(commandList.SelectedItem==selected && Field(panel,"CommandRecipeArguments").Text=="keep draft","switching commands cannot silently discard an unsaved draft");
                All<Button>(panel).Single(b=>b.Name=="ResetCommandDraft").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));commandList.SelectedIndex=0;Check(commandList.SelectedIndex==0 && !Dirty(panel),"reset draft permits selecting another saved command");
                Field(panel,"CommandRecipeId").Text="invalid";Field(panel,"CommandRecipeExecutable").Text="missing.exe";
                Check(!Save(panel) && Dirty(panel),"invalid command draft cannot be silently discarded by settings save");
                Console.WriteLine("Work settings tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{if(pet!=null){pet.Close();Pump();}app.Shutdown();}
        }
        static void RejectWithoutMutation(UserControl panel,PetWindow pet,string name,string value)
        {
            var field=Field(panel,name);string previous=field.Text;string live=State(pet.Settings),disk=State(pet.Store.Load());field.Text=value;
            Check(!Save(panel) && State(pet.Settings)==live && State(pet.Store.Load())==disk && Dirty(panel) && !String.IsNullOrWhiteSpace(All<TextBlock>(panel).Single(t=>t.Name=="WorkSettingsNotice").Text),"invalid "+name+" input is explained and leaves memory and disk unchanged: "+(value.Length>20?"overlong text":value));field.Text=previous;
        }
        static string State(PetSettings s){return String.Join("|",s.AgentFullAccess,s.AgentWorkspace,s.AgentPlanMode,s.AgentGoal,s.AgentBackupFiles,s.AgentContextTokenLimit,s.ShowPredictedMessages,s.AgentDetailedProcess);}
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
