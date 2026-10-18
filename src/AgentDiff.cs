using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    public sealed class AgentDiffLine
    {
        public string Kind;
        public int? BeforeLine,AfterLine;
        public string Text;
    }
    public sealed class AgentDiffResult
    {
        public readonly List<AgentDiffLine> Lines=new List<AgentDiffLine>();
        public int? AddedLines,RemovedLines;
        public bool Truncated,Unknown;
    }
    internal static class AgentDiff
    {
        internal const int MaximumOutputLines=2000,MaximumOutputChars=120000;
        internal static AgentDiffResult Build(string before,string after)
        {
            var result=new AgentDiffResult();string[] a=Lines(before),b=Lines(after);
            if((long)a.Length*b.Length>1000000){result.Unknown=true;return result;}
            var dp=new int[a.Length+1,b.Length+1];
            for(int i=a.Length-1;i>=0;i--)for(int j=b.Length-1;j>=0;j--)dp[i,j]=a[i]==b[j]?dp[i+1,j+1]+1:Math.Max(dp[i+1,j],dp[i,j+1]);
            int x=0,y=0,added=0,removed=0,chars=0;
            while(x<a.Length || y<b.Length)
            {
                AgentDiffLine line;
                if(x<a.Length && y<b.Length && a[x]==b[y]){line=new AgentDiffLine{Kind="context",BeforeLine=x+1,AfterLine=y+1,Text=a[x]};x++;y++;}
                else if(x<a.Length && (y>=b.Length || dp[x+1,y]>=dp[x,y+1])){line=new AgentDiffLine{Kind="removed",BeforeLine=x+1,Text=a[x++]};removed++;}
                else{line=new AgentDiffLine{Kind="added",AfterLine=y+1,Text=b[y++]};added++;}
                bool include=result.Lines.Count<MaximumOutputLines && chars+line.Text.Length<=MaximumOutputChars;if(include){result.Lines.Add(line);chars+=line.Text.Length+1;}else result.Truncated=true;
            }
            if(x<a.Length || y<b.Length)result.Truncated=true;
            result.AddedLines=added;result.RemovedLines=removed;return result;
        }
        static string[] Lines(string text)
        {
            if(String.IsNullOrEmpty(text))return new string[0];
            var lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
            if(lines.Length>0 && lines[lines.Length-1]=="")Array.Resize(ref lines,lines.Length-1);
            return lines;
        }
    }
}
