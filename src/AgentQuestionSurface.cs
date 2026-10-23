using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    internal sealed class AgentQuestionSurface:Border,IDisposable
    {
        readonly AgentQuestionController controller;readonly Func<AgentQuestionRequest,bool> accepts;readonly StackPanel cards=new StackPanel();bool disposed;
        internal AgentQuestionSurface(AgentQuestionController controller,Func<AgentQuestionRequest,bool> accepts)
        {
            this.controller=controller;this.accepts=accepts;Name="AgentQuestionSurface";var scroll=Ui.Scroll(cards);scroll.MaxHeight=340;Child=scroll;Margin=new Thickness(0,0,0,10);if(controller!=null)controller.Changed+=Refresh;Refresh();
        }
        internal static bool Matches(ConversationSession session,AgentQuestionRequest request){var kernel=session.KernelSession;var task=session.CurrentAgentTask;return kernel!=null&&task!=null&&request.SessionId==kernel.SessionId&&request.TaskId==task.Id;}
        internal void Refresh(){if(disposed||Dispatcher.HasShutdownStarted)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(Refresh));return;}cards.Children.Clear();if(controller!=null)foreach(var request in controller.Requests.Where(accepts))cards.Children.Add(new AgentQuestionCard(controller,request));Visibility=cards.Children.Count==0?Visibility.Collapsed:Visibility.Visible;}
        public void Dispose(){disposed=true;if(controller!=null)controller.Changed-=Refresh;cards.Children.Clear();}
    }
    internal sealed class AgentQuestionCard:Border
    {
        internal AgentQuestionCard(AgentQuestionController controller,AgentQuestionRequest request)
        {
            Name="AgentQuestionCard";Tag=request.CallId;CornerRadius=new CornerRadius(14);Padding=new Thickness(14);BorderThickness=new Thickness(1);BorderBrush=Ui.Line;Background=ThemeManager.Brush("#FAFBFD","#151D2A");Margin=new Thickness(0,0,0,6);
            var body=new StackPanel();Child=body;body.Children.Add(Ui.Text(request.Question,14,Ui.Ink));body.Children.Add(Ui.Text(TextResources.Get("agentQuestionSurface.agentQuestionCard.text"),11,Ui.Muted));
            for(int i=0;i<request.Options.Length;i++){int index=i;var option=Ui.Button(request.Options[i]);option.Name="AgentQuestionOption"+i;option.Content=new TextBlock{Text=request.Options[i],TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center};option.HorizontalAlignment=HorizontalAlignment.Stretch;option.Click+=delegate{if(controller.Submit(request.SessionId,request.TaskId,request.CallId,new AgentQuestionAnswer{OptionIndex=index}))IsEnabled=false;};body.Children.Add(option);}
            if(request.AllowFreeInput){var input=Ui.Field("",true);input.Name="AgentQuestionAnswer";input.MaxLength=8000;input.MaxHeight=100;body.Children.Add(input);var send=Ui.Button(TextResources.Get("agentQuestionSurface.agentQuestionCard.text.alternativeA"),true);send.Name="SubmitAgentQuestion";send.Click+=delegate{if(controller.Submit(request.SessionId,request.TaskId,request.CallId,new AgentQuestionAnswer{Text=input.Text}))IsEnabled=false;};body.Children.Add(send);}
        }
    }
}
