using System;
using System.IO;
using System.Text;
using Dafeiyu;
namespace Dafeiyu.Tests
{
    public static class WpfRelationshipPackTests
    {
        public static int Main()
        {
            try{
                int count=0;foreach(string action in new[]{"waving","petting","../../run.exe","unknown-action","special2"}){
                    string json="{\"version\":1,\"name\":\"Legacy\",\"atlas\":\"atlas.png\",\"extra_animations\":{\""+action+"\":{}},\"relationship\":{\"stages\":{\"familiar\":{\"action\":\""+action+"\"}}}}";bool rejected=false;SkinManifest manifest=null;
                    try{using(var input=new MemoryStream(Encoding.UTF8.GetBytes(json)))manifest=SkinCatalog.ReadManifest(input);}catch(InvalidDataException){rejected=true;}
                    bool allowed=action=="waving"||action=="petting";if(allowed?(rejected||manifest.Relationship.Stages["familiar"].Action!=action):!rejected)throw new Exception("v1 action validation: "+action);count++;Console.WriteLine("PASS v1 "+action+" supported="+allowed);
                }Console.WriteLine("WpfRelationshipPackTests: "+count+" passed");return 0;
            }catch(Exception error){Console.WriteLine("FAIL WpfRelationshipPackTests: "+error);return 1;}
        }
    }
}
