using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class LiveChangeSummary:Button
    {
        readonly TextBlock label=new TextBlock{Name="AgentLiveChangeStatus",FontSize=12,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};
        readonly Action<AgentTaskState> open;AgentTaskState task;
        internal AgentChangeSummaryData Summary{get;private set;}
        internal LiveChangeSummary(Action<AgentTaskState> open)
        {
            this.open=open;Name="AgentLiveChangeSummary";Style=Ui.Button("").Style;Content=label;MinHeight=32;Padding=new Thickness(12,5,12,5);Margin=new Thickness(0);HorizontalAlignment=HorizontalAlignment.Right;Background=ThemeManager.Surface;BorderBrush=AgentUi.Line;Foreground=AgentUi.Ink;Cursor=System.Windows.Input.Cursors.Hand;Visibility=Visibility.Collapsed;
            Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=12,ShadowDepth=1,Opacity=.10,Color=Colors.Black};Summary=AgentChangeSummaryData.FromTask(null);
            Click+=delegate{if(task!=null&&Visibility==Visibility.Visible&&Summary.FileCount>0&&this.open!=null)this.open(AgentChangeSummaryData.CopyTask(task));};
        }
        internal void Refresh(AgentTaskState snapshot,bool working)
        {
            task=working&&snapshot!=null&&snapshot.Status=="running"?AgentChangeSummaryData.CopyTask(snapshot):null;Summary=AgentChangeSummaryData.FromTask(task);label.Inlines.Clear();
            if(task==null||Summary.FileCount==0){task=null;Visibility=Visibility.Collapsed;ToolTip=null;IsEnabled=false;return;}
            label.Inlines.Add(LocalizedUi.With(new Run(TextResources.Format("liveChangeSummary.refresh.labelInlines",Summary.FileCount)){Foreground=AgentUi.Ink},"Text",()=>(TextResources.Format("liveChangeSummary.refresh.labelInlines",Summary.FileCount))));
            if(Summary.AddedLines.HasValue&&Summary.RemovedLines.HasValue){label.Inlines.Add(new Run("   +"+Summary.AddedLines.Value){Foreground=Ui.Brush("#159A5A")});label.Inlines.Add(new Run("  −"+Summary.RemovedLines.Value){Foreground=Ui.Brush("#D65151")});}
            else label.Inlines.Add(LocalizedUi.With(new Run(TextResources.Get("liveChangeSummary.refresh.labelInlines.alternativeA")){Foreground=AgentUi.Muted},"Text",()=>(TextResources.Get("liveChangeSummary.refresh.labelInlines.alternativeA"))));
            ToolTip=TextResources.Get("liveChangeSummary.refresh.toolTip");IsEnabled=open!=null;Visibility=Visibility.Visible;
        }
    }
}
