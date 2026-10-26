using System;using System.IO;using System.Linq;
namespace Dafeiyu.Tests {public static class SeasonalKeepsakeTests {
public static int Main(){try{int checks=0;Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);checks++;};
check(HolidayKeepsakeRegistry.BuiltIn.Definitions.Count==10,"ten independently registered festivals");
var catalog=new HolidayCatalog(Path.GetFullPath("assets/holidays"));
foreach(var outfit in catalog.Outfits){var definition=HolidayKeepsakeRegistry.BuiltIn.Find(outfit.HolidayId);check(definition!=null,"registered "+outfit.HolidayId);
var scene=RelationshipStageArt.Holiday("official:"+outfit.CharacterId,outfit.CharacterId,outfit.HolidayId,outfit.OutfitId,outfit.Version);check(scene!=null,"exact reviewed scene "+outfit.HolidayId+"/"+outfit.CharacterId);
using(var input=File.OpenRead(Path.Combine("assets",scene.RelativePath)))check(RelationshipStageArt.ReadVerified(input,scene).Length==scene.ByteLength,"source digest");
RelationshipStageArtAsset ignored;check(!RelationshipStageArt.TryGet(scene.CompanionId,outfit.HolidayId,out ignored),"holiday cannot leak to affinity stages");
check(RelationshipStageArt.Holiday("custom-pack:"+outfit.CharacterId,outfit.CharacterId,outfit.HolidayId,outfit.OutfitId,outfit.Version)==null,"custom provenance excluded");
check(RelationshipStageArt.Holiday(scene.CompanionId,outfit.CharacterId,outfit.HolidayId,outfit.OutfitId,"99.0.0")==null,"unknown scene revision excluded");
var record=new HolidayKeepsakeRecord{CompanionId=scene.CompanionId,HolidayId=outfit.HolidayId,CompanionName="Test",OccurrenceYear=2026};var card=HolidayKeepsakeCardData.Owned(record,HolidayKeepsakeRegistry.BuiltIn);
check(card.Style==outfit.HolidayId,"distinct style");check(card.Title!=TextResources.Get("holidayKeepsake.historyTitle"),"localized reviewed title");check(RelationshipCardDecoration.PathFor(card.Style)!=RelationshipCardDecoration.PathFor("unknown"),"distinct ornament");}
check(RelationshipStageArt.All.Count==80,"30 affinity plus50holiday scenes");Console.WriteLine("PASS: "+checks+" seasonal keepsake checks");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}}
