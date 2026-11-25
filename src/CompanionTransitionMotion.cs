using System;

namespace Dafeiyu
{
    internal static class CompanionTransitionMotion
    {
        internal const string Off="off", Ripple="ripple", Zipper="zipper", RandomSelection="random";
        internal static readonly string[] Choices={Off,Ripple,Zipper,RandomSelection};
        static readonly Random random=new Random();

        internal static string Normalize(string value)
        {return Array.IndexOf(Choices,value)>=0?value:Ripple;}

        internal static string Resolve(string value)
        {
            value=Normalize(value);
            if(value!=RandomSelection)return value;
            lock(random)return random.Next(2)==0?Ripple:Zipper;
        }

        internal static TimeSpan Duration(string effect)
        {return effect==Off?TimeSpan.Zero:TimeSpan.FromMilliseconds(effect==Zipper?700:650);}

        internal static string Caption(){return TextResources.Get("companionTransition.motion.label");}

        internal static string Label(string value)
        {
            switch(value)
            {
                case Off:return TextResources.Get("companionTransition.motion.off");
                case Zipper:return TextResources.Get("companionTransition.motion.zipper");
                case RandomSelection:return TextResources.Get("companionTransition.motion.random");
                default:return TextResources.Get("companionTransition.motion.ripple");
            }
        }
    }
}
