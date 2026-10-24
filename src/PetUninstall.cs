using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Dafeiyu.Maintenance;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        UninstallOwner uninstallOwner;UninstallTicket uninstallTicket;bool uninstallPreparing,uninstallCloseRequested;
        string uninstallSettingsIdentity;CancellationToken uninstallCancellation;
        Task uninstallCompletion=Task.FromResult(0);
        internal Task UninstallCompletion {get{return uninstallCompletion;}}
        void CheckUninstallBusy()
        {
            if(closing||IsUpdating||HasActiveAgentJobs||DictationActive||LiveActive||(call!=null&&call.IsVisible)||HasQueuedMessages||
                (conversation!=null&&conversation.IsBusy)||(workspaces!=null&&workspaces.Busy)||(inputQueue!=null&&inputQueue.IsRunning)||
                (chat!=null&&(chat.HasDraft||chat.HasUnsavedMcpEdits))||(quick!=null&&quick.HasDraft)||(preferences!=null&&preferences.HasUnsavedEdits)||
                (mcpConnections!=null&&mcpConnections.Status.Any(s=>s.State=="connecting")))throw new InvalidOperationException(TextResources.Get("dataMaintenance.busy"));
            lock(imageRepairAdmissionGate)if(pendingImageRepairs.Count!=0)throw new InvalidOperationException(TextResources.Get("dataMaintenance.busy"));
        }
        internal Task<UninstallOwner> PrepareUninstallAsync(CancellationToken token)
        {return Dispatcher.InvokeAsync(()=>PrepareUninstallOnDispatcherAsync(token)).Task.Unwrap();}
        async Task<UninstallOwner> PrepareUninstallOnDispatcherAsync(CancellationToken token)
        {
            Dispatcher.VerifyAccess();CheckUninstallBusy();token.ThrowIfCancellationRequested();
            UninstallOwner owner=null;
            try{
                owner=await UninstallOwner.AcquireAsync(DataMaintenance);token.ThrowIfCancellationRequested();CheckUninstallBusy();
                await CheckUninstallStoresAsync(owner);token.ThrowIfCancellationRequested();
                await SaveUninstallSettingsAsync(owner,token);return owner;
            }catch{if(owner!=null)owner.Dispose();throw;}
        }
        async Task CheckUninstallStoresAsync(UninstallOwner owner)
        {
            owner.Require(DataMaintenance);
            var sessions=DataMaintenance.GetParticipants<ConversationSession>().Concat(ConversationSessions).Distinct().ToArray();
            await Task.Run(()=>Task.WhenAll(sessions.Select(s=>s.SaveForUninstallAsync(owner))));
            await Relationships.CheckForUninstallAsync(owner);await HolidayKeepsakes.FlushAsync();
            if(imageTasks!=null)imageTasks.CheckForUninstall(owner);
            if(agentJobServices!=null)await agentJobServices.CheckForUninstallAsync(owner);
            owner.Require(DataMaintenance);
        }
        async Task SaveUninstallSettingsAsync(UninstallOwner owner,CancellationToken token,bool capturePosition=true)
        {
            try{
                do{
                    owner.Require(DataMaintenance);token.ThrowIfCancellationRequested();
                    if(capturePosition&&handle!=IntPtr.Zero){var position=DesktopInterop.Bounds(handle);Settings.X=position.Left;Settings.Y=position.Top;}
                    var snapshot=SettingsStore.Snapshot(Settings);string identity=SettingsStore.SnapshotIdentity(snapshot);deferredSettingsSave=false;
                    await Store.SaveExactSnapshotAsync(snapshot);owner.Require(DataMaintenance);token.ThrowIfCancellationRequested();
                    if(identity!=SettingsStore.SnapshotIdentity(Settings))deferredSettingsSave=true;
                    if(!deferredSettingsSave){uninstallSettingsIdentity=identity;return;}
                }while(true);
            }catch{deferredSettingsSave=true;throw;}
        }
        internal Task<string> UninstallAsync(CancellationToken token)
        {return Dispatcher.InvokeAsync(()=>UninstallOnDispatcherAsync(token)).Task.Unwrap();}
        async Task<string> UninstallOnDispatcherAsync(CancellationToken token)
        {
            if(uninstallPreparing)return TextResources.Get("dataMaintenance.busy");
            uninstallPreparing=true;uninstallCancellation=token;
            try{
                token.ThrowIfCancellationRequested();string directory=AppDomain.CurrentDomain.BaseDirectory;
                if(await Task.Run(()=>UpdateInstall.GetUninstallerPath(directory))==null)return TextResources.Get("uninstall.portable");
                uninstallOwner=await PrepareUninstallAsync(token);
                uninstallTicket=await UninstallHandoff.PrepareAsync(directory);token.ThrowIfCancellationRequested();
                return await CloseForUninstallAsync(uninstallOwner,uninstallTicket,token)?"":TextResources.Get("uninstall.closeCanceled");
            }
            catch(OperationCanceledException){return TextResources.Get("uninstall.closeCanceled");}
            catch(Exception error){
                if(error is InvalidOperationException&&error.Message==TextResources.Get("dataMaintenance.busy"))return TextResources.Get("dataMaintenance.busy");
                RuntimeDiagnostics.Record(error);return uninstallOwner!=null&&uninstallOwner.IsClosed?TextResources.Get("uninstall.handoffFailed"):TextResources.Get("uninstall.prepareFailed");
            }
            finally{if(uninstallOwner==null||!uninstallOwner.IsClosed)AbortUninstall();}
        }
        internal Task<bool> CloseForUninstallAsync(UninstallOwner owner,UninstallTicket ticket,CancellationToken token)
        {return Dispatcher.InvokeAsync(()=>CloseForUninstallOnDispatcherAsync(owner,ticket,token)).Task.Unwrap();}
        async Task<bool> CloseForUninstallOnDispatcherAsync(UninstallOwner owner,UninstallTicket ticket,CancellationToken token)
        {
            Dispatcher.VerifyAccess();owner.Require(DataMaintenance);
            if(uninstallOwner!=null&&!ReferenceEquals(owner,uninstallOwner))throw new InvalidOperationException("Another uninstall attempt owns this host.");
            uninstallOwner=owner;uninstallTicket=ticket;uninstallPreparing=true;uninstallCancellation=token;
            try{
                await CheckUninstallStoresAsync(owner);await SaveUninstallSettingsAsync(owner,token);
                token.ThrowIfCancellationRequested();uninstallCloseRequested=true;Close();
                if(!owner.IsClosed)return false;
                await uninstallCompletion;return true;
            }finally{if(!owner.IsClosed)AbortUninstall();}
        }
        void AbortUninstall()
        {
            if(uninstallTicket!=null){uninstallTicket.Abort();uninstallTicket.Dispose();uninstallTicket=null;}
            if(uninstallOwner!=null){uninstallOwner.Dispose();uninstallOwner=null;}
            uninstallPreparing=uninstallCloseRequested=false;uninstallSettingsIdentity=null;
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            if(!uninstallPreparing){base.OnClosing(e);return;}
            base.OnClosing(e);
            if(!uninstallCloseRequested){e.Cancel=true;return;}
            if(e.Cancel||uninstallCancellation.IsCancellationRequested||uninstallOwner==null||uninstallSettingsIdentity!=SettingsStore.SnapshotIdentity(Settings)||deferredSettingsSave){e.Cancel=true;return;}
            uninstallOwner.Require(DataMaintenance);
        }
        protected override void OnClosed(EventArgs e)
        {
            if(uninstallCloseRequested&&uninstallOwner!=null){
                uninstallOwner.MarkClosed();closing=true;
                // Dispatch after all actual Closed subscribers, which may still coalesce a save.
                uninstallCompletion=Dispatcher.InvokeAsync(()=>CompleteUninstallCloseAsync(uninstallOwner,uninstallTicket)).Task.Unwrap();
            }
            base.OnClosed(e);
        }
        async Task CompleteUninstallCloseAsync(UninstallOwner owner,UninstallTicket ticket)
        {
            try{
                owner.Require(DataMaintenance,true);ApplicationRestart.Requested=false;
                timer.Stop();CompositionTarget.Rendering-=RenderFrame;DisposeRelationshipFeedback();await HolidayKeepsakes.ShutdownAsync();
                if(agentJobServices!=null){var report=await agentJobServices.CloseForUninstallAsync(owner);if(!report.CleanupConfirmed)throw new IOException(TextResources.Get("dataMaintenance.storageFailure"));}
                await Relationships.CheckForUninstallAsync(owner);await Relationships.ShutdownAsync();
                imageRepairExitDrain=FreezeImageRepairAdmission();await imageRepairExitDrain;
                if(imageTasks!=null){imageTasks.CheckForUninstall(owner);await imageTasks.StopAsync(CancellationToken.None);}
                var sessions=DataMaintenance.GetParticipants<ConversationSession>();
                FinalizeConversationServices();await Task.WhenAll(sessions.Select(s=>s.FlushHistoryAsync()));
                if(sessions.Any(s=>!String.IsNullOrEmpty(s.MaintenanceStorageWarning)))throw new IOException(TextResources.Get("dataMaintenance.storageFailure"));
                DisposeOfficialCompanions();DisposeHoliday();if(hotkeys!=null)hotkeys.Dispose();if(updates!=null)updates.Dispose();
                foreach(var window in agentJobWindows.ToArray())window.Close();if(guide!=null)guide.Close();if(palette!=null)palette.Close();
                CleanupClosedWindow();await ImageServicesShutdown;owner.Require(DataMaintenance,true);
                if(deferredSettingsSave||uninstallSettingsIdentity!=SettingsStore.SnapshotIdentity(Settings))await SaveUninstallSettingsAsync(owner,CancellationToken.None,false);
                if(ticket==null)throw new IOException(TextResources.Get("uninstall.handoffFailed"));ticket.Commit();
            }
            catch(Exception error){if(ticket!=null)ticket.Abort();RuntimeDiagnostics.Record(error);throw;}
            finally{if(ticket!=null)ticket.Dispose();owner.Dispose();}
        }
    }
}
