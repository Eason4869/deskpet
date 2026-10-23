using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    public sealed class AgentProfile
    {
        public string Id,Description,Mode;public bool ReadOnly;public string ModelPreference;
    }
    public sealed class AgentProfileRegistry
    {
        readonly Dictionary<string,AgentProfile> profiles=new Dictionary<string,AgentProfile>(StringComparer.Ordinal);
        public AgentProfileRegistry()
        {
            Add(new AgentProfile{Id="build",Description=TextResources.Get("agentProfile.agentProfileRegistry.description"),Mode="primary",ReadOnly=false,ModelPreference=""});
            Add(new AgentProfile{Id="plan",Description=TextResources.Get("agentProfile.agentProfileRegistry.description.alternativeA"),Mode="primary",ReadOnly=true,ModelPreference=""});
            Add(new AgentProfile{Id="explore",Description=TextResources.Get("agentProfile.agentProfileRegistry.description.alternativeB"),Mode="subagent",ReadOnly=true,ModelPreference=""});
            Add(new AgentProfile{Id="general",Description=TextResources.Get("agentProfile.agentProfileRegistry.description.alternativeC"),Mode="subagent",ReadOnly=false,ModelPreference=""});
        }
        public AgentProfile Get(string id){AgentProfile profile;return profiles.TryGetValue(id??"",out profile)?profile:null;}
        public AgentPermissionRuntime PermissionRuntime(string id)
        {
            var profile=Get(id);if(profile==null)throw new ArgumentException(TextResources.Format("agentProfile.permissionRuntime.template",id));
            var runtime=new AgentPermissionRuntime();
            runtime.Add(new AgentPermissionRule{Action="read",Resource="**",Effect=AgentPermissionEffect.Allow});
            runtime.Add(new AgentPermissionRule{Action="network",Resource="**",Effect=AgentPermissionEffect.Allow});runtime.Add(new AgentPermissionRule{Action="github_read",Resource="**",Effect=AgentPermissionEffect.Allow});
            if(!profile.ReadOnly)
            {
                runtime.Add(new AgentPermissionRule{Action="write",Resource="**",Effect=AgentPermissionEffect.Allow});
                runtime.Add(new AgentPermissionRule{Action="shell",Resource="**",Effect=AgentPermissionEffect.Allow});
                runtime.Add(new AgentPermissionRule{Action="github_write",Resource="**",Effect=AgentPermissionEffect.Ask});runtime.Add(new AgentPermissionRule{Action="subagent",Resource="**",Effect=AgentPermissionEffect.Allow});
            }
            return runtime;
        }
        void Add(AgentProfile profile){profiles[profile.Id]=profile;}
    }
}
