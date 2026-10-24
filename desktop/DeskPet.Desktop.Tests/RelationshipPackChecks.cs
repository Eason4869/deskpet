using System.Text;
using System.Text.Json;
using Dafeiyu;
using DeskPet.Desktop;

internal static class RelationshipPackChecks
{
    internal static int Run()
    {
        int checks=0,failures=0;
        void Check(bool valid,string name){checks++;Console.WriteLine((valid?"PASS ":"FAIL ")+name);if(!valid)failures++;}
        PetSkin Read(string json){using var input=new MemoryStream(Encoding.UTF8.GetBytes(json));return SpriteAssets.ReadManifest(input,"skin-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","unused");}
        string V1(string action)=>JsonSerializer.Serialize(new Dictionary<string,object>{["version"]=1,["name"]="Legacy",["atlas"]="atlas.png",["extra_animations"]=new Dictionary<string,object>{{action,new{}}},["relationship"]=new{stages=new Dictionary<string,object>{{"familiar",new{action}}}}});
        bool Reject(string json){try{Read(json);return false;}catch(Exception error)when(error is InvalidDataException or JsonException or ArgumentException){return true;}}
        foreach(string action in new[]{"waving","petting"}){var skin=Read(V1(action));Check(skin.Relationship?.Stages["familiar"].Action==action && skin.ExtraClips.Count==0,"v1 supported fallback action "+action);}
        foreach(string action in new[]{"../../run.exe","unknown-action","special2"})Check(Reject(V1(action)),"v1 raw extras cannot authorize "+action);
        var baseClips=AnimationPlayer.States.Concat(SpriteAssets.Interactions).ToDictionary(name=>name,_=>(object)new{file="clip.png",columns=8,frames=1,fps=8,loop=false});
        string V2(string action,int frames)=>JsonSerializer.Serialize(new{version=2,name="Illustrated",cell_width=480,cell_height=520,animations=baseClips,gaze=new{file="gaze.png",columns=4,frames=16},extra_animations=new Dictionary<string,object>{{action,new{file="extra.png",columns=8,frames,fps=8,loop=false}}},relationship=new{stages=new Dictionary<string,object>{{"familiar",new{action}}}}});
        var valid=Read(V2("special2",1));Check(valid.ExtraClips.ContainsKey("special2") && valid.Relationship?.Stages["familiar"].Action=="special2","v2 validated loaded extra authorizes action");
        Check(Reject(V2("special2",0)),"v2 malformed known extra cannot authorize action");Check(Reject(V2("../../run.exe",1)),"v2 unknown extra cannot authorize action");
        if(failures>0)throw new Exception("Relationship pack checks: "+failures+" failed of "+checks);return checks;
    }
}
