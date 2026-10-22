using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace Dafeiyu
{
    public sealed class HolidayCloud
    {
        public double X {get;internal set;} public double Y {get;internal set;} public double Radius {get;internal set;} public double Opacity {get;internal set;}
    }
    public sealed class HolidayTransition
    {
        double elapsed;bool committed;
        public string TargetOutfitId {get;private set;} public bool Active {get;private set;} public bool CommitPending {get;private set;}
        public double Progress {get{return Math.Min(1,elapsed/.8);}}
        public double Opacity {get{return !Active?0:elapsed<.25?elapsed/.25:elapsed<=.45?1:Math.Max(0,(.8-elapsed)/.35);}}
        public IList<HolidayCloud> Clouds
        {
            get {
                var clouds=new List<HolidayCloud>();if(Active)for(int i=0;i<8;i++) {
                    double angle=i*Math.PI/4;double spread=.18+(1-Opacity)*.55;
                    clouds.Add(new HolidayCloud{X=.5+Math.Cos(angle)*spread,Y=.5+Math.Sin(angle)*spread,Radius=.56,Opacity=Opacity});
                }return new ReadOnlyCollection<HolidayCloud>(clouds);
            }
        }
        public void Start(string targetOutfitId){TargetOutfitId=targetOutfitId;elapsed=0;committed=false;CommitPending=false;Active=true;}
        public void Advance(double deltaSeconds)
        {if(!Active || Double.IsNaN(deltaSeconds) || Double.IsInfinity(deltaSeconds) || deltaSeconds<=0)return;elapsed+=deltaSeconds;if(elapsed>=.35 && !committed){committed=true;CommitPending=true;}if(elapsed>=.8){elapsed=.8;Active=false;}}
        public bool TakeCommit(){if(!CommitPending)return false;CommitPending=false;return true;}
        public void FinishImmediately(){if(Active && !committed){committed=true;CommitPending=true;}elapsed=.8;Active=false;}
    }
}
