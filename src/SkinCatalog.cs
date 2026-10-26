using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    [DataContract] public sealed class SkinManifest
    {
        [DataMember(Name="id",EmitDefaultValue=false)] public string PackageId {get;set;}
        [DataMember(Name="pack_version",EmitDefaultValue=false)] public string PackVersion {get;set;}
        [DataMember(Name="avatar",EmitDefaultValue=false)] public string Avatar {get;set;}
        [DataMember(Name="special_action2",EmitDefaultValue=false)] public string SpecialAction2 {get;set;}
        [DataMember(Name="extra_animations",EmitDefaultValue=false)] public Dictionary<string,IllustratedAnimation> ExtraAnimations {get;set;}
        [DataMember(Name="alternate_animations",EmitDefaultValue=false)] public Dictionary<string,IllustratedAnimation> AlternateAnimations {get;set;}
        [DataMember(Name="relationship",EmitDefaultValue=false)] public RelationshipPackContent Relationship {get;set;}
        [DataMember(Name="voice_presets",EmitDefaultValue=false)] public VoiceRecommendations VoicePresets {get;set;}
        [DataMember(Name="version")] public int Version { get; set; }
        [DataMember(Name="name")] public string Name { get; set; }
        [DataMember(Name="atlas")] public string Atlas { get; set; }
        [DataMember(Name="system_prompt",EmitDefaultValue=false)] public string SystemPrompt { get; set; }
        [DataMember(Name="description",EmitDefaultValue=false)] public string Description { get; set; }
        [DataMember(Name="format",EmitDefaultValue=false)] public string Format { get; set; }
        [DataMember(Name="cell_width",EmitDefaultValue=false)] public int CellWidth { get; set; }
        [DataMember(Name="cell_height",EmitDefaultValue=false)] public int CellHeight { get; set; }
        [DataMember(Name="preview",EmitDefaultValue=false)] public string Preview { get; set; }
        [DataMember(Name="special_action",EmitDefaultValue=false)] public string SpecialAction { get; set; }
        [DataMember(Name="animations",EmitDefaultValue=false)] public Dictionary<string,IllustratedAnimation> Animations { get; set; }
        [DataMember(Name="gaze",EmitDefaultValue=false)] public IllustratedGaze Gaze { get; set; }
    }
    public sealed class SkinDefinition
    {
        private static readonly int[] MarfuRightStride={0,2,3,2,4,2,6,2};
        public SkinManifest Manifest { get; private set; }
        public bool Illustrated { get { return Manifest!=null && Manifest.Version==2; } }
        public string SpecialAction { get {
            if(Illustrated && !String.IsNullOrWhiteSpace(Manifest.SpecialAction))return Manifest.SpecialAction;
            if(Embedded){switch(BuiltinCharacterId??Id){case "e-bao":return TextResources.Get("skinCatalog.specialAction.text");case "dafeiyu":return TextResources.Get("skinCatalog.specialAction.text.alternativeA");case "marfu":return TextResources.Get("skinCatalog.specialAction.text.alternativeB");}}
            return TextResources.Get("skinCatalog.specialAction.text.alternativeC");
        } }
        internal SkinDefinition(string id,string folder,SkinManifest manifest) : this(id,SkinCatalog.CleanName(manifest.Name),folder,manifest.Preview,false,manifest.SystemPrompt,manifest.Description) { Manifest=manifest; }
        internal SkinDefinition(string id,string name,string atlas,string prompt,string description,SkinManifest manifest)
            : this(id,name,atlas,null,false,prompt,description) { Manifest=manifest; }
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string DisplayName { get { return OfficialCompanionProfiles.GetDisplayName(BuiltinCharacterId,Name); } }
        public string DisplayDescription { get { var profile=OfficialCompanionProfiles.Find(BuiltinCharacterId);return profile==null?Description:profile.Description; } }
        public string Source { get; private set; }
        public string Preview { get; private set; }
        public bool Embedded { get; private set; }
        // Trusted registration metadata; imported manifests cannot assign this.
        public string BuiltinCharacterId {get;internal set;}
        internal HolidayCatalog PackageHolidayCatalog {get;set;}
        internal string OutfitCharacterId {get{return BuiltinCharacterId??(PackageHolidayCatalog!=null&&Manifest!=null?Manifest.PackageId:null);}}
        string pickerKey;
        public string PickerKey {get{return pickerKey??(!String.IsNullOrEmpty(BuiltinCharacterId)?"official:"+BuiltinCharacterId:Illustrated&&!String.IsNullOrEmpty(Manifest.PackageId)?"custom-pack:"+Manifest.PackageId:"legacy-skin:"+Id);}internal set{pickerKey=value;}}
        public bool IsPlaceholder {get;internal set;}
        public bool IsOfficial {get{return OfficialCompanionProfiles.IsOfficialId(BuiltinCharacterId);}}
        readonly string nativeSourceKey=Guid.NewGuid().ToString("N");
        public string NativeSourceKey {get{return nativeSourceKey;}}
        internal OfficialCompanionInstallation OfficialInstallation {get;set;}
        internal SpriteAtlas PreparedOfficialAtlas {get;set;}
        internal bool FixtureEmbedded {get;set;}
        public VoiceRecommendations VoiceRecommendations {get{return Manifest!=null&&Manifest.VoicePresets!=null?Manifest.VoicePresets:IsOfficial?OfficialCompanionProfiles.Find(BuiltinCharacterId).VoicePresets:null;}}
        internal bool Bundled { get; set; }
        public string SystemPrompt { get; private set; }
        public string Description { get; private set; }
        public SkinDefinition(string id,string name,string source,string preview,bool embedded,string prompt=null,string description=null)
        {
            Id=id; Name=name; Source=source; Preview=preview; Embedded=embedded;
            SystemPrompt=String.IsNullOrWhiteSpace(prompt) ? (id=="dafeiyu" ? PersonaProfiles.Dafeiyu : id=="e-bao" ? PersonaProfiles.Ebao : PersonaProfiles.Generic(name)) : prompt.Trim();
            Description=description ?? (id=="e-bao" ? TextResources.Get("skinCatalog.skinDefinition.descriptionDescription") : id=="dafeiyu" ? TextResources.Get("skinCatalog.skinDefinition.descriptionDescription.alternativeA") : TextResources.Get("skinCatalog.skinDefinition.descriptionDescription.alternativeB"));
        }
        public override string ToString() { return DisplayName; }
        public SpriteAtlas Load()
        {
            if(IsPlaceholder)throw new InvalidOperationException(TextResources.Get("skinCatalog.load.text"));
            if(PreparedOfficialAtlas!=null)return PreparedOfficialAtlas;
            return Illustrated ? new SpriteAtlas(Source,Manifest) : new SpriteAtlas(Source,Embedded);
        }
        public int ActionFrame(int row,int phase)
        {
            // Emphasize Marfu's planted/tucked/extended poses instead of holding
            // several near-identical planted frames; every selection is real artwork.
            if(!Embedded || (BuiltinCharacterId??Id)!="marfu" || phase<0 || phase>=8) return phase;
            return row==1 || row==2 ? MarfuRightStride[phase] : phase;
        }
        public int ActionRow(int row) { return MirrorsAction(row) ? 1 : row; }
        public bool MirrorsAction(int row) { string id=BuiltinCharacterId??Id;return Embedded && (id=="marfu" || id=="dafeiyu") && row==2; }
        public AnimationPlayer CreateAnimation(int frameMultiplier=1)
        {
            double period=Embedded && ((BuiltinCharacterId??Id)=="marfu" || (BuiltinCharacterId??Id)=="e-bao" || (BuiltinCharacterId??Id)=="dafeiyu") ? .22 : .11;
            double[] durations=null;
            if(Illustrated) durations=AnimationPlayer.States.Select(n=>Manifest.Animations[n].Loop ? 0 : Manifest.Animations[n].CompletionDuration).ToArray();
            if(AdditionalInteractions.Has(this,"jumping")){if(durations==null)durations=new double[AnimationPlayer.States.Length];IllustratedAnimation clip;durations[4]=AdditionalInteractions.Specification(this,"jumping",false,out clip)?clip.CompletionDuration:1;}
            return new AnimationPlayer(frameMultiplier,period,durations);
        }
    }
    public sealed class SkinCatalog
    {
        private static readonly Dictionary<string,BitmapSource> avatars=new Dictionary<string,BitmapSource>();
        private readonly string directory;
        private readonly string dataRoot;
        private readonly List<SkinDefinition> skins=new List<SkinDefinition>();
        readonly Dictionary<string,SkinDefinition> officialRegistry=new Dictionary<string,SkinDefinition>(StringComparer.Ordinal);
        readonly HashSet<string> customPackageIds=new HashSet<string>(StringComparer.Ordinal);
        private readonly List<SkinDefinition> supplied=new List<SkinDefinition>();
        SkinDefinition embeddedFallback;
        private readonly Dictionary<string,string> aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        public IList<SkinDefinition> Skins { get { return skins.AsReadOnly(); } }
        internal event Action<SkinDefinition> CompanionRemoved;
        public bool HasRemoved {get{return CompanionPackStorage.Hidden(dataRoot).Count>0;}}
        public SkinCatalog(string dataDirectory)
        {
            dataRoot=dataDirectory;directory=Path.Combine(dataDirectory,"skins");
            var existing=Task.Run(()=>DiscoverExisting()).GetAwaiter().GetResult();
            var untrusted=existing.Where(s=>!s.IsOfficial).Select(s=>s.Id).ToArray();
            foreach(var custom in existing.Where(s=>!s.IsOfficial&&s.Illustrated&&!String.IsNullOrEmpty(s.Manifest.PackageId)))customPackageIds.Add(custom.Manifest.PackageId);
            var resources=new HashSet<string>(Assembly.GetExecutingAssembly().GetManifestResourceNames(),StringComparer.Ordinal);
            foreach(var profile in OfficialCompanionProfiles.All) {
                string persona=OfficialCompanionIdentity.ResolveOfficial(dataRoot,profile.Id,untrusted);
                var canonical=existing.FirstOrDefault(s=>s.BuiltinCharacterId==profile.Id);SkinDefinition registered=canonical;
                if(registered==null) {
                    string atlas=profile.Id=="e-bao"?"Dafeiyu.EbaoAtlas":profile.Id=="dafeiyu"?"Dafeiyu.Atlas":profile.Id=="marfu"?"Dafeiyu.MarfuAtlas":null;
                    string preview=profile.Id=="e-bao"?"Dafeiyu.EbaoPreview":profile.Id=="dafeiyu"?"Dafeiyu.Preview":"Dafeiyu.MarfuPreview";
                    bool embedded=atlas!=null&&resources.Contains(atlas);
                    registered=embedded?new SkinDefinition(persona,profile.Name,atlas,preview,true,profile.SystemPrompt,profile.Description){BuiltinCharacterId=profile.Id,FixtureEmbedded=profile.Id!="e-bao"}:new SkinDefinition(persona,profile.Name,null,null,false,profile.SystemPrompt,profile.Description){BuiltinCharacterId=profile.Id,IsPlaceholder=true};
                }
                if(registered.Id!=persona&&registered.Illustrated)registered=new SkinDefinition(persona,registered.Source,registered.Manifest){BuiltinCharacterId=profile.Id};
                if(profile.Id=="e-bao"&&registered.Embedded)embeddedFallback=registered;
                if(resources.Contains("Dafeiyu.Bundled."+(profile.Id=="xiaobei"?"Xiaobei":profile.Id=="gugugaga"?"Gugugaga":"None")+".pet.json")){registered.FixtureEmbedded=true;registered.Bundled=true;}
                officialRegistry[profile.Id]=registered;skins.Add(registered);
            }
            foreach(var bundled in BundledSkinCache.Registered(dataDirectory)) {
                var current=FindOfficial(bundled.BuiltinCharacterId);
                if(current!=null&&current.IsPlaceholder)ReplaceOfficial(new SkinDefinition(current.Id,bundled.Source,bundled.Manifest){BuiltinCharacterId=current.BuiltinCharacterId,Bundled=true,FixtureEmbedded=true});
            }
            foreach(var item in existing.Where(s=>!s.IsOfficial))skins.Add(item);
            ApplyHidden();
        }
        List<SkinDefinition> DiscoverExisting()
        {
            var result=new List<SkinDefinition>();
            foreach(var pack in CompanionPackStorage.Installed(dataRoot)) {
                try {
                    var manifest=ReadManifest(Path.Combine(pack.Value,"pet.json"));if(manifest.PackageId!=pack.Key)continue;
                    IllustratedPack.Validate(pack.Value,manifest,false);
                    result.Add(new SkinDefinition(OfficialCompanionIdentity.ResolveCustom(dataRoot,pack.Key,new string[0],false),pack.Value,manifest));
                }catch(Exception e){if(!CatalogError(e))throw;}
            }
            if(Directory.Exists(directory))foreach(string folder in Directory.EnumerateDirectories(directory).Take(256)) {
                try {
                    var manifest=ReadManifest(Path.Combine(folder,"pet.json"));
                    if(manifest.Version==2){IllustratedPack.Validate(folder,manifest,false);result.Add(new SkinDefinition(Path.GetFileName(folder),folder,manifest));continue;}
                    string atlas=AtlasPath(folder,manifest.Atlas);ValidatePng(atlas);
                    result.Add(new SkinDefinition(Path.GetFileName(folder),CleanName(manifest.Name),atlas,manifest.SystemPrompt,manifest.Description,manifest));
                }catch(Exception e){if(!CatalogError(e))throw;}
            }
            foreach(var item in result.Where(s=>s.Illustrated))foreach(string id in new[]{"xiaobei","gugugaga"}) {
                try {
                    var proven=OfficialCompanionLegacy.MatchImport(item.Source,id,CancellationToken.None);if(proven==null)continue;
                    string assigned=CompanionPackStorage.AssetPath(dataRoot,"official-companions/"+id+"/identity.txt");
                    if(File.Exists(assigned)&&(new FileInfo(assigned).Length>128||File.ReadAllText(assigned)!=item.Id))continue;
                    WpfOfficialNativeValidator.ValidateDefault(item.Source,CancellationToken.None);item.BuiltinCharacterId=id;
                    if(item.Id.StartsWith("skin-",StringComparison.Ordinal))CompanionPackStorage.RetainIdentity(dataRoot,id,item.Id);
                    break;
                }catch(Exception e){if(!CatalogError(e))throw;}
            }
            foreach(string id in new[]{"xiaobei","gugugaga"})if(!result.Any(s=>s.BuiltinCharacterId==id)) {
                try {
                    var cached=OfficialCompanionLegacy.FindBundled(dataRoot,id,CancellationToken.None);if(cached==null)continue;
                    WpfOfficialNativeValidator.ValidateDefault(cached.RootDirectory,CancellationToken.None);result.Add(new SkinDefinition(CompanionPackStorage.Identity(dataRoot,id),cached.RootDirectory,ReadManifest(Path.Combine(cached.RootDirectory,"pet.json"))){BuiltinCharacterId=id});
                }catch(Exception e){if(!CatalogError(e))throw;}
            }
            return result;
        }
        static bool CatalogError(Exception e){return e is IOException||e is InvalidDataException||e is UnauthorizedAccessException||e is SerializationException||e is ArgumentException||e is InvalidOperationException||e is NotSupportedException;}
        public SkinDefinition Find(string id){string alias;if(id!=null&&aliases.TryGetValue(id,out alias))id=alias;return skins.FirstOrDefault(s=>s.Id==id)??skins.FirstOrDefault(s=>s.PickerKey==id)??FindOfficial(id)??skins[0];}
        public SkinDefinition FindPicker(string key){return skins.FirstOrDefault(s=>s.PickerKey==key);}
        public SkinDefinition FindOfficial(string id){SkinDefinition found;return id!=null&&officialRegistry.TryGetValue(id,out found)?found:null;}
        internal SkinDefinition EmbeddedFallback(){return embeddedFallback??skins.First(s=>s.BuiltinCharacterId=="e-bao"&&s.Embedded);}
        internal void ReplaceOfficial(SkinDefinition skin){officialRegistry[skin.BuiltinCharacterId]=skin;int index=skins.FindIndex(s=>s.BuiltinCharacterId==skin.BuiltinCharacterId);if(index>=0)skins[index]=skin;else if(skin.BuiltinCharacterId=="e-bao"||!CompanionPackStorage.Hidden(dataRoot).Contains(skin.PickerKey))skins.Add(skin);}
        internal void PreserveActive(SkinDefinition skin){if(skin.IsOfficial)ReplaceOfficial(skin);else{int index=skins.FindIndex(s=>s.PickerKey==skin.PickerKey);if(index>=0)skins[index]=skin;}}
        internal SkinDefinition FromOfficial(OfficialCompanionInstallation installation){var current=FindOfficial(installation.Asset.Id);return new SkinDefinition(current.Id,installation.RootDirectory,ReadManifest(installation.PetManifestPath)){BuiltinCharacterId=installation.Asset.Id,OfficialInstallation=installation};}
        void Put(SkinDefinition skin)
        {
            string key=skin.PickerKey;int index=skins.FindIndex(s=>s.PickerKey==key);
            skins.RemoveAll(s=>s.PickerKey==key);
            skins.Insert(index<0?skins.Count:Math.Min(index,skins.Count),skin);
        }
        void ApplyHidden()
        {
            var hidden=CompanionPackStorage.Hidden(dataRoot);var fallback=skins[0];
            skins.RemoveAll(s=>!(s.IsOfficial&&s.BuiltinCharacterId=="e-bao")&&(hidden.Contains(s.PickerKey)||!s.IsOfficial&&hidden.Contains(s.Illustrated&&!String.IsNullOrEmpty(s.Manifest.PackageId)?s.Manifest.PackageId:s.Id)));
            if(skins.Count==0)skins.Add(fallback);
        }
        public void Remove(SkinDefinition skin)
        {
            if(skin!=null&&skin.IsOfficial&&skin.BuiltinCharacterId=="e-bao")throw new InvalidOperationException(TextResources.Get("skinCatalog.remove.text"));
            if(skins.Count<=1)throw new InvalidOperationException(TextResources.Get("skinCatalog.remove.text.alternativeA"));
            if(!skins.Contains(skin))throw new InvalidOperationException(TextResources.Get("skinCatalog.remove.text.alternativeB"));
            CompanionPackStorage.SetHidden(dataRoot,skin.PickerKey,true);skins.Remove(skin);if(CompanionRemoved!=null)CompanionRemoved(skin);
        }
        public void RestoreRemoved()
        {
            CompanionPackStorage.RestoreHidden(dataRoot);var restored=new SkinCatalog(dataRoot);
            embeddedFallback=restored.embeddedFallback;skins.Clear();skins.AddRange(restored.skins);officialRegistry.Clear();foreach(var pair in restored.officialRegistry)officialRegistry[pair.Key]=pair.Value;customPackageIds.Clear();foreach(string id in restored.customPackageIds)customPackageIds.Add(id);supplied.Clear();supplied.AddRange(restored.supplied);aliases.Clear();foreach(var pair in restored.aliases)aliases[pair.Key]=pair.Value;
        }
        void ReconcileLegacy(bool restore=false)
        {
            foreach(var legacy in skins.Where(s=>s.Illustrated&&!s.IsOfficial).ToArray())foreach(string id in new[]{"xiaobei","gugugaga"}) {
                var source=Task.Run(()=>OfficialCompanionLegacy.MatchImport(legacy.Source,id,CancellationToken.None)).GetAwaiter().GetResult();if(source==null)continue;
                Task.Run(()=>WpfOfficialNativeValidator.ValidateDefault(legacy.Source,CancellationToken.None)).GetAwaiter().GetResult();
                var official=FindOfficial(id);if(official==null)continue;
                var updated=new SkinDefinition(official.Id,legacy.Source,legacy.Manifest){BuiltinCharacterId=id};
                aliases[legacy.Id]=official.Id;skins.Remove(legacy);ReplaceOfficial(updated);break;
            }
        }
        static bool SameArtwork(SkinDefinition left,SkinDefinition right)
        {
            if(left.Name!=right.Name || left.SystemPrompt!=right.SystemPrompt)return false;
            foreach(string action in IllustratedPack.Actions) {
                var a=left.Manifest.Animations[action];var b=right.Manifest.Animations[action];
                if(a.Frames!=b.Frames || a.Fps!=b.Fps || a.Loop!=b.Loop || a.HoldLastSeconds!=b.HoldLastSeconds || !SameFile(left.Source,a.File,right.Source,b.File))return false;
            }
            return SameFile(left.Source,left.Manifest.Gaze.File,right.Source,right.Manifest.Gaze.File) && SameFile(left.Source,left.Manifest.Preview,right.Source,right.Manifest.Preview);
        }
        static bool SameFile(string left,string a,string right,string b)
        {
            using(var x=File.OpenRead(IllustratedPack.AssetPath(left,a)))using(var y=File.OpenRead(IllustratedPack.AssetPath(right,b)))using(var sha=SHA256.Create())return x.Length==y.Length && sha.ComputeHash(x).SequenceEqual(sha.ComputeHash(y));
        }
        public SkinDefinition Import(string path)
        {
            if(String.Equals(Path.GetExtension(path),".zip",StringComparison.OrdinalIgnoreCase))return CompanionPackStorage.ReadZip(path,dataRoot,Import);
            string atlas,name,prompt=null,description=null;SkinManifest legacy=null;
            if(String.Equals(Path.GetExtension(path),".json",StringComparison.OrdinalIgnoreCase))
            { var m=ReadManifest(path); if(m.Version==2)return ImportIllustrated(path,m); legacy=m;atlas=AtlasPath(Path.GetDirectoryName(Path.GetFullPath(path)),m.Atlas); name=CleanName(m.Name); prompt=m.SystemPrompt; description=m.Description; }
            else
            { if(!String.Equals(Path.GetExtension(path),".png",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(TextResources.Get("skinCatalog.import.text")); atlas=path; name=CleanName(Path.GetFileNameWithoutExtension(path)); }
            ValidatePng(atlas);
            // Decode and validate every required frame before adding a saved design.
            try { new SpriteAtlas(atlas,false); }
            catch(Exception e)
            {
                if(!(e is FileFormatException || e is NotSupportedException || e is ArgumentException)) throw;
                throw new InvalidOperationException(TextResources.Get("skinCatalog.import.text.alternativeA"),e);
            }
            string id="skin-"+Guid.NewGuid().ToString("N");
            string destination=Path.Combine(directory,id);
            Directory.CreateDirectory(destination);
            try
            {
                File.Copy(atlas,Path.Combine(destination,"spritesheet.png"),false);
                var manifest=new SkinManifest { Version=1,Name=name,Atlas="spritesheet.png",SystemPrompt=String.IsNullOrWhiteSpace(prompt) ? PersonaProfiles.Generic(name) : prompt,Description=description,Relationship=legacy==null?null:legacy.Relationship };
                using(var stream=File.Create(Path.Combine(destination,"pet.json"))) Serializer().WriteObject(stream,manifest);
                var skin=new SkinDefinition(id,name,Path.Combine(destination,"spritesheet.png"),manifest.SystemPrompt,description,manifest);
                skins.Add(skin); return skin;
            }
            catch
            {
                // This directory is a new GUID child created by this import alone.
                foreach(string file in Directory.GetFiles(destination)) File.Delete(file);
                Directory.Delete(destination); throw;
            }
        }
        private SkinDefinition ImportIllustrated(string path,SkinManifest manifest)
        {
            string source=Path.GetDirectoryName(Path.GetFullPath(path));
            var files=IllustratedPack.Validate(source,manifest,true);
            if(!String.IsNullOrEmpty(manifest.PackageId)) {
                if(!String.Equals(Path.GetFileName(path),"pet.json",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(TextResources.Get("skinCatalog.importIllustrated.text"));
                bool existing=customPackageIds.Contains(manifest.PackageId);
                var inventory=WpfOfficialNativeValidator.ValidateLocalPackage(source,CancellationToken.None);
                CompanionPackageInventory installedInventory=null;
                string installedFolder=CompanionPackStorage.Install(source,dataRoot,manifest.PackageId,inventory.AllReferences,
                    folder=>{installedInventory=WpfOfficialNativeValidator.ValidateLocalPackage(folder,CancellationToken.None);});
                string persona=OfficialCompanionIdentity.ResolveCustom(dataRoot,manifest.PackageId,skins.Where(s=>s.IsOfficial).Select(s=>s.Id),!existing);
                var updated=new SkinDefinition(persona,installedFolder,manifest){PackageHolidayCatalog=installedInventory.HolidayCatalog};customPackageIds.Add(manifest.PackageId);
                CompanionPackStorage.SetHidden(dataRoot,manifest.PackageId,false);
                CompanionPackStorage.SetHidden(dataRoot,updated.PickerKey,false);Put(updated);return updated;
            }
            string id="skin-"+Guid.NewGuid().ToString("N"),destination=Path.Combine(directory,id);
            Directory.CreateDirectory(destination);
            try {
                foreach(string relative in files) {
                    string target=Path.Combine(destination,relative.Replace('/',Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(IllustratedPack.AssetPath(source,relative),target,false);
                }
                // Validate the copied bytes before publishing the catalog entry.
                IllustratedPack.Validate(destination,manifest,true);
                manifest.Name=CleanName(manifest.Name);
                using(var stream=File.Create(Path.Combine(destination,"pet.json"))) Serializer().WriteObject(stream,manifest);
                var skin=new SkinDefinition(id,destination,manifest);skins.Add(skin);ReconcileLegacy(true);return Find(skin.Id);
            } catch { Directory.Delete(destination,true);throw; }
        }
        internal static DataContractJsonSerializer Serializer()
        { return new DataContractJsonSerializer(typeof(SkinManifest),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}); }
        private static SkinManifest ReadManifest(string path)
        {
            using(var stream=File.OpenRead(path))return ReadManifest(stream);
        }
        internal static SkinManifest ReadManifest(Stream stream)
        {
            if(stream.Length>65536) throw new InvalidOperationException(TextResources.Get("skinCatalog.readManifest.text"));
            SkinManifest result;
            using(var copy=new MemoryStream()){stream.CopyTo(copy);copy.Position=0;result=(SkinManifest)Serializer().ReadObject(copy);var raw=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=65536}.DeserializeObject(Encoding.UTF8.GetString(copy.ToArray()).TrimStart('\ufeff')) as Dictionary<string,object>;object extension;if(raw!=null && raw.TryGetValue("relationship",out extension))result.Relationship=RelationshipContent.ParseObject(extension);}
            if(result==null || (result.Version!=1 && result.Version!=2)) throw new InvalidOperationException(TextResources.Get("skinCatalog.readManifest.text.alternativeA"));
            if((result.SystemPrompt ?? "").Length>8000 || (result.Description ?? "").Length>1000) throw new InvalidOperationException(TextResources.Get("skinCatalog.readManifest.text.alternativeB"));
            IEnumerable<string> relationshipActions=IllustratedPack.Actions;
            if(result.Version==2 && result.ExtraAnimations!=null)relationshipActions=relationshipActions.Concat(result.ExtraAnimations.Keys.Where(a=>a=="special2"||a=="stumble"));
            RelationshipContent.Validate(result.Relationship,relationshipActions);
            return result;
        }
        private static string AtlasPath(string folder,string filename)
        {
            if(String.IsNullOrWhiteSpace(filename) || filename!=Path.GetFileName(filename) || filename.IndexOfAny(new char[]{'/', '\\', ':'})>=0 || !String.Equals(Path.GetExtension(filename),".png",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(TextResources.Get("skinCatalog.atlasPath.text"));
            return Path.Combine(folder,filename);
        }
        internal static string CleanName(string name)
        {
            string result=new string((name??"").Where(c=>!Char.IsControl(c)).ToArray()).Trim();
            return result.Length==0 ? TextResources.Get("skinCatalog.cleanName.text") : result.Substring(0,Math.Min(32,result.Length));
        }
        public static void ValidatePng(string path)
        {
            using(var stream=File.OpenRead(path))
            {
                if(stream.Length>25*1024*1024 || stream.Length<33) throw new InvalidOperationException(TextResources.Get("skinCatalog.validatePng.text"));
                byte[] header=new byte[33]; if(stream.Read(header,0,header.Length)!=header.Length) throw new InvalidOperationException(TextResources.Get("skinCatalog.validatePng.text.alternativeA"));
                byte[] signature={137,80,78,71,13,10,26,10};
                if(!header.Take(8).SequenceEqual(signature) || Encoding.ASCII.GetString(header,12,4)!="IHDR") throw new InvalidOperationException(TextResources.Get("skinCatalog.validatePng.text.alternativeB"));
                int width=BigEndian(header,16),height=BigEndian(header,20);
                SpriteAtlas.ValidateDimensions(width,height);
            }
        }
        private static int BigEndian(byte[] b,int i) { return (b[i]<<24)|(b[i+1]<<16)|(b[i+2]<<8)|b[i+3]; }
        public static BitmapSource GetAvatar(SkinDefinition skin)
        {
            if(skin.IsPlaceholder)return OfficialAvatar(skin);
            if(skin.Illustrated && !String.IsNullOrEmpty(skin.Manifest.Avatar)) {
                string path=IllustratedPack.AssetPath(skin.Source,skin.Manifest.Avatar);
                lock(avatars) { BitmapSource cached;if(avatars.TryGetValue(path,out cached))return cached;
                    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=256;image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();image.Freeze();if(avatars.Count>=24)avatars.Clear();avatars.Add(path,image);return image; }
            }
            // Imported designs keep their own artwork, even when their ID matches a preset.
            if(!skin.Embedded && !skin.Bundled)return GetPreview(skin);
            lock(avatars)
            {
                BitmapSource cached;if(avatars.TryGetValue(skin.Id,out cached))return cached;
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Dafeiyu.Avatar."+(skin.BuiltinCharacterId??skin.Id)))
                {
                    if(stream==null)return GetPreview(skin);
                    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=256;image.StreamSource=stream;image.EndInit();image.Freeze();avatars.Add(skin.Id,image);return image;
                }
            }
        }
        static BitmapSource OfficialAvatar(SkinDefinition skin)
        {
            var profile=OfficialCompanionProfiles.Find(skin.BuiltinCharacterId);if(profile==null)return null;
            lock(avatars) {
                string key="official-avatar:"+profile.Id;BitmapSource cached;if(avatars.TryGetValue(key,out cached))return cached;
                using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream(profile.AvatarResourceName)) {
                    if(input==null)return null;
                    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=256;image.StreamSource=input;image.EndInit();image.Freeze();
                    if(avatars.Count>=24)avatars.Clear();avatars[key]=image;return image;
                }
            }
        }
        public static DataTemplate AvatarTemplate(Action<SkinDefinition> remove=null,Func<bool> canRemove=null)
        {
            var row=new FrameworkElementFactory(typeof(System.Windows.Controls.DockPanel));
            row.SetValue(System.Windows.Controls.DockPanel.LastChildFillProperty,true);
            if(remove!=null) {
                var button=new FrameworkElementFactory(typeof(System.Windows.Controls.Button));button.SetValue(FrameworkElement.NameProperty,"DeleteCompanion");button.SetValue(System.Windows.Controls.ContentControl.ContentProperty,"\uE74D");button.SetValue(System.Windows.Controls.Control.FontFamilyProperty,new System.Windows.Media.FontFamily("Segoe MDL2 Assets"));button.SetValue(FrameworkElement.WidthProperty,28.0);button.SetValue(FrameworkElement.HeightProperty,28.0);button.SetValue(FrameworkElement.MarginProperty,new Thickness(8,0,0,0));button.SetValue(System.Windows.Controls.Control.BackgroundProperty,System.Windows.Media.Brushes.Transparent);button.SetValue(System.Windows.Controls.Control.BorderThicknessProperty,new Thickness(0));button.SetValue(UIElement.FocusableProperty,false);button.SetValue(FrameworkElement.ToolTipProperty,TextResources.Get("skinCatalog.avatarTemplate.buttonSetValue"));button.SetValue(System.Windows.Controls.DockPanel.DockProperty,System.Windows.Controls.Dock.Right);
                button.SetBinding(UIElement.VisibilityProperty,new System.Windows.Data.Binding("IsVisible"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(System.Windows.Controls.ComboBoxItem),1),Converter=new System.Windows.Controls.BooleanToVisibilityConverter(),FallbackValue=Visibility.Collapsed});
                button.AddHandler(FrameworkElement.LoadedEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs e){var control=(System.Windows.Controls.Button)sender;var skin=control.DataContext as SkinDefinition;bool reserved=skin!=null&&skin.IsOfficial&&skin.BuiltinCharacterId=="e-bao";control.IsEnabled=!reserved&&(canRemove==null||canRemove());if(reserved)LocalizedUi.Set(control,"ToolTip",()=>(TextResources.Get("skinCatalog.avatarTemplate.controlToolTip")));}));
                button.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs e){e.Handled=true;var skin=((FrameworkElement)sender).DataContext as SkinDefinition;if(skin!=null)remove(skin);}));row.AppendChild(button);
            }
            var image=new FrameworkElementFactory(typeof(System.Windows.Controls.Image));image.SetValue(FrameworkElement.WidthProperty,28.0);image.SetValue(FrameworkElement.HeightProperty,28.0);image.SetValue(UIElement.ClipProperty,new System.Windows.Media.RectangleGeometry(new Rect(0,0,28,28),5.6,5.6));image.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,9,0));image.SetBinding(System.Windows.Controls.Image.SourceProperty,new System.Windows.Data.Binding{Converter=new AvatarConverter()});row.AppendChild(image);
            var name=new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));name.SetBinding(System.Windows.Controls.TextBlock.TextProperty,new System.Windows.Data.Binding("DisplayName"));name.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(name);return new DataTemplate{VisualTree=row};
        }
        sealed class AvatarConverter:System.Windows.Data.IValueConverter
        {
            public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture){return value is SkinDefinition?GetAvatar((SkinDefinition)value):null;}
            public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture){throw new NotSupportedException();}
        }
        public static BitmapSource GetPreview(SkinDefinition skin)
        {
            if(skin.Illustrated) return IllustratedPack.ReadImage(IllustratedPack.AssetPath(skin.Source,skin.Manifest.Preview),480,520);
            using(Stream stream=skin.Embedded ? Assembly.GetExecutingAssembly().GetManifestResourceStream(skin.Preview) : File.OpenRead(skin.Source))
            {
                if(stream==null) throw new InvalidOperationException(TextResources.Get("skinCatalog.getPreview.text"));
                var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;
                image.StreamSource=stream; image.DecodePixelWidth=skin.Embedded ? 480 : 3840; image.EndInit(); image.Freeze();
                if(skin.Embedded) return image;
                var crop=new CroppedBitmap(image,new Int32Rect(0,0,image.PixelWidth/8,image.PixelHeight/11)); crop.Freeze(); return crop;
            }
        }
    }
}
