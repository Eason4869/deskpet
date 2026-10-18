using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed partial class ConversationComposer
    {
        internal bool TryExecutePermissionCommand(string text)
        {
            string command=(text??"").Trim();if(!command.StartsWith("/permissions",StringComparison.OrdinalIgnoreCase)&&!command.StartsWith("/sandbox",StringComparison.OrdinalIgnoreCase))return false;
            if(conversationOnly||!WorkSettings.AgentEnabled){status.Text="请在工作入口中更改权限。";return true;}
            string[] parts=command.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);string head=parts[0].ToLowerInvariant();
            if(head=="/permissions"&&parts.Length==1){OpenPermissions();return true;}
            if(head=="/sandbox"&&parts.Length==1){status.Text=AgentAccessPolicy.Label(WorkSettings);return true;}
            if(parts.Length!=2){status.Text="使用 /permissions ask|auto|full 或 /sandbox on|off";return true;}
            if(head=="/permissions"){
                if(parts[1]=="ask")SelectPermissions(AgentApprovalMode.Ask,true);
                else if(parts[1]=="auto")SelectPermissions(AgentApprovalMode.Auto,true);
                else if(parts[1]=="full")SelectPermissions(AgentApprovalMode.Full,false);
                else status.Text="使用 /permissions ask|auto|full";
            }else if(head=="/sandbox"&&(parts[1]=="on"||parts[1]=="off")){
                var mode=WorkSettings.AgentApprovalMode;if(mode==AgentApprovalMode.Legacy||mode==AgentApprovalMode.Full)mode=AgentApprovalMode.Ask;SelectPermissions(mode,parts[1]=="on");
            }else status.Text="使用 /sandbox on|off";
            return true;
        }
        void SelectPermissions(AgentApprovalMode mode,bool sandbox)
        {
            if(session.IsBusy||queue.HasPending){status.Text="请先停止当前任务并处理排队消息，再更改权限。";return;}
            var current=WorkSettings;bool expanding=mode==AgentApprovalMode.Full&&current.AgentApprovalMode!=AgentApprovalMode.Full||current.AgentSandboxEnabled&&!sandbox;
            if(expanding&&!(mode==AgentApprovalMode.Full?PermissionChangeDialog.ConfirmFullAccess(host):PermissionChangeDialog.Confirm(host,"关闭工作区沙箱？","将以当前 Windows 账户权限访问文件与网络。此设置只影响当前会话，不会自动获得管理员权限。")))return;
            Try(delegate{pet.SetConversationPermissions(session,mode,sandbox);RefreshMode();status.Text="当前会话："+AgentAccessPolicy.Label(WorkSettings)+"。";});
        }
        internal void OpenPermissions()
        {
            if(modePicker==null)return;var cfg=WorkSettings;var menu=ChatUi.Menu();menu.Name="AgentPermissionsMenu";menu.MinWidth=340;
            var heading=Ui.Text("应如何批准桌宠操作？",13,AgentUi.Muted);heading.Margin=new Thickness(0,2,0,7);menu.Items.Add(new MenuItem{Header=heading,IsEnabled=false});
            AddPermissionItem(menu,"请求批准","编辑外部文件和使用互联网时始终询问",AgentApprovalMode.Ask,cfg);
            AddPermissionItem(menu,"帮我批准","自动审核权限请求，不确定时请你批准",AgentApprovalMode.Auto,cfg);
            AddPermissionItem(menu,"完全访问权限","可访问当前账户可访问的文件与互联网",AgentApprovalMode.Full,cfg);
            modePicker.ContextMenu=menu;menu.PlacementTarget=modePicker;menu.Placement=PlacementMode.Top;menu.IsOpen=true;
        }
        void AddPermissionItem(ContextMenu menu,string title,string description,AgentApprovalMode mode,PetSettings cfg)
        {
            var color=mode==AgentApprovalMode.Full?Ui.Brush("#F36C21"):AgentUi.Ink;var text=new StackPanel{Margin=new Thickness(0,3,8,3)};text.Children.Add(Ui.Text(title,14,color));text.Children.Add(Ui.Text(description,12,mode==AgentApprovalMode.Full?color:AgentUi.Muted));
            var row=new Grid{Width=300};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(20)});row.Children.Add(PermissionGlyph(mode,color));Grid.SetColumn(text,1);row.Children.Add(text);var check=Ui.Text(AgentAccessPolicy.DisplayMode(cfg)==mode?"✓":"",15,AgentUi.Ink);check.VerticalAlignment=VerticalAlignment.Center;check.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(check,2);row.Children.Add(check);
            var item=new MenuItem{Name="Permission"+mode,Header=row,Padding=new Thickness(0,2,2,2)};item.Click+=delegate{SelectPermissions(mode,mode!=AgentApprovalMode.Full);};menu.Items.Add(item);
        }
        internal static FrameworkElement PermissionGlyph(AgentApprovalMode mode,Brush color)
        {
            string shape=mode==AgentApprovalMode.Ask?"M 5 8 L 5 3 C 5 1 7 1 7 3 L 7 7 L 7 2 C 7 0 9 0 9 2 L 9 7 L 9 3 C 9 1 11 1 11 3 L 11 8 L 11 5 C 11 3 13 3 13 5 L 13 10 C 13 15 7 17 4 13 L 1 9 C 0 7 2 6 3 8 L 5 10":mode==AgentApprovalMode.Full?"M 8 1 L 14 3 L 14 9 C 14 12 11 15 8 16 C 5 15 2 12 2 9 L 2 3 Z M 8 5 L 8 9 M 8 11 L 8 12":"M 8 1 L 14 3 L 14 9 C 14 12 11 15 8 16 C 5 15 2 12 2 9 L 2 3 Z M 5 8 L 7 10 L 11 6";
            return new System.Windows.Shapes.Path{Name="PermissionGlyph"+mode,Data=Geometry.Parse(shape),Stroke=color,StrokeThickness=1.35,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=16,Height=16,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left};
        }
    }
    internal sealed class PermissionChangeDialog:Window
    {
        internal PermissionChangeDialog(string title,string description,bool fullAccess=false)
        {
            Ui.Window(this,title,fullAccess?650:520,235);Name="PermissionChangeDialog";SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-24;
            var body=new StackPanel{Margin=new Thickness(26,24,26,24)};
            var heading=new DockPanel{Margin=new Thickness(0,0,22,0)};var warning=UiIcons.Image("warning",AgentUi.Ink);warning.Width=warning.Height=20;warning.VerticalAlignment=VerticalAlignment.Center;warning.Margin=new Thickness(0,0,10,0);heading.Children.Add(warning);var titleText=Ui.Text(title,21);titleText.FontWeight=FontWeights.SemiBold;titleText.LineHeight=28;titleText.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;titleText.TextWrapping=TextWrapping.Wrap;heading.Children.Add(titleText);body.Children.Add(heading);
            var detail=Ui.Text(description,14,AgentUi.Muted);detail.TextWrapping=TextWrapping.Wrap;detail.Margin=new Thickness(0,14,0,16);body.Children.Add(detail);
            if(fullAccess){
                var capabilities=new StackPanel();
                AddCapability(capabilities,"folder","文件和文件夹","读取、创建、编辑或删除当前账户可访问位置的文件。",false);
                AddCapability(capabilities,"terminal","终端命令","终端启用后，可运行命令、安装软件及更改当前账户设置。",true);
                AddCapability(capabilities,"globe","互联网和已连接的应用","访问网站、发送数据，并使用已启用的连接与工具。",true);
                body.Children.Add(new Border{Name="FullAccessCapabilities",Child=capabilities,Background=Ui.Brush("#EEEEF0"),CornerRadius=new CornerRadius(16),Padding=new Thickness(18,6,18,6)});
                var risk=Ui.Text("这可能带来敏感数据丢失或泄露、提示注入等风险。你可以随时切回其他模式；此设置不会自动获得管理员权限。",13,AgentUi.Muted);risk.Name="FullAccessRiskNotice";risk.TextWrapping=TextWrapping.Wrap;risk.Margin=new Thickness(0,16,0,20);body.Children.Add(risk);
            }
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=Ui.Button("取消");cancel.Name="CancelPermissionChange";cancel.IsCancel=true;cancel.IsDefault=true;cancel.MinWidth=82;cancel.Height=38;cancel.Padding=new Thickness(16,0,16,0);cancel.Click+=delegate{DialogResult=false;};actions.Children.Add(cancel);var allow=Ui.Button(fullAccess?"确认开启":"确认更改");allow.Name="ConfirmPermissionChange";allow.MinWidth=110;allow.Height=38;allow.Padding=new Thickness(16,0,16,0);allow.Margin=new Thickness(12,0,0,0);allow.Background=Ui.Brush("#FAE7E6");allow.BorderBrush=Brushes.Transparent;allow.Foreground=Ui.Brush("#D94A40");allow.Click+=delegate{DialogResult=true;};actions.Children.Add(allow);body.Children.Add(actions);DialogUi.Apply(this,new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        }
        static void AddCapability(StackPanel parent,string icon,string title,string description,bool divider)
        {
            var row=new Grid{Margin=new Thickness(0)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(30)});row.ColumnDefinitions.Add(new ColumnDefinition());var glyph=UiIcons.Image(icon,Ui.Brush(icon=="terminal"?"#53565C":"#279FE8"));glyph.Width=glyph.Height=23;glyph.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(glyph);var text=new StackPanel{Margin=new Thickness(12,10,0,10)};var label=Ui.Text(title,14);label.FontWeight=FontWeights.SemiBold;label.LineHeight=20;label.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;text.Children.Add(label);var explanation=Ui.Text(description,12,AgentUi.Muted);explanation.TextWrapping=TextWrapping.Wrap;explanation.LineHeight=18;explanation.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;text.Children.Add(explanation);Grid.SetColumn(text,1);row.Children.Add(text);parent.Children.Add(new Border{Child=row,BorderBrush=Ui.Brush("#DEDFE2"),BorderThickness=new Thickness(0,divider?1:0,0,0)});
        }
        internal static bool ConfirmFullAccess(Window owner){return new PermissionChangeDialog("要开启完全访问权限吗？","桌宠将能够在未经逐次批准的情况下，访问当前账户可访问的文件、互联网和已启用的工具。",true){Owner=owner}.ShowDialog()==true;}
        internal static bool Confirm(Window owner,string title,string description){return new PermissionChangeDialog(title,description){Owner=owner}.ShowDialog()==true;}
    }
}
