using System;
namespace Dafeiyu
{
    internal static class AgentLineChanges
    {
        internal static void Count(string before,string after,out int? added,out int? removed)
        {
            var diff=AgentDiff.Build(before,after);added=diff.AddedLines;removed=diff.RemovedLines;
        }
    }
}
