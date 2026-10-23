using System;

namespace Dafeiyu
{
    public sealed class GreetingSchedule
    {
        readonly int minimum,maximum;
        readonly Func<double> sample;
        DateTime previous;
        public DateTime NextAt {get;private set;}
        public GreetingSchedule(DateTime now,int minMinutes,int maxMinutes,Func<double> randomSample)
        {
            minimum=Math.Max(1,Math.Min(1440,minMinutes));
            maximum=Math.Max(minimum,Math.Min(1440,maxMinutes));
            sample=randomSample ?? new Random().NextDouble;
            Reset(now);
        }
        public void Reset(DateTime now)
        {
            double value=sample();if(Double.IsNaN(value) || Double.IsInfinity(value))value=.5;
            NextAt=now.AddMinutes(minimum+(maximum-minimum)*Math.Max(0,Math.Min(1,value)));
            previous=now;
        }
        public bool TryTake(DateTime now,bool allowed)
        {
            // A suspended PC or a hidden/paused timer never accumulates greetings.
            if(now<previous || now-previous>TimeSpan.FromMinutes(2)){Reset(now);return false;}
            previous=now;if(now<NextAt)return false;
            // Short walks, clicks and open panels defer one due greeting; they
            // must not restart a 30-minute wait on every return to idle.
            if(!allowed){NextAt=now.AddSeconds(5);return false;}
            Reset(now);return true;
        }
        public void Defer(DateTime now){previous=now;NextAt=now.AddSeconds(5);}
    }

    public static class GreetingPhrases
    {
        public const int Count=12;
        public static string Choose(string skinId,string name,DateTime localTime,int index)
        {
            string greeting=localTime.Hour<6?TextResources.Get("greetingSchedule.choose.hourLocalTimeHour"):localTime.Hour<11?TextResources.Get("greetingSchedule.choose.hourLocalTimeHour.alternativeA"):localTime.Hour<14?TextResources.Get("greetingSchedule.choose.hourLocalTimeHour.alternativeB"):localTime.Hour<18?TextResources.Get("greetingSchedule.choose.hourLocalTimeHour.alternativeC"):TextResources.Get("greetingSchedule.choose.hourLocalTimeHour.alternativeD");
            string[] lines;
            if(skinId=="e-bao")lines=new[]{TextResources.Format("greetingSchedule.choose.linesGreeting",greeting),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeC"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeD"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeE"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeF"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeG"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeH"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeI"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeJ"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeK"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeL"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeM")};
            else if(skinId=="dafeiyu")lines=new[]{TextResources.Format("greetingSchedule.choose.linesGreeting.alternativeA",greeting),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeN"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeO"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeP"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeQ"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeR"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeS"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeT"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeU"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeV"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeW"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeX")};
            else if(skinId=="marfu")lines=new[]{TextResources.Format("greetingSchedule.choose.linesGreeting.alternativeB",greeting),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeY"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeZ"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAA"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAB"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAC"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAD"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAE"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAF"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAG"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAH"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAI")};
            else lines=new[]{greeting+TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAJ")+(name??TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAK"))+TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAL"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAM"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeO"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAN"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAO"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAP"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAQ"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAR"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAS"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAT"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAU"),TextResources.Get("greetingSchedule.choose.linesGreeting.alternativeAV")};
            return lines[(int)((uint)index%(uint)lines.Length)];
        }
    }
}
