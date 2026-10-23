using System;
namespace Dafeiyu
{
    public sealed class HolidayPlayback
    {
        double elapsed;
        public HolidayPlaybackTicket Ticket {get;private set;} public int Frame {get;private set;}
        public bool Active {get;private set;} public bool Completed {get;private set;}
        public bool FirstFrameRendered {get{return Ticket!=null && Ticket.FirstFrameRendered;}}
        public void Start(HolidayPlaybackTicket ticket)
        {
            Cancel();if(ticket==null || ticket.Cancelled || ticket.Clip==null || ticket.Clip.Loop)return;
            Ticket=ticket;Frame=0;elapsed=0;Active=true;Completed=false;
        }
        public void MarkFirstFrameRendered(){if(Active && Ticket!=null && !Ticket.Cancelled)Ticket.FirstFrameRendered=true;}
        public void Advance(double deltaSeconds)
        {
            if(!Active)return;if(Ticket.Cancelled){Cancel();return;}
            if(!FirstFrameRendered || Double.IsNaN(deltaSeconds) || Double.IsInfinity(deltaSeconds) || deltaSeconds<=0)return;
            elapsed+=deltaSeconds;double duration=1/Ticket.Clip.Fps;
            if(elapsed>=duration) {
                // A late UI frame advances one pose, preserving a visible duration for every pose.
                elapsed=0;
                if(Frame+1<Ticket.Clip.FrameCount)Frame++;
                else {Active=false;Completed=true;Ticket.PlaybackCompleted=true;}
            }
        }
        public void Cancel(){if(Ticket!=null && !Completed)Ticket.PlaybackCompleted=false;Ticket=null;Active=false;Completed=false;Frame=0;elapsed=0;}
    }
}
