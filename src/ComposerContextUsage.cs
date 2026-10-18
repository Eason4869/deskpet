using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Dafeiyu
{
    internal sealed partial class ConversationComposer
    {
        Button contextPreview;Path contextArc;ToolTip contextTip;
        FrameworkElement CreateContextControls()
        {
            var panel=new Grid{HorizontalAlignment=HorizontalAlignment.Right,MinWidth=0};panel.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});panel.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=0});
            var ring=new Grid{Width=14,Height=14};ring.Children.Add(new Ellipse{Stroke=Ui.Brush("#DADDE0"),StrokeThickness=2});contextArc=new Path{Stroke=Ui.Brush("#747B83"),StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};ring.Children.Add(contextArc);
            contextPreview=Ui.Button("");contextPreview.Name="ContextUsagePreview";contextPreview.Content=ring;contextPreview.Width=28;contextPreview.Height=32;contextPreview.MinWidth=0;contextPreview.MinHeight=0;contextPreview.Padding=new Thickness(0);contextPreview.Margin=new Thickness(0);contextPreview.Background=Brushes.Transparent;contextPreview.BorderBrush=Brushes.Transparent;contextPreview.VerticalAlignment=VerticalAlignment.Center;
            contextTip=new ToolTip{Template=hintTemplate,Foreground=Brushes.White,Placement=System.Windows.Controls.Primitives.PlacementMode.Top,HorizontalContentAlignment=HorizontalAlignment.Center};contextPreview.RenderTransform=new TranslateTransform(0,4);contextPreview.ToolTip=contextTip;host.Closed+=delegate{contextTip.IsOpen=false;};ToolTipService.SetInitialShowDelay(contextPreview,200);contextPreview.ToolTipOpening+=delegate{RefreshContextUsage();};contextPreview.Click+=delegate{RefreshContextUsage();contextTip.PlacementTarget=contextPreview;contextTip.IsOpen=!contextTip.IsOpen;};
            panel.Children.Add(contextPreview);Grid.SetColumn(modelPicker,1);panel.Children.Add(modelPicker);return panel;
        }
        void RefreshContextUsage()
        {
            if(contextPreview==null)return;contextPreview.Visibility=pet.Settings.ShowContextUsage?Visibility.Visible:Visibility.Collapsed;if(!pet.Settings.ShowContextUsage){contextTip.IsOpen=false;return;}var model=pet.Settings.Text;int capacity=model.ContextWindowTokens;var usage=session.CurrentContextUsage;bool measured=usage!=null&&usage.Model==model.Model&&usage.Endpoint==model.BaseUrl;int tokens;
            if(measured)tokens=usage.Tokens;else{var messages=session.History.ForRequest(pet.Settings.SystemPrompt??"",input.Text??"");tokens=AgentContextBudget.EstimateTokens(messages.Select(m=>(object)new{role=m.Role,content=ChatAttachment.Content(m,model.Protocol=="anthropic")}));}
            double ratio=capacity>0?Math.Min(1,(double)tokens/capacity):0;double angle=Math.Max(.01,Math.Min(.999,ratio))*2*Math.PI;var figure=new PathFigure{StartPoint=new Point(7,1)};figure.Segments.Add(new ArcSegment(new Point(7+6*Math.Sin(angle),7-6*Math.Cos(angle)),new Size(6,6),0,ratio>.5,SweepDirection.Clockwise,true));contextArc.Data=new PathGeometry(new[]{figure});contextArc.Visibility=capacity>0?Visibility.Visible:Visibility.Collapsed;
            string text="上下文窗口（估算）\n"+(capacity>0?Math.Round(100.0*tokens/capacity)+"% 已用\n约 "+tokens.ToString("N0")+" 标记，共 "+capacity.ToString("N0"):"约 "+tokens.ToString("N0")+" 标记 · 总容量未知\n请在模型配置中填写上下文容量");
            var policy=AgentContextPolicy.Resolve(ModelContextCapacity.Budget(model,WorkSettings.AgentContextTokenLimit));text+="\n"+(measured?"上次请求输入预算：":"输入预算上限：")+(measured?usage.Budget:policy.InputBudget).ToString("N0")+(measured?"（已扣工具与协议开销）":"（发送前再扣工具与协议开销）")+"\n输出预留："+policy.OutputReserve.ToString("N0")+" · 安全预留："+policy.SafetyReserve.ToString("N0");if(measured&&usage.Status=="compacting")text+="\n正在压缩上下文…";else if(measured&&usage.Compactions>0)text+="\n已自动压缩 "+usage.Compactions+" 次";text+="\n"+(measured?"上次请求的上下文；实际计费以服务端为准":"按待发送历史估算；实际计费以服务端为准");
            contextTip.Content=new TextBlock{Text=text,TextAlignment=TextAlignment.Center,Foreground=Brushes.White,FontSize=12};System.Windows.Automation.AutomationProperties.SetName(contextPreview,text);
        }
    }
}
