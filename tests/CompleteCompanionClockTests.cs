using System;
namespace Dafeiyu.Tests {
public static class CompleteCompanionClockTests {
static int checks;
static void Check(bool yes,string message){if(!yes)throw new Exception(message);checks++;}
static OutfitClipDefinition Clip(string name,int frames,double fps,bool loop){return new OutfitClipDefinition(name+".png",frames,frames,fps,loop,480,520,0,0,name,"default");}
public static int Main(){try {
var player=new AnimationPlayer();player.ClipOverride=name=>name=="jumping"?Clip(name,8,10,false):name=="waving"?Clip(name,8,4,true):null;
player.Play("jumping",.2);player.Advance(.7);Check(player.State=="jumping","jump must complete all eight poses");player.Advance(.101);Check(player.State=="idle","jump finishes at declared duration");
player.Play("waving",1);player.Advance(1.9);Check(player.State=="waving","wave completes a full cycle");player.Advance(.101);Check(player.State=="idle","wave finishes after full cycle");
var interaction=new InteractionAnimation();
foreach(var name in new[]{"petting","poked"}){double fps=name=="petting"?5:12.3076923077;interaction.ConfigureClips(null,false,n=>n==name?Clip(n,8,fps,false):null);interaction.Start(name);for(int i=0;i<8;i++){Check(interaction.Active&&interaction.Frame==i,name+" pose "+i);interaction.Advance(1/fps);}Check(!interaction.Active,name+" completes");}
interaction.ConfigureClips(null,false,n=>n=="sleeping"?Clip(n,8,2,true):null);interaction.Start("sleeping");interaction.Advance(1.5);Check(interaction.Frame==3,"sleep uses own fps");
interaction.ConfigureClips(null,false,n=>n=="petting"?Clip(n,8,5,false):null);interaction.Start("petting");interaction.Advance(.8);interaction.ConfigureClips(null,false,n=>n=="petting"?Clip(n,8,10,false):null);Check(Math.Abs(interaction.Elapsed-.4)<1e-8&&interaction.Frame==4,"appearance switch preserves phase");
interaction.ConfigureClips(null,false,null);interaction.Start("petting");interaction.Advance(.8);Check(interaction.Frame==2,"default timing preserved");
Console.WriteLine("PASS: "+checks+" complete companion clock checks");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}}
