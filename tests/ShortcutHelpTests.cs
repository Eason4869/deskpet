using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class ShortcutHelpTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject node){yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,i)))yield return child;}
        static string[] Titles(Window dialog){return Walk(dialog).OfType<TextBlock>().Where(t=>t.Name=="ShortcutCommandTitle").Select(t=>t.Text).ToArray();}
        static object Command(Type type,string group,string title,string gesture)
        {var command=Activator.CreateInstance(type,true);var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;type.GetField("Group",flags).SetValue(command,group);type.GetField("Title",flags).SetValue(command,title);type.GetField("GestureLabel",flags).SetValue(command,gesture);return command;}
        [STAThread]public static int Main(string[] args)
        {
            Application app=null;Window owner=null,dialog=null;
            try
            {
                var assembly=typeof(PetWindow).Assembly;var dialogType=assembly.GetType("Dafeiyu.ShortcutHelpDialog");
                Check(dialogType!=null && typeof(Window).IsAssignableFrom(dialogType),"shortcut reference dialog exists");if(args.Contains("--contract-only"))return 0;
                var commandType=assembly.GetType("Dafeiyu.ShellCommand");Check(commandType!=null,"shortcut reference consumes real shell commands");
                var commands=Array.CreateInstance(commandType,4);commands.SetValue(Command(commandType,"文件","新聊天","Ctrl+N"),0);commands.SetValue(Command(commandType,"文件","关于",null),1);commands.SetValue(Command(commandType,"编辑","粘贴","Ctrl+V"),2);commands.SetValue(Command(commandType,"视图","新建标签页","Ctrl+Shift+B"),3);
                app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};owner=new Window{Width=1000,Height=800};owner.Show();
                dialog=(Window)Activator.CreateInstance(dialogType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{owner,commands},null);dialog.Show();Pump();
                Check(Object.ReferenceEquals(dialog.Owner,owner) && dialog.WindowStyle==WindowStyle.None && !dialog.ShowInTaskbar,"shortcut overlay uses actual owner and client styling");
                Check(Titles(dialog).SequenceEqual(new[]{"新聊天","粘贴","新建标签页"}),"only commands with actual gesture labels appear in group order");
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));string preview=Path.Combine(Directory.GetCurrentDirectory(),"work","unified-shortcuts-dialog.png");using(var output=File.Create(preview))png.Save(output);Console.WriteLine("Preview: "+preview);
                var search=Walk(dialog).OfType<TextBox>().Single(t=>t.Name=="ShortcutSearch");search.Text="粘贴";Pump();Check(Titles(dialog).SequenceEqual(new[]{"粘贴"}),"search filters by command title");
                search.Text="ctrl+shift+b";Pump();Check(Titles(dialog).SequenceEqual(new[]{"新建标签页"}),"search matches gestures without case sensitivity");
                search.Text="文件";Pump();Check(Titles(dialog).SequenceEqual(new[]{"新聊天"}),"search filters by group");
                search.Text="no-such-shortcut";Pump();Check(Titles(dialog).Length==0 && Walk(dialog).OfType<TextBlock>().Any(t=>t.Name=="ShortcutEmptyState" && t.IsVisible),"empty results offer a clear visible state");
                search.Clear();Pump();Check(Titles(dialog).Length==3,"clearing search restores all supported shortcuts");
                var scroll=Walk(dialog).OfType<ScrollViewer>().Single(t=>t.Name=="ShortcutScroll");Check(scroll.Focusable && scroll.Focus(),"shortcut list can receive keyboard scrolling focus");
                Check(Walk(dialog).OfType<Button>().Any(b=>b.IsCancel),"dialog provides a cancel button for Escape");
                var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(dialog),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};dialog.RaiseEvent(key);Pump();Check(!dialog.IsVisible,"Escape closes the shortcut overlay");
                Console.WriteLine("Shortcut help tests: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}
            finally{if(dialog!=null)dialog.Close();if(owner!=null)owner.Close();if(app!=null)app.Shutdown();}
        }
    }
}
