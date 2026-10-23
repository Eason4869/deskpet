using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Dafeiyu
{
    // A single quiet line above the composer, using the running task's records.
    internal sealed class AgentChangeStatus : TextBlock,IDisposable
    {
        ConversationSession session;readonly Window host;bool disposed,queued;
        internal AgentChangeStatus(PetWindow pet,Window window,ConversationSession sessionOverride=null)
        {
            session=sessionOverride??pet.Conversation;host=window;Name="AgentLiveChangeStatus";FontSize=12;Foreground=AgentUi.Muted;HorizontalAlignment=HorizontalAlignment.Center;Margin=new Thickness(0,0,0,9);TextWrapping=TextWrapping.NoWrap;Visibility=Visibility.Collapsed;
            session.AgentTaskChanged+=Changed;session.StateChanged+=Changed;host.Closed+=Closed;Refresh();
        }
        internal void Rebind(ConversationSession next)
        {
            if(next==null)throw new ArgumentNullException("session");if(ReferenceEquals(session,next))return;if(!disposed){session.AgentTaskChanged-=Changed;session.StateChanged-=Changed;}session=next;if(!disposed){session.AgentTaskChanged+=Changed;session.StateChanged+=Changed;}Refresh();
        }
        void Changed(){if(disposed || queued)return;queued=true;Dispatcher.BeginInvoke(new Action(delegate{queued=false;if(!disposed)Refresh();}));}
        void Refresh()
        {
            var task=session.CurrentAgentTask;Inlines.Clear();if(!session.IsBusy || task==null || task.Status!="running"){Visibility=Visibility.Collapsed;return;}
            var summary=AgentChangeSummaryData.FromTask(task);var files=summary.ActiveFiles;if(files.Count==0){Visibility=Visibility.Collapsed;return;}
            Inlines.Add(new Run(TextResources.Format("agentChangeStatus.refresh.inlines",files.Count)));if(summary.AddedLines.HasValue&&summary.RemovedLines.HasValue){Inlines.Add(new Run("  +"+summary.AddedLines.Value){Foreground=Ui.Brush("#159A5A")});Inlines.Add(new Run("  −"+summary.RemovedLines.Value){Foreground=Ui.Brush("#D65151")});}
            ToolTip=String.Join(Environment.NewLine,files.Take(5).Select(c=>Path.GetFileName(c.Path)))+(files.Count>5?Environment.NewLine+"…":"");Visibility=Visibility.Visible;
        }
        void Closed(object sender,EventArgs e){Dispose();}
        public void Dispose(){if(disposed)return;disposed=true;session.AgentTaskChanged-=Changed;session.StateChanged-=Changed;host.Closed-=Closed;}
    }
}
