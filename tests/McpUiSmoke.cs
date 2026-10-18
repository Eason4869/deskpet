using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class McpUiSmoke
    {
        static int count,created;
        static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        static IEnumerable<T> All<T>(DependencyObject node)where T:DependencyObject{if(node is T)yield return (T)node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(node,i)))yield return item;}
        static T Find<T>(DependencyObject node,string name)where T:FrameworkElement{return All<T>(node).Single(t=>t.Name==name);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));}
        static void Click(DependencyObject node,string name){var button=Find<Button>(node,name);Check(button.IsVisible&&button.IsEnabled,name+" is available to the user");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        static void Save(FrameworkElement page,string path){page.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth),(int)Math.Ceiling(page.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(page);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
        static void Inside(FrameworkElement page,FrameworkElement child){Point at=child.TranslatePoint(new Point(),page);Check(at.X>=-1&&at.X+child.ActualWidth<=page.ActualWidth+1,child.Name+" fits the narrow MCP page");}
        sealed class Transport:IMcpTransport,IDisposable{public Task<McpToolDefinition[]> ListToolsAsync(CancellationToken c){return Task.FromResult(new[]{new McpToolDefinition{Name="read",InputSchema=new Dictionary<string,object>()}});}public Task<string> CallAsync(string n,Dictionary<string,object> a,CancellationToken c){return Task.FromResult("fixture");}public void Dispose(){}}
        [STAThread] public static int Main(string[] args)
        {
            if(args.Contains("--compile-only")){Console.WriteLine("MCP UI tests compiled; GUI execution intentionally deferred.");return 0;}
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;ChatWindow host=null;Window preview=null;PluginsPage previewPage=null;
            try{
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mcp-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                string evidence=Path.Combine(Directory.GetCurrentDirectory(),"work","mcp-ui-evidence");Directory.CreateDirectory(evidence);
                var config=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ShowPredictedMessages=false,SaveChatHistory=false,OnboardingState="completed"};foreach(string shortcut in config.Hotkeys.Keys.ToArray())config.Hotkeys[shortcut]="";
                pet=new PetWindow(null,config,new SettingsStore(Path.Combine(root,"settings.json")),null);var manager=new McpConnectionManager(c=>{created++;return new Transport();});typeof(PetWindow).GetField("mcpConnections",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,manager);
                pet.ShowMcpPage();Pump();host=app.Windows.OfType<ChatWindow>().Single(w=>w.IsVisible);Pump();
                Check(created==0,"opening extension settings starts no connection");Check(!Find<TextBox>(host,"McpServerName").IsVisible,"opening MCP shows the server list and hides configuration fields");Check(Find<TextBlock>(host,"McpServersEmpty").IsVisible,"empty MCP list explains that no servers are configured");
                Click(host,"AddMcpServer");Check(Find<TextBox>(host,"McpServerName").IsVisible,"explicit add opens the in-page server editor");
                Find<TextBox>(host,"McpServerName").Text="本地测试";Find<TextBox>(host,"McpServerEndpoint").Text="https://fixture.invalid/mcp";Find<PasswordBox>(host,"McpServerKey").Password="fixture-secret";
                Check(host.HasUnsavedMcpEdits,"MCP edits participate in unsaved-state detection");Check(!pet.CanApplyUpdate(true),"unsaved independent MCP page blocks update restart");
                Click(host,"BackMcpServers");Check(!Find<TextBox>(host,"McpServerName").IsVisible&&Find<TextBlock>(host,"McpServerRowStatus").Text.Contains("未保存")&&config.McpServers.Count==0,"returning to list retains an indicated draft without saving");Check(!Find<Button>(host,"ConnectMcpServer").IsEnabled&&created==0,"an unsaved row cannot silently save or connect");
                Click(host,"EditMcpServer");Check(Find<TextBox>(host,"McpServerName").Text=="本地测试"&&Find<PasswordBox>(host,"McpServerKey").Password=="fixture-secret","editing the draft restores fields and the independent credential");
                Click(host,"PluginsSkillsTab");Click(host,"PluginsMcpTab");Check(Find<TextBox>(host,"McpServerName").IsVisible&&Find<TextBox>(host,"McpServerName").Text=="本地测试"&&host.HasUnsavedMcpEdits,"switching tabs preserves the in-page editor and unsaved draft");
                Find<ComboBox>(host,"McpServerTransport").SelectedIndex=1;Find<TextBox>(host,"McpServerEndpoint").Text=Assembly.GetExecutingAssembly().Location;Find<TextBox>(host,"McpServerDirectory").Text=root;Find<TextBox>(host,"McpServerArguments").Text="[1]";
                Click(host,"BackMcpServers");Click(host,"EditMcpServer");Check(Find<TextBox>(host,"McpServerArguments").Text=="[1]","invalid argument text survives list navigation for correction");Click(host,"SaveMcpServers");Check(config.McpServers.Count==0&&Find<TextBox>(host,"McpServerName").IsVisible&&Find<TextBlock>(host,"McpConnectionStatus").Text.Contains("字符串"),"invalid stdio arguments cannot save and show a useful validation error");
                Find<TextBox>(host,"McpServerArguments").Text="[\"--fixture\"]";Find<ComboBox>(host,"McpServerTransport").SelectedIndex=0;Find<TextBox>(host,"McpServerEndpoint").Text="http://remote.invalid/mcp";Click(host,"SaveMcpServers");Check(config.McpServers.Count==0&&created==0,"unsupported remote HTTP cannot save or connect");
                Find<TextBox>(host,"McpServerEndpoint").Text="https://fixture.invalid/mcp";Click(host,"SaveMcpServers");
                Check(config.McpServers.Count==1&&!File.ReadAllText(Path.Combine(root,"settings.json")).Contains("fixture-secret"),"server configuration saves with encrypted credential");Check(!host.HasUnsavedMcpEdits&&!Find<TextBox>(host,"McpServerName").IsVisible&&created==0,"save returns to clean list without connecting");
                Click(host,"ConnectMcpServer");Check(created==1&&manager.Status.Single().State=="connected"&&Find<TextBlock>(host,"McpServerRowStatus").Text.Contains("已连接"),"explicit row action connects configured transport and shows actual state");
                Click(host,"DisconnectMcpServer");Check(manager.Status.Single().State=="disconnected"&&Find<TextBlock>(host,"McpServerRowStatus").Text.Contains("未连接"),"explicit row action disconnects owned transport and refreshes state");
                host.Close();pet.ShowMcpPage();Pump();host=app.Windows.OfType<ChatWindow>().Single(w=>w.IsVisible);Pump();Check(!Find<TextBox>(host,"McpServerName").IsVisible&&created==1,"reopening starts with list without reconnecting");Click(host,"EditMcpServer");Check(Find<TextBox>(host,"McpServerName").Text=="本地测试"&&Find<PasswordBox>(host,"McpServerKey").Password=="fixture-secret","saved server editor restores fields and encrypted credential");host.Close();host=null;

                previewPage=new PluginsPage(pet);preview=new Window{Content=previewPage};Ui.Window(preview,"MCP · 本地样本验收",1050,840);preview.MinWidth=0;preview.MinHeight=0;preview.Show();previewPage.SelectMcp();Pump();Save(previewPage,Path.Combine(evidence,"mcp-server-list.png"));
                foreach(double width in new[]{460d,360d}){preview.Width=width;Pump();preview.UpdateLayout();var panel=Find<McpSettingsPanel>(previewPage,"McpSettingsPanel");Check(previewPage.DesiredSize.Width<=previewPage.ActualWidth+1,"MCP list measures inside "+width+" DIP host");Inside(previewPage,Find<Border>(previewPage,"McpServerRow"));foreach(string action in new[]{"EditMcpServer","ConnectMcpServer","DisconnectMcpServer"})Inside(previewPage,Find<Button>(previewPage,action));Save(previewPage,Path.Combine(evidence,"mcp-list-narrow-"+width+".png"));Click(previewPage,"EditMcpServer");foreach(string field in new[]{"McpServerName","McpServerEndpoint","McpServerTransport","McpServerKey","SaveMcpServers"})Inside(previewPage,Find<FrameworkElement>(previewPage,field));var scroll=All<ScrollViewer>(panel).First();Check(scroll.ExtentWidth<=scroll.ViewportWidth+1,"MCP editor does not overflow horizontally at "+width+" DIP");Save(previewPage,Path.Combine(evidence,"mcp-editor-narrow-"+width+".png"));Click(previewPage,"BackMcpServers");}
                preview.Width=1050;Pump();Click(previewPage,"EditMcpServer");Save(previewPage,Path.Combine(evidence,"mcp-server-editor.png"));Check(created==1,"viewing and editing saved servers never reconnects them");
                Console.WriteLine("MCP UI tests: "+count+" passed. Evidence: "+evidence);return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{if(preview!=null)preview.Close();if(previewPage!=null)previewPage.Dispose();if(host!=null)host.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}