using System;
using System.IO;
namespace Dafeiyu.Tests
{
    public static class AgentTaskMemoryTests
    {
        static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        public static int Main(){try{string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"facts-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var a=new AgentTaskMemory(Path.Combine(root,"project-a"));var b=new AgentTaskMemory(Path.Combine(root,"project-b"));var fact=new AgentProjectFact{Text="Tests run through the fixture script",TaskId="confirmed-task",SourceFile="test.ps1"};Check(a.List().Count==0,"facts are empty until explicitly saved");a.Save(fact);Check(new AgentTaskMemory(Path.Combine(root,"project-a")).ForPrompt().Contains("confirmed-task"),"manually saved fact retains source across reopen");Check(b.List().Count==0,"facts do not cross projects");fact.Text="Updated fixture detail";a.Save(fact);Check(a.List().Count==1&&a.ForPrompt().Contains("Updated fixture detail"),"fact is editable without duplicating");a.Delete(fact.Id);Check(!a.ForPrompt().Contains("Updated fixture detail")&&a.List().Count==0,"deletion invalidates subsequent prompt content");bool rejected=false;try{a.Save(new AgentProjectFact{Text="no source"});}catch(ArgumentException){rejected=true;}Check(rejected,"facts require an explicit source task");Console.WriteLine("Agent task memory tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
