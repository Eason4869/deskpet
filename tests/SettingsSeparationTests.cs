using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class SettingsSeparationTests
    {
        static int passed,failed;
        const BindingFlags Members=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject {if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
        static void Run(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
        static System.Collections.Generic.IEnumerable<T> Logical<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;foreach(var value in LogicalTreeHelper.GetChildren(root)){var child=value as DependencyObject;if(child!=null)foreach(var nested in Logical<T>(child))yield return nested;}}
        static T Control<T>(DependencyObject root,string name)where T:FrameworkElement{return Logical<T>(root).Single(c=>c.Name==name);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        static void Wait(Task task){var timeout=DateTime.UtcNow.AddSeconds(15);while(!task.IsCompleted){if(DateTime.UtcNow>timeout)throw new TimeoutException("settings save timeout");Pump();Thread.Sleep(10);}task.GetAwaiter().GetResult();}
        static void Capture(Window host,string path){host.UpdateLayout();var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)host.ActualWidth,(int)host.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(host);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var stream=File.Create(path))png.Save(stream);}
        [STAThread] public static int Main()
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window host=null;
            try{
                string root=Path.GetFullPath(Path.Combine("work","settings-separation-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
                var cfg=new PetSettings{AgentWorkspace=root,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,SaveChatHistory=false,AgentApprovalMode=AgentApprovalMode.Ask};foreach(var name in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[name]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);host=new Window{Width=1030,Height=800};host.Show();
                Run("saving a stale work draft preserves current global conversation preferences",delegate{
                    var panel=new WorkSettingsPanel(pet);host.Content=panel;host.UpdateLayout();
                    cfg.ShowPredictedMessages=false;cfg.PlainTextEditor=true;cfg.ShowContextUsage=false;cfg.SendOnEnter=false;cfg.FollowUpBehavior="steer";cfg.AgentDetailedProcess=true;
                    Control<TextBox>(panel,"AgentGoal").Text="work-only edit";Check(panel.Save(),"work save failed");
                    var saved=pet.Store.Load();Check(!saved.ShowPredictedMessages&&saved.PlainTextEditor&&!saved.ShowContextUsage&&!saved.SendOnEnter&&saved.FollowUpBehavior=="steer"&&saved.AgentDetailedProcess,"work save overwrote another page's preferences");
                    Check(!All<FrameworkElement>(panel).Any(c=>new[]{"ShowPredictedMessages","PlainTextEditor","ShowContextUsage","SendShortcut","FollowUpBehavior","AgentDetailedProcess"}.Contains(c.Name)),"global controls remain in work settings");
                });
                Run("conversation settings save only their own preferences and restore drafts without disk writes",delegate{
                    var type=typeof(PetWindow).Assembly.GetType("Dafeiyu.ConversationSettingsPanel");Check(type!=null,"conversation settings page is missing");
                    var panel=(UserControl)Activator.CreateInstance(type,new object[]{pet});host.Content=panel;host.UpdateLayout();
                    Control<CheckBox>(panel,"ShowPredictedMessages").IsChecked=true;Control<CheckBox>(panel,"ReadReplies").IsChecked=true;Control<ComboBox>(panel,"DefaultConversationMode").SelectedIndex=0;
                    cfg.AgentGoal="newer work goal";Check((bool)type.GetMethod("Save").Invoke(panel,null),"conversation save failed");
                    var saved=pet.Store.Load();Check(saved.ShowPredictedMessages&&saved.ReadReplies&&!saved.AgentEnabled&&saved.AgentGoal=="newer work goal","conversation save touched work fields or missed preferences");
                    Control<CheckBox>(panel,"PlainTextEditor").IsChecked=!saved.PlainTextEditor;var before=File.ReadAllBytes(Path.Combine(root,"settings.json"));
                    Control<Button>(panel,"RestoreConversationSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!(bool)type.GetProperty("HasUnsavedChanges").GetValue(panel,null)&&File.ReadAllBytes(Path.Combine(root,"settings.json")).SequenceEqual(before),"restore did not discard only the local draft");
                    Control<CheckBox>(panel,"ShowPredictedMessages").IsChecked=false;pet.Store.BeforeExactSave=delegate{throw new IOException("fixture save failure");};
                    try{var failedSave=(Task<bool>)type.GetMethod("SaveAsync").Invoke(panel,null);Wait(failedSave);Check(!failedSave.Result&&cfg.ShowPredictedMessages&&File.ReadAllBytes(Path.Combine(root,"settings.json")).SequenceEqual(before)&&(bool)type.GetProperty("HasUnsavedChanges").GetValue(panel,null),"failed async save published a draft or lost dirty state");}finally{pet.Store.BeforeExactSave=null;}
                    var success=(Task<bool>)type.GetMethod("SaveAsync").Invoke(panel,null);Wait(success);Check(success.Result&&!pet.Store.Load().ShowPredictedMessages&&!(bool)type.GetProperty("HasUnsavedChanges").GetValue(panel,null),"async retry did not publish and acknowledge only conversation preferences");
                    foreach(var language in new[]{"zh_CN","en_US"})foreach(var theme in new[]{"light","dark"}){TextResources.ApplyLanguage(TextResources.PrepareLanguage(language));ThemeManager.Apply(theme);host.Width=520;host.Height=740;Pump();for(int section=0;section<3;section++){type.GetMethod("SelectSection",Members).Invoke(panel,new object[]{section});Pump();Capture(host,Path.Combine(root,"conversation-settings-"+language+"-"+theme+"-"+section+".png"));}}ThemeManager.Apply("system");TextResources.ApplyLanguage(TextResources.PrepareLanguage("zh_CN"));host.Width=1030;host.Height=800;
                });
                Run("saving a conversation preference preserves newer runtime mode and read-aloud choices",delegate{
                    var panel=new ConversationSettingsPanel(pet);host.Content=panel;host.UpdateLayout();panel.SelectSection(1);Control<CheckBox>(panel,"PlainTextEditor").IsChecked=false;
                    cfg.AgentEnabled=true;cfg.ReadReplies=false;Check(panel.Save(),"conversation save failed");var stored=pet.Store.Load();
                    Check(stored.AgentEnabled&&!stored.ReadReplies&&!stored.PlainTextEditor,"saving an input draft reverted runtime mode or read-aloud changes");
                    Check(Control<ComboBox>(panel,"DefaultConversationMode").SelectedIndex==1&&!Control<CheckBox>(panel,"ReadReplies").IsChecked.Value&&!panel.HasUnsavedChanges,"successful save did not acknowledge newer runtime preferences");
                });
                Run("asynchronous conversation save retains edits made after submission",delegate{
                    var panel=new ConversationSettingsPanel(pet);host.Content=panel;host.UpdateLayout();panel.SelectSection(1);Control<CheckBox>(panel,"PlainTextEditor").IsChecked=true;
                    using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                        pet.Store.BeforeExactSave=delegate{entered.Set();if(!release.Wait(10000))throw new IOException("fixture release timeout");};
                        Task<bool> saving=null;
                        try{saving=panel.SaveAsync();var deadline=DateTime.UtcNow.AddSeconds(10);while(!entered.IsSet){if(DateTime.UtcNow>deadline)throw new TimeoutException("fixture save did not enter");Pump();Thread.Sleep(10);}Control<CheckBox>(panel,"PlainTextEditor").IsChecked=false;release.Set();Wait(saving);Check(saving.Result&&pet.Store.Load().PlainTextEditor&&!Control<CheckBox>(panel,"PlainTextEditor").IsChecked.Value&&panel.HasUnsavedChanges,"save acknowledgement discarded a later local draft");}
                        finally{release.Set();pet.Store.BeforeExactSave=null;if(saving!=null)Wait(saving);}
                    }
                    Check(panel.Save()&&!pet.Store.Load().PlainTextEditor&&!panel.HasUnsavedChanges,"later draft could not be saved after the earlier transaction");
                });
                Run("preference restoration refreshes restored inputs while retaining unrelated conversation drafts",delegate{
                    var type=typeof(PetWindow).Assembly.GetType("Dafeiyu.ConversationSettingsPanel");Check(type!=null,"conversation settings page is missing");
                    var panel=(UserControl)Activator.CreateInstance(type,new object[]{pet});host.Content=panel;host.UpdateLayout();
                    int modeDraft=cfg.AgentEnabled?0:1;bool speechDraft=!cfg.ReadReplies;Control<ComboBox>(panel,"DefaultConversationMode").SelectedIndex=modeDraft;Control<CheckBox>(panel,"ReadReplies").IsChecked=speechDraft;Control<CheckBox>(panel,"AgentDetailedProcess").IsChecked=false;
                    cfg.ShowPredictedMessages=false;cfg.PlainTextEditor=false;cfg.ShowContextUsage=true;cfg.SendOnEnter=true;cfg.FollowUpBehavior="queue";
                    type.GetMethod("RefreshRestoredPreferences").Invoke(panel,null);
                    Check(Control<ComboBox>(panel,"DefaultConversationMode").SelectedIndex==modeDraft&&Control<CheckBox>(panel,"ReadReplies").IsChecked==speechDraft&&!Control<CheckBox>(panel,"AgentDetailedProcess").IsChecked.Value,"restoration discarded unrelated mode, read-aloud or process-display drafts");
                    Check(!Control<CheckBox>(panel,"ShowPredictedMessages").IsChecked.Value&&!Control<CheckBox>(panel,"PlainTextEditor").IsChecked.Value&&Control<CheckBox>(panel,"ShowContextUsage").IsChecked.Value&&(bool)type.GetProperty("HasUnsavedChanges").GetValue(panel,null),"restored preferences did not refresh independently of unrelated drafts");
                });
                Run("conversation tabs change actual content while keeping one shared unsaved draft",delegate{
                    var type=typeof(PetWindow).Assembly.GetType("Dafeiyu.ConversationSettingsPanel");Check(type!=null,"conversation settings page is missing");
                    var panel=(UserControl)Activator.CreateInstance(type,new object[]{pet});host.Content=panel;host.UpdateLayout();
                    var select=type.GetMethod("SelectSection",Members);select.Invoke(panel,new object[]{1});host.UpdateLayout();Control<CheckBox>(panel,"PlainTextEditor").IsChecked=true;
                    select.Invoke(panel,new object[]{2});host.UpdateLayout();Check(!Control<ComboBox>(panel,"SendShortcut").IsVisible&&Control<CheckBox>(panel,"ReadReplies").IsVisible,"tabs do not change actual content");Control<CheckBox>(panel,"ReadReplies").IsChecked=false;
                    select.Invoke(panel,new object[]{0});host.UpdateLayout();Check(Control<ComboBox>(panel,"DefaultConversationMode").IsVisible&&!Control<CheckBox>(panel,"PlainTextEditor").IsVisible,"defaults tab exposes another section");
                    select.Invoke(panel,new object[]{1});host.UpdateLayout();Check(Control<CheckBox>(panel,"PlainTextEditor").IsChecked.Value&&(bool)type.GetProperty("HasUnsavedChanges").GetValue(panel,null),"changing tabs lost the input draft");
                    Check((bool)type.GetMethod("Save").Invoke(panel,null)&&pet.Store.Load().PlainTextEditor&&!pet.Store.Load().ReadReplies,"saving one tab did not save the shared conversation draft");
                });
                Run("project global defaults populate a draft while preserving identity and directory",delegate{
                    var project=ProjectCatalogStore.CreateProject(pet.Projects,"Project identity",root);project.Goal="project goal";
                    typeof(PetWindow).GetField("editingProject",Members).SetValue(pet,project);cfg.AgentGoal="global goal";cfg.AgentCommandTimeoutSeconds=135;cfg.AgentApprovalMode=AgentApprovalMode.Legacy;cfg.AgentFullAccess=false;
                    var panel=new WorkSettingsPanel(pet);host.Content=panel;host.UpdateLayout();Control<TextBox>(panel,"ProjectName").Text="project draft name";Control<TextBox>(panel,"AgentWorkspace").Text=root;
                    Control<Button>(panel,"UseGlobalWorkDefaults").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));TextResources.ApplyLanguage(TextResources.PrepareLanguage("zh_CN"));
                    Check(Control<TextBox>(panel,"AgentGoal").Text=="global goal"&&Control<TextBox>(panel,"AgentCommandTimeoutSeconds").Text=="135","global values not copied into draft");
                    Check(Control<TextBox>(panel,"ProjectName").Text=="project draft name"&&Control<TextBox>(panel,"AgentWorkspace").Text==root&&project.Name=="Project identity"&&project.Goal=="project goal"&&panel.HasUnsavedChanges,"defaults changed project identity, directory or live configuration");
                    Check(!All<Button>(panel).Any(b=>b.Name=="WorkSettingsArchivedTab"),"archives remain a work configuration tab");
                    Check(panel.Save()&&project.ApprovalMode==AgentApprovalMode.Legacy&&!project.FullAccess&&project.Name=="project draft name"&&project.Workspace==root,"saving defaults changed legacy permission semantics or project identity");
                    Control<TextBox>(panel,"AgentGoal").Text="unsaved project goal";var beforeRestore=File.ReadAllBytes(Path.Combine(root,"settings.json"));Control<Button>(panel,"RestoreWorkSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(Control<TextBox>(panel,"AgentGoal").Text=="global goal"&&!panel.HasUnsavedChanges&&File.ReadAllBytes(Path.Combine(root,"settings.json")).SequenceEqual(beforeRestore),"project restore did not discard its local draft independently of global settings");
                    typeof(PetWindow).GetField("editingProject",Members).SetValue(pet,null);
                });
                Console.WriteLine("Settings separation: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
            }finally{if(host!=null)host.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
