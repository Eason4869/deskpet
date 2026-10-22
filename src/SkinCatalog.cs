using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
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
            if(Embedded){switch(Id){case "e-bao":return "傲娇扭头";case "dafeiyu":return "摇尾欢呼";case "marfu":return "挥杖流光";}}
            return "伙伴招牌";
        } }
        internal SkinDefinition(string id,string folder,SkinManifest manifest) : this(id,SkinCatalog.CleanName(manifest.Name),folder,manifest.Preview,false,manifest.SystemPrompt,manifest.Description) { Manifest=manifest; }
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Source { get; private set; }
        public string Preview { get; private set; }
        public bool Embedded { get; private set; }
        // Trusted registration metadata; imported manifests cannot assign this.
        public string BuiltinCharacterId {get;internal set;}
        internal bool Bundled { get; set; }
        public string SystemPrompt { get; private set; }
        public string Description { get; private set; }
        public SkinDefinition(string id,string name,string source,string preview,bool embedded,string prompt=null,string description=null)
        {
            Id=id; Name=name; Source=source; Preview=preview; Embedded=embedded;
            SystemPrompt=String.IsNullOrWhiteSpace(prompt) ? (id=="dafeiyu" ? PersonaProfiles.Dafeiyu : id=="e-bao" ? PersonaProfiles.Ebao : PersonaProfiles.Generic(name)) : prompt.Trim();
            Description=description ?? (id=="e-bao" ? "傲娇毒舌 · 白发正太" : id=="dafeiyu" ? "温和俏皮 · 蓝白鲸鱼娘" : "你的专属桌面伙伴");
        }
        public override string ToString() { return Name; }
        public SpriteAtlas Load()
        {
            return Illustrated ? new SpriteAtlas(Source,Manifest) : new SpriteAtlas(Source,Embedded);
        }
        public int ActionFrame(int row,int phase)
        {
            // Emphasize Marfu's planted/tucked/extended poses instead of holding
            // several near-identical planted frames; every selection is real artwork.
            if(!Embedded || Id!="marfu" || phase<0 || phase>=8) return phase;
            return row==1 || row==2 ? MarfuRightStride[phase] : phase;
        }
        public int ActionRow(int row) { return MirrorsAction(row) ? 1 : row; }
        public bool MirrorsAction(int row) { return Embedded && (Id=="marfu" || Id=="dafeiyu") && row==2; }
        public AnimationPlayer CreateAnimation(int frameMultiplier=1)
        {
            double period=Embedded && (Id=="marfu" || Id=="e-bao" || Id=="dafeiyu") ? .22 : .11;
            double[] durations=null;
            if(Illustrated) durations=AnimationPlayer.States.Select(n=>Manifest.Animations[n].Loop ? 0 : Manifest.Animations[n].Frames/Manifest.Animations[n].Fps).ToArray();
            if(AdditionalInteractions.Has(this,"jumping")){if(durations==null)durations=new double[AnimationPlayer.States.Length];IllustratedAnimation clip;durations[4]=AdditionalInteractions.Specification(this,"jumping",false,out clip)?clip.Frames/clip.Fps:1;}
            return new AnimationPlayer(frameMultiplier,period,durations);
        }
    }
    public sealed class SkinCatalog
    {
        private static readonly Dictionary<string,BitmapSource> avatars=new Dictionary<string,BitmapSource>();
        private readonly string directory;
        private readonly string dataRoot;
        private readonly List<SkinDefinition> skins=new List<SkinDefinition>();
        private readonly List<SkinDefinition> supplied=new List<SkinDefinition>();
        private readonly Dictionary<string,string> aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        public IList<SkinDefinition> Skins { get { return skins.AsReadOnly(); } }
        public bool HasRemoved {get{return CompanionPackStorage.Hidden(dataRoot).Count>0;}}
        public SkinCatalog(string dataDirectory)
        {
            dataRoot=dataDirectory;
            directory=Path.Combine(dataDirectory,"skins");
            skins.Add(new SkinDefinition("e-bao","E包","Dafeiyu.EbaoAtlas","Dafeiyu.EbaoPreview",true){BuiltinCharacterId="e-bao"});
            skins.Add(new SkinDefinition("dafeiyu","大肥鱼","Dafeiyu.Atlas","Dafeiyu.Preview",true){BuiltinCharacterId="dafeiyu"});
            skins.Add(new SkinDefinition("marfu","马尔芙","Dafeiyu.MarfuAtlas","Dafeiyu.MarfuPreview",true,
                PersonaProfiles.Marfu,
                "聪明俏皮 · 金发绿眼魔法少女"){BuiltinCharacterId="marfu"});
            skins.AddRange(BundledSkinCache.Registered(dataDirectory));
            supplied.AddRange(skins.Where(s=>s.Illustrated && !String.IsNullOrEmpty(s.Manifest.PackageId)));
            foreach(var pack in CompanionPackStorage.Installed(dataDirectory)) {
                try { var manifest=ReadManifest(Path.Combine(pack.Value,"pet.json"));
                    if(manifest.PackageId!=pack.Key)continue;
                    IllustratedPack.Validate(pack.Value,manifest,false);
                    Put(new SkinDefinition(CompanionPackStorage.Identity(dataDirectory,pack.Key),pack.Value,manifest));
                } catch(Exception e) {if(!(e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is SerializationException || e is ArgumentException || e is InvalidOperationException))throw;}
            }
            if(!Directory.Exists(directory)){ApplyHidden();return;}
            foreach(string folder in Directory.GetDirectories(directory))
            {
                try
                {
                    var manifest=ReadManifest(Path.Combine(folder,"pet.json"));
                    if(manifest.Version==2) { IllustratedPack.Validate(folder,manifest,false);skins.Add(new SkinDefinition(Path.GetFileName(folder),folder,manifest));continue; }
                    string atlas=AtlasPath(folder,manifest.Atlas); ValidatePng(atlas);
                    skins.Add(new SkinDefinition(Path.GetFileName(folder),CleanName(manifest.Name),atlas,null,false,manifest.SystemPrompt,manifest.Description));
                }
                catch(Exception e)
                { if(!(e is IOException || e is UnauthorizedAccessException || e is SerializationException || e is ArgumentException || e is InvalidOperationException)) throw; }
            }
            ReconcileLegacy();
            ApplyHidden();
        }
        public SkinDefinition Find(string id) { string package;if(id!=null && aliases.TryGetValue(id,out package))id=package;return skins.FirstOrDefault(s=>s.Id==id || s.Illustrated && s.Manifest.PackageId==id) ?? skins[0]; }
        void Put(SkinDefinition skin)
        {
            string package=skin.Manifest.PackageId;int index=skins.FindIndex(s=>s.Id==skin.Id || s.Illustrated && s.Manifest.PackageId==package);
            skins.RemoveAll(s=>s.Id==skin.Id || s.Illustrated && s.Manifest.PackageId==package);
            skins.Insert(index<0?skins.Count:Math.Min(index,skins.Count),skin);
        }
        void ApplyHidden()
        {
            var hidden=CompanionPackStorage.Hidden(dataRoot);var fallback=skins[0];
            skins.RemoveAll(s=>hidden.Contains(s.Illustrated && !String.IsNullOrEmpty(s.Manifest.PackageId)?s.Manifest.PackageId:s.Id));
            if(skins.Count==0)skins.Add(fallback);
        }
        public void Remove(SkinDefinition skin)
        {
            if(skins.Count<=1)throw new InvalidOperationException("至少保留一位伙伴。");
            if(!skins.Contains(skin))throw new InvalidOperationException("伙伴已不在列表中。");
            CompanionPackStorage.SetHidden(dataRoot,skin.Illustrated && !String.IsNullOrEmpty(skin.Manifest.PackageId)?skin.Manifest.PackageId:skin.Id,true);skins.Remove(skin);
        }
        public void RestoreRemoved()
        {
            CompanionPackStorage.RestoreHidden(dataRoot);var restored=new SkinCatalog(dataRoot);
            skins.Clear();skins.AddRange(restored.skins);supplied.Clear();supplied.AddRange(restored.supplied);aliases.Clear();foreach(var pair in restored.aliases)aliases[pair.Key]=pair.Value;
        }
        void ReconcileLegacy(bool restore=false)
        {
            foreach(var legacy in skins.Where(s=>s.Illustrated && !s.Bundled && String.IsNullOrEmpty(s.Manifest.PackageId)).ToArray()) {
                var original=supplied.Concat(skins.Where(s=>s.Illustrated && !String.IsNullOrEmpty(s.Manifest.PackageId))).FirstOrDefault(p=>SameArtwork(legacy,p));
                if(original==null)continue;
                string package=original.Manifest.PackageId,identity=CompanionPackStorage.RetainIdentity(dataRoot,package,legacy.Id);
                var current=skins.FirstOrDefault(s=>s.Illustrated && s.Manifest.PackageId==package)??original;
                if(restore)CompanionPackStorage.SetHidden(dataRoot,package,false);
                aliases[legacy.Id]=package;
                skins.Remove(legacy);Put(new SkinDefinition(identity,current.Source,current.Manifest){Bundled=current.Bundled,BuiltinCharacterId=current.BuiltinCharacterId});
            }
        }
        static bool SameArtwork(SkinDefinition left,SkinDefinition right)
        {
            if(left.Name!=right.Name || left.SystemPrompt!=right.SystemPrompt)return false;
            foreach(string action in IllustratedPack.Actions) {
                var a=left.Manifest.Animations[action];var b=right.Manifest.Animations[action];
                if(a.Frames!=b.Frames || a.Fps!=b.Fps || a.Loop!=b.Loop || !SameFile(left.Source,a.File,right.Source,b.File))return false;
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
            string atlas,name,prompt=null,description=null;
            if(String.Equals(Path.GetExtension(path),".json",StringComparison.OrdinalIgnoreCase))
            { var m=ReadManifest(path); if(m.Version==2)return ImportIllustrated(path,m); atlas=AtlasPath(Path.GetDirectoryName(Path.GetFullPath(path)),m.Atlas); name=CleanName(m.Name); prompt=m.SystemPrompt; description=m.Description; }
            else
            { if(!String.Equals(Path.GetExtension(path),".png",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请选择透明 PNG 精灵图或 pet.json 外观文件。"); atlas=path; name=CleanName(Path.GetFileNameWithoutExtension(path)); }
            ValidatePng(atlas);
            // Decode and validate every required frame before adding a saved design.
            try { new SpriteAtlas(atlas,false); }
            catch(Exception e)
            {
                if(!(e is FileFormatException || e is NotSupportedException || e is ArgumentException)) throw;
                throw new InvalidOperationException("PNG 图像无法解码，请重新导出完整的透明精灵图。",e);
            }
            string id="skin-"+Guid.NewGuid().ToString("N");
            string destination=Path.Combine(directory,id);
            Directory.CreateDirectory(destination);
            try
            {
                File.Copy(atlas,Path.Combine(destination,"spritesheet.png"),false);
                var manifest=new SkinManifest { Version=1,Name=name,Atlas="spritesheet.png",SystemPrompt=String.IsNullOrWhiteSpace(prompt) ? PersonaProfiles.Generic(name) : prompt,Description=description };
                using(var stream=File.Create(Path.Combine(destination,"pet.json"))) new DataContractJsonSerializer(typeof(SkinManifest)).WriteObject(stream,manifest);
                var skin=new SkinDefinition(id,name,Path.Combine(destination,"spritesheet.png"),null,false,manifest.SystemPrompt,description);
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
                if(!String.Equals(Path.GetFileName(path),"pet.json",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("完整角色包说明请命名为 pet.json。");
                string installedFolder=CompanionPackStorage.Install(source,dataRoot,manifest.PackageId,files,
                    folder=>IllustratedPack.Validate(folder,ReadManifest(Path.Combine(folder,"pet.json")),true));
                var updated=new SkinDefinition(CompanionPackStorage.Identity(dataRoot,manifest.PackageId),installedFolder,manifest);
                CompanionPackStorage.SetHidden(dataRoot,manifest.PackageId,false);
                Put(updated);ReconcileLegacy();return Find(manifest.PackageId);
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
            if(stream.Length>65536) throw new InvalidOperationException("外观说明文件过大。");
            var result=(SkinManifest)Serializer().ReadObject(stream);
            if(result==null || (result.Version!=1 && result.Version!=2)) throw new InvalidOperationException("外观版本应为 1 或 2。");
            if((result.SystemPrompt ?? "").Length>8000 || (result.Description ?? "").Length>1000) throw new InvalidOperationException("人格最多 8000 字，简介最多 1000 字。");
            return result;
        }
        private static string AtlasPath(string folder,string filename)
        {
            if(String.IsNullOrWhiteSpace(filename) || filename!=Path.GetFileName(filename) || filename.IndexOfAny(new char[]{'/', '\\', ':'})>=0 || !String.Equals(Path.GetExtension(filename),".png",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("atlas 必须是与 pet.json 同文件夹的 PNG 文件名。");
            return Path.Combine(folder,filename);
        }
        internal static string CleanName(string name)
        {
            string result=new string((name??"").Where(c=>!Char.IsControl(c)).ToArray()).Trim();
            return result.Length==0 ? "新伙伴" : result.Substring(0,Math.Min(32,result.Length));
        }
        public static void ValidatePng(string path)
        {
            using(var stream=File.OpenRead(path))
            {
                if(stream.Length>25*1024*1024 || stream.Length<33) throw new InvalidOperationException("PNG 文件大小应小于 25 MB。");
                byte[] header=new byte[33]; if(stream.Read(header,0,header.Length)!=header.Length) throw new InvalidOperationException("PNG 文件不完整。");
                byte[] signature={137,80,78,71,13,10,26,10};
                if(!header.Take(8).SequenceEqual(signature) || Encoding.ASCII.GetString(header,12,4)!="IHDR") throw new InvalidOperationException("文件不是有效的 PNG 精灵图。");
                int width=BigEndian(header,16),height=BigEndian(header,20);
                SpriteAtlas.ValidateDimensions(width,height);
            }
        }
        private static int BigEndian(byte[] b,int i) { return (b[i]<<24)|(b[i+1]<<16)|(b[i+2]<<8)|b[i+3]; }
        public static BitmapSource GetAvatar(SkinDefinition skin)
        {
            if(skin.Illustrated && !String.IsNullOrEmpty(skin.Manifest.Avatar)) {
                string path=IllustratedPack.AssetPath(skin.Source,skin.Manifest.Avatar);
                lock(avatars) { BitmapSource cached;if(avatars.TryGetValue(path,out cached))return cached;
                    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=256;image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();image.Freeze();avatars.Add(path,image);return image; }
            }
            // Imported designs keep their own artwork, even when their ID matches a preset.
            if(!skin.Embedded && !skin.Bundled)return GetPreview(skin);
            lock(avatars)
            {
                BitmapSource cached;if(avatars.TryGetValue(skin.Id,out cached))return cached;
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Dafeiyu.Avatar."+skin.Id))
                {
                    if(stream==null)return GetPreview(skin);
                    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=256;image.StreamSource=stream;image.EndInit();image.Freeze();avatars.Add(skin.Id,image);return image;
                }
            }
        }
        public static DataTemplate AvatarTemplate(Action<SkinDefinition> remove=null,Func<bool> canRemove=null)
        {
            var row=new FrameworkElementFactory(typeof(System.Windows.Controls.DockPanel));
            row.SetValue(System.Windows.Controls.DockPanel.LastChildFillProperty,true);
            if(remove!=null) {
                var button=new FrameworkElementFactory(typeof(System.Windows.Controls.Button));button.SetValue(FrameworkElement.NameProperty,"DeleteCompanion");button.SetValue(System.Windows.Controls.ContentControl.ContentProperty,"\uE74D");button.SetValue(System.Windows.Controls.Control.FontFamilyProperty,new System.Windows.Media.FontFamily("Segoe MDL2 Assets"));button.SetValue(FrameworkElement.WidthProperty,28.0);button.SetValue(FrameworkElement.HeightProperty,28.0);button.SetValue(FrameworkElement.MarginProperty,new Thickness(8,0,0,0));button.SetValue(System.Windows.Controls.Control.BackgroundProperty,System.Windows.Media.Brushes.Transparent);button.SetValue(System.Windows.Controls.Control.BorderThicknessProperty,new Thickness(0));button.SetValue(UIElement.FocusableProperty,false);button.SetValue(FrameworkElement.ToolTipProperty,"删除伙伴（保留聊天和配置）");button.SetValue(System.Windows.Controls.DockPanel.DockProperty,System.Windows.Controls.Dock.Right);
                button.SetBinding(UIElement.VisibilityProperty,new System.Windows.Data.Binding("IsVisible"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(System.Windows.Controls.ComboBoxItem),1),Converter=new System.Windows.Controls.BooleanToVisibilityConverter(),FallbackValue=Visibility.Collapsed});
                button.AddHandler(FrameworkElement.LoadedEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs e){((System.Windows.Controls.Button)sender).IsEnabled=canRemove==null || canRemove();}));
                button.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs e){e.Handled=true;var skin=((FrameworkElement)sender).DataContext as SkinDefinition;if(skin!=null)remove(skin);}));row.AppendChild(button);
            }
            var image=new FrameworkElementFactory(typeof(System.Windows.Controls.Image));image.SetValue(FrameworkElement.WidthProperty,28.0);image.SetValue(FrameworkElement.HeightProperty,28.0);image.SetValue(UIElement.ClipProperty,new System.Windows.Media.RectangleGeometry(new Rect(0,0,28,28),5.6,5.6));image.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,9,0));image.SetBinding(System.Windows.Controls.Image.SourceProperty,new System.Windows.Data.Binding{Converter=new AvatarConverter()});row.AppendChild(image);
            var name=new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));name.SetBinding(System.Windows.Controls.TextBlock.TextProperty,new System.Windows.Data.Binding("Name"));name.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(name);return new DataTemplate{VisualTree=row};
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
                if(stream==null) throw new InvalidOperationException("找不到外观预览。");
                var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;
                image.StreamSource=stream; image.DecodePixelWidth=skin.Embedded ? 480 : 3840; image.EndInit(); image.Freeze();
                if(skin.Embedded) return image;
                var crop=new CroppedBitmap(image,new Int32Rect(0,0,image.PixelWidth/8,image.PixelHeight/11)); crop.Freeze(); return crop;
            }
        }
    }
}
