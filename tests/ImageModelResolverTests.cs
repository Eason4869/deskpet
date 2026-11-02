using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
 public static class ImageModelResolverTests
 {
  static int checks;
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
  static T RoundTrip<T>(T value){using(var stream=new MemoryStream()){var serializer=new DataContractJsonSerializer(typeof(T));serializer.WriteObject(stream,value);stream.Position=0;return (T)serializer.ReadObject(stream);}}
  static string Json<T>(T value){using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);return Encoding.UTF8.GetString(stream.ToArray());}}
  static TextProviderSettings Provider(string id,CapabilitySupport images){var p=new TextProviderSettings{Id=id,Name=id,Connection=new ConnectionSettings{BaseUrl="https://"+id+".example/v1",Model="vision",UseProxy=true,ProxyAddress="127.0.0.1:7891"},Models=new List<string>{"vision"}};p.Connection.ApiKey=id+"-secret";p.ModelCapabilities["vision"]=new ModelCapabilitySetting{Images=images,Tools=CapabilitySupport.Unsupported};p.Normalize();return p;}
  static PetSettings Settings(CapabilitySupport images){return new PetSettings{Text=new ConnectionSettings{BaseUrl="https://chat.example/v1",Model="chat",ImageSupport=images},TextProviders=new List<TextProviderSettings>{Provider("first",CapabilitySupport.Supported),Provider("second",CapabilitySupport.Unsupported)},ActiveTextProviderId="first",ImageDescriptionModel=new ModelReference{ProviderId="first",ModelId="vision"}};}
  public static int Main()
  {
   string root=Path.GetFullPath(Path.Combine("work","image-model-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
   try{Candidates();AutomaticImageModels();Routes();Storage(root);ScopedSave(root);Workspace(root);Console.WriteLine("ImageModelResolverTests: "+checks+" passed");return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}finally{Directory.Delete(root,true);}
  }
  static void AutomaticImageModels()
  {
   foreach(string id in new[]{"mimo-v2.6-flash","mimo-v2.6-pro"}){
    var provider=new TextProviderSettings{Id="mimo-auto",Name="MiMo",Connection=new ConnectionSettings{BaseUrl="https://token-plan-cn.xiaomimimo.com/v1",Model=id},Models=new List<string>{id}};provider.Normalize();
    var settings=new PetSettings{Text=new ConnectionSettings{BaseUrl="https://chat.example/v1",Model="chat",ImageSupport=CapabilitySupport.Unsupported},TextProviders=new List<TextProviderSettings>{provider},ImageDescriptionModel=new ModelReference{ProviderId=provider.Id,ModelId=id}};
    Check(ImageModelResolver.Candidates(settings).Any(c=>c.ProviderId==provider.Id && c.ModelId==id),"automatic image capability includes "+id+" in the picker");
    Check(ImageModelResolver.Resolve(settings,null).Route==ImageRouteKind.Described,"automatic image capability routes through "+id+" without a manual Supported override");
    provider.ModelCapabilities[id]=new ModelCapabilitySetting{Images=CapabilitySupport.Unsupported};Check(ImageModelResolver.Candidates(settings).Count==0 && ImageModelResolver.Resolve(settings,null).Route==ImageRouteKind.Unavailable,"manual Unsupported overrides known model capability");
    provider.ModelCapabilities[id].Images=CapabilitySupport.Unknown;provider.ServerCapabilities[id]=new ModelCapabilitySetting{Images=CapabilitySupport.Unsupported};Check(ImageModelResolver.Candidates(settings).Count==0,"server Unsupported overrides the built-in known model fallback");
   }
  }
  static void Candidates()
  {
   var method=typeof(ImageModelResolver).GetMethod("Candidates");
   Check(method!=null,"image model picker exposes supported candidates");
   var cfg=Settings(CapabilitySupport.Supported);cfg.TextProviders.Add(Provider("unknown",CapabilitySupport.Unknown));
   var result=(IList<ModelReference>)method.Invoke(null,new object[]{cfg});
   Check(result.Count==1&&result[0].ProviderId=="first"&&result[0].ModelId=="vision","candidate list excludes unsupported and unknown models even when current model is multimodal");
  }
  static void ScopedSave(string root)
  {
   var method=typeof(ProjectCatalogStore).GetMethod("SaveImageDescriptionOverride");Check(method!=null,"catalog has scoped image selection save");
   string path=Path.Combine(root,"selection-catalog.json");var store=new ProjectCatalogStore(path);var first=new ConversationDefinition{Id="first",Name="old",Mode="chat"};var second=new ConversationDefinition{Id="second",Name="second",Mode="chat"};var catalog=new ProjectCatalog{UnassignedConversations=new List<ConversationDefinition>{first,second}};catalog.LegacyImageDescriptionOverrides["other"]=new ImageDescriptionOverride{Mode="disabled"};store.Save(catalog);
   var newer=new ProjectCatalogStore(path).Load();newer.UnassignedConversations[0].Name="concurrent rename";newer.UnassignedConversations[1].ImageDescriptionOverride=new ImageDescriptionOverride{Mode="disabled"};new ProjectCatalogStore(path).Save(newer);
   method.Invoke(store,new object[]{null,"first",new ImageDescriptionOverride{Mode="selected",Model=new ModelReference{ProviderId="p",ModelId="m"}}});var saved=store.Load();
   Check(saved.UnassignedConversations[0].Name=="concurrent rename"&&saved.UnassignedConversations[0].ImageDescriptionOverride.Model.ProviderId=="p"&&saved.UnassignedConversations[1].ImageDescriptionOverride.Mode=="disabled"&&saved.LegacyImageDescriptionOverrides["other"].Mode=="disabled","scoped selection preserves concurrent ordinary metadata and other overrides");
   var clear=typeof(ProjectCatalogStore).GetMethod("ClearImageDescriptionOverrides");Check(clear!=null,"catalog exposes scoped all-history selection cleanup");clear.Invoke(store,new object[0]);saved=store.Load();Check(saved.UnassignedConversations[0].Name=="concurrent rename"&&saved.UnassignedConversations.All(c=>c.ImageDescriptionOverride==null)&&saved.LegacyImageDescriptionOverrides.Count==0,"all-history cleanup preserves unrelated catalog metadata");
  }
  static void Routes()
  {
   var cfg=Settings(CapabilitySupport.Supported);var direct=ImageModelResolver.Resolve(cfg,null);
   Check(direct.Route==ImageRouteKind.Direct&&direct.DescriptionConnection==null&&direct.ConversationConnection.Model=="chat","explicit supported current model stays direct despite configured description");
   cfg.Text.ImageSupport=CapabilitySupport.Unsupported;var described=ImageModelResolver.Resolve(cfg,null);
   Check(described.Route==ImageRouteKind.Described&&described.DescriptionConnection.Model=="vision"&&described.DescriptionConnection.ApiKey=="first-secret"&&described.DescriptionConnection.UseProxy,"unsupported current model resolves full independent description connection without requiring tools");
   cfg.Text.ImageSupport=CapabilitySupport.Unknown;cfg.ImageDescriptionModel=null;
   Check(ImageModelResolver.Resolve(cfg,null).Route==ImageRouteKind.Unavailable,"unknown current image capability without selected fallback is unavailable");
   cfg.ImageDescriptionModel=new ModelReference{ProviderId="first",ModelId="vision"};
   Check(ImageModelResolver.Resolve(cfg,null).Route==ImageRouteKind.Described&&ImageModelResolver.Resolve(cfg,null).Reason=="current-images-unknown","unknown current capability permits explicit supported fallback with safe reason");
   Check(ImageModelResolver.Resolve(cfg,new ImageDescriptionOverride{Mode="disabled"}).Route==ImageRouteKind.Unavailable,"explicit disabled overrides global fallback");
   cfg.ImageDescriptionOverride=new ImageDescriptionOverride{Mode="disabled"};
   Check(ImageModelResolver.Resolve(cfg,null).Route==ImageRouteKind.Unavailable&&ImageModelResolver.Resolve(cfg,new ImageDescriptionOverride{Mode="inherit"}).Route==ImageRouteKind.Described,"effective settings override applies by default and explicit inherit restores global");cfg.ImageDescriptionOverride=null;
   var selected=new ImageDescriptionOverride{Mode="selected",Model=new ModelReference{ProviderId="second",ModelId="vision"}};
   Check(ImageModelResolver.Resolve(cfg,selected).Route==ImageRouteKind.Unavailable,"same model ID from different provider never borrows capabilities or credentials");
   cfg.TextProviders[1].ModelCapabilities["vision"].Images=CapabilitySupport.Unknown;
   Check(ImageModelResolver.Resolve(cfg,selected).Route==ImageRouteKind.Unavailable,"unknown fallback cannot send images");
   cfg.TextProviders[1].ServerCapabilities["vision"]=new ModelCapabilitySetting{Images=CapabilitySupport.Supported};
   Check(ImageModelResolver.Resolve(cfg,selected).Route==ImageRouteKind.Described,"server metadata explicitly supports fallback");
   cfg.TextProviders[1].ModelCapabilities["vision"].Images=CapabilitySupport.Unsupported;
   Check(ImageModelResolver.Resolve(cfg,selected).Route==ImageRouteKind.Unavailable,"user unsupported override wins over supported metadata");
   cfg.TextProviders[0].Name="renamed";cfg.TextProviders[0].ModelDisplayNames["vision"]="new alias";
   Check(ImageModelResolver.Resolve(cfg,null).Route==ImageRouteKind.Described,"provider and display renames retain stable reference");
   var before=Json(cfg);var snapshot=ImageModelResolver.Resolve(cfg,null);
   Check(Json(cfg)==before&&cfg.Text.Model=="chat"&&cfg.ActiveTextProviderId=="first"&&cfg.AgentEnabled,"resolving never changes input settings active model or Agent mode");
   cfg.Text.BaseUrl="https://new-chat.example/v1";cfg.TextProviders[0].Connection.ApiKey="new-secret";cfg.TextProviders[0].Connection.BaseUrl="https://changed.example/v1";
   Check(snapshot.ConversationConnection.BaseUrl=="https://chat.example/v1"&&snapshot.DescriptionConnection.BaseUrl=="https://first.example/v1"&&snapshot.DescriptionConnection.ApiKey=="first-secret","request route retains detached connection snapshots after later changes");
   Check(ImageModelResolver.Resolve(cfg,null).Route==ImageRouteKind.Unavailable,"endpoint change invalidates previously asserted fallback capabilities without mutating provider");
   Check(cfg.TextProviders[0].ModelCapabilities["vision"].Images==CapabilitySupport.Supported,"endpoint validation is performed on copy");
   cfg.TextProviders.RemoveAt(0);
   Check(ImageModelResolver.Resolve(cfg,null).Reason=="description-provider-missing","deleted provider does not match by same model ID");
   cfg.ImageDescriptionModel=new ModelReference{ProviderId="second",ModelId="removed"};
   Check(ImageModelResolver.Resolve(cfg,null).Reason=="description-model-missing","deleted model remains invalid instead of selecting another model");
   cfg.Text.ImageSupport=CapabilitySupport.Supported;
   Check(ImageModelResolver.Resolve(cfg,new ImageDescriptionOverride{Mode="disabled"}).Route==ImageRouteKind.Direct,"disabled fallback still permits direct supported current model");
   cfg.Text.ImageSupport=CapabilitySupport.Unknown;cfg.Text.MetadataImageSupport=CapabilitySupport.Supported;
   Check(ImageModelResolver.Resolve(cfg,null).Route==ImageRouteKind.Direct,"current model server metadata permits direct route");
   cfg.Text.ImageSupport=CapabilitySupport.Unsupported;
   Check(ImageModelResolver.Resolve(cfg,null).Route==ImageRouteKind.Unavailable,"current model user override wins over metadata");
   cfg=Settings(CapabilitySupport.Unknown);cfg.TextProviders[0].Connection.Protocol="anthropic";
   Check(ImageModelResolver.Resolve(cfg,null).Route==ImageRouteKind.Unavailable,"protocol change invalidates endpoint-bound capabilities");
   cfg=Settings(CapabilitySupport.Unknown);cfg.TextProviders[0].Connection.BaseUrl="";
   Check(ImageModelResolver.Resolve(cfg,null).Route==ImageRouteKind.Unavailable,"empty fallback destination is unavailable");
  }
  static void Storage(string root)
  {
   PetSettings legacy;using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("{}")))legacy=(PetSettings)new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream);legacy.Normalize();
   Check(!legacy.DesktopObservationEnabled&&legacy.DesktopObservationConsentVersion==0&&legacy.DesktopObservationConsentDestination==""&&legacy.DesktopObservationExcludedApps.Count==0&&legacy.ImageDescriptionModel==null,"old JSON defaults observation off and no description model");
   var cfg=Settings(CapabilitySupport.Unknown);cfg.DesktopObservationEnabled=true;cfg.DesktopObservationConsentVersion=1;cfg.DesktopObservationConsentDestination=" https://first.example/v1 ";cfg.DesktopObservationExcludedApps=new List<string>{" app ","app",null,""};cfg.ImageDescriptionModel.ProviderId=" first ";cfg.ImageDescriptionModel.ModelId=" vision ";cfg.Normalize();
   Check(cfg.ImageDescriptionModel.ProviderId=="first"&&cfg.ImageDescriptionModel.ModelId=="vision"&&cfg.DesktopObservationConsentDestination=="https://first.example/v1"&&cfg.DesktopObservationExcludedApps.SequenceEqual(new[]{"app"}),"normalization trims stable refs consent and distinct app exclusions");
   cfg.ImageDescriptionOverride=new ImageDescriptionOverride{Mode="selected",Model=cfg.ImageDescriptionModel.Copy()};var copy=cfg.Copy();copy.ImageDescriptionModel.ModelId="copy";copy.ImageDescriptionOverride.Model.ProviderId="copy";copy.DesktopObservationExcludedApps.Add("copy");
   Check(cfg.ImageDescriptionModel.ModelId=="vision"&&cfg.ImageDescriptionOverride.Model.ProviderId=="first"&&cfg.DesktopObservationExcludedApps.Count==1,"settings copy detaches reference override and exclusion collection");
   Check(Json(cfg.ImageDescriptionModel).Contains("ProviderId")&&!Json(cfg.ImageDescriptionModel).Contains("secret")&&!Json(cfg).Contains("ImageDescriptionOverride"),"new persistent reference contains IDs only and effective override is not serialized globally");
   var loaded=RoundTrip(cfg);
   Check(loaded.ImageDescriptionModel.ModelId=="vision"&&loaded.DesktopObservationEnabled&&loaded.DesktopObservationConsentVersion==1&&loaded.DesktopObservationExcludedApps[0]=="app","export import roundtrip retains new fields");
   var invalid=new ImageDescriptionOverride{Mode="unexpected",Model=new ModelReference{ProviderId="first",ModelId="vision"}};invalid.Normalize();
   Check(invalid.Mode=="inherit"&&invalid.Model==null,"unknown override normalizes to inherit without stale selected model");
   var disabled=new ImageDescriptionOverride{Mode="disabled",Model=cfg.ImageDescriptionModel.Copy()};disabled.Normalize();Check(disabled.Model==null,"disabled override drops unused reference");
   var missing=new ImageDescriptionOverride{Mode="selected"};missing.Normalize();Check(missing.Mode=="selected"&&missing.Model==null,"broken selected reference remains selected and never silently inherits global");
   string path=Path.Combine(root,"settings.json");var store=new SettingsStore(path);store.Save(cfg);var patch=store.CapturePreferences(cfg,"Volume");var newer=store.Load();newer.ImageDescriptionModel.ModelId="deleted-but-retained";newer.DesktopObservationConsentDestination="https://new.example/v1";new SettingsStore(path).Save(newer);store.SavePreferencePatchAsync(patch).GetAwaiter().GetResult();
   Check(store.Load().ImageDescriptionModel.ModelId=="deleted-but-retained"&&store.Load().DesktopObservationConsentDestination=="https://new.example/v1","older ordinary preference patch cannot overwrite newer image model or consent");
   var stale=store.Load();stale.Volume=84;patch=store.CapturePreferences(stale,"Volume");newer=store.Load();newer.DesktopObservationExcludedApps.Add("second-app");store.Save(newer);store.SavePreferencePatchAsync(patch).GetAwaiter().GetResult();
   Check(store.Load().DesktopObservationExcludedApps.Count==2,"ordinary preference merge retains newer app exclusions");
   store.RestoreDefaultPreferencesAsync(newer,new Dafeiyu.Maintenance.DataMaintenanceGate()).GetAwaiter().GetResult();
   Check(!newer.DesktopObservationEnabled&&!store.Load().DesktopObservationEnabled&&newer.ImageDescriptionModel.ModelId=="deleted-but-retained"&&newer.DesktopObservationConsentVersion==1&&newer.DesktopObservationExcludedApps.Count==2,"preference reset disables observation while preserving model consent and exclusion configuration");
   var chat=new ConversationDefinition{Id="chat",Name="chat",Mode="chat",ImageDescriptionOverride=new ImageDescriptionOverride{Mode="selected",Model=new ModelReference{ProviderId="first",ModelId="vision"}}};
   var catalog=new ProjectCatalog{UnassignedConversations=new List<ConversationDefinition>{chat}};catalog.LegacyImageDescriptionOverrides["pet"]=chat.ImageDescriptionOverride.Copy();var detached=catalog.Copy();detached.UnassignedConversations[0].ImageDescriptionOverride.Model.ModelId="copy";detached.LegacyImageDescriptionOverrides["pet"].Model.ModelId="copy";
   Check(chat.ImageDescriptionOverride.Model.ModelId=="vision"&&catalog.LegacyImageDescriptionOverrides["pet"].Model.ModelId=="vision","catalog copy deeply detaches both persistent and legacy root overrides");
   var catalogStore=new ProjectCatalogStore(Path.Combine(root,"catalog.json"));catalogStore.Save(catalog);var restored=catalogStore.Load();
   Check(restored.LegacyImageDescriptionOverrides["pet"].Model.ModelId=="vision"&&restored.UnassignedConversations[0].ImageDescriptionOverride.Mode=="selected","catalog saves legacy persona override and conversation override independently");
   restored.RemoveLegacyImageDescriptionOverride("pet");Check(restored.LegacyImageDescriptionOverrides.Count==0&&restored.UnassignedConversations[0].ImageDescriptionOverride!=null,"persona history cleanup removes only that root override");
   restored.LegacyImageDescriptionOverrides["other"]=new ImageDescriptionOverride{Mode="disabled"};restored.ClearImageDescriptionOverrides();Check(restored.LegacyImageDescriptionOverrides.Count==0&&restored.UnassignedConversations[0].ImageDescriptionOverride==null,"all-history cleanup clears root and persistent conversation overrides");
   ProjectCatalog oldCatalog;using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("{\"Version\":1,\"Projects\":[]}")))oldCatalog=(ProjectCatalog)new DataContractJsonSerializer(typeof(ProjectCatalog)).ReadObject(stream);ProjectCatalogStore.NormalizeDefaults(oldCatalog);Check(oldCatalog.LegacyImageDescriptionOverrides.Count==0,"old catalog JSON initializes empty legacy overrides without history migration");
  }
  sealed class Api:IConversationApi {public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){return Task.FromResult("");}public Task<string> TranscribeAsync(ConnectionSettings s,byte[] b,CancellationToken c){return Task.FromResult("");}public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){return Task.FromResult(new byte[0]);}public void Dispose(){}}
  sealed class Speech:IReplySpeech {public Task SpeakAsync(string t,PetSettings s,CancellationToken c){return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
  static void Workspace(string root)
  {
   var cfg=Settings(CapabilitySupport.Unknown);var project=new ProjectDefinition{Id="p",Name="p",Workspace=root};var definition=new ConversationDefinition{Id="c",Name="c",Mode="chat",ImageDescriptionOverride=new ImageDescriptionOverride{Mode="disabled"}};
   using(var workspace=ConversationWorkspace.Create(project,definition,root,"pet",()=>new Api(),()=>new Speech(),()=>null,()=>cfg,()=>false)){
    var effective=workspace.EffectiveSettings();Check(ImageModelResolver.Resolve(effective,null).Route==ImageRouteKind.Unavailable&&workspace.ProjectId=="p"&&workspace.Id=="c","effective conversation settings carry local override and preserve ownership");
    definition.ImageDescriptionOverride.Mode="inherit";Check(effective.ImageDescriptionOverride.Mode=="disabled"&&ImageModelResolver.Resolve(workspace.EffectiveSettings(),null).Route==ImageRouteKind.Described,"effective settings detach override so switching only affects next request");
    Check(cfg.ImageDescriptionOverride==null&&cfg.AgentEnabled,"conversation override does not modify global settings or Agent mode");
   }
  }
 }
}
