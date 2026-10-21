using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace Dafeiyu
{
    internal sealed class AgentApprovalSurface:Border,IDisposable
    {
        readonly AgentApprovalController controller;readonly Func<AgentApprovalRequest,bool> accepts;
        readonly StackPanel cards=new StackPanel();bool disposed;
        internal AgentApprovalSurface(AgentApprovalController controller,Func<AgentApprovalRequest,bool> accepts)
        {
            this.controller=controller;this.accepts=accepts;Name="AgentApprovalSurface";Visibility=Visibility.Collapsed;
            var scroll=Ui.Scroll(cards);scroll.MaxHeight=340;Child=scroll;Margin=new Thickness(0,0,0,10);
            Loaded+=delegate{Refresh();};SizeChanged+=delegate{var host=Window.GetWindow(this);if(host!=null)scroll.MaxHeight=Math.Max(130,Math.Min(340,host.ActualHeight*.42));};
            if(controller!=null)controller.Changed+=Refresh;Refresh();
        }
        internal static bool Matches(ConversationSession session,AgentApprovalRequest request)
        {
            var kernel=session.KernelSession;
            if(!String.IsNullOrEmpty(request.SessionId))return kernel!=null&&request.SessionId==kernel.SessionId;
            var task=session.CurrentAgentTask;return task!=null&&!String.IsNullOrEmpty(request.TaskId)&&request.TaskId==task.Id;
        }
        void Refresh()
        {
            if(disposed)return;cards.Children.Clear();
            if(controller!=null)foreach(var item in controller.Requests.Where(p=>accepts(p.Request)))cards.Children.Add(Card(item));
            Visibility=cards.Children.Count==0?Visibility.Collapsed:Visibility.Visible;
        }
        static FrameworkElement Card(AgentApprovalController.Pending pending)
        {
            var request=pending.Request;var body=new StackPanel();
            var header=new DockPanel{Margin=new Thickness(0,0,0,10)};var icon=UiIcons.Image(request.Action=="shell"?"terminal":request.Action=="network"?"globe":"file",Ui.Blue);icon.Width=icon.Height=20;icon.Margin=new Thickness(0,2,10,0);DockPanel.SetDock(icon,Dock.Left);header.Children.Add(icon);
            var title=new StackPanel();title.Children.Add(new TextBlock{Text="允许本次操作？",FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=Ui.Ink});title.Children.Add(Ui.Text(OperationLabel(request),11,Ui.Muted));header.Children.Add(title);body.Children.Add(header);
            string preview=request.Arguments??"";
            if(request.Action=="shell")try{var args=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.DeserializeObject(preview) as Dictionary<string,object>;object command;if(args!=null&&args.TryGetValue("command",out command)&&command is string)preview=(string)command;}catch(ArgumentException){}catch(InvalidOperationException){}
            var commandBox=Ui.Field(Bounded(preview),true);commandBox.Name="AgentApprovalArguments";commandBox.FontFamily=new FontFamily("Consolas");commandBox.FontSize=12;commandBox.IsReadOnly=true;commandBox.MaxHeight=112;commandBox.Background=ThemeManager.Brush("#F0F3F8","#151D2A");commandBox.BorderThickness=new Thickness(0);commandBox.ContextMenu=ChatUi.EditorMenu(commandBox,true);body.Children.Add(commandBox);
            var resource=Ui.Text(request.Resource??"",11,Ui.Muted);resource.Name="AgentApprovalResource";resource.Margin=new Thickness(0,7,0,4);body.Children.Add(resource);
            if(request.Action=="shell"){var isolation=Ui.Text(request.IsolationDescription??"将在当前账户下执行，请核对命令及其可能访问的资源。",11,Ui.Muted);isolation.Margin=new Thickness(0,2,0,4);body.Children.Add(isolation);}
            if(!String.IsNullOrWhiteSpace(request.ReviewReason))body.Children.Add(Ui.Text(request.ReviewReason,11,Ui.Muted));
            var details=new StackPanel();details.Children.Add(Ui.Text("项目："+request.ProjectId+"\n任务："+request.TaskId+"\n调用："+request.CallId,11,Ui.Muted));
            var raw=Ui.Field(Bounded(request.Arguments??""),true);raw.IsReadOnly=true;raw.MaxHeight=120;raw.FontSize=11;raw.ContextMenu=ChatUi.EditorMenu(raw,true);details.Children.Add(raw);
            var expander=new Expander{Name="AgentApprovalDetails",Header=Ui.InlineText("详细信息",11,Ui.Muted),Content=details,Foreground=Ui.Muted,FontSize=11,Margin=new Thickness(0,4,0,8)};ChatUi.ProcessExpander(expander);body.Children.Add(expander);
            var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
            var reject=Ui.Button("拒绝");reject.Name="RejectAgentOperation";reject.MinHeight=32;reject.Click+=delegate{pending.Decide(false);};buttons.Children.Add(reject);
            var allow=Ui.Button("仅本次允许",true);allow.Name="AllowAgentOperation";allow.MinHeight=32;allow.Margin=new Thickness(0);allow.Click+=delegate{pending.Decide(true);};buttons.Children.Add(allow);body.Children.Add(buttons);
            return new Border{Name="AgentApprovalCard",Tag=request.CallId,Child=body,CornerRadius=new CornerRadius(14),Padding=new Thickness(14),BorderThickness=new Thickness(1),BorderBrush=Ui.Line,Background=Ui.Brush("#FAFBFD"),Margin=new Thickness(0,0,0,6)};
        }
        static string Bounded(string value){return value.Length<=24000?value:value.Substring(0,24000)+"\n（预览已截断）";}
        static string OperationLabel(AgentApprovalRequest request){return request.Action=="shell"?"运行命令":request.Action=="network"?"读取网页":request.Action=="write"?"修改项目文件":request.ToolName??"Agent 操作";}
        public void Dispose(){if(disposed)return;disposed=true;if(controller!=null)controller.Changed-=Refresh;cards.Children.Clear();Visibility=Visibility.Collapsed;}
    }
}
