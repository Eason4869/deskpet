using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class ProviderMemoryTests
    {
        static int checks;static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        [MethodImpl(MethodImplOptions.NoInlining)]static WeakReference SetUnrelatedDraft(PetSettings settings,out TextProviderEditor editor)
        {
            string unrelated=new string('x',100000);var reference=new WeakReference(unrelated);settings.AgentGlobalInstructions=unrelated;
            editor=new TextProviderEditor(settings);settings.AgentGlobalInstructions="";return reference;
        }
        static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);}
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Window host=null;
            try
            {
                var cfg=new PetSettings{Text=new ConnectionSettings{BaseUrl="https://example.com/v1",Model="fixture"}};cfg.Normalize();
                host=new Window();Ui.Window(host,"memory fixture",960,700);TextProviderEditor editor;var unrelated=SetUnrelatedDraft(cfg,out editor);
                // Controlled collection verifies reachability only, never performance or retained-byte savings.
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                Check(!unrelated.IsAlive,"provider baseline does not retain unrelated global instruction buffer");
                host.Content=editor;host.Show();Pump();
                var name=All<TextBox>(editor).Single(x=>x.Name=="TextProviderName");
                cfg.TextProviders[0].Name="external saved name";Check(editor.RefreshSaved(cfg)&&name.Text=="external saved name","clean provider accepts external saved configuration");
                name.Text="local draft";cfg.TextProviders[0].Name="new external name";
                Check(!editor.RefreshSaved(cfg)&&name.Text=="local draft","detached lightweight baseline preserves local edits");
                GC.KeepAlive(editor);Console.WriteLine("Provider memory: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.Error.WriteLine("FAIL "+error);return 1;}
            finally{if(host!=null)host.Close();app.Shutdown();}
        }
    }
}
