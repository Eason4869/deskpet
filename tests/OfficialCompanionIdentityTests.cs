using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Runtime.Serialization.Json;
namespace Dafeiyu.Tests {
public static class OfficialCompanionIdentityTests {
 static int passed;
 static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
 static Type Identity { get { var t=typeof(PetSettings).Assembly.GetType("Dafeiyu.OfficialCompanionIdentity");if(t==null)throw new Exception("identity helper exists");return t;} }
 static string Official(string root,string id,params string[] occupied){return (string)Identity.GetMethod("ResolveOfficial").Invoke(null,new object[]{root,id,occupied});}
 static string Custom(string root,string id,bool fresh,params string[] occupied){return (string)Identity.GetMethod("ResolveCustom").Invoke(null,new object[]{root,id,occupied,fresh});}
 static string Key(string key){return (string)Identity.GetMethod("NormalizePickerKey").Invoke(null,new object[]{key});}
 static void Reject(Action action,string name){try{action();}catch(TargetInvocationException e){if(e.InnerException is InvalidDataException){Check(true,name);return;}throw;}throw new Exception(name);}
 public static int Main(){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"identity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{Run(root);Console.WriteLine("Official identity/profile tests: "+passed+" passed");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}finally{Directory.Delete(root,true);}}
 static void Run(string root){
  string old="skin-"+new string('a',32);CompanionPackStorage.RetainIdentity(root,"xiaobei",old);
  string official=Official(root,"xiaobei",old);Check(official.StartsWith("skin-")&&official!=old,"spoofed custom retains persona while official receives separate alias");
  Check(CompanionPackStorage.Identity(root,"xiaobei")==old&&Custom(root,"xiaobei",false)==old,"existing custom identity mapping is untouched");
  Check(Official(root,"xiaobei")==official,"official assignment remains stable after conflicting custom disappears");
  Check(File.ReadAllText(Path.Combine(root,"official-companions/xiaobei/identity.txt"))==official,"official alias persists outside versions");
  string normal=Official(root,"e-bao");Check(normal=="e-bao"&&File.Exists(Path.Combine(root,"official-companions/e-bao/identity.txt")),"ordinary official identity is persisted too");
  string custom=Custom(root,"e-bao",true,normal);Check(custom.StartsWith("skin-")&&custom!=normal,"new custom import cannot take official persona");
  Check(Custom(root,"e-bao",false,normal)==custom&&CompanionPackStorage.Identity(root,"e-bao")==normal,"custom override stable without rewriting historical identity");
  Reject(()=>Official(root,"e-bao",normal),"existing conflicting official ownership fails closed");
  Reject(()=>Custom(root,"e-bao",false,custom),"conflicting stored custom alias fails closed");
  var concurrent=Enumerable.Range(0,16).Select(i=>Task.Run(()=>Official(root,"dafeiyu","dafeiyu"))).ToArray();Task.WaitAll(concurrent);Check(concurrent.Select(t=>t.Result).Distinct().Count()==1,"concurrent creators publish exactly one stable alias");
  Check(!Directory.GetFiles(root,".identity-*.tmp",SearchOption.AllDirectories).Any(),"atomic sidecars leave no temporary debris");
  string shared="skin-"+new string('b',32);CompanionPackStorage.RetainIdentity(root,"gugugaga",shared);string assigned=Official(root,"gugugaga");Check(assigned==shared,"proven canonical legacy alias is retained");
  string duplicateRoot=Path.Combine(root,"duplicate");CompanionPackStorage.RetainIdentity(duplicateRoot,"xiaobei",shared);CompanionPackStorage.RetainIdentity(duplicateRoot,"gugugaga",shared);string first=Official(duplicateRoot,"xiaobei");Check(Official(duplicateRoot,"gugugaga")!=first,"two official roles cannot share a retained persona alias");
  Directory.CreateDirectory(Path.Combine(root,"official-companions/marfu"));File.WriteAllText(Path.Combine(root,"official-companions/marfu/identity.txt"),new string('x',129));Reject(()=>Official(root,"marfu"),"oversized sidecar rejected without replacement");
  File.WriteAllText(Path.Combine(root,"official-companions/marfu/identity.txt"),"../e-bao");Reject(()=>Official(root,"marfu"),"malformed sidecar is not rotated");
  Check(Key("official:gugugaga")=="official:gugugaga"&&Key("custom-pack:my-pet")=="custom-pack:my-pet"&&Key("legacy-skin:"+old)=="legacy-skin:"+old,"three stable picker namespaces accepted");
  foreach(string bad in new[]{"official:my-pet","custom-pack:../escape","custom-pack:con","legacy-skin:skin-AAAA","official:e-bao\n"," official:e-bao","",null,new string('x',300)})Check(Key(bad)=="","malformed picker key discarded");
  var selected=typeof(PetSettings).GetProperty("SelectedCompanionKey");var pending=typeof(PetSettings).GetProperty("PendingOfficialCompanionId");Check(selected!=null&&pending!=null,"settings exposes independent selection and restore fields");
  var settings=new PetSettings();settings.SkinId=old;selected.SetValue(settings,"official:xiaobei",null);pending.SetValue(settings,"xiaobei",null);settings.Normalize();var copy=settings.Copy();Check((string)selected.GetValue(copy,null)=="official:xiaobei"&&(string)pending.GetValue(copy,null)=="xiaobei"&&copy.SkinId==old,"copy preserves picker pending and persona independently");
  using(var bytes=new MemoryStream()){var serializer=new DataContractJsonSerializer(typeof(PetSettings));serializer.WriteObject(bytes,settings);bytes.Position=0;var loaded=(PetSettings)serializer.ReadObject(bytes);loaded.Normalize();Check((string)selected.GetValue(loaded,null)=="official:xiaobei"&&(string)pending.GetValue(loaded,null)=="xiaobei","serialized settings retain offline restore");}
  selected.SetValue(settings,"official:spoof",null);pending.SetValue(settings,"../escape",null);settings.Normalize();Check((string)selected.GetValue(settings,null)==""&&(string)pending.GetValue(settings,null)==""&&settings.SkinId==old,"normalization rejects unknown provenance without guessing persona");
  var profiles=typeof(PetSettings).Assembly.GetType("Dafeiyu.OfficialCompanionProfiles");Check(profiles!=null,"small shared official profiles exist");var find=profiles.GetMethod("Find");
  foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}){object p=find.Invoke(null,new object[]{id});Check(p!=null,"official profile "+id);var type=p.GetType();Check((string)type.GetProperty("AvatarResourceName").GetValue(p,null)=="Dafeiyu.Avatar."+id,"avatar resource "+id);Check(((string)type.GetProperty("SystemPrompt").GetValue(p,null)).Length>60,"full persona "+id);var voices=(VoiceRecommendations)type.GetProperty("VoicePresets").GetValue(p,null);string before=voices.Speech[0];voices.Speech[0]="changed";var again=(VoiceRecommendations)type.GetProperty("VoicePresets").GetValue(p,null);Check(again.Speech[0]==before,"voice metadata cannot be mutated "+id);}
  Check(find.Invoke(null,new object[]{"spoof"})==null,"unknown profile cannot acquire official metadata");
 }
}}

