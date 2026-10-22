using System;
using System.Collections.Generic;
namespace Dafeiyu.Tests {
public static class GeneratedImageGroupTests {
static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
public static int Main(){try{
var g=new GeneratedImageGroupState();var assets=new List<GeneratedImage>();
g.Update(new[]{"a","b","c"},assets,null);assets.Add(new GeneratedImage{Id="B",ImageJobId="b"});g.Update(new[]{"a","b","c"},assets,null);Check(g.Keys[0]=="a"&&g.Keys[1]=="b"&&g.SelectedId=="a","OutOfOrderCompletionKeepsSlots");
g.Select("b");assets.Add(new GeneratedImage{Id="A",ImageJobId="a"});g.Update(new[]{"a","b","c","d"},assets,null);Check(g.SelectedId=="b","UserSelectionSurvivesLaterResults");g.Select("c");Check(g.SelectedImage==null&&g.Select("b"),"MixedSuccessFailureAndPendingCanSwitch");Check(g.SelectedImage.Id=="B","ActionsTargetSelectedImage");var other=new GeneratedImageGroupState();other.Update(new[]{"a"},assets,null);Check(other.SelectedId=="a"&&g.SelectedId=="b","SeparateMessagesNeverMerge");var restored=new GeneratedImageGroupState();restored.Update(new[]{"a","b","c"},assets,null);Check(String.Join(",",restored.Keys)=="a,b,c","HistoryRestoresGroupOrder");var dropped=new GeneratedImageGroupState();dropped.Update(new[]{"old"},new List<GeneratedImage>(),id=>new GeneratedImage{Id="saved",ImageJobId=id});Check(dropped.SelectedImage.Id=="saved","RetentionDroppedSnapshotResolvesByJobId");Console.WriteLine("GeneratedImageGroupTests: 7 passed");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}}
