using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests {
 public static class PopupLifecycleTests {
  static int checks;static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
  static IEnumerable<T> All<T>(DependencyObject p)where T:DependencyObject{if(p is T)yield return(T)p;for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(p,i)))yield return child;}
  static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));}
  [STAThread]public static int Main(){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Window window=null;ProjectSidebar sidebar=null;var errors=new List<Exception>();app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){errors.Add(e.Exception);e.Handled=true;};try{
   string root=Path.Combine(Directory.GetCurrentDirectory(),"work","popup-lifecycle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var catalog=new ProjectCatalog();var a=ProjectCatalogStore.CreateProject(catalog,"First project",root);var b=ProjectCatalogStore.CreateProject(catalog,"Second project",root);
   sidebar=new ProjectSidebar(new ProjectCatalogStore(Path.Combine(root,"projects.json")),catalog,p=>{},(p,c)=>{},p=>{},p=>{},p=>{});window=new Window{Content=sidebar,Width=340,Height=740,Left=-10000,Top=0,ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};window.Show();Pump();var first=All<Button>(sidebar).First(x=>Object.Equals(x.Tag,a.Id));var second=All<Button>(sidebar).First(x=>Object.Equals(x.Tag,b.Id));var show=typeof(ProjectSidebar).GetMethod("ShowHover",BindingFlags.Instance|BindingFlags.NonPublic);
   for(int round=0;round<20;round++){
    show.Invoke(sidebar,new object[]{first,a});Pump();var retired=sidebar.HoverDetails;var original=retired.Child;show.Invoke(sidebar,new object[]{second,b});var current=sidebar.HoverDetails;
    var interest=typeof(Popup).GetMethod("GetChildInterestPoints",BindingFlags.Instance|BindingFlags.NonPublic);if(interest!=null){try{interest.Invoke(retired,new object[]{PlacementMode.Right});}catch(TargetInvocationException e){Console.WriteLine("REPRO "+e.InnerException.GetType().FullName+"\n"+e.InnerException.StackTrace);throw;}}
    Check(!retired.IsOpen&&Object.ReferenceEquals(retired.Child,original)&&!Object.ReferenceEquals(current,retired),"retired popup keeps its visual child while a separate popup opens "+round);current.Child.Measure(new Size(280,900));current.Child.Arrange(new Rect(0,0,280,180+round));window.Width=340+round%2;window.UpdateLayout();Pump();Check(current.IsOpen&&errors.Count==0,"rapid hover replacement and native resize have no dispatcher exception "+round);
   }
   sidebar.Dispose();Pump();Check(!sidebar.HoverDetails.IsOpen&&errors.Count==0,"disposing sidebar closes its popup before detaching rows");window.Content=null;Pump();Check(errors.Count==0,"detaching sidebar after popup close is safe");Console.WriteLine("Popup lifecycle tests: "+checks+" passed");return 0;
  }catch(Exception e){Console.Error.WriteLine(e.GetType().FullName+" "+e.Message+"\n"+e.StackTrace);return 1;}finally{if(sidebar!=null)sidebar.Dispose();if(window!=null)window.Close();app.Shutdown();}}
 }
}
