using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Dafeiyu.Tests
{
    public static class HolidaySourceBindingTests
    {
        static int passed;
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;}
        static string Key(OutfitDefinition target){return target.SourceKey;}
        static string Key(OutfitClipDefinition target){return target.SourceKey;}
        static HolidayCatalog Merge(HolidayCatalog original,string id,HolidayCatalog replacement)
        {return original.WithCharacter(id,replacement);}
        static void Replace(HolidayController controller,HolidayCatalog replacement){controller.ReplaceCatalog(replacement);}
        static byte[] Read(OutfitDefinition definition){return Read(definition.OpenPreview());}
        static byte[] Read(OutfitClipDefinition definition){return Read(definition.OpenResource());}
        static byte[] Read(Stream stream)
        {using(var input=stream){using(var result=new MemoryStream()){input.CopyTo(result);return result.ToArray();}}}
        static void Reject(Action action,string name)
        {try{action();}catch(ArgumentException){passed++;return;}catch(InvalidDataException){passed++;return;}throw new Exception(name);}
        static HolidayContext Context()
        {return new HolidayContext{PersonaId="persona",CharacterId="e-bao",LocalTime=new DateTime(2026,10,31),MonotonicSeconds=0,CanAnimateTransition=true,CanPlayEasterEgg=true,ResourceVersion="same-package"};}
        public static int Main()
        {
            passed=0;
            string root=Path.Combine(Path.GetTempPath(),"holiday-source-"+Guid.NewGuid().ToString("N"));
            try {
                Directory.CreateDirectory(root);
                string oldRoot=Fixture(root,"old","e-bao",41),newRoot=Fixture(root,"new","e-bao",93),otherRoot=Fixture(root,"other","marfu",121);
                BoundPixelsAndRepairGeneration(oldRoot,newRoot);
                UnrelatedReplacementPreservesPlayback(oldRoot,otherRoot);
                ChangedSourceInvalidatesTickets(oldRoot,newRoot);
                FailedSourceRecovery(oldRoot,newRoot,otherRoot);
                CompositionRejectsPartialInput(oldRoot,newRoot,otherRoot,root);
                ReplacementNotificationBeforeRefresh(oldRoot,otherRoot);
                Console.WriteLine("Holiday source binding tests: "+passed+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL holiday source binding: "+e);return 1;}
            finally{
                string resolved=Path.GetFullPath(root),temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(resolved.StartsWith(temporary,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("holiday-source-",StringComparison.Ordinal) && Directory.Exists(resolved))Directory.Delete(resolved,true);
            }
        }
        static void BoundPixelsAndRepairGeneration(string oldRoot,string newRoot)
        {
            var oldCatalog=new HolidayCatalog(oldRoot);var current=new HolidayCatalog(newRoot);
            var old=oldCatalog.Outfits.Single();var fresh=current.Outfits.Single();
            Check(old.Ready && fresh.Ready && oldCatalog.Warning==null && current.Warning==null,"valid complete filesystem fixtures load");
            var oldClip=old.FindClip("idle","default");string key=Key(old);
            Check(!String.IsNullOrEmpty(key) && !key.Contains("|") && key==Key(oldClip) && key==Key(old.Gaze) && key==Key(old.EasterEgg),"one loaded source binds every render path");
            Check(Key(fresh)!=key && Key(new HolidayCatalog(oldRoot).Outfits.Single())!=key,"new root and same-root repair each receive a fresh render generation");
            byte[] oldBytes=File.ReadAllBytes(Path.Combine(oldRoot,"sheet.png")),newBytes=File.ReadAllBytes(Path.Combine(newRoot,"sheet.png"));
            Check(!oldBytes.SequenceEqual(newBytes),"same-id version fixtures contain different actual raster bytes");
            var controller=new HolidayController(new HolidayCalendar(),oldCatalog,new HolidayState(),s=>{});controller.Refresh(Context());Replace(controller,current);
            File.WriteAllText(Path.Combine(oldRoot,"current.txt"),newRoot);
            Check(Read(oldClip).SequenceEqual(oldBytes),"captured old clip reads original root after replacement and pointer write");
            Check(Read(fresh.FindClip("idle","default")).SequenceEqual(newBytes),"fresh clip reads replacement root");
            Check(Read(old).SequenceEqual(File.ReadAllBytes(Path.Combine(oldRoot,"preview.png"))),"captured preview retains its original root");
            Check(!Read(old).SequenceEqual(Read(fresh)),"preview source is independent of identical outfit id and version");
            Check(Read(old.Gaze).SequenceEqual(File.ReadAllBytes(Path.Combine(oldRoot,"gaze.png"))) && Read(old.EasterEgg).SequenceEqual(oldBytes),"gaze and egg use their captured opener");
            File.Delete(Path.Combine(oldRoot,"sheet.png"));bool missing=false;
            try{Read(oldClip);}catch(FileNotFoundException){missing=true;}finally{File.WriteAllBytes(Path.Combine(oldRoot,"sheet.png"),oldBytes);}
            Check(missing,"bound missing file exposes its I/O failure instead of silently switching sources");
            var noPreview=new OutfitDefinition("e-bao","empty","halloween","empty","1",false,false,new OutfitClipDefinition[0],null,null,null,null);
            Check(noPreview.OpenPreview()==null,"missing optional preview has no stream");
        }
        static void UnrelatedReplacementPreservesPlayback(string root,string otherRoot)
        {
            var catalog=new HolidayCatalog(root);var original=catalog.Outfits.Single();var other=new HolidayCatalog(otherRoot);
            var state=new HolidayState();int writes=0,changes=0;var controller=new HolidayController(new HolidayCalendar(),catalog,state,s=>writes++);
            Check(Object.ReferenceEquals(controller.Catalog,catalog),"catalog accessor retains the injected source wrapper");
            var input=Context();controller.Refresh(input);var greeting=controller.CreateGreetingContext(0);var ticket=controller.TryBeginHover(0);
            Check(ticket!=null && controller.MarkHoverFirstFrame(ticket),"rendered egg begins before unrelated installation");
            long revision=controller.Revision;int previousWrites=writes;bool transition=controller.TransitionRequested;
            controller.Changed+=(sender,args)=>changes++;var merged=Merge(catalog,"marfu",other);Replace(controller,merged);
            Check(Object.ReferenceEquals(controller.Catalog,merged),"catalog accessor follows atomic replacement without rebuilding definitions");
            Check(Object.ReferenceEquals(controller.EffectiveOutfit,original) && Object.ReferenceEquals(merged.Outfits[0],original),"unaffected actual outfit and ordering are retained");
            Check(controller.Revision==revision && controller.TransitionRequested==transition && changes==1 && writes==previousWrites,"unrelated catalog replacement notifies once without revision transition or save changes");
            Check(controller.MarkHoverFirstFrame(ticket) && controller.IsGreetingCurrent(greeting),"unrelated installation retains egg and greeting currency");
            Check(controller.TryBeginHover(0)==null,"catalog replacement does not create a hover entry");
            ticket.PlaybackCompleted=true;Check(controller.CompleteHover(ticket) && state.WardrobeDiscovered,"existing ticket can still complete and unlock durably");
            Check(state.ForCharacter("e-bao").UnlockedOutfits.Contains(original.OutfitId) && state.ForCharacter("marfu").UnlockedOutfits.Count==0,"unlock stays on original role");
            controller.LeaveHover();input.MonotonicSeconds=59;controller.Refresh(input);Check(controller.TryBeginHover(0)==null,"unrelated replacement preserves first-frame cooldown");
            Check(merged.ReportUnavailable(other.Outfits.Single()) && !merged.ReportUnavailable(other.Outfits.Single()),"retained replacement definition remains eligible for exact-object one-shot failure");
        }
        static void ChangedSourceInvalidatesTickets(string root,string newRoot)
        {
            var catalog=new HolidayCatalog(root);var replacement=new HolidayCatalog(newRoot);int changes=0;
            var controller=new HolidayController(new HolidayCalendar(),catalog,new HolidayState(),s=>{});controller.Refresh(Context());
            var old=controller.EffectiveOutfit;var ticket=controller.TryBeginHover(0);var greeting=controller.CreateGreetingContext(0);long revision=controller.Revision;
            controller.Changed+=(sender,args)=>changes++;Replace(controller,Merge(catalog,"e-bao",replacement));
            Check(controller.Revision==revision+1 && changes==1 && Object.ReferenceEquals(controller.EffectiveOutfit,replacement.Outfits.Single()),"same-id same-version new source changes revision once");
            ticket.PlaybackCompleted=true;Check(!controller.MarkHoverFirstFrame(ticket) && !controller.CompleteHover(ticket) && !controller.WardrobeVisible,"old source ticket cannot render or unlock replacement");
            Check(!controller.IsGreetingCurrent(greeting),"old source greeting is stale");
            Check(!controller.ReportUnavailable(old) && replacement.Outfits.Single().Ready,"late old-object rejection cannot disable current source");
        }
        static void FailedSourceRecovery(string root,string newRoot,string otherRoot)
        {
            var catalog=new HolidayCatalog(root);var state=new HolidayState();var old=catalog.Outfits.Single();state.Unlock("e-bao",old.OutfitId);state.ForCharacter("e-bao").PreferredOutfitId=old.OutfitId;
            int writes=0,changes=0;var controller=new HolidayController(new HolidayCalendar(),catalog,state,s=>writes++);controller.Refresh(Context());
            Check(controller.ReportUnavailable(old) && controller.EffectiveOutfit==null,"native rejection falls back to original");
            long revision=controller.Revision;int saved=writes;controller.Changed+=(sender,args)=>changes++;
            var merged=Merge(catalog,"marfu",new HolidayCatalog(otherRoot));Replace(controller,merged);
            Check(controller.EffectiveOutfit==null && controller.Revision==revision && !old.Ready && changes==1 && writes==saved,"unrelated role does not reset rejected-source fallback");
            Check(!controller.ReportUnavailable(old),"retained failed source remains one-shot");
            var repaired=new HolidayCatalog(root);Replace(controller,Merge(merged,"e-bao",repaired));
            Check(Object.ReferenceEquals(controller.EffectiveOutfit,repaired.Outfits.Single()) && controller.Revision==revision+1 && changes==2,"fresh same-root source repair recovers current role once");
            Check(controller.PreferredOutfitId==old.OutfitId && controller.WardrobeVisible && state.ForCharacter("e-bao").UnlockedOutfits.Count==1 && writes==saved,"repair preserves preference ownership discovery and persistence callback");
            Check(!controller.ReportUnavailable(old) && controller.EffectiveOutfit.Ready,"stale failure cannot reject repaired generation");
        }
        static void CompositionRejectsPartialInput(string root,string newRoot,string otherRoot,string temp)
        {
            var catalog=new HolidayCatalog(root);var old=catalog.Outfits.Single();var replacement=new HolidayCatalog(newRoot);
            Reject(()=>Merge(catalog,"foreign",replacement),"unknown character rejected");
            Reject(()=>Merge(catalog,"e-bao",null),"null replacement rejected");
            Reject(()=>Merge(catalog,"e-bao",new HolidayCatalog(otherRoot)),"foreign role rejected");
            Reject(()=>Merge(catalog,"e-bao",new HolidayCatalog(new[]{replacement.Outfits.Single(),new HolidayCatalog(otherRoot).Outfits.Single()})),"mixed-role replacement is rejected atomically");
            Reject(()=>Merge(catalog,"e-bao",new HolidayCatalog(new OutfitDefinition[0])),"empty role cannot clear good source");
            Reject(()=>Merge(catalog,"e-bao",new HolidayCatalog(new OutfitDefinition[]{null})),"null outfit rejected");
            Reject(()=>Merge(catalog,"e-bao",new HolidayCatalog(new[]{replacement.Outfits.Single(),replacement.Outfits.Single()})),"duplicate outfits rejected");
            var incomplete=new OutfitDefinition("e-bao","broken","halloween","broken","1",true,true,new OutfitClipDefinition[0],null,null,null,null);
            Reject(()=>Merge(catalog,"e-bao",new HolidayCatalog(new[]{incomplete})),"incomplete ready-marked definition rejected");
            var unavailable=new HolidayCatalog(newRoot);unavailable.ReportUnavailable(unavailable.Outfits.Single());
            Reject(()=>Merge(catalog,"e-bao",unavailable),"native-rejected role rejected");
            string broken=Fixture(temp,"broken","e-bao",12);File.Delete(Path.Combine(broken,"gaze.png"));var warning=new HolidayCatalog(broken);
            Check(!String.IsNullOrEmpty(warning.Warning),"broken fixture has diagnostic");Reject(()=>Merge(catalog,"e-bao",warning),"warning-bearing replacement rejected");
            Check(catalog.Outfits.Count==1 && Object.ReferenceEquals(catalog.Outfits[0],old) && old.Ready,"all rejected replacements leave original catalog unchanged");
            var diagnosticBase=new HolidayCatalog(Path.Combine(temp,"missing"));var recovered=Merge(diagnosticBase,"e-bao",replacement);
            Check(recovered.Warning==diagnosticBase.Warning && Object.ReferenceEquals(recovered.Outfits.Single(),replacement.Outfits.Single()),"composition retains original diagnostics and incoming actual object");
            Reject(()=>Merge(catalog,"e-bao",recovered),"warning-bearing replacement is rejected even when every remaining outfit is ready");
            var obsolete=new OutfitDefinition("e-bao","obsolete","halloween","old","1",true,true,old.Clips,old.Gaze,old.EasterEgg,old.Preview,old.ActionName);
            var withObsolete=new HolidayCatalog(new[]{old,obsolete});Check(Merge(withObsolete,"e-bao",replacement).Find("e-bao","obsolete")==null,"whole-role replacement removes obsolete outfits");
            string invalid=Fixture(temp,"escape","e-bao",13);string json=File.ReadAllText(Path.Combine(invalid,"outfit.json"));File.WriteAllText(Path.Combine(invalid,"outfit.json"),json.Replace("sheet.png","../sheet.png"));
            var escaped=new HolidayCatalog(invalid);Check(escaped.Outfits.Count==1 && !escaped.Outfits[0].Ready && !String.IsNullOrEmpty(escaped.Warning),"bound filesystem loader rejects traversing resource paths");
        }
        static void ReplacementNotificationBeforeRefresh(string root,string otherRoot)
        {
            var catalog=new HolidayCatalog(root);var replacement=Merge(catalog,"marfu",new HolidayCatalog(otherRoot));int changes=0;
            var controller=new HolidayController(new HolidayCalendar(),catalog,new HolidayState(),s=>{});controller.Changed+=(sender,args)=>changes++;
            Replace(controller,replacement);Check(changes==1 && controller.Revision==0,"catalog may be replaced before first context without inventing revision");
            Replace(controller,replacement);Check(changes==1,"same catalog instance is a no-op");
            Reject(()=>Replace(controller,null),"null controller replacement rejected");Check(changes==1,"invalid replacement does not notify");
            var input=Context();input.CharacterId="marfu";controller.Refresh(input);Check(controller.EffectiveOutfit==replacement.Find("marfu","halloween-v1"),"first refresh uses preinstalled replacement catalog");
            input.CharacterId=null;controller.Refresh(input);long revision=controller.Revision;changes=0;Replace(controller,Merge(replacement,"e-bao",new HolidayCatalog(root)));
            Check(controller.EffectiveOutfit==null && controller.Revision==revision && changes==1,"catalog changes notify selectors even with no visible outfit");
        }
        static string Fixture(string parent,string name,string character,byte red)
        {
            string root=Path.Combine(parent,name);Directory.CreateDirectory(root);WritePng(Path.Combine(root,"sheet.png"),3840,520,red);WritePng(Path.Combine(root,"gaze.png"),1920,2080,red);WritePng(Path.Combine(root,"preview.png"),480,520,red);
            string[] defaults={"idle","running-right","running-left","waving","jumping","failed","waiting","running","review","lifted","landing","petting","poked","feeding","happy","sleeping","special","stumble","special2"};
            string[] alternates={"lifted","landing","feeding","happy","sleeping","special","special2","stumble"};
            var clips=new List<string>();foreach(string action in defaults)clips.Add(Clip(action,"default","sheet.png",8,8,8,true));foreach(string action in alternates)clips.Add(Clip(action,"alternate","sheet.png",8,8,8,true));if(character=="e-bao" || character=="gugugaga")clips.Add(Clip("jumping","alternate","sheet.png",8,8,8,false));
            string identity="\"character_id\":\""+character+"\",\"outfit_id\":\"halloween-v1\",\"holiday_id\":\"halloween\",\"name\":\"Halloween\",\"version\":\"1\"";
            File.WriteAllText(Path.Combine(root,"catalog.json"),"{\"schema_version\":1,\"outfits\":[{"+identity+",\"ready\":true,\"greetings_ready\":true,\"definition\":\"outfit.json\"}]}");
            File.WriteAllText(Path.Combine(root,"outfit.json"),"{\"schema_version\":1,"+identity+",\"clips\":["+String.Join(",",clips)+"],\"gaze\":"+Clip("gaze","default","gaze.png",16,4,8,true)+",\"easter_egg\":"+Clip("egg","default","sheet.png",8,8,4,false)+",\"preview\":\"preview.png\",\"action_name\":\"Trick or treat\"}");return root;
        }
        static string Clip(string action,string variant,string path,int count,int columns,int fps,bool loop)
        {return "{\"action\":\""+action+"\",\"variant\":\""+variant+"\",\"path\":\""+path+"\",\"frame_count\":"+count+",\"columns\":"+columns+",\"fps\":"+fps+",\"loop\":"+(loop?"true":"false")+",\"frame_width\":480,\"frame_height\":520}";}
        static void WritePng(string path,int width,int height,byte red)
        {
            byte[] row=new byte[1+width*4];for(int x=0;x<width;x++){row[1+x*4]=red;row[2+x*4]=37;row[3+x*4]=149;row[4+x*4]=255;}
            byte[] compressed;uint a=1,b=0;
            using(var packed=new MemoryStream()){packed.WriteByte(0x78);packed.WriteByte(0x9c);using(var deflate=new DeflateStream(packed,CompressionMode.Compress,true)){for(int y=0;y<height;y++){deflate.Write(row,0,row.Length);foreach(byte item in row){a=(a+item)%65521;b=(b+a)%65521;}}}WriteU32(packed,(b<<16)|a);compressed=packed.ToArray();}
            using(var output=File.Create(path)){output.Write(new byte[]{137,80,78,71,13,10,26,10},0,8);using(var header=new MemoryStream()){WriteU32(header,(uint)width);WriteU32(header,(uint)height);header.Write(new byte[]{8,6,0,0,0},0,5);Chunk(output,"IHDR",header.ToArray());}Chunk(output,"IDAT",compressed);Chunk(output,"IEND",new byte[0]);}
        }
        static void Chunk(Stream output,string type,byte[] data)
        {
            WriteU32(output,(uint)data.Length);byte[] name=Encoding.ASCII.GetBytes(type);output.Write(name,0,4);output.Write(data,0,data.Length);uint crc=0xffffffff;
            foreach(byte item in name.Concat(data)){crc^=item;for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}WriteU32(output,crc^0xffffffff);
        }
        static void WriteU32(Stream output,uint value){output.WriteByte((byte)(value>>24));output.WriteByte((byte)(value>>16));output.WriteByte((byte)(value>>8));output.WriteByte((byte)value);}
    }
}
