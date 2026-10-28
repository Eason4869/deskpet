using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace Dafeiyu
{
    [DataContract]
    public sealed class PersonaVoiceProfile
    {
        [DataMember] public Dictionary<string,string> Speech { get; set; }
        [DataMember] public Dictionary<string,string> Live { get; set; }
        public PersonaVoiceProfile() { Speech=new Dictionary<string,string>();Live=new Dictionary<string,string>(); }
        public void Normalize() { Speech=Clean(Speech);Live=Clean(Live); }
        static Dictionary<string,string> Clean(Dictionary<string,string> source)
        {
            var result=new Dictionary<string,string>();if(source==null)return result;
            foreach(var pair in source) {
                if(result.Count>=8)break;
                if(String.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length>128 || pair.Value==null || pair.Value.Length>512)continue;
                result[pair.Key]=pair.Value.Trim();
            }
            return result;
        }
        public PersonaVoiceProfile Copy() { return new PersonaVoiceProfile{Speech=Clean(Speech),Live=Clean(Live)}; }
    }
    [DataContract]
    public sealed class ConnectionSettings
    {
        [DataMember] public string BaseUrl { get; set; }
        [DataMember] public string Model { get; set; }
        [DataMember] public int ContextWindowTokens {get;set;}
        internal int RequestContextCapacity {get;set;}
        [DataMember] public CapabilitySupport ToolSupport {get;set;}
        [DataMember] public CapabilitySupport ImageSupport {get;set;}
        [DataMember] public CapabilitySupport MetadataToolSupport {get;set;}
        [DataMember] public CapabilitySupport MetadataImageSupport {get;set;}
        [DataMember] public string EncryptedKey { get; set; }
        [DataMember] public string Protocol { get; set; }
        [DataMember] public bool StreamReplies { get; set; }
        [OnDeserializing] private void BeforeDeserialize(StreamingContext context) { StreamReplies=true; }
        [DataMember] public bool UseProxy { get; set; }
        [DataMember] public string ProxyAddress { get; set; }
        public ConnectionSettings() { StreamReplies=true; BaseUrl = ""; Model = ""; EncryptedKey = ""; Protocol = "auto"; ProxyAddress=ProxyConfiguration.DefaultAddress; }
        public string ApiKey
        {
            get
            {
                if (String.IsNullOrEmpty(EncryptedKey)) return "";
                try { return Encoding.UTF8.GetString(SecretProtection.Unprotect(Convert.FromBase64String(EncryptedKey))); }
                catch (CryptographicException) { return ""; }
                catch (FormatException) { return ""; }
            }
            set
            {
                string key = (value ?? "").Trim();
                EncryptedKey = key.Length == 0 ? "" : Convert.ToBase64String(SecretProtection.Protect(Encoding.UTF8.GetBytes(key)));
            }
        }
        public ConnectionSettings Copy()
        {
            return new ConnectionSettings { BaseUrl = BaseUrl, Model = Model, ContextWindowTokens=ContextWindowTokens, RequestContextCapacity=RequestContextCapacity, ToolSupport=ToolSupport, ImageSupport=ImageSupport, MetadataToolSupport=MetadataToolSupport, MetadataImageSupport=MetadataImageSupport, EncryptedKey = EncryptedKey, Protocol = Protocol, StreamReplies=StreamReplies, UseProxy = UseProxy, ProxyAddress=ProxyAddress };
        }
        public void Normalize() { BaseUrl = (BaseUrl ?? "").Trim(); Model = (Model ?? "").Trim(); EncryptedKey = EncryptedKey ?? ""; Protocol = String.IsNullOrWhiteSpace(Protocol) ? "auto" : Protocol; ProxyAddress=ProxyConfiguration.Normalize(ProxyAddress); }
    }

    public static class ProviderIdentity
    {
        public static string Brand(string address)
        {
            Uri uri;if(!Uri.TryCreate(address,UriKind.Absolute,out uri))return "";string host=uri.Host.ToLowerInvariant();
            string[,] domains={{"openai.com","openai"},{"anthropic.com","anthropic"},{"googleapis.com","gemini"},{"deepseek.com","deepseek"},{"xiaomimimo.com","xiaomimimo"},{"volces.com","doubao"},{"volcengine.com","doubao"},{"moonshot.cn","moonshot"},{"moonshot.ai","moonshot"},{"kimi.com","moonshot"},{"minimax.io","minimax"},{"minimaxi.com","minimax"},{"minimax.chat","minimax"},{"bigmodel.cn","zhipu"},{"dashscope.aliyuncs.com","qwen"},{"dashscope-intl.aliyuncs.com","qwen"},{"ollama.com","ollama"}};
            for(int i=0;i<domains.GetLength(0);i++)if(host==domains[i,0] || host.EndsWith("."+domains[i,0],StringComparison.Ordinal))return domains[i,1];
            return uri.IsLoopback && uri.Port==11434?"ollama":"";
        }
        public static string Name(ConnectionSettings settings)
        {
            string known=NameForBrand(Brand(settings.BaseUrl));if(known.Length>0)return known;
            Uri uri;return Uri.TryCreate(settings.BaseUrl,UriKind.Absolute,out uri)?(uri.IsLoopback?TextResources.Get("settings.name.text"):uri.Host):TextResources.Get("settings.name.text.alternativeA");
        }
        public static string NameForBrand(string brand)
        {
            switch(brand)
            {
                case "openai":return "OpenAI";case "anthropic":return "Anthropic";case "gemini":return "Gemini";case "deepseek":return "DeepSeek";case "xiaomimimo":return "MiMo";case "doubao":return "火山方舟";case "moonshot":return "Kimi";case "minimax":return "MiniMax";case "zhipu":return "智谱";case "qwen":return "千问";case "ollama":return "Ollama";
            }
            return "";
        }
        // Translate generated labels only; user-entered provider names and persisted IDs stay intact.
        public static string DisplayName(string name,string brand)
        {
            if(name==TextResources.CatalogFor("zh_CN").Get("settings.name.text.alternativeA"))return TextResources.Get("settings.name.text.alternativeA");
            if(name==NameForBrand(brand))switch(brand){case "doubao":return TextResources.Get("providers.brands.doubao");case "zhipu":return TextResources.Get("providers.brands.zhipu");case "qwen":return TextResources.Get("providers.brands.qwen");}
            return name;
        }
        public static string DisplayName(TextProviderSettings provider){return DisplayName(provider.Name,provider.BrandId);}
        public static string Name(TextProviderSettings provider){string name=NameForBrand(provider.BrandId);return name.Length>0?name:Name(provider.Connection);}
        public static bool AutomaticName(string name,TextProviderSettings provider)
        {int number;bool generated=name.StartsWith("自定义服务 ",StringComparison.Ordinal) && Int32.TryParse(name.Substring(6),out number);return name=="默认服务" || name=="自定义服务" || generated || name==Name(provider);}
    }
    [DataContract]
    public sealed class TextProviderSettings
    {
        public const int MaximumModels=100;
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string BrandId { get; set; }
        [DataMember] public ConnectionSettings Connection { get; set; }
        [DataMember] public List<string> Models { get; set; }
        [DataMember] public Dictionary<string,string> ModelDisplayNames { get; set; }
        [DataMember] public Dictionary<string,int> ModelContextWindows=new Dictionary<string,int>();
        [DataMember] public Dictionary<string,int> ModelContextOverrides=new Dictionary<string,int>();
        [DataMember] public string CapabilityEndpoint="";
        [DataMember] public Dictionary<string,ModelCapabilitySetting> ServerCapabilities=new Dictionary<string,ModelCapabilitySetting>();
        [DataMember] public Dictionary<string,ModelCapabilitySetting> ModelCapabilities=new Dictionary<string,ModelCapabilitySetting>();
        public ModelCapabilitySetting Capabilities(string model)
        {ModelCapabilitySetting value;return ModelCapabilities!=null && ModelCapabilities.TryGetValue(model??"",out value)&&value!=null?value.Copy():new ModelCapabilitySetting();}
        public void ApplyModelCapabilities(ConnectionSettings target)
        {var caps=Capabilities(target.Model);target.ToolSupport=caps.Tools;target.ImageSupport=caps.Images;ModelCapabilitySetting server;server=ServerCapabilities!=null&&ServerCapabilities.TryGetValue(target.Model??"",out server)?server:null;target.MetadataToolSupport=server==null?CapabilitySupport.Unknown:server.Tools;target.MetadataImageSupport=server==null?CapabilitySupport.Unknown:server.Images;}
        public int ContextCapacity(string model){int value;if(ModelContextOverrides!=null && ModelContextOverrides.TryGetValue(model,out value))return value;return ModelContextWindows!=null && ModelContextWindows.TryGetValue(model,out value)?value:0;}
        public TextProviderSettings() { Id=Guid.NewGuid().ToString("N");Name="自定义服务";Connection=new ConnectionSettings();Models=new List<string>();ModelDisplayNames=new Dictionary<string,string>(); }
        public void Normalize()
        {
            if(String.IsNullOrWhiteSpace(Id) || Id.Length>128)Id=Guid.NewGuid().ToString("N");
            Connection=Connection ?? new ConnectionSettings();Connection.Normalize();
            if(String.IsNullOrWhiteSpace(BrandId)){BrandId=ProviderIdentity.Brand(Connection.BaseUrl);if(BrandId.Length==0)BrandId="custom";}
            else if(BrandId!="custom" && ProviderIdentity.NameForBrand(BrandId).Length==0)BrandId="custom";
            Name=String.IsNullOrWhiteSpace(Name) || Name=="默认服务"?ProviderIdentity.Name(this):Name.Trim();if(Name.Length>80)Name=Name.Substring(0,80);
            var clean=new List<string>();
            if(Models!=null)foreach(string model in Models) { string value=(model ?? "").Trim();if(value.Length>0 && value.Length<=256 && !clean.Contains(value) && clean.Count<MaximumModels)clean.Add(value); }
            string selected=Connection.Model;
            if(selected.Length>0 && selected.Length<=256 && !clean.Contains(selected)) { if(clean.Count>=MaximumModels)clean.RemoveAt(clean.Count-1);clean.Add(selected); }
            if(!clean.Contains(selected))Connection.Model=clean.Count>0?clean[0]:"";
            Models=clean;
            string endpoint=Connection.BaseUrl.TrimEnd('/')+"|"+(Connection.Protocol=="auto"?"openai":Connection.Protocol);
            if(!String.IsNullOrEmpty(CapabilityEndpoint)&&CapabilityEndpoint.EndsWith("|auto",StringComparison.Ordinal))CapabilityEndpoint=CapabilityEndpoint.Substring(0,CapabilityEndpoint.Length-4)+"openai";
            if(!String.IsNullOrEmpty(CapabilityEndpoint) && CapabilityEndpoint!=endpoint){ServerCapabilities=new Dictionary<string,ModelCapabilitySetting>();ModelCapabilities=new Dictionary<string,ModelCapabilitySetting>();ModelContextWindows=new Dictionary<string,int>();ModelContextOverrides=new Dictionary<string,int>();}
            CapabilityEndpoint=endpoint;
            ModelCapabilities=(ModelCapabilities??new Dictionary<string,ModelCapabilitySetting>()).Where(p=>clean.Contains(p.Key)&&p.Value!=null).ToDictionary(p=>p.Key,p=>new ModelCapabilitySetting{Tools=AgentCapabilityResolver.Valid(p.Value.Tools),Images=AgentCapabilityResolver.Valid(p.Value.Images)});
            ServerCapabilities=(ServerCapabilities??new Dictionary<string,ModelCapabilitySetting>()).Where(p=>p.Value!=null).Take(1000).ToDictionary(p=>p.Key,p=>new ModelCapabilitySetting{Tools=AgentCapabilityResolver.Valid(p.Value.Tools),Images=AgentCapabilityResolver.Valid(p.Value.Images)});
            ApplyModelCapabilities(Connection);
            var labels=new Dictionary<string,string>(StringComparer.Ordinal);
            if(ModelDisplayNames!=null)foreach(var pair in ModelDisplayNames){string label=(pair.Value ?? "").Trim();if(clean.Contains(pair.Key) && label.Length>0){if(label.Length>80)label=label.Substring(0,80);labels[pair.Key]=label;}}
            ModelDisplayNames=labels;
            ModelContextWindows=ModelContextCapacity.Clean(ModelContextWindows,clean);ModelContextOverrides=ModelContextCapacity.Clean(ModelContextOverrides,clean);Connection.ContextWindowTokens=ContextCapacity(Connection.Model);
        }
        public string DisplayName(string model) {string label;return ModelDisplayNames!=null && model!=null && ModelDisplayNames.TryGetValue(model,out label) && !String.IsNullOrWhiteSpace(label)?label:model ?? "";}
        public TextProviderSettings Copy() { return new TextProviderSettings {Id=Id,Name=Name,BrandId=BrandId,CapabilityEndpoint=CapabilityEndpoint,ServerCapabilities=(ServerCapabilities??new Dictionary<string,ModelCapabilitySetting>()).Where(p=>p.Value!=null).ToDictionary(p=>p.Key,p=>p.Value.Copy()),ModelCapabilities=(ModelCapabilities??new Dictionary<string,ModelCapabilitySetting>()).Where(p=>p.Value!=null).ToDictionary(p=>p.Key,p=>p.Value.Copy()),ModelContextWindows=ModelContextCapacity.Copy(ModelContextWindows),ModelContextOverrides=ModelContextCapacity.Copy(ModelContextOverrides),ModelDisplayNames=ModelDisplayNames==null?new Dictionary<string,string>():new Dictionary<string,string>(ModelDisplayNames),Connection=(Connection ?? new ConnectionSettings()).Copy(),Models=Models==null?new List<string>():new List<string>(Models)}; }
        public override string ToString() {return ProviderIdentity.DisplayName(this);}
    }

    internal static class AtomicFile
    {
        internal static void Commit(string temporary,string destination)
        {
            for(int attempt=0;;attempt++)try{if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);return;}
            catch(IOException e){int code=e.HResult&0xffff;if(attempt>=5 || (code!=32&&code!=33&&code!=1175&&code!=1176&&code!=1177&&code!=80&&code!=183))throw;System.Threading.Thread.Sleep(10<<attempt);}
        }
    }
    internal static class ModelContextCapacity
    {
        internal const int Maximum=2000000;
        internal static Dictionary<string,int> Copy(Dictionary<string,int> values){return values==null?new Dictionary<string,int>():new Dictionary<string,int>(values);}
        internal static Dictionary<string,int> Clean(Dictionary<string,int> values,List<string> models){var result=new Dictionary<string,int>();if(values!=null)foreach(var pair in values)if(models.Contains(pair.Key)&&pair.Value>=1024&&pair.Value<=Maximum)result[pair.Key]=pair.Value;return result;}
        internal static int Read(Dictionary<string,object> metadata){if(metadata==null)return 0;object raw;int value;foreach(string key in new[]{"context_length","context_window","context_window_tokens","max_context_length","max_model_len","input_token_limit"})if(metadata.TryGetValue(key,out raw)&&Int32.TryParse(Convert.ToString(raw,System.Globalization.CultureInfo.InvariantCulture),out value)&&value>=1024&&value<=Maximum)return value;foreach(string key in new[]{"top_provider","capabilities","limits"})if(metadata.TryGetValue(key,out raw)){value=Read(raw as Dictionary<string,object>);if(value>0)return value;}return 0;}
        internal static int Budget(ConnectionSettings model,int configured){int budget=configured<=0?32768:Math.Max(1024,Math.Min(Maximum,configured));return model!=null&&model.ContextWindowTokens>0?Math.Max(1024,Math.Min(Maximum,model.ContextWindowTokens)):budget;}
    }
    [DataContract]
    public sealed class ServiceProviderSettings
    {
        [DataMember] public List<TextProviderSettings> Providers {get;set;}
        [DataMember] public string ActiveProviderId {get;set;}
        public ServiceProviderSettings(){Providers=new List<TextProviderSettings>();ActiveProviderId="";}
        public ServiceProviderSettings Copy(){var copy=new ServiceProviderSettings{ActiveProviderId=ActiveProviderId};if(Providers!=null)foreach(var provider in Providers)if(provider!=null)copy.Providers.Add(provider.Copy());return copy;}
    }
    [DataContract]
    public sealed class PetSettings
    {
        [IgnoreDataMember] public VoiceRecommendations ActiveVoiceRecommendations {get;set;}
        public const int OnboardingLastStep=16;
        [DataMember] public string OnboardingState { get; set; }
        [DataMember] public string OnboardingLessonId {get;set;}
        [DataMember] public List<string> OnboardingCompletedLessons {get;set;}
        [DataMember] public List<string> OnboardingSkippedLessons {get;set;}
        [DataMember] public List<string> OnboardingVerifiedLessons {get;set;}
        [DataMember] public int OnboardingStep { get; set; }
        [DataMember] public int OnboardingVersion { get; set; }
        [DataMember] public string LastOnboardingSeries { get; set; }
        [DataMember] public bool SaveChatHistory { get; set; }
        [DataMember] public bool AgentEnabled { get; set; }
        [DataMember] public bool AgentCommandsEnabled {get;set;}
        [DataMember] public string AgentCommandRecipes {get;set;}
        [DataMember] public int AgentCommandTimeoutSeconds {get;set;}
        [DataMember] public int AgentBackgroundJobTimeoutSeconds {get;set;}
        [DataMember] public AgentTerminalPolicy AgentTerminalPolicy {get;set;}
        [DataMember] public AgentApprovalMode AgentApprovalMode {get;set;}
        [DataMember] public bool ShowProjectSidebar {get;set;}
        [DataMember] public bool AgentSandboxEnabled {get;set;}
        [DataMember] public string AgentSandboxReadDirectories {get;set;}
        [DataMember] public bool AgentTrustProjectInstructions {get;set;}
        [DataMember] public string AgentGlobalInstructions {get;set;}
        [DataMember] public string AgentSkillsDirectory {get;set;}
        [DataMember] public List<McpServerConfiguration> McpServers {get;set;}
        [DataMember] public bool AgentFullAccess { get; set; }
        [DataMember] public bool AgentBackupFiles { get; set; }
        [DataMember] public bool ShowPredictedMessages {get;set;}
        [DataMember] public bool PlainTextEditor {get;set;}
        [DataMember] public bool ShowContextUsage {get;set;}
        [DataMember] public bool SendOnEnter {get;set;}
        [DataMember] public string FollowUpBehavior {get;set;}
        [DataMember] public int AgentContextTokenLimit {get;set;}
        [DataMember] public bool AgentDetailedProcess { get; set; }
        [DataMember] public string AgentWorkspace { get; set; }
        [DataMember] public string GitHubRepository { get; set; }
        [DataMember] public ConnectionSettings GitHub { get; set; }
        [DataMember] public bool AgentPlanMode { get; set; }
        [DataMember] public string AgentGoal { get; set; }
        [DataMember] public string ThemeMode {get;set;}
        [DataMember] public string UiLanguage {get;set;}
        [DataMember] public bool QuietMode { get; set; }
        [DataMember] public bool HideOnFullscreen { get; set; }
        [DataMember] public bool ClickThrough { get; set; }
        [DataMember] public bool LockPosition { get; set; }
        [DataMember] public Dictionary<string,string> Hotkeys { get; set; }
        [DataMember] public Dictionary<string,string> LocalShortcuts { get; set; }
        [DataMember] public double Scale { get; set; }
        [DataMember] public int Volume { get; set; }
        [DataMember] public bool Topmost { get; set; }
        [DataMember] public bool FollowCursor { get; set; }
        [DataMember] public bool Wander { get; set; }
        [DataMember] public bool StumbleEnabled { get; set; }
        [DataMember] public bool AutoSleepEnabled { get; set; }
        [DataMember] public bool GreetingEnabled { get; set; }
        [DataMember] public bool AiGreetingEnabled { get; set; }
        [DataMember] public int GreetingMinMinutes { get; set; }
        [DataMember] public int GreetingMaxMinutes { get; set; }
        [DataMember] public string SkinId { get; set; }
        [DataMember] public string SelectedCompanionKey { get; set; }
        [DataMember] public string PendingOfficialCompanionId { get; set; }
        [DataMember] public double? X { get; set; }
        [DataMember] public double? Y { get; set; }
        [DataMember] public bool ReadReplies { get; set; }
        [DataMember] public bool UseCloudVoice { get; set; }
        // Read old releases' reuse switches once; new configuration keeps independent credentials.
        [DataMember(EmitDefaultValue=false)] public bool VoiceUsesTextService { get; set; }
        [DataMember(EmitDefaultValue=false)] public bool SpeechUsesTextService { get; set; }
        [DataMember] public string VoiceMode { get; set; }
        [DataMember] public string LiveVoice { get; set; }
        [DataMember] public ConnectionSettings Live { get; set; }
        [DataMember] public Dictionary<string,string> PersonaOverrides { get; set; }
        [DataMember] public int PersonaProfileVersion { get; set; }
        [DataMember] public int VoiceProfileVersion { get; set; }
        [DataMember] public Dictionary<string,PersonaVoiceProfile> VoiceProfiles { get; set; }
        [DataMember] public string RepositoryUrl { get; set; }
        [DataMember] public bool UpdateUseProxy { get; set; }
        [DataMember] public string UpdateProxyAddress { get; set; }
        public const string DefaultUpdateMirror="https://gh-proxy.org";
        [DataMember] public bool UpdateUseMirror { get; set; }
        [DataMember] public string UpdateMirrorAddress { get; set; }
        [DataMember] public UpdateChannel UpdateChannel { get; set; }
        [DataMember] public bool AutoUpdate { get; set; }
        [DataMember] public long LastUpdateCheckUtcTicks { get; set; }
        [DataMember] public string VoiceName { get; set; }
        public static string DefaultAgentSystemPrompt {get{return TextResources.Get("personaProfiles.defaults.agent");}}
        [DataMember] public string AgentSystemPrompt {get;set;}
        [DataMember] public string SystemPrompt { get; set; }
        [DataMember] public ConnectionSettings Text { get; set; }
        [DataMember] public List<TextProviderSettings> TextProviders { get; set; }
        [DataMember] public string ActiveTextProviderId { get; set; }
        [DataMember] public List<ImageGenerationProvider> ImageProviders {get;set;}
        [DataMember] public string ActiveImageProviderId {get;set;}
        [DataMember] public int ImageGenerationConcurrency {get;set;}
        [DataMember] public bool ImageGenerationMotionEnabled {get;set;}
        [DataMember] public string ImageGalleryLayout {get;set;}
        [DataMember] public Dictionary<string,ServiceProviderSettings> AudioProviders {get;set;}
        [DataMember] public ConnectionSettings Transcription { get; set; }
        [DataMember] public ConnectionSettings Speech { get; set; }
        public PetSettings() { Defaults(); }
        [OnDeserializing] private void BeforeDeserialize(StreamingContext context) { Defaults();UpdateChannel=UpdateChannel.Stable;AgentApprovalMode=AgentApprovalMode.Legacy;AgentSandboxEnabled=false;OnboardingVersion=0;OnboardingState="legacy";LastOnboardingSeries=OnboardingUpgrade.LegacySeries;LiveVoice="Kore"; }
        private void Defaults()
        {
            ThemeMode="system";
            UiLanguage=UiLanguages.Default;
            OnboardingLessonId="";OnboardingCompletedLessons=new List<string>();OnboardingSkippedLessons=new List<string>();OnboardingVerifiedLessons=new List<string>();AgentSystemPrompt=DefaultAgentSystemPrompt;OnboardingState="new";OnboardingStep=0;OnboardingVersion=1;LastOnboardingSeries="";
            SaveChatHistory=true; QuietMode=false; HideOnFullscreen=true; ClickThrough=false; LockPosition=false;
            AgentFullAccess=true;AgentBackupFiles=true;AgentContextTokenLimit=32768;AgentDetailedProcess=false;ShowPredictedMessages=true;PlainTextEditor=false;ShowContextUsage=true;SendOnEnter=true;FollowUpBehavior="queue";
            // Full conversation defaults to work mode; quick chat is always
            // conversation-only and switches its own requests regardless.
            AgentEnabled=true;AgentWorkspace=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","agent-workspace");GitHubRepository="";GitHub=new ConnectionSettings{BaseUrl="https://api.github.com",Protocol="github"};
            AgentPlanMode=false;AgentGoal="";AgentCommandsEnabled=false;AgentCommandRecipes="[]";AgentCommandTimeoutSeconds=120;AgentBackgroundJobTimeoutSeconds=1800;
            ShowProjectSidebar=true;AgentApprovalMode=AgentApprovalMode.Auto;AgentSandboxEnabled=true;AgentTerminalPolicy=AgentTerminalPolicy.Disabled;
            LocalShortcuts=ShortcutCatalog.Defaults();
            Hotkeys=new Dictionary<string,string> { {"quick","Ctrl+Alt+Space"},{"hide","Ctrl+Alt+H"},{"quiet","Ctrl+Alt+D"},{"passthrough","Ctrl+Alt+P"} };
            Scale = 1.25; Volume = 50; Topmost = true; FollowCursor = true; Wander = false; StumbleEnabled=true;AutoSleepEnabled=true;
            GreetingEnabled=true; AiGreetingEnabled=false; GreetingMinMinutes=30; GreetingMaxMinutes=60;
            SkinId = "e-bao";
            SelectedCompanionKey = ""; PendingOfficialCompanionId = "";
            ReadReplies = true; UseCloudVoice = false; VoiceUsesTextService = false; VoiceName = "";
            TextProviders=new List<TextProviderSettings>();ActiveTextProviderId="";AudioProviders=new Dictionary<string,ServiceProviderSettings>();
            ImageProviders=new List<ImageGenerationProvider>();ActiveImageProviderId="";
            ImageGenerationConcurrency=1;ImageGenerationMotionEnabled=true;ImageGalleryLayout="masonry";
            Text = new ConnectionSettings(); Transcription = new ConnectionSettings(); Speech = new ConnectionSettings();
            Live = new ConnectionSettings { BaseUrl="wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent", Model="gemini-3.8-live", Protocol="gemini-live" };
            VoiceMode="pipeline"; LiveVoice="Puck"; PersonaOverrides=new Dictionary<string,string>();
            VoiceProfiles=new Dictionary<string,PersonaVoiceProfile>();
            RepositoryUrl=ReleaseInfo.RepositoryUrl;
            UpdateProxyAddress=ProxyConfiguration.DefaultAddress;
            UpdateMirrorAddress=DefaultUpdateMirror;
            AutoUpdate=true;UpdateChannel=ReleaseInfo.DisplayVersion.Contains("-dev")?UpdateChannel.Preview:UpdateChannel.Stable;
            SystemPrompt = PersonaProfiles.Generic("{pet_name}");
        }
        public void Normalize()
        {
            ThemeMode=ThemeColors.Normalize(ThemeMode);
            UiLanguage=UiLanguages.Normalize(UiLanguage);
            ImageGenerationConcurrency=Math.Max(1,Math.Min(4,ImageGenerationConcurrency));
            ImageGalleryLayout=ImageGalleryLayout=="grid"?"grid":"masonry";
            if(String.IsNullOrWhiteSpace(AgentSystemPrompt))AgentSystemPrompt=DefaultAgentSystemPrompt;if(AgentSystemPrompt.Length>16000)AgentSystemPrompt=AgentSystemPrompt.Substring(0,16000);
            var mcpIds=new HashSet<string>();McpServers=(McpServers??new List<McpServerConfiguration>()).Where(c=>c!=null).Take(16).Select(c=>{var copy=c.Copy();if(String.IsNullOrWhiteSpace(copy.Id)||copy.Id.Length>80||!mcpIds.Add(copy.Id)){copy.Id=Guid.NewGuid().ToString("N");mcpIds.Add(copy.Id);}return copy;}).ToList();
            if(!Enum.IsDefined(typeof(AgentTerminalPolicy),AgentTerminalPolicy))AgentTerminalPolicy=AgentTerminalPolicy.Disabled;
            if(!Enum.IsDefined(typeof(AgentApprovalMode),AgentApprovalMode))AgentApprovalMode=AgentApprovalMode.Legacy;
            if(AgentApprovalMode==AgentApprovalMode.Full)AgentSandboxEnabled=false;
            if(OnboardingState!="new" && OnboardingState!="deferred" && OnboardingState!="skipped" && OnboardingState!="completed")OnboardingState="completed";
            if(FollowUpBehavior!="steer")FollowUpBehavior="queue";
            if(String.IsNullOrWhiteSpace(AgentWorkspace))AgentWorkspace=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","agent-workspace");GitHubRepository=(GitHubRepository??"").Trim();GitHub=GitHub??new ConnectionSettings{BaseUrl="https://api.github.com",Protocol="github"};GitHub.Normalize();
            AgentCommandRecipes="[]";AgentCommandsEnabled=false;AgentCommandTimeoutSeconds=Math.Max(1,Math.Min(600,AgentCommandTimeoutSeconds));
            AgentBackgroundJobTimeoutSeconds=Math.Max(1,Math.Min(28800,AgentBackgroundJobTimeoutSeconds));
            if(AgentContextTokenLimit==0)AgentContextTokenLimit=32768;AgentContextTokenLimit=Math.Max(8192,Math.Min(2000000,AgentContextTokenLimit));
            AgentGoal=(AgentGoal??"").Trim();if(AgentGoal.Length>4000)AgentGoal=AgentGoal.Substring(0,4000);
            // Version 1 inserts the double-click lesson before the old service steps.
            if(OnboardingVersion<1){OnboardingStep=Math.Max(0,Math.Min(12,OnboardingStep));if(OnboardingStep>=7)OnboardingStep++;OnboardingVersion=1;}
            OnboardingStep=Math.Max(0,Math.Min(OnboardingLastStep,OnboardingStep));
            OnboardingLessonId=(OnboardingLessonId??"");if(OnboardingLessonId.Length>128)OnboardingLessonId="";
            OnboardingCompletedLessons=NormalizeGuideIds(OnboardingCompletedLessons);OnboardingSkippedLessons=NormalizeGuideIds(OnboardingSkippedLessons);OnboardingVerifiedLessons=NormalizeGuideIds(OnboardingVerifiedLessons);
            LocalShortcuts=LocalShortcuts ?? new Dictionary<string,string>();
            foreach(var pair in ShortcutCatalog.Defaults())if(!LocalShortcuts.ContainsKey(pair.Key))LocalShortcuts[pair.Key]=pair.Value;else LocalShortcuts[pair.Key]=(LocalShortcuts[pair.Key]??"").Trim();
            Hotkeys=Hotkeys ?? new Dictionary<string,string>();
            foreach(var pair in new Dictionary<string,string> { {"quick","Ctrl+Alt+Space"},{"hide","Ctrl+Alt+H"},{"quiet","Ctrl+Alt+D"},{"passthrough","Ctrl+Alt+P"} })
                if(!Hotkeys.ContainsKey(pair.Key))Hotkeys[pair.Key]=pair.Value; else Hotkeys[pair.Key]=(Hotkeys[pair.Key] ?? "").Trim();
            if (Double.IsNaN(Scale) || Double.IsInfinity(Scale)) Scale = 1.25;
            Scale = Math.Max(.75, Math.Min(2.25, Scale)); Volume = Math.Max(0, Math.Min(100, Volume));
            GreetingMinMinutes=Math.Max(1,Math.Min(1440,GreetingMinMinutes));
            GreetingMaxMinutes=Math.Max(GreetingMinMinutes,Math.Min(1440,GreetingMaxMinutes));
            if (X.HasValue && (Double.IsNaN(X.Value) || Double.IsInfinity(X.Value))) X = null;
            if (Y.HasValue && (Double.IsNaN(Y.Value) || Double.IsInfinity(Y.Value))) Y = null;
            Text = Text ?? new ConnectionSettings(); Transcription = Transcription ?? new ConnectionSettings(); Speech = Speech ?? new ConnectionSettings();
            Text.Normalize(); Transcription.Normalize(); Speech.Normalize();EnsureTextProviders();EnsureImageProviders();
            if(PersonaProfileVersion==0 && Speech.Model.StartsWith("mimo-",StringComparison.OrdinalIgnoreCase) && Speech.BaseUrl.Length==0 && Speech.EncryptedKey.Length==0)SpeechUsesTextService=true;
            if(VoiceUsesTextService)MigrateAudioCredentials("asr",Transcription);
            if(SpeechUsesTextService)MigrateAudioCredentials("speech",Speech);
            VoiceUsesTextService=SpeechUsesTextService=false;
            Live=Live ?? new ConnectionSettings(); Live.Normalize(); PersonaOverrides=PersonaOverrides ?? new Dictionary<string,string>();
            AudioProviders=AudioProviders ?? new Dictionary<string,ServiceProviderSettings>();
            var cleanAudio=new Dictionary<string,ServiceProviderSettings>();foreach(string kind in new[]{"asr","speech","live"})if(AudioProviders.ContainsKey(kind)){var view=ProviderView(kind);cleanAudio[kind]=new ServiceProviderSettings{Providers=view.TextProviders,ActiveProviderId=view.ActiveTextProviderId};}AudioProviders=cleanAudio;
            VoiceMode=VoiceMode=="live" ? "live" : "pipeline"; LiveVoice=String.IsNullOrWhiteSpace(LiveVoice) ? "Kore" : LiveVoice.Trim();
            RepositoryUrl=ReleaseInfo.RepositoryUrl;
            UpdateProxyAddress=ProxyConfiguration.Normalize(UpdateProxyAddress);
            UpdateMirrorAddress=String.IsNullOrWhiteSpace(UpdateMirrorAddress)?DefaultUpdateMirror:UpdateMirrorAddress.Trim().TrimEnd('/');
            if(UpdateUseMirror)UpdateUseProxy=false;if(!Enum.IsDefined(typeof(UpdateChannel),UpdateChannel))UpdateChannel=UpdateChannel.Stable;
            if(LastUpdateCheckUtcTicks<0 || LastUpdateCheckUtcTicks>DateTime.UtcNow.AddDays(1).Ticks)LastUpdateCheckUtcTicks=0;
            VoiceName = (VoiceName ?? "").Trim(); SystemPrompt = SystemPrompt ?? "";
            SkinId = String.IsNullOrWhiteSpace(SkinId) ? "e-bao" : SkinId;
            SelectedCompanionKey = OfficialCompanionIdentity.NormalizePickerKey(SelectedCompanionKey);
            PendingOfficialCompanionId = OfficialCompanionProfiles.IsOfficialId(PendingOfficialCompanionId) ? PendingOfficialCompanionId : "";
            var profiles=new Dictionary<string,PersonaVoiceProfile>();
            if(VoiceProfiles!=null)foreach(var pair in VoiceProfiles) {
                if(profiles.Count>=32)break;
                if(String.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length>256 || pair.Value==null)continue;
                pair.Value.Normalize();profiles[pair.Key]=pair.Value;
            }
            VoiceProfiles=profiles;VoiceProfileVersion=Math.Max(0,VoiceProfileVersion);
        }
        public void EnsureImageProviders()
        {
            var clean=new List<ImageGenerationProvider>();var ids=new HashSet<string>(StringComparer.Ordinal);
            if(ImageProviders!=null)foreach(var provider in ImageProviders){if(provider==null || clean.Count>=32)continue;provider.Normalize();if(!ids.Add(provider.Id)){provider.Id=Guid.NewGuid().ToString("N");ids.Add(provider.Id);}clean.Add(provider);}
            ImageProviders=clean;
            if(!clean.Exists(p=>p.Id==ActiveImageProviderId))ActiveImageProviderId=clean.Count==0?"":clean[0].Id;
        }
        public ImageGenerationProvider SelectedImageProvider()
        {
            EnsureImageProviders();var selected=ImageProviders.Find(p=>p.Id==ActiveImageProviderId);
            return selected==null?null:selected.Copy();
        }
        public void EnsureTextProviders()
        {
            Text=Text ?? new ConnectionSettings();Text.Normalize();
            var clean=new List<TextProviderSettings>();var ids=new HashSet<string>(StringComparer.Ordinal);
            if(TextProviders!=null)foreach(var provider in TextProviders) {
                if(provider==null || clean.Count>=32)continue;
                provider.Normalize();if(!ids.Add(provider.Id)){provider.Id=Guid.NewGuid().ToString("N");ids.Add(provider.Id);}clean.Add(provider);
            }
            if(clean.Count==0) {var initial=new TextProviderSettings{Name=ProviderIdentity.Name(Text),Connection=Text.Copy()};initial.Normalize();clean.Add(initial);}
            TextProviders=clean;
            if(!clean.Exists(p=>p.Id==ActiveTextProviderId))ActiveTextProviderId=clean[0].Id;
            var active=clean.Find(p=>p.Id==ActiveTextProviderId);if(String.IsNullOrWhiteSpace(active.Connection.BaseUrl)&&active.Models.Count==0&&!String.IsNullOrWhiteSpace(Text.BaseUrl)){active.Connection=Text.Copy();active.Normalize();}if(active.Connection.Model==Text.Model && active.Connection.BaseUrl==Text.BaseUrl)Text.ContextWindowTokens=active.ContextCapacity(Text.Model);
        }
        public bool SelectTextModel(string providerId,string model)
        {
            EnsureTextProviders();
            var provider=TextProviders.Find(p=>p.Id==providerId);
            if(provider==null || !provider.Models.Contains(model ?? ""))return false;
            provider.Connection.Model=model;provider.Connection.ContextWindowTokens=provider.ContextCapacity(model);provider.ApplyModelCapabilities(provider.Connection);Text=provider.Connection.Copy();ActiveTextProviderId=provider.Id;return true;
        }
        public PetSettings ProviderView(string kind)
        {
            if(kind=="text")return this;
            if(kind!="asr" && kind!="speech" && kind!="live")throw new ArgumentException("Unknown service kind.");
            AudioProviders=AudioProviders ?? new Dictionary<string,ServiceProviderSettings>();ServiceProviderSettings group;
            if(!AudioProviders.TryGetValue(kind,out group) || group==null){group=new ServiceProviderSettings();AudioProviders[kind]=group;}
            var connection=kind=="asr"?Transcription:kind=="speech"?Speech:Live;
            var view=new PetSettings{Text=(connection ?? new ConnectionSettings()).Copy(),TextProviders=group.Providers,ActiveTextProviderId=group.ActiveProviderId};view.EnsureTextProviders();
            group.Providers=view.TextProviders;group.ActiveProviderId=view.ActiveTextProviderId;return view;
        }
        public ServiceProviderSettings ProviderSnapshot(string kind)
        {
            var view=ProviderView(kind);view.EnsureTextProviders();
            return new ServiceProviderSettings{Providers=view.TextProviders,ActiveProviderId=view.ActiveTextProviderId}.Copy();
        }
        public void ApplyProviders(string kind,List<TextProviderSettings> providers,string active,ConnectionSettings connection)
        {
            if(kind=="text"){TextProviders=providers;ActiveTextProviderId=active;Text=connection;return;}
            ProviderView(kind);AudioProviders[kind]=new ServiceProviderSettings{Providers=providers,ActiveProviderId=active};
            if(kind=="asr")Transcription=connection;else if(kind=="speech")Speech=connection;else Live=connection;
        }
        public ConnectionSettings TranscriptionConnection()
        {
            return Transcription.Copy();
        }
        public ConnectionSettings SpeechConnection()
        {
            return Speech.Copy();
        }
        void MigrateAudioCredentials(string kind,ConnectionSettings target)
        {
            target.BaseUrl=Text.BaseUrl;target.EncryptedKey=Text.EncryptedKey;target.UseProxy=Text.UseProxy;target.ProxyAddress=Text.ProxyAddress;
            ServiceProviderSettings group;if(AudioProviders!=null && AudioProviders.TryGetValue(kind,out group) && group!=null && group.Providers!=null){var active=group.Providers.Find(p=>p!=null && p.Id==group.ActiveProviderId);if(active!=null)active.Connection=target.Copy();}
        }
        static List<string> NormalizeGuideIds(List<string> ids){return (ids??new List<string>()).Where(id=>!String.IsNullOrWhiteSpace(id)&&id.Length<=128).Distinct().Take(256).ToList();}
        public PetSettings Copy()
        {
            var voiceProfiles=new Dictionary<string,PersonaVoiceProfile>();
            if(VoiceProfiles!=null)foreach(var pair in VoiceProfiles)if(pair.Value!=null)voiceProfiles[pair.Key]=pair.Value.Copy();
            var providers=new List<TextProviderSettings>();if(TextProviders!=null)foreach(var provider in TextProviders)if(provider!=null)providers.Add(provider.Copy());
            var images=new List<ImageGenerationProvider>();if(ImageProviders!=null)foreach(var provider in ImageProviders)if(provider!=null)images.Add(provider.Copy());
            var audioProviders=new Dictionary<string,ServiceProviderSettings>();if(AudioProviders!=null)foreach(var pair in AudioProviders)if(pair.Value!=null)audioProviders[pair.Key]=pair.Value.Copy();
            return new PetSettings { ThemeMode=ThemeMode,UiLanguage=UiLanguage,ShowProjectSidebar=ShowProjectSidebar,OnboardingLessonId=OnboardingLessonId,OnboardingCompletedLessons=new List<string>(OnboardingCompletedLessons??new List<string>()),OnboardingSkippedLessons=new List<string>(OnboardingSkippedLessons??new List<string>()),OnboardingVerifiedLessons=new List<string>(OnboardingVerifiedLessons??new List<string>()),AgentSystemPrompt=AgentSystemPrompt,AgentCommandTimeoutSeconds=AgentCommandTimeoutSeconds,AgentBackgroundJobTimeoutSeconds=AgentBackgroundJobTimeoutSeconds,AgentTerminalPolicy=AgentTerminalPolicy,AgentApprovalMode=AgentApprovalMode,AgentSandboxEnabled=AgentSandboxEnabled,AgentSandboxReadDirectories=AgentSandboxReadDirectories,AgentTrustProjectInstructions=AgentTrustProjectInstructions,AgentGlobalInstructions=AgentGlobalInstructions,AgentSkillsDirectory=AgentSkillsDirectory,McpServers=(McpServers??new List<McpServerConfiguration>()).Where(s=>s!=null).Take(16).Select(s=>s.Copy()).ToList(),AgentFullAccess=AgentFullAccess,AgentBackupFiles=AgentBackupFiles,AgentContextTokenLimit=AgentContextTokenLimit,GitHubRepository=GitHubRepository,GitHub=GitHub==null?null:GitHub.Copy(),ShowPredictedMessages=ShowPredictedMessages,PlainTextEditor=PlainTextEditor,ShowContextUsage=ShowContextUsage,SendOnEnter=SendOnEnter,FollowUpBehavior=FollowUpBehavior,AgentDetailedProcess=AgentDetailedProcess,AgentPlanMode=AgentPlanMode,AgentGoal=AgentGoal,AgentEnabled=AgentEnabled,AgentWorkspace=AgentWorkspace,AudioProviders=audioProviders,TextProviders=providers,ActiveTextProviderId=ActiveTextProviderId,OnboardingState=OnboardingState,OnboardingStep=OnboardingStep,OnboardingVersion=OnboardingVersion,LastOnboardingSeries=LastOnboardingSeries,SaveChatHistory=SaveChatHistory,QuietMode=QuietMode,HideOnFullscreen=HideOnFullscreen,ClickThrough=ClickThrough,LockPosition=LockPosition,LocalShortcuts=new Dictionary<string,string>(LocalShortcuts??ShortcutCatalog.Defaults()),Hotkeys=new Dictionary<string,string>(Hotkeys),Scale=Scale,Volume=Volume,Topmost=Topmost,FollowCursor=FollowCursor,Wander=Wander,SkinId=SkinId,SelectedCompanionKey=OfficialCompanionIdentity.NormalizePickerKey(SelectedCompanionKey),PendingOfficialCompanionId=OfficialCompanionProfiles.IsOfficialId(PendingOfficialCompanionId)?PendingOfficialCompanionId:"",X=X,Y=Y,
                ImageGenerationConcurrency=ImageGenerationConcurrency,ImageGenerationMotionEnabled=ImageGenerationMotionEnabled,ImageGalleryLayout=ImageGalleryLayout,ImageProviders=images,ActiveImageProviderId=ActiveImageProviderId,AutoSleepEnabled=AutoSleepEnabled,StumbleEnabled=StumbleEnabled,GreetingEnabled=GreetingEnabled,AiGreetingEnabled=AiGreetingEnabled,GreetingMinMinutes=GreetingMinMinutes,GreetingMaxMinutes=GreetingMaxMinutes,
                ReadReplies=ReadReplies,UseCloudVoice=UseCloudVoice,VoiceUsesTextService=VoiceUsesTextService,VoiceName=VoiceName,SystemPrompt=SystemPrompt,
                SpeechUsesTextService=SpeechUsesTextService,VoiceMode=VoiceMode,LiveVoice=LiveVoice,Live=Live.Copy(),PersonaProfileVersion=PersonaProfileVersion,VoiceProfileVersion=VoiceProfileVersion,VoiceProfiles=voiceProfiles,
                PersonaOverrides=new Dictionary<string,string>(PersonaOverrides),RepositoryUrl=RepositoryUrl,UpdateUseProxy=UpdateUseProxy,UpdateProxyAddress=UpdateProxyAddress,UpdateUseMirror=UpdateUseMirror,UpdateMirrorAddress=UpdateMirrorAddress,UpdateChannel=UpdateChannel,AutoUpdate=AutoUpdate,LastUpdateCheckUtcTicks=LastUpdateCheckUtcTicks,Text=Text.Copy(),Transcription=Transcription.Copy(),Speech=Speech.Copy() };
        }
    }

    public sealed partial class SettingsStore
    {
        private readonly string path;
        readonly WriteState writeState;
        System.Threading.SemaphoreSlim writes{get{return writeState.Gate;}}
        string committedLanguage{get{return writeState.Language;}set{writeState.Language=value;}}
        int completedSnapshotWrites;
        internal int CompletedSnapshotWrites{get{return System.Threading.Volatile.Read(ref completedSnapshotWrites);}}
        public string Warning { get; private set; }
        public string DirectoryPath { get { return Path.GetDirectoryName(path); } }
        public SettingsStore(string filePath) { path = Path.GetFullPath(filePath);writeState=GetWriteState(path); }
        public PetSettings Load()
        {
            Warning = "";
            if (!File.Exists(path)) return new PetSettings();
            try
            {
                using (var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                {
                    if (stream.Length > 1024 * 1024) throw new SerializationException("Settings too large.");
                    var settings = (PetSettings)new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream);
                    if (settings == null) throw new SerializationException("Empty settings.");
                    settings.Normalize();
                    if ((!String.IsNullOrEmpty(settings.Text.EncryptedKey) && settings.Text.ApiKey.Length == 0) ||
                        (!String.IsNullOrEmpty(settings.Transcription.EncryptedKey) && settings.Transcription.ApiKey.Length == 0) ||
                        (!String.IsNullOrEmpty(settings.Speech.EncryptedKey) && settings.Speech.ApiKey.Length == 0) ||
                        (!String.IsNullOrEmpty(settings.Live.EncryptedKey) && settings.Live.ApiKey.Length == 0))
                        Warning = TextResources.Get("settings.load.warning");
                    foreach(var provider in settings.TextProviders)if(!String.IsNullOrEmpty(provider.Connection.EncryptedKey) && provider.Connection.ApiKey.Length==0)Warning=TextResources.Get("settings.load.warning");
                    foreach(var provider in settings.ImageProviders)if(!String.IsNullOrEmpty(provider.Connection.EncryptedKey) && provider.Connection.ApiKey.Length==0)Warning=TextResources.Get("settings.load.warning.alternativeA");
                    return settings;
                }
            }
            catch (Exception e)
            {
                if (!(e is SerializationException || e is IOException || e is UnauthorizedAccessException || e is ArgumentException)) throw;
                Warning = TextResources.Get("settings.load.warning.alternativeB");
                try { File.Copy(path, path + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"), false); }
                catch (IOException) { Warning += TextResources.Get("settings.load.warning.alternativeC"); }
                catch (UnauthorizedAccessException) { Warning += TextResources.Get("settings.load.warning.alternativeC"); }
                return new PetSettings();
            }
        }
        public void SaveUiLanguage(PetSettings settings,string code)
        {
            if(settings==null)throw new ArgumentNullException("settings");
            SaveLanguageSelection(settings,code,false);
        }
        internal void SaveLanguageSelection(PetSettings settings,string code,bool guide){SaveLanguageSelectionCore(settings,code,guide,false);}
        internal System.Threading.Tasks.Task SaveLanguageSelectionAsync(PetSettings detached,string code,bool guide)
        {return System.Threading.Tasks.Task.Run(()=>SaveLanguageSelectionCore(detached,code,guide,true));}
        void SaveLanguageSelectionCore(PetSettings settings,string code,bool guide,bool ownsPublication)
        {
            if(!ownsPublication&&(System.Threading.Volatile.Read(ref writeState.Resetting)!=0||System.Threading.Volatile.Read(ref writeState.Publishing)!=0))throw new IOException(TextResources.Get("dataMaintenance.busy"));
            writes.Wait();
            try{
                if(!ownsPublication&&(System.Threading.Volatile.Read(ref writeState.Resetting)!=0||System.Threading.Volatile.Read(ref writeState.Publishing)!=0))throw new IOException(TextResources.Get("dataMaintenance.busy"));
                var json=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=16*1024*1024,RecursionLimit=128};
                Dictionary<string,object> values;
                try{
                    string raw=File.ReadAllText(path,new UTF8Encoding(false,true));
                    using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(raw)))
                        if(new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream)==null)throw new InvalidDataException("Empty settings.");
                    values=ReadLanguageSettings(raw,json);
                }catch(FileNotFoundException){values=DefaultLanguageSettings(json);}
                catch(DirectoryNotFoundException){values=DefaultLanguageSettings(json);}
                catch(Exception error){if(!(error is SerializationException||error is ArgumentException||error is InvalidOperationException||error is DecoderFallbackException))throw;throw new InvalidDataException("Cannot read existing settings; language was not changed.",error);}
                string normalized=UiLanguages.Normalize(code);values["UiLanguage"]=json.Serialize(normalized);
                if(guide){values["OnboardingLessonId"]=json.Serialize("welcome");values["OnboardingStep"]="0";values["OnboardingState"]=json.Serialize("deferred");}
                Directory.CreateDirectory(DirectoryPath);string temporary=path+".tmp";
                // Never delete a temporary file owned/locked by another writer on failure.
                using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){
                    string output="{"+String.Join(",",values.Select(pair=>json.Serialize(pair.Key)+":"+(string)pair.Value))+"}";
                    var bytes=new UTF8Encoding(false).GetBytes(output);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
                }
                if(BeforeLanguageCommit!=null)BeforeLanguageCommit();
                AtomicFile.Commit(temporary,path);
                committedLanguage=normalized;settings.UiLanguage=normalized;
                if(guide){long order=System.Threading.Interlocked.Increment(ref writeState.Order);foreach(string field in new[]{"OnboardingLessonId","OnboardingStep","OnboardingState"})writeState.Fields[field]=order;}
                if(guide){settings.OnboardingLessonId="welcome";settings.OnboardingStep=0;settings.OnboardingState="deferred";}
                Warning="";
            }finally{writes.Release();}
        }
        static Dictionary<string,object> DefaultLanguageSettings(System.Web.Script.Serialization.JavaScriptSerializer json)
        {using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(PetSettings)).WriteObject(stream,new PetSettings());return ReadLanguageSettings(Encoding.UTF8.GetString(stream.ToArray()),json);}}
        // Settings were validated above. Keep each value's JSON bytes as text rather
        // than round-tripping unknown future fields through a numeric CLR type.
        static Dictionary<string,object> ReadLanguageSettings(string raw,System.Web.Script.Serialization.JavaScriptSerializer json)
        {
            if(raw.Length>json.MaxJsonLength)throw new InvalidDataException("Settings exceed the language-save limit.");
            var values=new Dictionary<string,object>();int position=0;
            Action whitespace=delegate{while(position<raw.Length&&Char.IsWhiteSpace(raw[position]))position++;};
            whitespace();if(position==raw.Length||raw[position++]!='{')throw new InvalidDataException("Expected a settings object.");
            while(true){
                whitespace();if(position<raw.Length&&raw[position]=='}'){position++;break;}
                int keyStart=position;if(position==raw.Length||raw[position++]!='"')throw new InvalidDataException("Expected a settings key.");
                bool escaped=false,closed=false;
                while(position<raw.Length){char c=raw[position++];if(escaped){escaped=false;continue;}if(c=='\\'){escaped=true;continue;}if(c=='"'){closed=true;break;}}
                if(!closed)throw new InvalidDataException("Unterminated settings key.");
                string key=json.Deserialize<string>(raw.Substring(keyStart,position-keyStart));
                whitespace();if(position==raw.Length||raw[position++]!=':')throw new InvalidDataException("Expected a settings value.");whitespace();
                int start=position,depth=0;bool quoted=false;escaped=false;
                while(position<raw.Length){
                    char c=raw[position];
                    if(quoted){if(escaped)escaped=false;else if(c=='\\')escaped=true;else if(c=='"')quoted=false;}
                    else if(c=='"')quoted=true;
                    else if(c=='{'||c=='['){if(++depth>json.RecursionLimit)throw new InvalidDataException("Settings exceed the depth limit.");}
                    else if(c=='}'||c==']'){if(depth==0)break;depth--;}
                    else if(c==','&&depth==0)break;
                    position++;
                }
                if(quoted||depth!=0||position==start||values.ContainsKey(key))throw new InvalidDataException("Invalid or duplicate settings member.");
                values.Add(key,raw.Substring(start,position-start));whitespace();
                if(position<raw.Length&&raw[position]==','){position++;continue;}
                if(position<raw.Length&&raw[position]=='}'){position++;break;}
                throw new InvalidDataException("Unterminated settings object.");
            }
            whitespace();if(position!=raw.Length)throw new InvalidDataException("Unexpected settings suffix.");return values;
        }
        public void Save(PetSettings settings)
        {
            if((System.Threading.Volatile.Read(ref writeState.Resetting)!=0||System.Threading.Volatile.Read(ref writeState.Publishing)!=0))throw new IOException(TextResources.Get("dataMaintenance.busy"));
            writes.Wait();
            try{if((System.Threading.Volatile.Read(ref writeState.Resetting)!=0||System.Threading.Volatile.Read(ref writeState.Publishing)!=0))throw new IOException(TextResources.Get("dataMaintenance.busy"));settings.Normalize();SaveSnapshot(settings,System.Threading.Interlocked.Increment(ref writeState.Order));}finally{writes.Release();}
        }
        // Save a detached candidate before publishing preferences. Deliberately
        // avoid Normalize here: provider migration is outside this operation.
        public async System.Threading.Tasks.Task RestoreDefaultPreferencesAsync(PetSettings settings,Maintenance.DataMaintenanceGate gate)
        {
            if(settings==null)throw new ArgumentNullException("settings");
            if(gate==null)throw new ArgumentNullException("gate");
            if(System.Threading.Interlocked.CompareExchange(ref writeState.Resetting,1,0)!=0)throw new IOException(TextResources.Get("dataMaintenance.busy"));
            try{using(await gate.BeginAsync()){
                if(System.Threading.Volatile.Read(ref writeState.Publishing)!=0)throw new IOException(TextResources.Get("dataMaintenance.busy"));
                var candidate=Snapshot(settings);CopyPreferences(new PetSettings(),candidate);
                await WriteExactCandidateAsync(candidate);CopyPreferences(candidate,settings);
            }}finally{System.Threading.Volatile.Write(ref writeState.Resetting,0);}
        }
        // Snapshot on the caller's UI context; lock ownership and durable I/O
        // stay entirely on the worker, so synchronous saves cannot deadlock UI.
        public System.Threading.Tasks.Task SaveExactSnapshotAsync(PetSettings settings)
        {if((System.Threading.Volatile.Read(ref writeState.Resetting)!=0||System.Threading.Volatile.Read(ref writeState.Publishing)!=0))throw new IOException(TextResources.Get("dataMaintenance.busy"));return WriteExactCandidateAsync(Snapshot(settings));}
        internal System.Threading.Tasks.Task SaveDetachedSnapshotAsync(PetSettings candidate){return WriteExactCandidateAsync(candidate,true);}
        System.Threading.Tasks.Task WriteExactCandidateAsync(PetSettings candidate,bool normalize=false)
        {
            long order=System.Threading.Interlocked.Increment(ref writeState.Order);
            return System.Threading.Tasks.Task.Run(delegate{
                if(BeforeExactSave!=null)BeforeExactSave();
                writes.Wait();try{if(normalize)candidate.Normalize();SaveSnapshot(candidate,order);}finally{writes.Release();}
            });
        }
        internal static PetSettings Snapshot(PetSettings settings)
        {
            var serializer=new DataContractJsonSerializer(typeof(PetSettings));
            using(var stream=new MemoryStream()){serializer.WriteObject(stream,settings);stream.Position=0;return (PetSettings)serializer.ReadObject(stream);}
        }
        internal static string SnapshotIdentity(PetSettings settings)
        {using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(PetSettings)).WriteObject(stream,settings);return Convert.ToBase64String(stream.ToArray());}}
        static void CopyPreferences(PetSettings source,PetSettings target)
        {
            target.ThemeMode=source.ThemeMode;target.Scale=source.Scale;target.Volume=source.Volume;
            target.Topmost=source.Topmost;target.FollowCursor=source.FollowCursor;target.Wander=source.Wander;
            target.StumbleEnabled=source.StumbleEnabled;target.AutoSleepEnabled=source.AutoSleepEnabled;
            target.GreetingEnabled=source.GreetingEnabled;target.AiGreetingEnabled=source.AiGreetingEnabled;
            target.GreetingMinMinutes=source.GreetingMinMinutes;target.GreetingMaxMinutes=source.GreetingMaxMinutes;
            target.QuietMode=source.QuietMode;target.HideOnFullscreen=source.HideOnFullscreen;
            target.ClickThrough=source.ClickThrough;target.LockPosition=source.LockPosition;
            target.SaveChatHistory=source.SaveChatHistory;target.ShowPredictedMessages=source.ShowPredictedMessages;
            target.PlainTextEditor=source.PlainTextEditor;target.ShowContextUsage=source.ShowContextUsage;
            target.SendOnEnter=source.SendOnEnter;target.FollowUpBehavior=source.FollowUpBehavior;
            target.ImageGenerationConcurrency=source.ImageGenerationConcurrency;target.ImageGenerationMotionEnabled=source.ImageGenerationMotionEnabled;
            target.ImageGalleryLayout=source.ImageGalleryLayout;
            target.Hotkeys=new Dictionary<string,string>(source.Hotkeys);target.LocalShortcuts=new Dictionary<string,string>(source.LocalShortcuts);
        }
        void SaveSnapshot(PetSettings settings,long order)
        {
            if(writeState.LastFull>order)throw new IOException("A newer configuration has already been saved.");
            if(writeState.Fields.Any(p=>p.Value>order)){
                PetSettings latest;
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))latest=(PetSettings)new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream);
                foreach(var item in writeState.Fields.Where(p=>p.Value>order))switch(item.Key){
                    case "Scale":settings.Scale=latest.Scale;break;case "Volume":settings.Volume=latest.Volume;break;
                    case "X":settings.X=latest.X;break;case "Y":settings.Y=latest.Y;break;
                    case "OnboardingLessonId":settings.OnboardingLessonId=latest.OnboardingLessonId;break;
                    case "OnboardingStep":settings.OnboardingStep=latest.OnboardingStep;break;
                    case "OnboardingState":settings.OnboardingState=latest.OnboardingState;break;
                }
            }
            if(committedLanguage!=null)settings.UiLanguage=committedLanguage;
            Directory.CreateDirectory(DirectoryPath);
            string temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                { new DataContractJsonSerializer(typeof(PetSettings)).WriteObject(stream, settings); stream.Flush(true); }
                AtomicFile.Commit(temporary,path);
                writeState.LastFull=Math.Max(writeState.LastFull,order);
                System.Threading.Interlocked.Increment(ref completedSnapshotWrites);
                Warning = "";
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
