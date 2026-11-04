using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ComposerPaletteTests
    {
        static int passed,failed;static readonly BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        static void Run(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var x in All<T>(VisualTreeHelper.GetChild(root,i)))yield return x;}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        [STAThread] public static int Main(){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var cfg=new PetSettings{AgentEnabled=false,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";var pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"palette-"+Guid.NewGuid().ToString("N"),"settings.json")),null);var chat=new ChatWindow(pet);chat.Show();Pump();var input=All<MarkdownEditor>(chat).Single(t=>t.Name=="MessageInput");var composer=typeof(ChatWindow).GetField("composer",hidden).GetValue(chat);var type=composer.GetType();var popup=(Popup)type.GetField("triggerPopup",hidden).GetValue(composer);
            try{
                Run("hidden quick chat never opens a palette when the shared full-chat draft changes",delegate{
                    var quick=new QuickChatWindow(pet);try{
                        quick.Show();Pump();var quickInput=All<MarkdownEditor>(quick).Single();quickInput.Focus();quickInput.Text="@";quickInput.CaretIndex=1;Pump();
                        var quickComposer=typeof(QuickChatWindow).GetField("composer",hidden).GetValue(quick);var quickPopup=(Popup)type.GetField("triggerPopup",hidden).GetValue(quickComposer);
                        Check(quickPopup.IsOpen,"visible quick-chat palette did not open");quick.Hide();chat.Activate();input.Focus();Pump();
                        Check(!quickPopup.IsOpen,"hiding quick chat left its palette open");
                        foreach(string trigger in new[]{"/","@"}){input.Text="";input.Text=trigger;input.CaretIndex=1;Pump();Check(popup.IsOpen,"full-chat palette did not open for "+trigger);Check(!quickPopup.IsOpen,"shared draft opened the hidden quick-chat palette for "+trigger);Check(quickInput.Text==trigger,"shared draft stopped synchronizing");}
                    }finally{quick.Close();input.Text="";input.Focus();Pump();}
                });
                Run("slash menu is grouped, scrollable and bounded by the composer",delegate{input.Text="/";input.CaretIndex=input.Text.Length;Pump();Check(popup.IsOpen&&All<ScrollViewer>(popup.Child).Any(s=>s.MaxHeight<=400),"command menu is not bounded/scrollable");Check(All<TextBlock>(popup.Child).Any(t=>t.Name=="ComposerCommandGroup"),"command groups missing");Check(((FrameworkElement)popup.Child).Width<=chat.ActualWidth-20,"menu exceeds chat width");});
                Run("mouse command removes only its trigger fragment and preserves the draft",delegate{input.Text="草稿 /导出记录 后缀";input.CaretIndex="草稿 /导出记录".Length;Pump();var rows=(System.Collections.IList)type.GetField("triggerRows",hidden).GetValue(composer);Check(rows.Count==1,"filter did not narrow to export");((Border)rows[0]).Child.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});Check(!input.Text.Contains("/导出记录")&&input.Text.StartsWith("草稿")&&input.Text.EndsWith("后缀"),"mouse command left trigger text or lost draft");});
                Run("slash side-chat command opens a temporary pane and preserves surrounding draft",delegate{input.Text="草稿 /侧边 后缀";input.CaretIndex="草稿 /侧边".Length;Pump();var rows=(System.Collections.IList)type.GetField("triggerRows",hidden).GetValue(composer);Check(rows.Count==1,"slash menu lacks the side-chat command");((Border)rows[0]).Child.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});Pump();Check(All<SideChatPane>(chat).Count()==1&&input.Text.StartsWith("草稿")&&input.Text.EndsWith("后缀")&&!input.Text.Contains("/侧边"),"side-chat command did not open its pane or preserve the draft");});
                Run("unavailable snapshot is disabled rather than executing a blank action",delegate{type.GetField("SnapshotTargetProvider",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(composer,(Func<ApplicationSnapshotTarget>)(()=>null));input.Text="@快照";input.CaretIndex=input.Text.Length;Pump();Check(All<FrameworkElement>(popup.Child).Any(e=>e.Name=="ComposerCommand"&&!e.IsEnabled),"unavailable snapshot appears enabled");});
                Run("chat header omits removed clear and export buttons; export remains in the file command",delegate{Check(!All<Button>(chat).Any(b=>b.Name=="ClearChat")&&!All<Button>(chat).Any(b=>b.Name=="ExportChat"&&b.IsVisible)&&All<Button>(chat).Any(b=>b.Name=="AppMenuFile"),"header actions do not match requested scope");});
            }finally{chat.Close();pet.Close();app.Shutdown();}Console.WriteLine("Composer palette tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
    }
}
