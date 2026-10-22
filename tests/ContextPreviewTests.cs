using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ContextPreviewTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try
            {
                var cfg=new PetSettings{AgentEnabled=false,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(Directory.GetCurrentDirectory(),"work","context-preview-"+Guid.NewGuid().ToString("N"),"settings.json")),null);
                foreach(bool quick in new[]{false,true})
                {
                    Window host=quick?(Window)new QuickChatWindow(pet):new ChatWindow(pet);host.Show();Pump();
                    var preview=All<Button>(host).Single(b=>b.Name=="ContextUsagePreview");var tip=(ToolTip)preview.ToolTip;
                    Click(preview);Check(tip.IsOpen,"click opens context preview "+quick);
                    preview.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});Pump();Check(tip.IsOpen,"pressing preview itself preserves click toggle "+quick);
                    var outside=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent};host.RaiseEvent(outside);Pump();Check(!outside.Handled,"outside click is not consumed "+quick);
                    Check(!tip.IsOpen,"outside click closes preview immediately "+quick);
                    Click(preview);Click(preview);Check(!tip.IsOpen,"second click still toggles preview closed "+quick);
                    Click(preview);host.Hide();Pump();Check(!tip.IsOpen,"hiding host closes preview "+quick);
                    host.Show();Pump();Click(preview);host.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(host),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});Pump();Check(!tip.IsOpen,"Escape closes preview "+quick);
                    Click(preview);var composer=(ConversationComposer)host.GetType().GetField("composer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(host);composer.Rebind(pet.Conversation,pet.InputQueue);Pump();Check(!tip.IsOpen,"rebinding closes old context preview "+quick);
                    Click(preview);host.Close();Pump();Check(!tip.IsOpen,"closing host closes preview "+quick);
                }
                Console.WriteLine("Context preview tests: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{foreach(Window window in app.Windows.Cast<Window>().ToArray())window.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
