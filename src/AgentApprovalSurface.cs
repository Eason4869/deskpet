using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace Dafeiyu
{
    // The presenter owns only a view. Pending decisions remain in the controller
    // when a composer is rebound, hidden, or replaced by another conversation.
    internal sealed class AgentApprovalSurface:IDisposable
    {
        static readonly List<AgentApprovalSurface> presenters=new List<AgentApprovalSurface>();
        static readonly Dictionary<AgentApprovalController.Pending,AgentApprovalSurface> claims=new Dictionary<AgentApprovalController.Pending,AgentApprovalSurface>();
        readonly AgentApprovalController controller;readonly Func<AgentApprovalRequest,bool> accepts;readonly Window operationHost;
        readonly FrameworkElement inputAnchor;
        AgentApprovalController.Pending shown;Window dialog;bool disposed,suppressClose;
        internal Window Presentation {get{return dialog;}}
        internal AgentApprovalSurface(AgentApprovalController controller,Func<AgentApprovalRequest,bool> accepts,Window operationHost,FrameworkElement inputAnchor=null)
        {
            this.controller=controller;this.accepts=accepts;this.operationHost=operationHost;this.inputAnchor=inputAnchor;
            operationHost.IsVisibleChanged+=HostVisibilityChanged;operationHost.Closed+=HostClosed;presenters.Add(this);
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
            if(disposed)return;
            var requests=controller==null?new AgentApprovalController.Pending[0]:controller.Requests.Where(p=>accepts(p.Request)).ToArray();
            if(dialog!=null&&(!operationHost.IsVisible||!requests.Contains(shown)))ClosePresentation();
            if(dialog!=null||!operationHost.IsVisible)return;
            var pending=requests.FirstOrDefault(p=>!claims.ContainsKey(p)&&!presenters.Any(other=>other!=this&&!other.disposed&&ReferenceEquals(other.controller,controller)&&other.operationHost.IsActive&&!operationHost.IsActive&&other.accepts(p.Request)));if(pending==null)return;
            claims.Add(pending,this);shown=pending;
            var window=new Window{Name="AgentApprovalWindow",Tag=pending.Request.CallId,Owner=operationHost,SizeToContent=SizeToContent.Height};dialog=window;
            LocalizedUi.Window(window,()=>TextResources.Get("agentApprovalSurface.card.text"),560,460);window.MinWidth=300;window.MinHeight=0;window.Height=Double.NaN;
            var source=PresentationSource.FromVisual(operationHost);double scale=source==null?1:source.CompositionTarget.TransformToDevice.M11;
            var area=DesktopInterop.WorkArea(new System.Windows.Interop.WindowInteropHelper(operationHost).Handle);double available=Math.Max(1,area.Height/scale-24),availableWidth=Math.Max(1,area.Width/scale-24);
            window.MinWidth=Math.Min(300,availableWidth);window.Width=Math.Min(560,availableWidth);window.MaxWidth=availableWidth;window.MaxHeight=available;
            var content=Card(pending,Math.Max(1,available-110));DialogUi.Apply(window,new AdornerDecorator{Child=content});
            window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
            if(AlignToInput(window))
            {
                window.WindowStartupLocation=WindowStartupLocation.Manual;
                window.Opacity=0;
            }
            EventHandler align=delegate{AlignToInput(window);};
            if(inputAnchor!=null)
            {
                inputAnchor.LayoutUpdated+=align;
                operationHost.LocationChanged+=align;
                window.SizeChanged+=delegate{AlignToInput(window);};
            }
            window.Closed+=delegate{
                if(inputAnchor!=null){inputAnchor.LayoutUpdated-=align;operationHost.LocationChanged-=align;}
                if(!suppressClose&&!pending.Result.Task.IsCompleted)pending.Decide(false);
                if(ReferenceEquals(dialog,window)){dialog=null;shown=null;claims.Remove(pending);}RefreshPresenters();
            };
            window.Loaded+=delegate{AlignToInput(window);window.Opacity=1;var reject=FindReject(content);if(reject!=null)reject.Focus();};window.Show();
        }
        bool AlignToInput(Window window)
        {
            if(inputAnchor==null||!inputAnchor.IsVisible||inputAnchor.ActualWidth<=0)return false;
            var source=PresentationSource.FromVisual(inputAnchor);
            if(source==null||source.CompositionTarget==null)return false;

            var scale=source.CompositionTarget.TransformToDevice;
            var left=inputAnchor.PointToScreen(new Point(0,0));
            var right=inputAnchor.PointToScreen(new Point(inputAnchor.ActualWidth,0));
            var area=DesktopInterop.WorkArea(new Point((left.X+right.X)/2,left.Y));
            var bottom=Math.Min(area.Bottom,inputAnchor.PointToScreen(new Point(0,-8)).Y);
            var ownerHandle=new WindowInteropHelper(operationHost).Handle;
            var top=Math.Max(area.Top,DesktopInterop.Bounds(ownerHandle).Top);

            var width=Math.Min(inputAnchor.ActualWidth,area.Width/scale.M11);
            var height=Math.Max(96,(bottom-top)/scale.M22);
            window.MinWidth=0;
            window.MaxWidth=area.Width/scale.M11;
            if(Math.Abs(window.Width-width)>.1)window.Width=width;
            if(Math.Abs(window.MaxHeight-height)>.1||Double.IsInfinity(window.MaxHeight))window.MaxHeight=height;

            var handle=new WindowInteropHelper(window).Handle;
            if(handle==IntPtr.Zero)return true;
            var bounds=DesktopInterop.Bounds(handle);
            var x=Math.Max(area.Left,Math.Min(left.X,area.Right-bounds.Width));
            var y=Math.Max(area.Top,bottom-bounds.Height);
            if(Math.Abs(bounds.Left-x)>.5||Math.Abs(bounds.Top-y)>.5)DesktopInterop.Move(handle,new Point(x,y));
            return true;
        }
        static Button FindReject(DependencyObject root){if(root is Button&&((Button)root).Name=="RejectAgentOperation")return(Button)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=FindReject(VisualTreeHelper.GetChild(root,i));if(found!=null)return found;}return null;}
        static void RefreshPresenters(){var app=Application.Current;if(app!=null&&!app.Dispatcher.HasShutdownStarted)app.Dispatcher.BeginInvoke(new Action(delegate{foreach(var presenter in presenters.ToArray())presenter.Refresh();}));}
        void ClosePresentation(){var window=dialog;var pending=shown;dialog=null;shown=null;if(pending!=null)claims.Remove(pending);if(window!=null){suppressClose=true;try{window.Close();}finally{suppressClose=false;}}}
        void HostVisibilityChanged(object sender,DependencyPropertyChangedEventArgs args){Refresh();RefreshPresenters();}
        void HostClosed(object sender,EventArgs args){if(shown!=null)shown.Decide(false);Dispose();}
        FrameworkElement Card(AgentApprovalController.Pending pending,double maxBodyHeight)
        {
            var request=pending.Request;var body=new StackPanel();
            var header=new DockPanel{Margin=new Thickness(0,0,32,10)};var icon=UiIcons.Image(request.Action=="shell"?"terminal":request.Action=="network"?"globe":"file",Ui.Blue);icon.Width=icon.Height=20;icon.Margin=new Thickness(0,2,10,0);DockPanel.SetDock(icon,Dock.Left);header.Children.Add(icon);
            var title=new StackPanel();title.Children.Add(LocalizedUi.With(new TextBlock{Text=TextResources.Get("agentApprovalSurface.card.text"),FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=Ui.Ink},"Text",()=>(TextResources.Get("agentApprovalSurface.card.text"))));title.Children.Add(LocalizedUi.Text(()=>OperationLabel(request),11,Ui.Muted));header.Children.Add(title);body.Children.Add(header);
            string preview=request.Arguments??"";
            if(request.Action=="shell")try{var args=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.DeserializeObject(preview) as Dictionary<string,object>;object command;if(args!=null&&args.TryGetValue("command",out command)&&command is string)preview=(string)command;}catch(ArgumentException){}catch(InvalidOperationException){}
            var commandBox=Ui.Field(Bounded(preview),true);commandBox.Name="AgentApprovalArguments";commandBox.FontFamily=new FontFamily("Consolas");commandBox.FontSize=12;commandBox.IsReadOnly=true;commandBox.MaxHeight=112;commandBox.Background=ThemeManager.Brush("#F0F3F8","#151D2A");commandBox.BorderThickness=new Thickness(0);commandBox.ContextMenu=ChatUi.EditorMenu(commandBox,true);body.Children.Add(commandBox);
            var resource=Ui.Text(request.Resource??"",11,Ui.Muted);resource.Name="AgentApprovalResource";resource.Margin=new Thickness(0,7,0,4);body.Children.Add(resource);
            if(request.Action=="shell"){var isolation=LocalizedUi.Text(()=>(request.IsolationDescription??TextResources.Get("agentApprovalSurface.card.text.alternativeA")),11,Ui.Muted);isolation.Margin=new Thickness(0,2,0,4);body.Children.Add(isolation);}
            if(!String.IsNullOrWhiteSpace(request.ReviewReason))body.Children.Add(Ui.Text(request.ReviewReason,11,Ui.Muted));
            var details=new StackPanel();details.Children.Add(LocalizedUi.Text(()=>(TextResources.Format("agentApprovalSurface.card.template",request.ProjectId,request.TaskId,request.CallId)),11,Ui.Muted));
            var raw=Ui.Field(Bounded(request.Arguments??""),true);raw.IsReadOnly=true;raw.MaxHeight=120;raw.FontSize=11;raw.ContextMenu=ChatUi.EditorMenu(raw,true);details.Children.Add(raw);
            var expander=new Expander{Name="AgentApprovalDetails",Header=LocalizedUi.InlineText(()=>(TextResources.Get("agentApprovalSurface.card.inlineText")),11,Ui.Muted),Content=details,Foreground=Ui.Muted,FontSize=11,Margin=new Thickness(0,4,0,8)};ChatUi.ProcessExpander(expander);body.Children.Add(expander);
            var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};
            var reject=LocalizedUi.Button(()=>(TextResources.Get("agentApprovalSurface.card.text.alternativeB")));reject.Name="RejectAgentOperation";reject.MinHeight=32;reject.IsDefault=true;reject.Click+=delegate{pending.Decide(false);Refresh();};buttons.Children.Add(reject);
            var allow=LocalizedUi.Button(()=>(TextResources.Get("agentApprovalSurface.card.text.alternativeC")),true);allow.Name="AllowAgentOperation";allow.MinHeight=32;allow.Margin=new Thickness(0);allow.Click+=delegate{pending.Decide(true);Refresh();};buttons.Children.Add(allow);
            var root=new DockPanel{Name="AgentApprovalCard",Tag=request.CallId,Margin=new Thickness(20)};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);var scroll=Ui.Scroll(body);scroll.MaxHeight=Math.Min(420,maxBodyHeight);root.Children.Add(scroll);return root;
        }
        static string Bounded(string value){return value.Length<=24000?value:TextResources.Format("agentApprovalSurface.bounded.template",value.Substring(0,24000));}
        static string OperationLabel(AgentApprovalRequest request){return request.Action=="shell"?TextResources.Get("agentApprovalSurface.operationLabel.text"):request.Action=="network"?TextResources.Get("agentApprovalSurface.operationLabel.text.alternativeA"):request.Action=="write"?TextResources.Get("agentApprovalSurface.operationLabel.text.alternativeB"):request.ToolName??TextResources.Get("agentApprovalSurface.operationLabel.text.alternativeC");}
        public void Dispose(){if(disposed)return;disposed=true;if(controller!=null)controller.Changed-=Refresh;operationHost.IsVisibleChanged-=HostVisibilityChanged;operationHost.Closed-=HostClosed;presenters.Remove(this);ClosePresentation();RefreshPresenters();}
    }
}
