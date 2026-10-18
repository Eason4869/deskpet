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
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class McpUiSmoke
    {
        static int count,created;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        static IEnumerable<T> All<T>(DependencyObject node)where T:DependencyObject{if(node is T)yield return (T)node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(node,i)))yield return item;}
        static T Find<T>(DependencyObject node,string name)where T:FrameworkElement{return All<T>(node).Single(t=>t.Name==name);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));}
        static void Click(DependencyObject node,string name){Find<Button>(node,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        sealed class Transport:IMcpTransport,IDisposable{public Task<McpToolDefinition[]> ListToolsAsync(CancellationToken c){return Task.FromResult(new[]{new McpToolDefinition{Name="read",InputSchema=new Dictionary<string,object>()}});}public Task<string> CallAsync(string n,Dictionary<string,object> a,CancellationToken c){return Task.FromResult("fixture");}public void Dispose(){}}
        [STAThread] public static int Main(){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;SettingsWindow host=null;try{string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mcp-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var config=new PetSettings{AutoUpdate=false,GreetingEnabled=false};foreach(string key in config.Hotkeys.Keys.ToArray())config.Hotkeys[key]="";pet=new PetWindow(null,config,new SettingsStore(Path.Combine(root,"settings.json")),null);var manager=new McpConnectionManager(c=>{created++;return new Transport();});typeof(PetWindow).GetField("mcpConnections",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,manager);host=new SettingsWindow(pet);host.Show();Find<ListBox>(host,"SettingsNavigation").SelectedIndex=5;Pump();Check(created==0,"opening extension settings starts no connection");Click(host,"AddMcpServer");Find<TextBox>(host,"McpServerName").Text="本地测试";Find<TextBox>(host,"McpServerEndpoint").Text="https://fixture.invalid/mcp";Find<PasswordBox>(host,"McpServerKey").Password="fixture-secret";Check(host.HasUnsavedEdits,"MCP edits participate in unsaved-state detection");Click(host,"SaveMcpServers");Check(config.McpServers.Count==1&&!File.ReadAllText(Path.Combine(root,"settings.json")).Contains("fixture-secret"),"server configuration saves with encrypted credential");Check(!host.HasUnsavedEdits,"saved extension configuration is clean");Click(host,"ConnectMcpServer");Check(created==1&&manager.Status.Single().State=="connected","explicit UI action connects actual configured transport");Click(host,"DisconnectMcpServer");Check(manager.Status.Single().State=="disconnected","explicit UI action disconnects owned transport");host.Close();host=new SettingsWindow(pet);host.Show();Find<ListBox>(host,"SettingsNavigation").SelectedIndex=5;Pump();Check(Find<TextBox>(host,"McpServerName").Text=="本地测试"&&Find<PasswordBox>(host,"McpServerKey").Password=="fixture-secret"&&created==1,"reopening restores fields without reconnecting");Console.WriteLine("MCP UI tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(host!=null)host.Close();if(pet!=null)pet.Close();app.Shutdown();}}
    }
}
