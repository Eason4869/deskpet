using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class SettingsOrganizationTests
    {
        static int checks;
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        static T Named<T>(DependencyObject root,string name)where T:FrameworkElement{return All<T>(root).Single(e=>e.Name==name);}
        static void Capture(Window window,string name){window.UpdateLayout();var view=(FrameworkElement)window.Content;var png=new PngBitmapEncoder();var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name+".png")))png.Save(stream);}
        [STAThread] public static int Main()
        {
            TextResources.ConfigureStartupLanguage("zh_CN");var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window host=null;SettingsPage page=null;
            try{
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-organization-"+Guid.NewGuid().ToString("N"));var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",AgentWorkspace=Path.Combine(folder,"workspace")};Directory.CreateDirectory(cfg.AgentWorkspace);foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);page=new SettingsPage(pet);host=new Window{Content=page,Width=1100,Height=800};host.Show();Pump();
                var navigation=Named<ListBox>(page,"SettingsNavigation");var actual=navigation.Items.Cast<ListBoxItem>().Select(i=>(string)i.Content).ToArray();
                Check(actual.SequenceEqual(new[]{"伙伴","模型配置","对话与输入","工作与权限","工具与连接","通用"}),"six settings categories have the approved labels and order");
                Check(Named<Button>(page,"SettingsAboutNavigation").IsVisible,"about and updates has a permanent footer entry");
                page.RestoreNavigation(6,0,null);Pump();Check(new[]{"ConversationDefaultsTab","ConversationInputTab","ConversationDisplayTab"}.All(n=>Named<Button>(page,n).IsVisible),"conversation settings offer three real content tabs");page.RestoreNavigation(6,1,null);Pump();var shortcut=Named<ComboBox>(page,"SendShortcut");int choice=shortcut.SelectedIndex==0?1:0;shortcut.SelectedIndex=choice;
                page.SelectNavigation(2);Pump();Check(!All<ComboBox>(page).Any(c=>c.Name=="SendShortcut"&&c.IsVisible),"work and permissions does not contain global input preferences");
                page.SelectNavigation(6);Pump();Check(Named<ComboBox>(page,"SendShortcut").SelectedIndex==choice&&page.HasUnsavedEdits,"navigation preserves the unsaved conversation draft");Capture(host,"settings-conversation-layout");
                page.SelectNavigation(1);Pump();Check(!All<CheckBox>(page).Any(c=>c.Name=="WebSearchEnabled"),"web search is removed from model configuration");Check(Named<Button>(page,"AiTextTab").IsVisible,"existing model tabs are retained");
                page.SelectNavigation(7);Pump();Check(Named<CheckBox>(page,"WebSearchEnabled").IsVisible,"web search lives under tools and connections");Capture(host,"settings-tools-layout");
                Check(!All<Button>(page).Any(b=>b.Name=="ToolsPluginsTab"),"tools does not duplicate the primary plugins page");page.RestoreNavigation(7,2,"ToolsChannelsTab");Pump();Check(Named<Button>(page,"ToolsChannelsTab").IsVisible&&page.SelectedSecondarySection==2,"message channels retain their navigation route");page.SelectNavigation(7);Pump();
                var search=Named<TextBox>(page,"SettingsCategorySearch");search.Text="网页搜索";search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(search),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});Pump();
                Check(page.SelectedNavigationIndex==7&&Named<CheckBox>(page,"WebSearchEnabled").IsKeyboardFocusWithin,"search locates and focuses a specific setting without enabling it");Check(!pet.Settings.WebSearch.Enabled,"search navigation did not execute the toggle");
                search.Clear();Named<Button>(page,"SettingsAboutNavigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(page.IsOnAboutPage,"footer about entry preserves the existing about route");
                host.Width=640;page.SelectNavigation(7);Pump();Capture(host,"settings-tools-narrow");Check(Named<CheckBox>(page,"WebSearchEnabled").ActualWidth>0,"tools settings remain usable in a narrow window");
                pet.ShowSettings();Pump();var main=app.Windows.OfType<ChatWindow>().Single(w=>w.IsVisible);main.Width=1280;main.Height=850;var integrated=All<SettingsPage>(main).Single();integrated.RestoreNavigation(6,1,null);Pump();
                Check(new[]{"ShellHome","ShellImages","ShellUsage","ShellMcp","ShellSettings"}.All(n=>Named<Button>(main,n).IsVisible),"the full application keeps every original primary navigation icon");Capture(main,"settings-full-window");Named<Button>(main,"ShellMcp").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(All<PluginsPage>(main).Any(p=>p.IsVisible),"skills and MCP remain available from the primary plugins icon");
                Console.WriteLine("Settings organization tests: "+checks+" passed");return 0;
            }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}finally{if(page!=null)page.Dispose();if(host!=null)host.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
