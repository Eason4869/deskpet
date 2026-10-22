using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    // The list and editor share local drafts; navigation never writes or connects.
    internal sealed class McpSettingsPanel : UserControl, IDisposable
    {
        internal event Action Saved;
        internal event Action EditorVisibilityChanged;
        readonly PetWindow pet;
        readonly List<McpServerConfiguration> drafts;
        readonly Dictionary<string,string> argumentDrafts=new Dictionary<string,string>();
        readonly StackPanel list=new StackPanel{Name="McpServerList"},rows=new StackPanel(),editor=new StackPanel{Name="McpServerEditor",Visibility=Visibility.Collapsed};
        readonly TextBox name=Ui.Field(),endpoint=Ui.Field(),arguments=Ui.Field("",true),directory=Ui.Field();
        readonly PasswordBox key=new PasswordBox{MinHeight=38,Padding=new Thickness(10,7,10,7),Background=ThemeManager.Surface,BorderBrush=Ui.Brush("#E0E3E8"),BorderThickness=new Thickness(1)};
        readonly ComboBox transport=new ComboBox{ItemsSource=new[]{"HTTP JSON","stdio"},MinHeight=38};
        readonly CheckBox enabled=Ui.Toggle("启用此配置",false,delegate{});
        readonly TextBlock status=Ui.Text("",12,Ui.Muted),editorTitle=Ui.Text("添加服务器",20,Ui.PageInk),endpointLabel=Ui.Text("服务 URL",12,Ui.PageInk),protocolHint=Ui.Text("",11,Ui.Muted),draftHint=Ui.Text("",11,Ui.Brush("#91682E"));
        readonly StackPanel stdioFields=new StackPanel();
        readonly Border credentials;
        readonly Button listAdd;
        readonly Button save=Ui.Button("保存配置",true),remove=Ui.Button("移除服务器");
        McpServerConfiguration selected;
        bool updating,disposed;

        internal McpSettingsPanel(PetWindow pet)
        {
            Name="McpSettingsPanel";this.pet=pet;
            drafts=(pet.Settings.McpServers??new List<McpServerConfiguration>()).Where(c=>c!=null).Select(c=>c.Copy()).ToList();
            foreach(var config in drafts)argumentDrafts[config.Id]=Json(config.Arguments??new string[0]);
            var body=new StackPanel{Margin=new Thickness(0,0,3,0)};
            var listTitle=Ui.Text("服务器",15,Ui.PageInk);listTitle.FontWeight=FontWeights.SemiBold;listTitle.Margin=new Thickness(8,0,0,12);list.Children.Add(listTitle);
            list.Children.Add(new Border{Height=1,Background=Ui.Brush("#ECEEF1"),Margin=new Thickness(0,0,0,14)});list.Children.Add(rows);
            var add=Button("添加服务器","AddMcpServer",BeginAdd);listAdd=add;add.HorizontalAlignment=HorizontalAlignment.Left;add.Margin=new Thickness(8,12,0,0);list.Children.Add(add);body.Children.Add(list);

            var back=Button("‹  返回列表","BackMcpServers",BackToList);back.HorizontalAlignment=HorizontalAlignment.Left;back.Background=Brushes.Transparent;back.BorderBrush=Brushes.Transparent;back.Padding=new Thickness(0,4,12,4);editor.Children.Add(back);
            editorTitle.FontWeight=FontWeights.SemiBold;editorTitle.Margin=new Thickness(0,12,0,2);editor.Children.Add(editorTitle);
            var note=Ui.Text("保存到本机后，在服务器列表手动连接。",12,Ui.Muted);note.Margin=new Thickness(0,0,0,18);editor.Children.Add(note);
            name.Name="McpServerName";endpoint.Name="McpServerEndpoint";arguments.Name="McpServerArguments";directory.Name="McpServerDirectory";key.Name="McpServerKey";transport.Name="McpServerTransport";enabled.Name="McpServerEnabled";
            foreach(var field in new[]{name,endpoint,arguments,directory}){field.Foreground=Ui.PageInk;field.BorderBrush=Ui.Brush("#E0E3E8");}
            arguments.MinHeight=70;arguments.MaxHeight=150;
            var basic=new StackPanel();Field(basic,"名称",name);Field(basic,"传输协议",transport);protocolHint.Margin=new Thickness(0,7,0,0);basic.Children.Add(protocolHint);editor.Children.Add(Card(basic));
            var connection=new StackPanel();endpointLabel.Margin=new Thickness(0,0,0,7);connection.Children.Add(endpointLabel);connection.Children.Add(endpoint);
            Field(stdioFields,"参数（JSON 字符串数组）",arguments);var argHint=Ui.Text("例如 [\"--port\", \"3000\"]，请勿在参数中填写密钥。",11,Ui.Muted);argHint.Margin=new Thickness(0,6,0,0);stdioFields.Children.Add(argHint);Field(stdioFields,"工作目录（可选）",directory);connection.Children.Add(stdioFields);editor.Children.Add(Card(connection));
            var secret=new StackPanel();Field(secret,"HTTP 独立凭证（可选）",key);var secretHint=Ui.Text("凭证在本机加密保存。",11,Ui.Muted);secretHint.Margin=new Thickness(0,7,0,0);secret.Children.Add(secretHint);credentials=Card(secret);editor.Children.Add(credentials);
            enabled.Foreground=Ui.PageInk;editor.Children.Add(enabled);
            remove.Name="RemoveMcpServer";remove.Foreground=Ui.Brush("#A65050");remove.Background=Brushes.Transparent;remove.BorderBrush=Brushes.Transparent;remove.HorizontalAlignment=HorizontalAlignment.Left;remove.Margin=new Thickness(0,6,0,0);remove.Click+=delegate{RemoveSelected();};editor.Children.Add(remove);body.Children.Add(editor);
            draftHint.Name="McpDraftStatus";draftHint.Margin=new Thickness(8,12,0,0);body.Children.Add(draftHint);
            save.Name="SaveMcpServers";save.Background=ThemeManager.ActionSurface;save.BorderBrush=save.Background;save.HorizontalAlignment=HorizontalAlignment.Left;save.Margin=new Thickness(0,16,0,0);save.Click+=delegate{Save();};body.Children.Add(save);
            status.Name="McpConnectionStatus";status.Margin=new Thickness(0,12,0,0);body.Children.Add(status);Content=Ui.Scroll(body);
            transport.SelectionChanged+=delegate{if(!updating)ShowProtocol();};
            foreach(var field in new[]{name,endpoint,arguments,directory})field.TextChanged+=delegate{Edited();};key.PasswordChanged+=delegate{Edited();};enabled.Checked+=delegate{Edited();};enabled.Unchecked+=delegate{Edited();};
            pet.McpConnections.Changed+=Changed;Show();RenderRows();SetEditor(false);
        }

        internal void UseHeaderAddButton(){listAdd.Visibility=Visibility.Collapsed;listAdd.Name="";}
        internal bool EditorVisible {get{return editor.Visibility==Visibility.Visible;}}
        internal void BeginAdd()
        {
            Capture();if(drafts.Count>=16){status.Text="最多保存 16 个服务器。";return;}
            var config=new McpServerConfiguration{Name="服务器 "+(drafts.Count+1),Enabled=true};drafts.Add(config);argumentDrafts[config.Id]="[]";selected=config;Show();SetEditor(true);
        }
        internal void OpenEditor(){if(selected==null)selected=drafts.FirstOrDefault();if(selected==null){BeginAdd();return;}Show();SetEditor(true);}
        void Edit(McpServerConfiguration config){Capture();selected=config;Show();SetEditor(true);}
        void BackToList(){Capture();SetEditor(false);RenderRows();}
        void SetEditor(bool visible)
        {
            bool changed=EditorVisible!=visible;editor.Visibility=visible?Visibility.Visible:Visibility.Collapsed;list.Visibility=visible?Visibility.Collapsed:Visibility.Visible;
            RefreshDraftStatus();if(changed){var handler=EditorVisibilityChanged;if(handler!=null)handler();}
        }
        void Edited(){if(updating)return;RefreshDraftStatus();}
        void RefreshDraftStatus()
        {
            bool dirty=HasUnsavedChanges;draftHint.Text=dirty?"有未保存的变更，返回列表和切换标签会保留草稿。":"";draftHint.Visibility=dirty?Visibility.Visible:Visibility.Collapsed;
            save.Visibility=EditorVisible||dirty?Visibility.Visible:Visibility.Collapsed;save.Content=EditorVisible?"保存配置":"保存变更";
        }
        void Capture()
        {
            if(updating||selected==null)return;
            selected.Name=name.Text.Trim();selected.Endpoint=endpoint.Text.Trim();selected.Transport=transport.SelectedIndex==1?"stdio":"http-json";selected.WorkingDirectory=directory.Text.Trim();selected.Enabled=enabled.IsChecked==true;if(selected.Credentials.ApiKey!=key.Password.Trim())selected.Credentials.ApiKey=key.Password;argumentDrafts[selected.Id]=arguments.Text;
            string[] parsed;if(ParseArguments(arguments.Text,out parsed))selected.Arguments=parsed;
        }
        void Show()
        {
            updating=true;var value=selected??new McpServerConfiguration();name.Text=value.Name;endpoint.Text=value.Endpoint;string raw;arguments.Text=selected!=null&&argumentDrafts.TryGetValue(value.Id,out raw)?raw:Json(value.Arguments??new string[0]);directory.Text=value.WorkingDirectory;key.Password=value.Credentials.ApiKey;transport.SelectedIndex=value.Transport=="stdio"?1:0;enabled.IsChecked=value.Enabled;
            editorTitle.Text=selected!=null&&SavedConfiguration(selected.Id)!=null?"编辑服务器":"添加服务器";remove.Visibility=selected!=null?Visibility.Visible:Visibility.Collapsed;updating=false;ShowProtocol();
        }
        void ShowProtocol()
        {
            bool stdio=transport.SelectedIndex==1;endpointLabel.Text=stdio?"可执行文件绝对路径":"服务 URL";endpoint.ToolTip=stdio?"选择已存在的可执行文件，例如 C:\\Tools\\server.exe":"HTTPS 地址，或本机 HTTP 地址";
            stdioFields.Visibility=stdio?Visibility.Visible:Visibility.Collapsed;credentials.Visibility=stdio?Visibility.Collapsed:Visibility.Visible;
            protocolHint.Text=stdio?"使用标准输入输出通信，仅在手动连接时启动进程。":"支持 2025-03-26 HTTP JSON 子集，不支持 SSE。";if(!updating)RefreshDraftStatus();
        }
        internal bool HasUnsavedChanges
        {
            get{Capture();foreach(var config in drafts){string raw;string[] parsed;if(config.Transport=="stdio"&&argumentDrafts.TryGetValue(config.Id,out raw)&&!ParseArguments(raw,out parsed))return true;}return Json(pet.Settings.McpServers??new List<McpServerConfiguration>())!=Json(drafts);}
        }
        static bool ParseArguments(string raw,out string[] parsed)
        {
            parsed=null;try{var values=new JavaScriptSerializer().DeserializeObject(raw??"") as object[];if(values==null||values.Length>32||values.Any(a=>!(a is string)||((string)a).Length>8000))return false;parsed=values.Cast<string>().ToArray();return true;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
        }
        static string Json(object value){return new JavaScriptSerializer().Serialize(value);}
        McpServerConfiguration SavedConfiguration(string id){return (pet.Settings.McpServers??new List<McpServerConfiguration>()).FirstOrDefault(c=>c!=null&&c.Id==id);}
        bool IsDirty(McpServerConfiguration config){Capture();var saved=SavedConfiguration(config.Id);string raw;string[] parsed;return saved==null||Json(saved)!=Json(config)||(config.Transport=="stdio"&&argumentDrafts.TryGetValue(config.Id,out raw)&&!ParseArguments(raw,out parsed));}
        void Validate(McpServerConfiguration value)
        {
            if(String.IsNullOrWhiteSpace(value.Name))throw new ArgumentException("请填写服务器名称。");
            if(value.Transport=="http-json"){Uri url;if(!Uri.TryCreate(value.Endpoint,UriKind.Absolute,out url)||url.UserInfo.Length>0||(url.Scheme!="https"&&!(url.Scheme=="http"&&url.IsLoopback)))throw new ArgumentException("服务 URL 需为 HTTPS，或本机 HTTP 地址；独立凭证请填入凭证栏。");return;}
            if(value.Transport!="stdio"||!Path.IsPathRooted(value.Endpoint)||!File.Exists(value.Endpoint))throw new ArgumentException("请填写已存在的可执行文件绝对路径。");
            if(value.WorkingDirectory.Length>0&&(!Path.IsPathRooted(value.WorkingDirectory)||!Directory.Exists(value.WorkingDirectory)))throw new ArgumentException("工作目录需为已存在的绝对路径，或留空。");
            string raw;string[] parsed;if(!argumentDrafts.TryGetValue(value.Id,out raw)||!ParseArguments(raw,out parsed))throw new ArgumentException("参数必须为最多 32 个字符串的 JSON 数组。");value.Arguments=parsed;
        }
        bool Save()
        {
            Capture();try{
                foreach(var value in drafts){try{Validate(value);}catch(ArgumentException){selected=value;Show();SetEditor(true);throw;}}
                var previous=pet.Settings.McpServers;pet.Settings.McpServers=drafts.Select(c=>c.Copy()).ToList();try{pet.Store.Save(pet.Settings);}catch{pet.Settings.McpServers=previous;throw;}
                // Persist first, then retire connections that no longer match the saved configuration.
                foreach(var old in previous??new List<McpServerConfiguration>()){if(old==null)continue;var current=drafts.FirstOrDefault(c=>c.Id==old.Id);if(current==null||Json(old)!=Json(current))pet.McpConnections.Disconnect(old.Id);}
                SetEditor(false);RenderRows();status.Text="配置已保存，在列表中手动连接。";var handler=Saved;if(handler!=null)handler();return true;
            }catch(ArgumentException e){status.Text=e.Message;return false;}catch(Exception){status.Text="配置未保存，请核对字段和目录权限。";return false;}
        }
        void RemoveSelected(){if(selected==null)return;Capture();argumentDrafts.Remove(selected.Id);drafts.Remove(selected);selected=null;Show();SetEditor(false);RenderRows();status.Text="已从草稿移除，保存变更后生效。";}
        async void Connect(McpServerConfiguration config)
        {
            if(IsDirty(config)){status.Text="请先保存此服务器的配置，再手动连接。";return;}
            var saved=SavedConfiguration(config.Id);if(saved==null)return;
            try{await pet.McpConnections.ConnectAsync(saved,CancellationToken.None);status.Text="";}catch(Exception){status.Text="连接未完成，请核对服务器配置与协议。";}finally{if(!disposed)RenderRows();}
        }
        void RenderRows()
        {
            if(disposed)return;rows.Children.Clear();
            if(drafts.Count==0){var empty=new StackPanel{Margin=new Thickness(22,39,22,43)};var icon=ShellIcons.Image("mcp",Ui.Brush("#9DA8B8"),34);icon.Margin=new Thickness(0,0,0,15);empty.Children.Add(icon);var title=Ui.Text("还没有服务器",16,Ui.PageInk);title.Name="McpServersEmpty";title.TextAlignment=TextAlignment.Center;empty.Children.Add(title);var hint=Ui.Text("添加你信任的 MCP 服务器，\n让伙伴在工作时使用外部工具。",12,Ui.Muted);hint.TextAlignment=TextAlignment.Center;hint.Margin=new Thickness(0,7,0,0);empty.Children.Add(hint);rows.Children.Add(Card(empty));}
            foreach(var config in drafts){
                var body=new StackPanel();var main=new Grid{MinHeight=66};main.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});main.ColumnDefinitions.Add(new ColumnDefinition());
                main.Children.Add(new Border{Width=40,Height=40,CornerRadius=new CornerRadius(10),Background=Ui.Brush("#F3F4F5"),Child=ShellIcons.Image("mcp",Ui.Brush("#72777F"),20),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left});
                var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,5,0,5)};var title=Ui.Text(config.Name,14,Ui.PageInk);title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;title.ToolTip=config.Name;copy.Children.Add(title);
                var state=pet.McpConnections.Status.FirstOrDefault(s=>s.Id==config.Id);bool dirty=IsDirty(config);var label=Ui.Text((config.Transport=="stdio"?"stdio":"HTTP JSON")+" · "+State(state)+(config.Enabled?"":" · 已停用")+(dirty?" · 未保存":""),12,Ui.Muted);label.Name="McpServerRowStatus";label.Tag=config.Id;copy.Children.Add(label);Grid.SetColumn(copy,1);main.Children.Add(copy);body.Children.Add(main);
                var actions=new WrapPanel{Margin=new Thickness(48,0,0,3)};var edit=Button("编辑","EditMcpServer",delegate{Edit(config);});edit.Tag=config.Id;actions.Children.Add(edit);
                bool active=state!=null&&(state.State=="connected"||state.State=="connecting");var connect=Button(state!=null&&state.State=="connecting"?"连接中…":"连接","ConnectMcpServer",delegate{Connect(config);});connect.Tag=config.Id;connect.IsEnabled=!active&&!dirty&&config.Enabled;connect.ToolTip=dirty?"请先保存配置":"手动连接此服务器";actions.Children.Add(connect);
                var disconnect=Button("断开","DisconnectMcpServer",delegate{pet.McpConnections.Disconnect(config.Id);status.Text="已断开连接。";});disconnect.Tag=config.Id;disconnect.IsEnabled=active;actions.Children.Add(disconnect);body.Children.Add(actions);
                rows.Children.Add(new Border{Name="McpServerRow",Tag=config.Id,Child=body,CornerRadius=new CornerRadius(10),Padding=new Thickness(8,7,8,7),Margin=new Thickness(0,0,0,8),Background=Brushes.Transparent});
            }
            RefreshDraftStatus();
        }
        static string State(McpConnectionStatus state){if(state==null)return "未连接";switch(state.State){case "connected":return "已连接 · "+state.Tools+" 个工具";case "connecting":return "连接中";case "failed":return "连接失败";default:return "未连接";}}
        static Border Card(UIElement child){return new Border{Child=child,CornerRadius=new CornerRadius(13),Background=ThemeManager.Surface,BorderBrush=Ui.Brush("#E6E9EE"),BorderThickness=new Thickness(1),Padding=new Thickness(16),Margin=new Thickness(0,0,0,12)};}
        static void Field(Panel panel,string label,UIElement field){var caption=Ui.Text(label,12,Ui.PageInk);caption.Margin=new Thickness(0,panel.Children.Count==0?0:14,0,7);panel.Children.Add(caption);panel.Children.Add(field);}
        static Button Button(string title,string controlName,Action action){var button=Ui.Button(title);button.Name=controlName;button.FontSize=12;button.Foreground=Ui.PageInk;button.BorderBrush=Ui.Brush("#E0E3E8");button.Padding=new Thickness(12,5,12,5);button.Margin=new Thickness(0,0,8,5);button.Click+=delegate{action();};System.Windows.Automation.AutomationProperties.SetName(button,title);return button;}
        void Changed(){if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(Changed));return;}RenderRows();}
        public void Dispose(){if(disposed)return;disposed=true;pet.McpConnections.Changed-=Changed;}
    }
}