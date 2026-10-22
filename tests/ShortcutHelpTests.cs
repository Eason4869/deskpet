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
        static void ShortcutConfiguration()
        {
            var catalog=typeof(PetWindow).Assembly.GetType("Dafeiyu.ShortcutCatalog");
            Check(catalog!=null,"editable shortcut catalog exists");
            HotkeyCombination digit;Check(HotkeyCombination.TryParse("Ctrl+1",out digit)&&digit.Key==49,"natural digit shortcuts parse without accepting numeric enums");
            var settings=new PetSettings();
            var local=typeof(PetSettings).GetProperty("LocalShortcuts");Check(local!=null,"local shortcut persistence exists");
            var values=(System.Collections.Generic.Dictionary<string,string>)local.GetValue(settings,null);
            Check(values["newchat"]=="Ctrl+N" && values["help"]=="Ctrl+OemQuestion","stable shell defaults");
            values["newchat"]="Ctrl+Shift+F12";values["help"]="";
            var copy=settings.Copy();Check(((System.Collections.Generic.Dictionary<string,string>)local.GetValue(copy,null))["newchat"]=="Ctrl+Shift+F12","copy preserves customized local shortcut");
            settings.Normalize();Check(values["help"]=="","normalization preserves disabled local shortcut");
            var validate=catalog.GetMethod("Validate",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            object[] arguments={settings,"newchat","Ctrl+C",null};Check(!(bool)validate.Invoke(null,arguments),"editing shortcuts reserved");
            foreach(string value in new[]{"Alt+Up","Alt+Down","Ctrl+Alt+Shift+Up"}){arguments=new object[]{settings,"newchat",value,null};Check(!(bool)validate.Invoke(null,arguments),"fixed queue reorder shortcuts remain reserved: "+value);}
            arguments=new object[]{settings,"newchat","Shift+N",null};Check(!(bool)validate.Invoke(null,arguments),"ordinary capital typing cannot become command shortcut");
            arguments=new object[]{settings,"newchat","Ctrl+Left",null};Check(!(bool)validate.Invoke(null,arguments),"word navigation remains available for editing");
            arguments=new object[]{settings,"newchat","Ctrl+Alt+Space",null};Check(!(bool)validate.Invoke(null,arguments),"local keys reject global collision");
            arguments=new object[]{settings,"newchat","Control+Shift+N",null};Check(!(bool)validate.Invoke(null,arguments),"invalid modifier rejected");
            arguments=new object[]{settings,"newchat","Ctrl+O",null};Check(!(bool)validate.Invoke(null,arguments),"duplicate local key rejected");
            arguments=new object[]{settings,"newchat","Ctrl+Shift+F11",null};Check((bool)validate.Invoke(null,arguments),"unused modified key accepted");
            arguments=new object[]{settings,"newchat","",null};Check((bool)validate.Invoke(null,arguments),"blank disables local shortcut");
            var match=catalog.GetMethod("IsMatch",BindingFlags.Static|BindingFlags.NonPublic);Check(match!=null,"runtime matcher accepts explicit key and modifiers");
            Check((bool)match.Invoke(null,new object[]{values["newchat"],Key.F12,ModifierKeys.Control|ModifierKeys.Shift}),"custom local shortcut matches actual key and modifiers");
            Check(!(bool)match.Invoke(null,new object[]{values["newchat"],Key.N,ModifierKeys.Control}),"old local shortcut stops matching after edit");
            Check(!(bool)match.Invoke(null,new object[]{"",Key.F12,ModifierKeys.Control|ModifierKeys.Shift}),"disabled shortcut does not match key event");
            Check(!(bool)match.Invoke(null,new object[]{"Ctrl+1",Key.D1,ModifierKeys.Control|ModifierKeys.Alt}),"extra modifiers do not accidentally dispatch shortcut");
            Check((bool)match.Invoke(null,new object[]{"Ctrl+,",Key.OemComma,ModifierKeys.Control}),"punctuation alias matches WPF key");
        }
        static void ShortcutEditor()
        {
            string folder=Path.Combine(Directory.GetCurrentDirectory(),"work","shortcut-editor-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            var store=new SettingsStore(Path.Combine(folder,"settings.json"));var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false,OnboardingState="completed"};
            PetWindow pet=null;Window host=null;
            try
            {
                pet=new PetWindow(null,cfg,store,null);var panel=new ShortcutSettingsPanel(pet);host=new Window{Width=700,Height=650,Content=panel};host.Show();Pump();
                var fields=Walk(panel).OfType<TextBox>().Where(t=>t.Name.StartsWith("Hotkey")).ToArray();Check(fields.Length==33,"editor includes global shell and menu actions");
                Check(fields.Single(t=>t.Name=="Hotkeyquick").Text=="Ctrl+Alt+Space","onboarding global input name retained");
                var guide=fields.Single(t=>t.Name=="Hotkeyquick");guide.Text="Alt+Ctrl+F12";Check(panel.HasUnsavedChanges,"guide shortcut edit marks a real draft");string guideError;Check(pet.UpdateHotkey("quick",guide.Text,out guideError),"guide saves through the existing hotkey path");Check(!panel.HasUnsavedChanges,"a shortcut already saved by the guide stops blocking other settings immediately");panel.RefreshState();Check(guide.Text=="Ctrl+Alt+F12"&&!panel.HasUnsavedChanges,"refresh synchronizes externally saved shortcut baseline and display");
                var local=fields.Single(t=>t.Name=="Hotkeynewchat");local.Text="Ctrl+Shift+F11";Check(panel.HasUnsavedChanges,"local draft marked unsaved");panel.RefreshState();Check(local.Text=="Ctrl+Shift+F11"&&panel.HasUnsavedChanges,"external refresh preserves shortcut draft");
                Walk(panel).OfType<Button>().Single(b=>b.Name=="SaveShortcutnewchat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(pet.Settings.LocalShortcuts["newchat"]=="Ctrl+Shift+F11"&&!panel.HasUnsavedChanges,"save applies local shortcut immediately");
                local.Text="Ctrl+O";Walk(panel).OfType<Button>().Single(b=>b.Name=="SaveShortcutnewchat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(pet.Settings.LocalShortcuts["newchat"]=="Ctrl+Shift+F11"&&local.Text=="Ctrl+O"&&panel.HasUnsavedChanges,"duplicate rejects without losing active binding or draft");
                local.Clear();Walk(panel).OfType<Button>().Single(b=>b.Name=="SaveShortcutnewchat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(pet.Settings.LocalShortcuts["newchat"]=="","blank save disables local action");
                var loaded=store.Load();Check(loaded.LocalShortcuts["newchat"]=="","disabled local shortcut persists on disk");
                Walk(panel).OfType<Button>().Single(b=>b.Name=="ResetShortcutnewchat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(local.Text=="Ctrl+N"&&store.Load().LocalShortcuts["newchat"]=="Ctrl+N","restore default applies and persists");
                local.Text="Ctrl+Shift+F11";Walk(panel).OfType<Button>().Single(b=>b.Name=="SaveShortcutnewchat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pet.ShowChat();Pump();var chat=Application.Current.Windows.OfType<ChatWindow>().Single();var commands=(System.Collections.IEnumerable)typeof(ChatWindow).GetField("shellCommands",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chat);
                var shellEntries=commands.Cast<object>().Cast<ShellCommand>().ToArray();Check(shellEntries.Count(c=>c.ShortcutId!=null)==25&&shellEntries.Where(c=>c.ShortcutId==null).All(c=>c.Group=="编辑"&&c.Gesture==null),"every application action maps to an editable shortcut and only built-in editing stays fixed");
                typeof(ChatWindow).GetMethod("RefreshShellShortcuts",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chat,null);var command=commands.Cast<object>().Single(c=>(string)c.GetType().GetField("ShortcutId",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c)=="newchat");
                Check((string)command.GetType().GetField("GestureLabel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(command)=="Ctrl+Shift+F11","existing shell help and menu labels resolve current saved settings");
                string error;Check(pet.UpdateHotkey("newchat","Ctrl+Shift+F10",out error),"editing local shortcut accepted while chat is open");typeof(ChatWindow).GetMethod("RefreshShellShortcuts",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chat,null);
                Check((string)command.GetType().GetField("GestureLabel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(command)=="Ctrl+Shift+F10","already open shell refreshes label after rebinding");
                foreach(var window in Application.Current.Windows.OfType<ChatWindow>().ToArray())window.Close();
            }
            finally{if(host!=null)host.Close();if(pet!=null)pet.Close();Pump();}
        }
        [STAThread]public static int Main(string[] args)
        {
            Application app=null;Window owner=null,dialog=null;
            try
            {
                var assembly=typeof(PetWindow).Assembly;var dialogType=assembly.GetType("Dafeiyu.ShortcutHelpDialog");
                ShortcutConfiguration();
                Check(dialogType!=null && typeof(Window).IsAssignableFrom(dialogType),"shortcut reference dialog exists");if(args.Contains("--contract-only"))return 0;
                var commandType=assembly.GetType("Dafeiyu.ShellCommand");Check(commandType!=null,"shortcut reference consumes real shell commands");
                var commands=Array.CreateInstance(commandType,4);commands.SetValue(Command(commandType,"文件","新聊天","Ctrl+N"),0);commands.SetValue(Command(commandType,"文件","关于",null),1);commands.SetValue(Command(commandType,"编辑","粘贴","Ctrl+V"),2);commands.SetValue(Command(commandType,"视图","新建标签页","Ctrl+Shift+B"),3);
                app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};ShortcutEditor();owner=new Window{Width=1000,Height=800};owner.Show();
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
