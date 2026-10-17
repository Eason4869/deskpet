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
            Add(new AgentProfile{Id="build",Description="Full development agent",Mode="primary",ReadOnly=false,ModelPreference=""});
            Add(new AgentProfile{Id="plan",Description="Read-only planning agent",Mode="primary",ReadOnly=true,ModelPreference=""});
            Add(new AgentProfile{Id="explore",Description="Read-only research subagent",Mode="subagent",ReadOnly=true,ModelPreference=""});
            Add(new AgentProfile{Id="general",Description="Research and multi-step subagent",Mode="subagent",ReadOnly=false,ModelPreference=""});
        }
        public AgentProfile Get(string id){AgentProfile profile;return profiles.TryGetValue(id??"",out profile)?profile:null;}
        public AgentPermissionRuntime PermissionRuntime(string id)
        {
            var profile=Get(id);if(profile==null)throw new ArgumentException("未知 Agent Profile："+id);
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
