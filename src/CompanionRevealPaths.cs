using System;

namespace Dafeiyu
{
    internal sealed class CompanionRevealPaths
    {
        internal const int RingCount=3;
        static readonly double[] trail={0,27,50};
        static readonly double[] thickness={3,2,1.5};
        readonly double x,y,reach;
        internal readonly ZipperThemeShape Zipper;

        CompanionRevealPaths(double width,double height,double avatarX,double avatarY,ZipperThemeShape zipper)
        {
            x=Math.Max(0,Math.Min(width,avatarX));
            y=Math.Max(0,Math.Min(height,avatarY));
            double farX=Math.Max(x,width-x),farY=Math.Max(y,height-y);
            reach=Math.Sqrt(farX*farX+farY*farY)+32;
            Zipper=zipper;
        }

        internal static CompanionRevealPaths Create(string effect,double width,double height,double avatarX,double avatarY,double seamX)
        {
            var zipper=effect==CompanionTransitionMotion.Zipper?new ZipperThemeShape(width,height,seamX,0,true):null;
            return new CompanionRevealPaths(width,height,avatarX,avatarY,zipper);
        }

        double Radius(double progress){return reach*(1-Math.Pow(1-Clamp(progress),1.35));}

        internal void WriteReveal(double progress,ILiquidThemeOutline outline)
        {
            if(progress<=0)return;
            if(Zipper!=null)Zipper.WriteOutline(progress,outline);
            else Circle(outline,Radius(progress),false);
        }

        internal void WriteRing(double progress,int index,ILiquidThemeOutline outline)
        {
            double radius=Radius(progress)-trail[index];
            double half=thickness[index]/2;
            if(progress<=0||progress>=1||radius<=half)return;
            Circle(outline,radius+half,false);
            Circle(outline,radius-half,true);
        }

        internal static double RingOpacity(double progress,int index,bool dark)
        {
            double strength=index==0?.90:index==1?.44:.25;
            return strength*(dark?1:.78)*Math.Min(1,progress/.08)*(1-Clamp((progress-.5)/.5));
        }

        void Circle(ILiquidThemeOutline outline,double radius,bool reverse)
        {
            double bend=radius*.5522847498;
            double rise=reverse?-radius:radius,tangent=reverse?-bend:bend;
            outline.MoveTo(x-radius,y);
            outline.CurveTo(x-radius,y-tangent,x-bend,y-rise,x,y-rise);
            outline.CurveTo(x+bend,y-rise,x+radius,y-tangent,x+radius,y);
            outline.CurveTo(x+radius,y+tangent,x+bend,y+rise,x,y+rise);
            outline.CurveTo(x-bend,y+rise,x-radius,y+tangent,x-radius,y);
            outline.Close();
        }

        static double Clamp(double progress){return Math.Max(0,Math.Min(1,progress));}
    }
}
