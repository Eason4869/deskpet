using System;

namespace Dafeiyu
{
    // Reviewed full-size quiet sheets. Callers must establish built-in provenance;
    // a matching name or package ID alone never grants access to these overrides.
    public sealed class CompanionQuietClip
    {
        public string Path {get;private set;}
        public string ResourceName {get;private set;}
        public int Width {get{return 480;}}
        public int Height {get{return 520;}}
        public int Columns {get{return 8;}}
        public int Frames {get{return 8;}}
        public double Fps {get;private set;}
        public double Duration {get{return Frames/Fps;}}
        public bool Loop {get{return true;}}
        internal CompanionQuietClip(string role,string clip,double fps)
        {Path="companion-quiet/"+role+"/"+clip+".png";ResourceName="Dafeiyu.Quiet."+role+"."+clip;Fps=fps;}
    }
    public static class CompanionQuietClips
    {
        static readonly string[] roles={"e-bao","dafeiyu","marfu","xiaobei","gugugaga"};
        static readonly CompanionQuietClip[,] clips=Create();
        static CompanionQuietClip[,] Create()
        {
            var result=new CompanionQuietClip[5,3];
            for(int i=0;i<roles.Length;i++){
                result[i,0]=new CompanionQuietClip(roles[i],"idle",4);
                result[i,1]=new CompanionQuietClip(roles[i],"sleeping",2.5);
                result[i,2]=new CompanionQuietClip(roles[i],"sleeping-alternate",2.5);
            }return result;
        }
        public static CompanionQuietClip Find(string characterId,string action,bool original)
        {
            int role=Array.IndexOf(roles,characterId);
            if(role<0 || action!="idle" && action!="sleeping")return null;
            return clips[role,action=="idle"?0:original?1:2];
        }
    }
}
