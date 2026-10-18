using System;
using System.Linq;
namespace Dafeiyu.Tests
{
    public static class AgentDiffTests
    {
        static int checks;
        static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                var replacement=AgentDiff.Build("a\nb\nc","a\nx\nc");
                Check(!replacement.Unknown && replacement.AddedLines==1 && replacement.RemovedLines==1,"AgentDiffCounts replacement reports exact added and removed lines");
                Check(replacement.Lines.Any(l=>l.Kind=="removed" && l.BeforeLine==2 && l.Text=="b") && replacement.Lines.Any(l=>l.Kind=="added" && l.AfterLine==2 && l.Text=="x"),"AgentDiffCounts preserves before and after line numbers");
                int? added,removed;AgentLineChanges.Count("a\nb\nc","a\nx\nc",out added,out removed);
                Check(added==1 && removed==1,"AgentLineChanges.Count uses the same diff result");
                var longBefore=String.Join("\n",Enumerable.Range(0,600).Select(i=>i==300?"old":"x".PadRight(300,'a')));
                var longAfter=String.Join("\n",Enumerable.Range(0,600).Select(i=>i==300?"new":"x".PadRight(300,'a')));
                var large=AgentDiff.Build(longBefore,longAfter);
                                Check(!large.Unknown && large.Truncated && large.AddedLines==1 && large.RemovedLines==1,"LargeDiffIsBounded truncates display but keeps exact counts");
                var lateBefore=String.Join("\n",Enumerable.Range(0,600).Select(i=>i==599?"old":"x".PadRight(300,'a')));var lateAfter=String.Join("\n",Enumerable.Range(0,600).Select(i=>i==599?"new":"x".PadRight(300,'a')));var late=AgentDiff.Build(lateBefore,lateAfter);Check(late.Truncated && late.AddedLines==1 && late.RemovedLines==1,"TruncatedDiffStillCountsLateChanges counts edits beyond the display limit");
                var hugeBefore=String.Join("\n",Enumerable.Repeat("a",1100));var hugeAfter=String.Join("\n",Enumerable.Repeat("b",1100));var unknown=AgentDiff.Build(hugeBefore,hugeAfter);
                Check(unknown.Unknown && !unknown.AddedLines.HasValue && !unknown.RemovedLines.HasValue && unknown.Lines.Count==0,"UnknownDiffIsHonest omits fabricated counts");
                Console.WriteLine("Agent diff tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
