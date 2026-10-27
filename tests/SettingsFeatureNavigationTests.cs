using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu.Tests
{
    // Actual WPF textbox/list/route coverage. Runs only on the caller's owned desktop.
    public static class SettingsFeatureNavigationTests
    {
        static int checks;static Application app;static PetWindow pet;static bool layoutBoundary;static string phase="startup";
        static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);checks++;Console.WriteLine("PASS "+reason);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root==null)yield break;if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static T Named<T>(DependencyObject root,string name)where T:FrameworkElement{var element=root as FrameworkElement;if(element!=null){element.ApplyTemplate();element.UpdateLayout();}var matches=All<T>(root).Where(c=>c.Name==name).ToArray();if(matches.Length!=1)throw new Exception("Expected one "+typeof(T).Name+" "+name+" found "+matches.Length+" in "+root.GetType().Name);return matches[0];}
        static void Trace(string value){phase=value;using(var process=System.Diagnostics.Process.GetCurrentProcess())Console.WriteLine("NAVIGATION_PHASE "+phase+" private_bytes="+process.PrivateMemorySize64+" managed_bytes="+GC.GetTotalMemory(false));Console.Out.Flush();}
        static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static void Key(UIElement control,System.Windows.Input.Key key){control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(control),0,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});}
        [DllImport("user32.dll",EntryPoint="PostMessageW",SetLastError=true)]static extern bool PostMessage(IntPtr window,uint message,IntPtr key,IntPtr flags);
        static void WindowEnter(Window owner,UIElement control){control.Focus();Check(control.IsKeyboardFocused,"search has actual keyboard focus before owned-window Enter");IntPtr handle=new WindowInteropHelper(owner).Handle;Check(PostMessage(handle,0x0100,new IntPtr(13),new IntPtr(1))&&PostMessage(handle,0x0101,new IntPtr(13),new IntPtr(unchecked((int)0xC0000001))),"Enter key messages queued only to this fixture window");}
        static string Hash(string file){using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(file)));}
        static int HistoryCount(ChatWindow chat){var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;return ((System.Collections.ICollection)typeof(ChatWindow).GetField("appHistory",flags).GetValue(chat)).Count;}
        static void Capture(FrameworkElement element,string path){element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(path))encoder.Save(output);}
        static async Task Run(string locale)
        {
            TextResources.ConfigureStartupLanguage(locale);
            string root=Path.GetFullPath(Path.Combine("work","feature-navigation","wpf-"+locale+"-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            var cfg=new PetSettings{UiLanguage=locale,AutoUpdate=false,OnboardingState="completed",GreetingEnabled=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,SaveChatHistory=false,ReadReplies=false,AgentWorkspace=Path.Combine(root,"workspace")};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";cfg.Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="offline-fixture"};
            var store=new SettingsStore(Path.Combine(root,"data","settings.json"));store.Save(cfg);
            pet=new PetWindow(null,cfg,store,null);pet.ShowChat();await Task.Delay(130);
            var chat=app.Windows.OfType<ChatWindow>().Single();chat.Width=1100;chat.Height=800;
            var draft=Named<MarkdownEditor>(chat,"MessageInput");draft.Text="S3 unsent composer";
            pet.ShowSettings();await Task.Delay(100);var page=Named<SettingsPage>(chat,"SettingsPage");
            chat.UpdateLayout();page.UpdateLayout();var search=Named<TextBox>(page,"SettingsCategorySearch");var navigation=Named<ListBox>(page,"SettingsNavigation");
            string history=Path.Combine(root,"data","history","s3-navigation-sentinel.json");Directory.CreateDirectory(Path.GetDirectoryName(history));File.WriteAllText(history,"owned history must survive navigation");string cache=Path.Combine(root,"data","updates","s3-navigation-sentinel.bin");Directory.CreateDirectory(Path.GetDirectoryName(cache));File.WriteAllText(cache,"owned cache must survive navigation");
            await pet.FlushDeferredSettingsAsync();string settingsHash=Hash(Path.Combine(root,"data","settings.json"));
            string historyHash=Hash(history),cacheHash=Hash(cache);
            // First RED: the old category-title-only filter hides every destination.
            search.Text=TextResources.Get("cache.title");await Task.Delay(30);
            Check(((ListBoxItem)navigation.Items[4]).Visibility==Visibility.Visible,"localized cache search exposes the existing About destination");
            int count=HistoryCount(chat);WindowEnter(chat,search);await Task.Delay(100);
            Check(page.IsOnAboutPage&&Named<Button>(page,"ClearCache").IsVisible,"search Enter lands on actual cache entry");
            Check(HistoryCount(chat)==count+1,"feature activation produces exactly one history entry");
            Check(Named<Button>(page,"ClearCache").IsKeyboardFocusWithin,"first cache search focuses the real action without clicking");
            Check(!app.Windows.OfType<CacheMaintenanceWindow>().Any()&&!app.Windows.OfType<DataActionConfirmationWindow>().Any(),"search does not open cleanup or confirmation dialogs");
            search.Text=(string)((ListBoxItem)navigation.Items[0]).Content;Key(search,System.Windows.Input.Key.Enter);await Task.Delay(30);Check(chat.CurrentPage=="settings"&&page.SelectedNavigationIndex==0,"exact category title keeps ordinary companion settings route");
            search.Text="";page.SelectAiSection(0);if(!layoutBoundary)await Task.Delay(40);var model=Named<TextBox>(page,"TextProviderName");string modelBefore=model.Text;model.Text="S3 unsaved provider";
            page.SelectGeneralSection(1);await Task.Delay(40);var shortcut=Named<TextBox>(page,"Hotkeytab");string shortcutBefore=shortcut.Text;shortcut.Text="Ctrl+Alt+Shift+F12";
            string[] labels={TextResources.Get("dataManagement.title"),TextResources.Get("dataManagement.clearHistory"),TextResources.Get("dataManagement.restorePreferences")};
            string[] targets={"GeneralHistoryTab","ClearAllHistory","RestoreDefaultPreferences"};
            for(int i=0;i<labels.Length;i++){
                count=HistoryCount(chat);search.Text=labels[i];Key(search,System.Windows.Input.Key.Enter);await Task.Delay(40);
                Check(Named<FrameworkElement>(page,targets[i]).IsVisible&&Named<Button>(page,"ClearAllHistory").IsVisible,"localized actual data landing "+targets[i]);
                Check(HistoryCount(chat)==count+1,"same-category anchor is one distinct route "+targets[i]);
                Check(!app.Windows.OfType<DataActionConfirmationWindow>().Any(),"data search never clicks "+targets[i]);
            }
            Click(Named<Button>(chat,"AppBack"));await Task.Delay(40);Check(Named<Button>(page,"ClearAllHistory").IsKeyboardFocusWithin,"back restores prior data action anchor");
            Click(Named<Button>(chat,"AppBack"));await Task.Delay(40);Click(Named<Button>(chat,"AppBack"));await Task.Delay(40);Check(ReferenceEquals(shortcut,Named<TextBox>(page,"Hotkeytab"))&&shortcut.Text=="Ctrl+Alt+Shift+F12","back restores cached General shortcut subpage and draft");
            Click(Named<Button>(chat,"AppForward"));await Task.Delay(40);Check(Named<Button>(page,"ClearAllHistory").IsVisible,"forward restores data subpage");
            foreach(string alias in new[]{TextResources.Get("settingsSearch.clearHistoryAlias"),TextResources.Get("settingsSearch.resetAlias")}){search.Text=alias;Key(search,System.Windows.Input.Key.Enter);await Task.Delay(30);Check(Named<Button>(page,"ClearAllHistory").IsVisible&&!app.Windows.OfType<DataActionConfirmationWindow>().Any(),"current-language alias navigates safely: "+alias);}
            search.Text=TextResources.Get("relationship.home.settings");Key(search,System.Windows.Input.Key.Enter);await Task.Delay(220);
            var home=Named<CompanionHomePage>(chat,"CompanionHomePage");await home.RefreshAsync();await Task.Delay(80);
            Check(chat.CurrentPage=="companion"&&Named<Expander>(home,"CompanionRelationshipSettings").IsExpanded,"localized relationship search expands actual cached home settings");
            Check(app.Windows.OfType<ChatWindow>().Count()==1&&home.PendingSettings.IsCompleted&&home.PendingRetry.IsCompleted,"home landing reuses original window without saving or retrying");
            var pendingSettings=home.PendingSettings;var pendingRetry=home.PendingRetry;await pet.Relationships.FlushAsync();using(var lease=await pet.DataMaintenance.BeginAsync()){Click(Named<Button>(chat,"AppBack"));await Task.Delay(20);search.Text=TextResources.Get("relationship.home.settings");Key(search,System.Windows.Input.Key.Enter);await home.RefreshAsync();Check(pet.DataMaintenance.IsMaintenanceActive&&ReferenceEquals(pendingSettings,home.PendingSettings)&&ReferenceEquals(pendingRetry,home.PendingRetry),"navigation under held maintenance neither saves nor retries nor takes ownership");}
            Capture(chat,Path.Combine(root,"relationship-settings.png"));
            Click(Named<Button>(chat,"AppBack"));await Task.Delay(50);Check(chat.CurrentPage=="settings"&&ReferenceEquals(page,Named<SettingsPage>(chat,"SettingsPage"))&&Named<Button>(page,"ClearAllHistory").IsVisible,"return restores same settings data route");
            Check(((ListBoxItem)navigation.Items[page.SelectedNavigationIndex]).Visibility==Visibility.Visible,"back clears an incompatible feature filter so selected route stays visible");
            search.Text=TextResources.Get("relationship.home.title");Key(search,System.Windows.Input.Key.Enter);await Task.Delay(100);Check(chat.CurrentPage=="companion"&&ReferenceEquals(home,Named<CompanionHomePage>(chat,"CompanionHomePage")),"localized home search reuses existing page");
            Click(Named<Button>(chat,"AppBack"));await Task.Delay(40);
            search.Text="unmatched-s3-query-42";await Task.Delay(20);Check(Named<TextBlock>(page,"SettingsSearchEmpty").IsVisible,"unknown search exposes explicit translated empty state");Key(search,System.Windows.Input.Key.Escape);Check(search.Text==""&&navigation.Items.OfType<ListBoxItem>().All(x=>x.Visibility==Visibility.Visible),"Escape restores ordinary navigation without an action");
            page.SelectAiSection(0);await Task.Delay(20);Check(ReferenceEquals(model,Named<TextBox>(page,"TextProviderName"))&&model.Text=="S3 unsaved provider","all feature navigation preserves cached AI draft");model.Text=modelBefore;page.SelectGeneralSection(1);await Task.Delay(20);Named<TextBox>(page,"Hotkeytab").Text=shortcutBefore;
            Check(Hash(history)==historyHash&&Hash(cache)==cacheHash&&Hash(Path.Combine(root,"data","settings.json"))==settingsHash,"search/navigation leave owned settings/history/cache bytes intact");
            Check(!app.Windows.OfType<DataActionConfirmationWindow>().Any()&&!app.Windows.OfType<CacheMaintenanceWindow>().Any()&&!app.Windows.OfType<UninstallConfirmationWindow>().Any(),"all search routes avoid destructive dialog creation");
            page.SelectGeneralSection(0);page.SelectPage(6);search.Text=TextResources.Get("dataManagement.clearHistory");page.SelectPage(7);chat.UpdateLayout();Check(Named<FrameworkElement>(page,"QuietMode").IsVisible,"legacy General route ignores a stale feature search and retains its selected subpage");
            search.Text=TextResources.Get("cache.title");Key(search,System.Windows.Input.Key.Down);Key(navigation,System.Windows.Input.Key.Enter);await Task.Delay(40);Check(page.IsOnAboutPage,"Down then Enter follows actual navigation list");
            foreach(string theme in new[]{"light","dark"}){
                Trace(theme+":apply-theme");ThemeManager.Apply(theme);chat.Width=1100;search.Text=TextResources.Get("dataManagement.clearHistory");Key(search,System.Windows.Input.Key.Enter);await Task.Delay(50);Trace(theme+":capture-search");Capture(chat,Path.Combine(root,"search-"+theme+".png"));
                chat.Width=620;await Task.Delay(70);Check(!search.IsVisible&&navigation.Items.OfType<ListBoxItem>().All(x=>x.Visibility==Visibility.Visible),"narrow layout keeps all normal destinations "+theme);page.SelectGeneralSection(2);await Task.Delay(40);Check(Named<Button>(page,"ClearAllHistory").IsVisible&&Named<Button>(page,"OpenPreservedAttachments").IsVisible,"narrow data actions and attachment entry remain reachable "+theme);Capture(chat,Path.Combine(root,"narrow-"+theme+".png"));
                page.SelectNavigation(0);await Task.Delay(20);Check(Named<Button>(page,"OpenCompanionHome").IsVisible,"narrow companion settings includes actual home link");Click(Named<Button>(page,"OpenCompanionHome"));await Task.Delay(80);Check(chat.CurrentPage=="companion"&&ReferenceEquals(home,Named<CompanionHomePage>(chat,"CompanionHomePage")),"normal related link opens same home without search");Click(Named<Button>(chat,"AppBack"));await Task.Delay(40);
            }
            Click(Named<Button>(chat,"ShellHome"));await Task.Delay(40);Check(ReferenceEquals(draft,Named<MarkdownEditor>(chat,"MessageInput"))&&draft.Text=="S3 unsent composer","return retains original unsent composer");
            Console.WriteLine("Settings feature navigation: "+locale+" "+checks+" passed; captures="+root);
            Console.WriteLine("Keyboard evidence: initial cache entry uses Win32 Enter key messages to the fixture's own HWND and actual keyboard focus; remaining cases use routed WPF preview key events. No user-desktop SendInput or macOS acceptance is claimed.");
        }
        [STAThread]public static int Main(string[] args){if(args.Length==1&&args[0]=="--compile-only")return 0;string locale=args.Length==0?"en_US":args[0];layoutBoundary=args.Contains("--layout-boundary");app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs error){Console.Error.WriteLine("NAVIGATION_DISPATCHER_FAILURE phase="+phase+" "+error.Exception);};AppDomain.CurrentDomain.UnhandledException+=delegate(object sender,UnhandledExceptionEventArgs error){Console.Error.WriteLine("NAVIGATION_UNHANDLED_FAILURE phase="+phase+" "+error.ExceptionObject);};int exit=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run(locale);exit=0;}catch(Exception e){Console.Error.WriteLine(e);}finally{foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();if(pet!=null)pet.Close();app.Shutdown();}}));app.Run();Console.WriteLine("NAVIGATION_PROCESS_EXIT locale="+locale+" result="+exit+" phase="+phase);return exit;}
    }
}
