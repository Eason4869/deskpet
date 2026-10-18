using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentCapabilityTests
    {
        static int count;static void Check(bool yes,string why){if(!yes)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        public static int Main(){try{
            var settings=new PetSettings();settings.Text=new ConnectionSettings{BaseUrl="https://compatible.invalid/v1",Model="unknown-local-model",ToolSupport=CapabilitySupport.Supported,ImageSupport=CapabilitySupport.Unsupported};
            var snapshot=AgentCapabilityResolver.Resolve(settings,null);
            Check(snapshot.Tools==CapabilitySupport.Supported && snapshot.SupportsTools,"explicit unknown model supports tools");
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"capability-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            using(var workspace=new AgentWorkspace(root)){workspace.ConfigureCapabilities(snapshot);Check(workspace.OpenAITools.Length>0,"explicit capability reaches schema");
                settings.Text.ToolSupport=CapabilitySupport.Unsupported;workspace.ConfigureCapabilities(AgentCapabilityResolver.Resolve(settings,null));
                Check(workspace.OpenAITools.Length==0,"disabled tools removed from schema");Check(workspace.ExecuteAsync("list_files","{}",CancellationToken.None).Result.StartsWith("错误："),"direct execution rechecks model capability");}
            settings.Text.ContextWindowTokens=131072;snapshot=AgentCapabilityResolver.Resolve(settings,null);Check(snapshot.ContextWindowTokens==131072,"model capacity wins over fallback");
            settings.Text.ContextWindowTokens=0;snapshot=AgentCapabilityResolver.Resolve(settings,null);Check(snapshot.ContextWindowTokens==settings.AgentContextTokenLimit && snapshot.ContextSource=="fallback","unknown capacity uses labelled fallback");
            var copy=settings.Copy();copy.Text.ToolSupport=CapabilitySupport.Supported;Check(settings.Text.ToolSupport==CapabilitySupport.Unsupported,"capability snapshots copy independently");
            var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(settings);var reloaded=store.Load();Check(reloaded.Text.ToolSupport==CapabilitySupport.Unsupported,"explicit unsupported survives persistence");
            var provider=new TextProviderSettings{Connection=new ConnectionSettings{BaseUrl="https://a.invalid/v1",Model="same"}};provider.Models.Add("same");provider.ModelCapabilities["same"]=new ModelCapabilitySetting{Tools=CapabilitySupport.Supported};provider.ModelContextWindows["same"]=131072;provider.Normalize();
            Check(provider.Capabilities("same").Tools==CapabilitySupport.Supported,"per-model capability retained");provider.Connection.BaseUrl="https://b.invalid/v1";provider.Normalize();Check(provider.Capabilities("same").Tools==CapabilitySupport.Unknown && provider.ContextCapacity("same")==0,"changing endpoint clears stale capability and capacity metadata");
            settings.Text.ToolSupport=CapabilitySupport.Unknown;settings.Text.MetadataToolSupport=CapabilitySupport.Unsupported;Check(!AgentCapabilityResolver.Resolve(settings,null).SupportsTools&&AgentCapabilityResolver.Resolve(settings,null).ToolSource=="server-metadata","explicit server metadata overrides legacy guess");settings.Text.ToolSupport=CapabilitySupport.Supported;Check(AgentCapabilityResolver.Resolve(settings,null).SupportsTools,"user override takes precedence over server metadata");
            var metadata=ModelCapabilitySetting.Read(new Dictionary<string,object>{{"supports_tools",false},{"capabilities",new Dictionary<string,object>{{"vision",true}}}});Check(metadata.Tools==CapabilitySupport.Unsupported&&metadata.Images==CapabilitySupport.Supported,"only explicit typed server capability fields are parsed");Check(ModelCapabilitySetting.Read(new Dictionary<string,object>{{"supports_tools","true"},{"id","vision-model"}}).Tools==CapabilitySupport.Unknown,"strings and model names do not manufacture metadata");
            Console.WriteLine("Agent capability tests: "+count+" passed");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
