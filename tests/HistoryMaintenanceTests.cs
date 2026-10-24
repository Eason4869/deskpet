using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.Maintenance;

namespace Dafeiyu.Tests
{
    public static class HistoryMaintenanceTests
    {
        static int passed,failed;
        static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=");
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static string Root(){string path=Path.Combine(Directory.GetCurrentDirectory(),"work","history-maintenance-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
        static void Run(string name,Func<Task> test){try{test().GetAwaiter().GetResult();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
        static ConversationSession Session(DataMaintenanceGate gate,HistoryStore store=null,Api api=null){return new ConversationSession(api??new Api(),new Speech(),()=>null,store,()=>true,maintenance:gate);}
        static async Task<string> Seed(string directory,string persona="pet",ChatAttachment file=null)
        {
            var log=new ConversationLog();log.Commit("private transcript","private answer");if(file!=null)log.Attach(log.Messages[0].Id,new[]{file});var store=new HistoryStore(directory);await store.SaveAsync(persona,log.Messages);Check(String.IsNullOrEmpty(store.Warning),"fixture save failed: "+store.Warning);return Directory.GetFiles(directory,"*.history").Last();
        }
        [STAThread] public static int Main()
        {
            passed=failed=0;
            Run("all canonical histories and contexts clear without touching project files or relationship receipts",async()=>{
                string root=Root();string[] owners={root,Path.Combine(root,"conversations","loaded"),Path.Combine(root,"conversations","unloaded"),Path.Combine(root,"projects","legacy","chats","old")};
                foreach(string owner in owners){await Seed(Path.Combine(owner,"history"),file:ChatAttachment.CreateImage("capture.png",Png));var kernel=new AgentSessionModel("session","project","chat");kernel.Enqueue(new AgentSessionInput{IdempotencyId="input",Text="secret context"});new AgentSessionStore(Path.Combine(owner,"kernel-sessions")).Save(kernel);await new AgentTaskStore(Path.Combine(owner,"history","agent-tasks")).SaveAsync(new AgentTaskState{Workspace=root,Goal="secret task",RelationshipSucceeded=true,RelationshipEventId="old-success"});}
                string backup=Path.Combine(root,"ownership-backups","old");await Seed(backup);
                string[] retained={"settings.json","relationships/receipts.bin","relationships/outbox.bin","project-facts.bin","agent-workspaces/project/history/keep.history",".deskpet-agent-kernel/jobs/owner.json","projects/legacy/definition.json","projects/legacy/project-facts.bin","source/.deskpet-agent-kernel/runs/background-jobs/owner.json"};
                foreach(string relative in retained){string path=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,Png);}
                var gate=new DataMaintenanceGate();using(var session=Session(gate,new HistoryStore(Path.Combine(owners[1],"history")))){
                    session.SelectPersona("pet");session.AddLiveTurn("other","cached text","answer");session.InputDraft="unsent draft";session.ReportNotice("private previous tool output");await session.FlushHistoryAsync();
                    var loadedKernel=new AgentSessionModel("loaded-session","project","chat");loadedKernel.Enqueue(new AgentSessionInput{IdempotencyId="cached",Text="cached model context"});loadedKernel.DeliverNext();loadedKernel.StartStep();session.ConfigureKernelSession(loadedKernel,new AgentSessionStore(Path.Combine(owners[1],"kernel-sessions")));
                    var result=await new HistoryMaintenanceService(root,gate).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Complete,"clear incomplete");
                    Check(result.RemovedFiles>=14,"missing canonical paths");foreach(string owner in owners){Check(!Directory.GetFiles(Path.Combine(owner,"history"),"*.history").Any(),"history survived");Check(!Directory.GetFiles(Path.Combine(owner,"history","agent-tasks"),"*.task").Any(),"task replay survived");Check(!Directory.GetFiles(Path.Combine(owner,"kernel-sessions"),"*.session").Any(),"kernel replay survived");}
                    Check(!Directory.GetFiles(backup,"*.history").Any(),"ownership backup survived");Check(session.History.Messages.Count==0&&session.InputDraft=="unsent draft","memory or draft wrong");session.SelectPersona("other");Check(session.History.Messages.Count==0,"cached persona resurrected");await session.SaveHistoryForCloseAsync();Check(new HistoryStore(Path.Combine(owners[1],"history")).Load("pet").Count==0,"close resurrected transcript");
                    Check(loadedKernel.Pending.Count==0&&loadedKernel.Delivered.Count==0&&loadedKernel.Events.Count==0&&loadedKernel.Step==0,"loaded model context survived");Check(session.LastNotice.Length==0,"previous conversation notice survived");
                    foreach(string relative in retained)Check(File.ReadAllBytes(Path.Combine(root,relative)).SequenceEqual(Png),"protected bytes changed: "+relative);
                    string archive=Directory.GetFiles(result.AttachmentDirectory,"*.png").Single();Check(File.ReadAllBytes(archive).SequenceEqual(Png),"sole image bytes lost");
                    var decoded=GeneratedImageValidation.Validate(File.ReadAllBytes(archive));Check(decoded.Width==1&&decoded.Height==1,"preserved PNG cannot decode");
                }
            });
            Run("unsaved loaded attachments and PDF originals survive before memory reset",async()=>{
                string root=Root();string external=Path.Combine(root,"original.pdf");byte[] pdf=System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nfixture original\n%%EOF");File.WriteAllBytes(external,pdf);var gate=new DataMaintenanceGate();using(var session=Session(gate)){
                    session.History.Commit("unsaved","reply");session.History.Attach(session.History.Messages[0].Id,new[]{ChatAttachment.CreateImage("screen.png",Png),ChatAttachment.Read(external)});
                    var result=await new HistoryMaintenanceService(root,gate).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Complete&&session.History.Messages.Count==0,"unsaved memory not cleared");Check(File.ReadAllBytes(Directory.GetFiles(result.AttachmentDirectory,"*.pdf").Single()).SequenceEqual(pdf)&&File.ReadAllBytes(external).SequenceEqual(pdf),"PDF original lost");Check(File.ReadAllBytes(Directory.GetFiles(result.AttachmentDirectory,"*.png").Single()).SequenceEqual(Png),"unsaved screenshot lost");
                }
            });
            Run("unreadable history and archive write failure retain originals and allow retry",async()=>{
                string root=Root();string file=await Seed(Path.Combine(root,"history"),file:ChatAttachment.CreateImage("sole.png",Png));byte[] before=File.ReadAllBytes(file);var service=new HistoryMaintenanceService(root,new DataMaintenanceGate());File.WriteAllText(service.AttachmentDirectory,"blocked directory");
                var result=await service.ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Partial&&result.Failures.Count>0&&File.ReadAllBytes(file).SequenceEqual(before),"archive failure deleted history");File.Delete(service.AttachmentDirectory);
                string corrupt=Path.Combine(root,"history","corrupt.history");File.WriteAllText(corrupt,"corrupt encrypted file");result=await service.ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Partial&&File.ReadAllText(corrupt)=="corrupt encrypted file"&&File.Exists(file),"corrupt history was silently cleared");File.Delete(corrupt);Check((await service.ClearAllAsync()).Status==HistoryMaintenanceStatus.Complete,"retry did not clear");
            });
            Run("persisted unloaded pending queues refuse without discarding their encrypted context",async()=>{
                string root=Root();string owner=Path.Combine(root,"conversations","unloaded");string history=await Seed(Path.Combine(owner,"history"));var cfg=new PetSettings();cfg.Text.BaseUrl="http://127.0.0.1/v1";cfg.Text.Model="test";
                using(var s=Session(new DataMaintenanceGate(),api:new Api{Fail=true}))using(var q=new ConversationQueue(s,()=>cfg,()=>"pet",Path.Combine(owner,"queued-messages.json"))){q.Enqueue("pending user task");await q.FlushAsync();Check(q.HasPending,"failed send was not requeued");var queues=(Dictionary<string,List<QueuedMessage>>)typeof(ConversationQueue).GetField("queues",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(q);queues.Values.Single()[0].Context=new List<ChatMessage>{new ChatMessage("assistant","encrypted previous context")};typeof(ConversationQueue).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(q,new object[0]);}
                string queue=Path.Combine(owner,"queued-messages.json");byte[] saved=File.ReadAllBytes(queue);var result=await new HistoryMaintenanceService(root,new DataMaintenanceGate()).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Busy&&File.Exists(history)&&File.ReadAllBytes(queue).SequenceEqual(saved),"unloaded pending queue lost or accepted");
            });
            Run("maintenance reserves synchronously, drains real writers and excludes new sends",async()=>{
                string root=Root();var gate=new DataMaintenanceGate();var store=new HistoryStore(Path.Combine(root,"history"));var held=new TaskCompletionSource<bool>();typeof(HistoryStore).GetField("worker",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(store,held.Task);
                using(var session=Session(gate,store)){
                    var log=new ConversationLog();log.Commit("late writer","answer");var saving=store.SaveAsync("pet",log.Messages);var clear=new HistoryMaintenanceService(root,gate).ClearAllAsync();Check(gate.IsMaintenanceActive&&!clear.IsCompleted,"not reserved before returning");bool refused=false;try{session.AddLiveTurn("pet","late input","reply");}catch(InvalidOperationException){refused=true;}held.SetResult(true);await saving;var result=await clear;Check(refused&&result.Status==HistoryMaintenanceStatus.Complete&&store.Load("pet").Count==0,"drain or admission failed");
                }
            });
            Run("archive readback failure retains loaded sole copies and supports retry",async()=>{
                string root=Root();var gate=new DataMaintenanceGate();using(var session=Session(gate)){
                    session.History.Commit("private text","answer");session.History.Attach(session.History.Messages[0].Id,new[]{ChatAttachment.CreateImage("sole.png",Png)});var service=new HistoryMaintenanceService(root,gate);
                    // Seed the expected content-addressed original using the actual archive.
                    new AttachmentArchive(service.AttachmentDirectory).Preserve(session.History.Messages[0].Attachments);string archive=Directory.GetFiles(service.AttachmentDirectory).Single();
                    using(var locked=new FileStream(archive,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
                        var result=await service.ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Partial&&session.History.Messages.Count==2,"failed readback discarded loaded-only image");
                    }
                    Check((await service.ClearAllAsync()).Status==HistoryMaintenanceStatus.Complete,"readback retry failed");Check(File.ReadAllBytes(archive).SequenceEqual(Png),"retry changed image");
                }
            });
            Run("partial deletion clears only removed cached histories and close cannot recreate them",async()=>{
                string root=Root();var gate=new DataMaintenanceGate();var store=new HistoryStore(Path.Combine(root,"history"));await Seed(store.DirectoryPath,"first");await Seed(store.DirectoryPath,"second");
                using(var session=Session(gate,store)){
                    session.SelectPersona("first");session.SelectPersona("second");string retained=store.MaintenancePath("first");byte[] before=File.ReadAllBytes(retained);
                    using(var locked=new FileStream(retained,FileMode.Open,FileAccess.Read,FileShare.Read)){
                        var result=await new HistoryMaintenanceService(root,gate).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Partial&&result.RemovedFiles==1,"in-use history was not partial");Check(session.History.Messages.Count==0,"removed cached history remains");await session.SaveHistoryForCloseAsync();Check(new HistoryStore(store.DirectoryPath).Load("second").Count==0,"close recreated cleared second persona");
                    }
                    Check(File.ReadAllBytes(retained).SequenceEqual(before),"failed removal changed original");
                }
            });
            Run("failed real checkpoint storage preserves unsaved guidance and never claims successful drain",async()=>{
                string root=Root();string directory=Path.Combine(root,"history");Directory.CreateDirectory(directory);string taskDirectory=Path.Combine(directory,"agent-tasks");File.WriteAllText(taskDirectory,"blocked task storage");var gate=new DataMaintenanceGate();
                using(var session=Session(gate,new HistoryStore(directory))){
                    var store=(AgentTaskStore)typeof(ConversationSession).GetField("taskStore",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);var task=new AgentTaskState{Workspace=root,Goal="pending checkpoint",Guidance=new List<AgentGuidance>{new AgentGuidance{Id="g",Text="private guidance",Attachments=new List<ChatAttachment>{ChatAttachment.CreateImage("pending.png",Png)}}}};await store.SaveAsync(task);Check(store.Warning.Length>0,"fixture did not fail");
                    var result=await new HistoryMaintenanceService(root,gate).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Partial&&store.MaintenancePending().Length==1,"quiescence mistaken for durable save");Check(File.ReadAllBytes(Directory.GetFiles(result.AttachmentDirectory,"*.png").Single()).SequenceEqual(Png),"pending-only image not preserved");
                    File.Delete(taskDirectory);await session.RetryAgentTaskSaveAsync();Check((await new HistoryMaintenanceService(root,gate).ClearAllAsync()).Status==HistoryMaintenanceStatus.Complete,"checkpoint retry failed");await session.RetryAgentTaskSaveAsync();Check(!Directory.GetFiles(taskDirectory,"*.task").Any(),"pending retry resurrected cleared task");
                }
            });
            Run("disabled-growth completed task cannot resume after clearing or restarting",async()=>{
                string root=Root();var gate=new DataMaintenanceGate();var cfg=new PetSettings{AgentEnabled=true,AgentWorkspace=root};var history=new HistoryStore(Path.Combine(root,"history"));var tasks=new AgentTaskStore(Path.Combine(history.DirectoryPath,"agent-tasks"));await tasks.SaveAsync(new AgentTaskState{Workspace=root,PersonaId="pet",Status="complete",Goal="old successful work",RelationshipCompanionId="pet",RelationshipEventId="old-root",RelationshipSucceeded=true});
                using(var session=Session(gate,history)){session.SelectPersona("pet");session.LoadAgentTask(cfg);Check(session.CurrentAgentTask.RelationshipSucceeded,"ever-success fixture missing");var result=await new HistoryMaintenanceService(root,gate).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Complete&&session.CurrentAgentTask==null,"old resume retained");bool refused=false;try{await session.ResumeAgentTaskAsync(cfg,"pet");}catch(InvalidOperationException){refused=true;}Check(refused,"old success reopened");}
                using(var restarted=Session(new DataMaintenanceGate(),new HistoryStore(history.DirectoryPath))){restarted.SelectPersona("pet");restarted.LoadAgentTask(cfg);Check(restarted.CurrentAgentTask==null,"restart restored eligible old root");}
            });
            Run("generated images and temporary originals survive while old delivery origins stay detached across restart",async()=>{
                string root=Root();var gate=new DataMaintenanceGate();string imageId,referenceId,taskId="completed-image";
                using(var assets=new GeneratedImageStore(root)){
                    var image=assets.Save(Png,new GeneratedImage{ImageJobId=taskId,Source=ImageGenerationSource.Conversation,Prompt="image prompt"},false);imageId=image.Id;
                    referenceId=assets.SaveReference(Png,new GeneratedImage(),false).Id;
                    var taskLog=new ImageGenerationTaskStore(root);taskLog.Save(new[]{new ImageGenerationTaskRecord{Id=taskId,QueuedUtc=DateTime.UtcNow,RequestId="request",State=ImageGenerationTaskState.Succeeded,ResultImageId=image.Id,Origin=new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId="chat",MessageId="old-message",PersonaId="pet"},DeliveryState=ImageTaskDeliveryState.AwaitingSync},new ImageGenerationTaskRecord{Id="failed-task",QueuedUtc=DateTime.UtcNow,State=ImageGenerationTaskState.Failed,ReferenceImageIds=new List<string>{referenceId},Origin=new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId="chat",MessageId="old-failed"}}});
                    var manager=new ImageGenerationTaskManager(taskLog,assets,new Executor(),new ImageAssetUsageRegistry());await manager.RestoreAsync(CancellationToken.None);
                    using(var session=new ConversationSession(new Api(),new Speech(),()=>null,new HistoryStore(Path.Combine(root,"history")),imageConversationId:"chat",maintenance:gate)){
                        session.ImageTasks=manager;session.ImageStore=assets;session.SelectPersona("pet");session.History.Commit("draw","answer");session.History.AttachGeneratedImage(session.History.Messages[1].Id,image);
                        var result=await new HistoryMaintenanceService(root,gate,manager).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Complete,"image clear failed");Check(manager.Get(taskId).Origin.ConversationId==null&&manager.Get(taskId).DeliveryState==ImageTaskDeliveryState.Detached,"live delivery origin remains");bool rejected=false;try{await session.RepairImageDeliveryAsync(taskId,CancellationToken.None);}catch(InvalidOperationException){rejected=true;}Check(rejected&&session.History.Messages.Count==0,"repair restored old history");
                    }
                }
                using(var assets=new GeneratedImageStore(root)){Check(assets.Read(imageId).SequenceEqual(Png)&&!assets.Get(imageId).Temporary,"temporary original did not survive shutdown");Check(assets.Read(referenceId).SequenceEqual(Png)&&!assets.Get(referenceId).Temporary,"task-only temporary reference lost");var manager=new ImageGenerationTaskManager(new ImageGenerationTaskStore(root),assets,new Executor(),new ImageAssetUsageRegistry());await manager.RestoreAsync(CancellationToken.None);Check(manager.Get(taskId).Origin.ConversationId==null,"restart restored origin");}
            });
            Run("corrupt embedded media is retained with an honest partial result",async()=>{
                string root=Root();string file=await Seed(Path.Combine(root,"history"),file:ChatAttachment.CreateImage("broken.png",new byte[]{137,80,78,71,1,2,3,4}));byte[] before=File.ReadAllBytes(file);
                var result=await new HistoryMaintenanceService(root,new DataMaintenanceGate()).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Partial&&File.ReadAllBytes(file).SequenceEqual(before),"bad media was reported preserved and original removed");
            });
            Run("actual relationship ledger receipts outbox and settings are byte identical and replay stays deduplicated",async()=>{
                string root=Root();const string pet="legacy-skin:skin-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";var time=RelationshipTime.Current();
                using(var runtime=new RelationshipRuntime(root)){
                    runtime.Record(pet,RelationshipEventType.CompletedConversation,"retained-event",time);Check(runtime.Warning.Length==0,"relationship fixture failed");runtime.Service.UpdateSettings(true,false,false);var before=Directory.GetFiles(Path.Combine(root,"relationships")).ToDictionary(p=>p,p=>File.ReadAllBytes(p));var score=runtime.Service.GetSnapshot(pet).State.Score;
                    await Seed(Path.Combine(root,"history"));var result=await new HistoryMaintenanceService(root,new DataMaintenanceGate()).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Complete,"clear failed");foreach(var file in before)Check(File.ReadAllBytes(file.Key).SequenceEqual(file.Value),"actual relationship bytes changed");runtime.Record(pet,RelationshipEventType.CompletedConversation,"retained-event",time);var after=runtime.Service.GetSnapshot(pet);Check(after.State.Score==score&&after.State.Receipts.Count==1&&!after.Settings.FeedbackEnabled&&!after.Settings.AiContextEnabled,"clear reopened reward or reset relationship settings");
                }
            });
            Run("corrupt and unsupported unloaded kernel contexts stop clearing before any mutation",async()=>{
                var valid=new AgentSessionModel("session","project","unloaded");valid.Enqueue(new AgentSessionInput{IdempotencyId="input",Text="retained model context"});
                byte[] plain=SessionBytes(valid);string json=System.Text.Encoding.UTF8.GetString(plain);
                byte[] futureJson=System.Text.Encoding.UTF8.GetBytes(json.Insert(1,"\"Version\":2,"));
                var invalid=new[]{new byte[]{1,2,3,4},System.Text.Encoding.ASCII.GetBytes("DPS2unsupported"),System.Text.Encoding.ASCII.GetBytes("DPS1bad-cipher"),System.Text.Encoding.UTF8.GetBytes("null"),System.Text.Encoding.UTF8.GetBytes(json.Replace("\"Pending\":[", "\"Pending\":[null,")),futureJson,System.Text.Encoding.ASCII.GetBytes("DPS1").Concat(SecretProtection.Protect(futureJson)).ToArray()};
                foreach(byte[] bytes in invalid){
                    string root=Root();string history=await Seed(Path.Combine(root,"history"),file:ChatAttachment.CreateImage("sole.png",Png));string directory=Path.Combine(root,"conversations","unloaded","kernel-sessions");Directory.CreateDirectory(directory);string file=Path.Combine(directory,"unknown.session");File.WriteAllBytes(file,bytes);
                    var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>File.ReadAllBytes(p));var dirs=Directory.GetDirectories(root,"*",SearchOption.AllDirectories).OrderBy(p=>p).ToArray();
                    var result=await new HistoryMaintenanceService(root,new DataMaintenanceGate()).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Partial&&result.RemovedFiles==0,"unreadable unloaded context was cleared");Check(result.Failures.Any(f=>f.Path==file&&!String.IsNullOrEmpty(f.Reason)),"missing context diagnostic");
                    Check(Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(p=>p).SequenceEqual(before.Keys.OrderBy(p=>p))&&Directory.GetDirectories(root,"*",SearchOption.AllDirectories).OrderBy(p=>p).SequenceEqual(dirs),"failed preflight created archive/temp/migration files");foreach(var entry in before)Check(File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value),"failed preflight changed original bytes");Check(File.Exists(history),"history removed before context validation");
                    if(bytes[0]==1){
                        var gate=new DataMaintenanceGate();using(var workspace=ConversationWorkspace.Create(new ProjectDefinition{Id="project",Workspace=root},new ConversationDefinition{Id="unloaded",Name="loaded corruption",Mode="chat"},root,"pet",()=>new Api(),()=>new Speech(),()=>null,()=>new PetSettings(),()=>true,maintenance:gate)){
                            Check(workspace.Session.HistoryWarning.Length>0,"loaded corrupt kernel failed to expose warning");var loaded=await new HistoryMaintenanceService(root,gate).ClearAllAsync();Check(loaded.Status==HistoryMaintenanceStatus.Partial&&loaded.RemovedFiles==0&&File.ReadAllBytes(file).SequenceEqual(bytes)&&!Directory.Exists(loaded.AttachmentDirectory),"loaded and unloaded corruption retention differ");
                        }
                    }
                }
            });
            Run("strict session inspection reads supported encrypted and legacy formats without migration or writes",async()=>{
                var inspect=typeof(AgentSessionStore).GetMethod("InspectFileStrict",BindingFlags.NonPublic|BindingFlags.Static);Check(inspect!=null,"read-only strict session inspector missing");
                string root=Root();string current=Path.Combine(root,"conversations","current","kernel-sessions"),legacy=Path.Combine(root,"projects","legacy","chats","old","kernel-sessions");Directory.CreateDirectory(legacy);
                var model=new AgentSessionModel("session","original-project","chat");model.Enqueue(new AgentSessionInput{IdempotencyId="input",Text="legacy private context"});model.DeliverNext();model.StartStep();model.CompleteStep("reply");
                // Execute the real task lifecycle producers. This in-memory workspace
                // does not run platform-specific tools or touch any project files.
                using(var task=AgentWorkspace.ForContextBudget(32768)){task.AttachKernelSession(model);task.BeginTask(new AgentTaskState{Id="task",Workspace=task.Root});task.ContextProgress("compacting");task.RecordCompaction(10,"fixture summary");task.FinishTask("complete","done");}
                model.Record("nullable-step",null,null);new AgentSessionStore(current).Save(model);string old=Path.Combine(legacy,"old.session");File.WriteAllBytes(old,SessionBytes(model));
                var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>File.ReadAllBytes(p));var dirs=Directory.GetDirectories(root,"*",SearchOption.AllDirectories).OrderBy(p=>p).ToArray();
                foreach(var entry in before){var inspected=(AgentSessionModel)inspect.Invoke(null,new object[]{entry.Key});Check(inspected.SessionId=="session"&&inspected.ProjectId=="original-project"&&inspected.Delivered.Single().Text=="legacy private context"&&inspected.Events.Count==9,"inspection changed model identity/context");foreach(string kind in new[]{"task.started","context.progress","context.compacted","task.completed"})Check(inspected.Events.Single(e=>e.Type==kind).InputId==null,"real work event lost nullable input id");Check(inspected.Events.Last().InputId==null&&inspected.Events.Last().StepId==null,"nullable Record identifiers changed");Check(File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value),"inspection rewrote encrypted or legacy bytes");}
                Check(Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(p=>p).SequenceEqual(before.Keys.OrderBy(p=>p))&&Directory.GetDirectories(root,"*",SearchOption.AllDirectories).OrderBy(p=>p).SequenceEqual(dirs),"inspection created temp/migration files");var result=await new HistoryMaintenanceService(root,new DataMaintenanceGate()).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Complete&&result.RemovedFiles==2&&!Directory.GetFiles(root,"*.session",SearchOption.AllDirectories).Any(),"valid supported contexts not cleared");
            });
            Run("nullable event identifiers do not permit numeric object or null event corruption",async()=>{
                var model=new AgentSessionModel("session","project","chat");model.Record("task.started",null,"task");string json=System.Text.Encoding.UTF8.GetString(SessionBytes(model));
                var invalid=new[]{json.Replace("\"InputId\":null","\"InputId\":7"),json.Replace("\"InputId\":null","\"InputId\":{}"),json.Replace("\"StepId\":\"task\"","\"StepId\":7"),json.Replace("\"StepId\":\"task\"","\"StepId\":{}"),json.Replace("\"Events\":[","\"Events\":[null,"),json.Replace("\"Type\":\"task.started\"","\"Type\":null"),json.Replace("\"Type\":\"task.started\"","\"Type\":\"\"")};
                foreach(string invalidJson in invalid)foreach(bool encrypted in new[]{false,true}){
                    Check(invalidJson!=json,"invalid event fixture replacement failed");string root=Root(),directory=Path.Combine(root,"kernel-sessions");Directory.CreateDirectory(directory);string path=Path.Combine(directory,"invalid.session");byte[] plain=System.Text.Encoding.UTF8.GetBytes(invalidJson),bytes=encrypted?System.Text.Encoding.ASCII.GetBytes("DPS1").Concat(SecretProtection.Protect(plain)).ToArray():plain;File.WriteAllBytes(path,bytes);
                    var result=await new HistoryMaintenanceService(root,new DataMaintenanceGate()).ClearAllAsync();Check(result.Status==HistoryMaintenanceStatus.Partial&&result.RemovedFiles==0&&result.Failures.Any(f=>f.Path==path)&&File.ReadAllBytes(path).SequenceEqual(bytes),"invalid event was accepted or modified");Check(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Single()==path&&!Directory.Exists(result.AttachmentDirectory),"invalid event inspection created files");
                }
            });
            Console.WriteLine("HistoryMaintenanceTests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
        static byte[] SessionBytes(AgentSessionModel model){using(var stream=new MemoryStream()){new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AgentSessionModel)).WriteObject(stream,model);return stream.ToArray();}}
        sealed class Api:IConversationApi
        {public bool Fail;public Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage> m,CancellationToken t){if(Fail)throw new ApiException("fixture failure");return Task.FromResult("answer");}public Task<string> TranscribeAsync(ConnectionSettings c,byte[] b,CancellationToken t){return Task.FromResult("");}public Task<byte[]> SpeechAsync(ConnectionSettings c,string v,string s,CancellationToken t){return Task.FromResult(new byte[0]);}public void Dispose(){}}
        sealed class Speech:IReplySpeech
        {public Task SpeakAsync(string s,PetSettings c,CancellationToken t){return Task.FromResult(0);}public void SetVolume(int v){}public void Stop(){}public void Dispose(){}}
        sealed class Executor:IImageGenerationExecutor
        {public Task<GeneratedImage> GenerateAsync(ImageGenerationRequest r,IProgress<ImageGenerationPhase> p,CancellationToken t){throw new InvalidOperationException("Unexpected image execution");}}
    }
}
