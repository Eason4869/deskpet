using System;
using System.Collections.Generic;

namespace Dafeiyu
{
    // A single elapsed-time timeline shared by both native home pages.
    public sealed class RelationshipHomeMotion
    {
        public const double CardSeconds = .35;
        public const double CardStagger = .09;
        public const double RelationReady = CardStagger + CardSeconds;
        public const double RingDegreesPerSecond = 306;
        readonly List<Track> tracks = new List<Track>();
        sealed class Track
        {
            internal double Delay, Duration;
            internal Action<double> Apply;
            internal bool Done;
        }
        public static double AlbumDelay(int index) { return .18 + Math.Max(0,index) * CardSeconds; }
        public static double Ease(double progress) { return 1 - Math.Pow(1-progress,3); }
        public static double RingDuration(double fraction) { return Math.Max(0,Math.Min(1,fraction)) * 360 / RingDegreesPerSecond; }
        public void Add(double delay,double duration,Action<double> apply)
        {
            tracks.Add(new Track{Delay=delay,Duration=duration,Apply=apply});
            apply(0);
        }
        // Returns true only while there are unfinished tracks. Late frames catch up.
        public bool Advance(double seconds)
        {
            bool running=false;
            foreach(var track in tracks)
            {
                if(track.Done)continue;
                double progress=seconds<track.Delay?0:track.Duration<=0?1:Math.Min(1,(seconds-track.Delay)/track.Duration);
                track.Apply(progress);
                track.Done=progress>=1;
                running|=!track.Done;
            }
            return running;
        }
        public void Finish()
        {
            foreach(var track in tracks)if(!track.Done){track.Apply(1);track.Done=true;}
            tracks.Clear();
        }
    }
}
