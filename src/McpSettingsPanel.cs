using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    internal sealed class McpSettingsPanel:UserControl,IDisposable
    {
        readonly PetWindow pet;readonly List<McpServerConfiguration> drafts;readonly ListBox servers=new ListBox{Name="McpServerList",MinHeight=90};
        readonly TextBox name=Ui.Field(),endpoint=Ui.Field(),arguments=Ui.Field(),directory=Ui.Field();readonly PasswordBox key=new PasswordBox();readonly ComboBox transport=new ComboBox{ItemsSource=new[]{"HTTP JSON（2025-03-26 子集，无 SSE）","stdio（用户点击连接后启动）"},MinHeight=32};readonly CheckBox enabled=Ui.Toggle("启用此配置",false,delegate{});readonly TextBlock status=Ui.Text("",12,Ui.Muted);McpServerConfiguration selected;bool updating,invalidArguments;
        internal McpSettingsPanel(PetWindow pet)
        {
            Name="McpSettingsPanel";this.pet=pet;drafts=(pet.Settings.McpServers??new List<McpServerConfiguration>()).Where(c=>c!=null).Select(c=>c.Copy()).ToList();
            var body=new StackPanel{Margin=new Thickness(26)};body.Children.Add(Ui.Text("MCP 扩展",22));var help=Ui.Text("保存配置后手动连接。再次启动应用不会自动启动扩展；未知工具每次确认，计划模式不开放 MCP 工具。支持范围按下方协议标注。",12,Ui.Muted);help.TextWrapping=TextWrapping.Wrap;body.Children.Add(help);body.Children.Add(servers);
            var manage=new WrapPanel();var add=Ui.Button("添加服务器");add.Name="AddMcpServer";add.Click+=delegate{Capture();if(drafts.Count>=16)return;var config=new McpServerConfiguration{Name="服务器 "+(drafts.Count+1),Enabled=true};drafts.Add(config);Refresh(config);};manage.Children.Add(add);var remove=Ui.Button("移除");remove.Click+=delegate{if(selected==null)return;pet.McpConnections.Disconnect(selected.Id);drafts.Remove(selected);Refresh(drafts.FirstOrDefault());};manage.Children.Add(remove);body.Children.Add(manage);
            name.Name="McpServerName";endpoint.Name="McpServerEndpoint";arguments.Name="McpServerArguments";directory.Name="McpServerDirectory";key.Name="McpServerKey";transport.Name="McpServerTransport";enabled.Name="McpServerEnabled";
            Ui.Label(body,"名称",name);Ui.Label(body,"传输协议",transport);Ui.Label(body,"服务 URL / 已有可执行文件绝对路径",endpoint);Ui.Label(body,"stdio 参数（JSON 字符串数组，不允许在参数中填写密钥）",arguments);Ui.Label(body,"stdio 工作目录",directory);Ui.Label(body,"HTTP 独立凭证（本地加密保存）",key);body.Children.Add(enabled);
            var actions=new WrapPanel();var save=Ui.Button("保存配置",true);save.Name="SaveMcpServers";save.Click+=delegate{Save();};actions.Children.Add(save);var connect=Ui.Button("连接");connect.Name="ConnectMcpServer";connect.Click+=async delegate{if(!Save()||selected==null)return;try{connect.IsEnabled=false;await pet.McpConnections.ConnectAsync(selected,CancellationToken.None);}catch(Exception e){status.Text=e is ApiException||e is ArgumentException||e is InvalidOperationException?e.Message:"连接未完成，请核对服务器。";}finally{connect.IsEnabled=true;RefreshStatus();}};actions.Children.Add(connect);var disconnect=Ui.Button("断开");disconnect.Name="DisconnectMcpServer";disconnect.Click+=delegate{if(selected!=null)pet.McpConnections.Disconnect(selected.Id);RefreshStatus();};actions.Children.Add(disconnect);body.Children.Add(actions);status.Name="McpConnectionStatus";status.TextWrapping=TextWrapping.Wrap;body.Children.Add(status);Content=Ui.Scroll(body);
            servers.SelectionChanged+=delegate{if(updating)return;Capture();selected=servers.SelectedItem as McpServerConfiguration;Show();};pet.McpConnections.Changed+=Changed;Refresh(drafts.FirstOrDefault());
        }
        void Capture(){if(updating||selected==null)return;selected.Name=name.Text.Trim();selected.Endpoint=endpoint.Text.Trim();selected.Transport=transport.SelectedIndex==1?"stdio":"http-json";selected.WorkingDirectory=directory.Text.Trim();selected.Enabled=enabled.IsChecked==true;if(key.Password!=selected.Credentials.ApiKey)selected.Credentials.ApiKey=key.Password;invalidArguments=false;try{selected.Arguments=new JavaScriptSerializer().Deserialize<string[]>(arguments.Text)??new string[0];}catch(ArgumentException){invalidArguments=true;}}
        void Refresh(McpServerConfiguration value){updating=true;servers.ItemsSource=null;servers.DisplayMemberPath="";servers.ItemsSource=drafts;selected=value;servers.SelectedItem=value;Show();updating=false;}
        void Show(){var value=selected??new McpServerConfiguration();name.Text=value.Name;endpoint.Text=value.Endpoint;arguments.Text=new JavaScriptSerializer().Serialize(value.Arguments??new string[0]);directory.Text=value.WorkingDirectory;key.Password=value.Credentials.ApiKey;transport.SelectedIndex=value.Transport=="stdio"?1:0;enabled.IsChecked=value.Enabled;RefreshStatus();}
        internal bool HasUnsavedChanges {get{Capture();if(invalidArguments)return true;var saved=pet.Settings.McpServers??new List<McpServerConfiguration>();if(saved.Count!=drafts.Count)return true;var json=new JavaScriptSerializer();return json.Serialize(saved)!=json.Serialize(drafts);}}
        bool Save(){try{if(selected!=null){var parsed=new JavaScriptSerializer().Deserialize<string[]>(arguments.Text);if(parsed==null||parsed.Length>32||parsed.Any(a=>a==null||a.Length>8000))throw new ArgumentException("参数必须为最多 32 个字符串的 JSON 数组。");selected.Arguments=parsed;}Capture();var previous=pet.Settings.McpServers;foreach(var value in drafts){var old=(previous??new List<McpServerConfiguration>()).FirstOrDefault(c=>c.Id==value.Id);if(old!=null&&new JavaScriptSerializer().Serialize(old)!=new JavaScriptSerializer().Serialize(value))pet.McpConnections.Disconnect(value.Id);}pet.Settings.McpServers=drafts.Select(c=>c.Copy()).ToList();try{pet.Store.Save(pet.Settings);}catch{pet.Settings.McpServers=previous;throw;}status.Text="配置已保存，点击连接后生效。";Refresh(selected);return true;}catch(Exception){status.Text="配置未保存，请核对字段和目录权限。";return false;}}
        void Changed(){if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshStatus));return;}RefreshStatus();}
        void RefreshStatus(){if(selected==null){status.Text="尚未配置服务器。";return;}var connection=pet.McpConnections.Status.FirstOrDefault(s=>s.Id==selected.Id);status.Text=connection==null?"未连接":connection.State+" · "+connection.Tools+" 个工具"+(String.IsNullOrEmpty(connection.Error)?"":"\n"+connection.Error);}
        public void Dispose(){pet.McpConnections.Changed-=Changed;}
    }
}
