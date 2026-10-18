using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace Dafeiyu.Tests
{
    public static class ConversationSwitchRegressionTests
    {
        static object Get(object value,string name){var t=value.GetType();var p=t.GetProperty(name);return p!=null?p.GetValue(value,null):t.GetField(name).GetValue(value);}
        static void Set(object value,string name,object data){var t=value.GetType();var p=t.GetProperty(name);if(p!=null)p.SetValue(value,data,null);else t.GetField(name).SetValue(value,data);}
        static object Invoke(object value,string name,params object[] args){try{return value.GetType().GetMethods().Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(value,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var x in All<T>(VisualTreeHelper.GetChild(root,i)))yield return x;}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        [StructLayout(LayoutKind.Sequential)]struct PointI{public int X,Y;public PointI(int x,int y){X=x;Y=y;}}
        [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
        [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(PointI point);
        [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
        static void StorageFailureDoesNotBlockBrowsing(string root)
        {
            string blocked=Path.Combine(root,"blocked-catalog");Directory.CreateDirectory(blocked);
            var catalog=new ProjectCatalog();var record=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="已有对话",HasUnreadCompletion=true};catalog.UnassignedConversations.Add(record);
            bool opened=false;var sidebar=new ProjectSidebar(new ProjectCatalogStore(blocked),catalog,p=>{},(p,c)=>opened=true,p=>{},p=>{},p=>{});
            typeof(ProjectSidebar).GetMethod("OpenConversation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(sidebar,new object[]{ProjectCatalogStore.UnassignedProject(catalog,new PetSettings()),record});
            if(!opened||!record.HasUnreadCompletion)throw new Exception("sidebar must delegate browsing without writing or clearing unread metadata");
            Console.WriteLine("Read-only sidebar navigation passed");
        }
        static void NativeClick(Button button,Window owner){owner.Topmost=true;owner.Activate();button.BringIntoView();Pump();var point=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));SetCursorPos((int)point.X,(int)point.Y);Pump();if(GetAncestor(WindowFromPoint(new PointI((int)point.X,(int)point.Y)),2)!=new WindowInteropHelper(owner).Handle)throw new Exception("native click target is not the owned fixture");mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);}
        [STAThread]public static int Main(string[] args)
        {
            SetProcessDPIAware();var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var errors=new List<Exception>();app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){errors.Add(e.Exception);e.Handled=true;};object pet=null;
            if(args.Length==0){string storageRoot=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","storage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(storageRoot);try{StorageFailureDoesNotBlockBrowsing(storageRoot);}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
            try{
                var assembly=args.Length==0?typeof(PetWindow).Assembly:Assembly.LoadFile(Path.GetFullPath(args[0]));string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","switch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                if(args.Length>1){File.Copy(Path.Combine(args[1],"projects.json"),Path.Combine(root,"projects.json"));foreach(string directory in new[]{"conversations","projects","history"}){string from=Path.Combine(args[1],directory);if(!Directory.Exists(from))continue;foreach(string source in Directory.EnumerateFiles(from,"*.json",SearchOption.AllDirectories)){if(Path.GetFileName(source)=="queued-messages.json"||new FileInfo(source).Length>64*1024*1024)continue;string target=Path.Combine(root,directory,source.Substring(from.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target);}}}
                bool cloneWork=args.Length>2&&args[2]=="work";
                var cfg=Activator.CreateInstance(assembly.GetType("Dafeiyu.PetSettings"));foreach(string name in new[]{"AutoUpdate","GreetingEnabled","ShowPredictedMessages","ReadReplies"})Set(cfg,name,false);Set(cfg,"SaveChatHistory",true);Set(cfg,"OnboardingState","completed");Set(cfg,"AgentWorkspace",args.Length>3?args[3]:root);Set(cfg,"AgentEnabled",true);var keys=(IDictionary)Get(cfg,"Hotkeys");foreach(string key in keys.Keys.Cast<string>().ToArray())keys[key]="";
                if(args.Length>1){var sourceSettings=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[1],"settings.json"))) as Dictionary<string,object>;object skin;if(sourceSettings.TryGetValue("SkinId",out skin)&&skin is string)Set(cfg,"SkinId",skin);}
                var connection=Get(cfg,"Text");Set(connection,"BaseUrl","http://127.0.0.1:1");Set(connection,"Model","fixture");
                var store=Activator.CreateInstance(assembly.GetType("Dafeiyu.SettingsStore"),new object[]{Path.Combine(root,"settings.json")});pet=Activator.CreateInstance(assembly.GetType("Dafeiyu.PetWindow"),new object[]{null,cfg,store,null});
                var catalog=Get(pet,"Projects");var definitions=(IList)Get(catalog,"UnassignedConversations");var type=assembly.GetType("Dafeiyu.ConversationDefinition");var ids=new List<string>();
                if(args.Length>1){definitions=(IList)Get(catalog,cloneWork?"UnassignedConversations":"QuickConversations");string petId=(string)Get(Get(pet,"Skin"),"Id");foreach(var d in definitions)if(cloneWork||(string)Get(d,"PetId")==petId)ids.Add((string)Get(d,"Id"));}else for(int i=0;i<3;i++){var d=Activator.CreateInstance(type);string id=Guid.NewGuid().ToString("N");Set(d,"Id",id);Set(d,"Name","保存的对话 "+i);Set(d,"Mode","work");definitions.Add(d);ids.Add(id);}
                var project=assembly.GetType("Dafeiyu.ProjectCatalogStore").GetMethod(args.Length>1&&!cloneWork?"QuickProject":"UnassignedProject").Invoke(null,new object[]{catalog,cfg});Invoke(Get(pet,"ProjectStore"),"Save",catalog);Invoke(pet,"OpenProjectConversation",project,definitions.Cast<object>().First(d=>ids.Contains((string)Get(d,"Id"))));Pump();var chat=app.Windows.OfType<Window>().Single(w=>w.GetType().FullName=="Dafeiyu.ChatWindow");chat.Width=1320;chat.Height=860;chat.UpdateLayout();Pump();
                int hoverCount=0;for(int round=0;round<12;round++)foreach(string id in ids){var button=All<Button>(chat).FirstOrDefault(b=>b.Name.EndsWith("_"+id,StringComparison.Ordinal)&&b.IsVisible);if(button==null)throw new Exception("saved sidebar conversation button missing: "+id);button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0){RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent});var sidebar=All<ProjectSidebar>(chat).FirstOrDefault(s=>s.IsVisible);if(sidebar!=null&&sidebar.HoverDetails.IsOpen)hoverCount++;Pump();if(args.Contains("--native"))NativeClick(button,chat);else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();if(errors.Count>0)throw new AggregateException(errors);if((string)Get(chat,"ConversationId")!=id)throw new Exception("sidebar click did not activate saved conversation");}
                if(hoverCount==0)throw new Exception("hovered conversation transition was never exercised");foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();Pump();if(errors.Count>0)throw new AggregateException(errors);Console.WriteLine("Saved sidebar switching: "+(ids.Count*12)+" clicks passed with "+hoverCount+" open hover transitions; version="+assembly.GetName().Version);return 0;
            }catch(Exception e){while(e!=null){Console.Error.WriteLine(e.GetType().FullName+"\n"+e.StackTrace);if(e.GetType()==typeof(Exception))Console.Error.WriteLine(e.Message);e=e.InnerException;}return 1;}finally{foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();app.Shutdown();}
        }
    }
}
