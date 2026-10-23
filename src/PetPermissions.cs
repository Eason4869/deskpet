using System;
using System.Linq;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        internal void SetConversationPermissions(ConversationSession session,AgentApprovalMode mode,bool sandbox)
        {
            var workspace=Workspaces.OpenWorkspaces.FirstOrDefault(w=>Object.ReferenceEquals(w.Session,session));
            if(session.IsBusy||(workspace!=null&&workspace.Queue.HasPending))throw new InvalidOperationException(TextResources.Get("petPermissions.setConversationPermissions.text"));
            if(mode==AgentApprovalMode.Full)sandbox=false;
            if(workspace==null)session.PermissionOverride=new AgentPermissionSelection{Mode=mode,Sandbox=sandbox};
            else{var d=workspace.Definition;bool oldOverride=d.HasPermissionOverride,oldSandbox=d.SandboxEnabled;var oldMode=d.ApprovalMode;try{d.HasPermissionOverride=true;d.SandboxEnabled=sandbox;d.ApprovalMode=mode;if(!workspace.IsDraft)ProjectStore.Save(Projects);}catch{d.HasPermissionOverride=oldOverride;d.SandboxEnabled=oldSandbox;d.ApprovalMode=oldMode;throw;}}
            RefreshChatAppearance();
        }
        internal bool WorkIsRunning {get{return Workspaces.OpenWorkspaces.Any(w=>w.EffectiveSettings().AgentEnabled&&w.Session.IsBusy);}}
    }
}
