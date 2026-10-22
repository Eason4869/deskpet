using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Dafeiyu.Tests
{
    public static class ImageGenerationTaskStoreTests
    {
        static int passed;
        public static int Main()
        {
            try { passed=0;OldIndexAndHistoryLoadWithoutNewMembers();TaskRecordsRoundTripWithoutSecrets();CorruptTaskFilePreservesOriginal();TaskStoreLimitRejectsBeforeOverwrite();Console.WriteLine("ImageGenerationTaskStoreTests: "+passed+" passed");return 0; }
            catch(Exception e){Console.WriteLine("FAIL ImageGenerationTaskStoreTests: "+e);return 1;}
        }
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;}
        static T Read<T>(string json){using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json)))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}
        static string Folder(){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"deskpet-image-task-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
        static string FilePath(string folder){return Path.Combine(folder,"generated-images","tasks.dat");}
        static ImageGenerationTaskRecord Record(string id){return new ImageGenerationTaskRecord{Id=id,RequestId="request-"+id,Origin=new ImageTaskOrigin{Source=ImageGenerationSource.Conversation,ConversationId="original-chat",MessageId="message",GroupId="group",Sequence=2},State=ImageGenerationTaskState.Queued,QueuedUtc=DateTime.UtcNow,ProviderId="service",ProviderName="Image service",Model="model",Prompt="private prompt",ReferenceImageIds=new List<string>{"reference"},DeliveryState=ImageTaskDeliveryState.Pending};}
        static void OldIndexAndHistoryLoadWithoutNewMembers()
        {
            var image=Read<GeneratedImage>("{\"Id\":\"old-image\"}");
            Check(image.Source==ImageGenerationSource.Unknown && image.ImageJobId==null && !image.IsFavorite && !image.TrashedUtc.HasValue,"old image defaults");
            var message=Read<ChatMessage>("{\"Id\":\"old-message\",\"Role\":\"assistant\",\"Content\":\"old\"}");
            Check(message.Id=="old-message" && message.ImageGenerationTaskIds==null && message.Copy().Id==message.Id,"old message retains id");
            message.ImageGenerationTaskIds=new List<string>{"job"};var copy=message.Copy();copy.ImageGenerationTaskIds.Add("other");
            Check(message.ImageGenerationTaskIds.Count==1,"message task ids copied");
            var imageCopy=image.Copy();imageCopy.IsFavorite=true;Check(!image.IsFavorite,"asset copied");
            string folder=Folder();try{
                string images=Path.Combine(folder,"generated-images");Directory.CreateDirectory(images);string index=Path.Combine(images,"index.dat");
                byte[] legacy=SecretProtection.Protect(Encoding.UTF8.GetBytes("{\"Images\":[{\"Id\":\"0123456789abcdef0123456789abcdef\",\"MediaType\":\"image/png\",\"Width\":1,\"Height\":1,\"Bytes\":1}]}"));File.WriteAllBytes(index,legacy);
                using(var store=new GeneratedImageStore(folder)){Check(store.List().Count==1 && store.List()[0].Source==ImageGenerationSource.Unknown,"legacy encrypted index loads");}
                Check(File.ReadAllBytes(index).SequenceEqual(legacy),"legacy index not rewritten");
                string history=Path.Combine(folder,"history");var historyStore=new HistoryStore(history);historyStore.SaveAsync("pet",new[]{new ChatMessage("user","legacy")}).GetAwaiter().GetResult();
                string historyFile=Directory.GetFiles(history,"*.history")[0];byte[] oldHistory=File.ReadAllBytes(historyFile);var loaded=historyStore.Load("pet");
                Check(loaded.Count==1 && loaded[0].ImageGenerationTaskIds==null && File.ReadAllBytes(historyFile).SequenceEqual(oldHistory),"legacy encrypted history loads without rewriting");
                loaded[0].ImageGenerationTaskIds=new List<string>{"retained-job"};historyStore.SaveAsync("pet",loaded).GetAwaiter().GetResult();Check(historyStore.Load("pet")[0].ImageGenerationTaskIds[0]=="retained-job","history round trips task ids");
            }finally{Directory.Delete(folder,true);}
        }
        static void TaskRecordsRoundTripWithoutSecrets()
        {
            string folder=Folder();try{
                var store=new ImageGenerationTaskStore(folder);Check(store.DirectoryPath==Path.Combine(Path.GetFullPath(folder),"generated-images"),"task directory accessor");var record=Record("job");store.Save(new[]{record});
                Check(!Encoding.UTF8.GetString(File.ReadAllBytes(FilePath(folder))).Contains("private prompt"),"encrypted at rest");
                string plain=Encoding.UTF8.GetString(SecretProtection.Unprotect(File.ReadAllBytes(FilePath(folder))));
                Check(!plain.Contains("Connection") && !plain.Contains("ApiKey") && !plain.Contains("Base64") && !plain.Contains("ReferenceImageBytes") && !plain.Contains("ReferenceImagePath"),"record schema excludes secrets and raw references");
                record.Origin.MessageId="mutated";record.ReferenceImageIds.Clear();var loaded=store.Load();
                Check(loaded[0].Id=="job" && loaded[0].Origin.MessageId=="message" && loaded[0].ReferenceImageIds.Count==1,"save isolation and origin id round trip");
                loaded[0].Origin.MessageId="mutated";loaded[0].ReferenceImageIds.Clear();Check(store.Load()[0].Origin.MessageId=="message","load isolation");
                var snapshot=new ImageGenerationTaskSnapshot(store.Load()[0]);var snapshotCopy=snapshot.Copy();snapshotCopy.Origin.MessageId="x";snapshotCopy.ReferenceImageIds.Clear();Check(snapshot.Origin.MessageId=="message" && snapshot.ReferenceImageIds.Count==1,"snapshot isolation");
                store.Save(new ImageGenerationTaskRecord[]{snapshot});Check(store.Load()[0].Id=="job","snapshots persist through record schema");
                var request=new ImageGenerationRequest{Provider=new ImageGenerationProvider(),ReferenceImageBytes=new byte[]{1},Prompt="frozen"};
                var submission=new ImageGenerationTaskSubmission("request",request,new ImageTaskOrigin{MessageId="original"});request.ReferenceImageBytes[0]=9;request.Provider.Name="changed";
                Check(submission.Request.ReferenceImageBytes[0]==1 && submission.Request.Provider.Name!="changed","submission freezes request");
                var requestCopy=submission.Request;requestCopy.ReferenceImageBytes[0]=7;Check(submission.Request.ReferenceImageBytes[0]==1,"submission access is isolated");
                var retry=Record("retry");retry.RetryOfTaskId="job";store.Save(new[]{retry});Check(store.Load()[0].RetryOfTaskId=="job" && store.Load()[0].Persistent,"retry identity and persistence default");
                var temporary=Record("temporary");temporary.Persistent=false;store.Save(new[]{retry,temporary});Check(store.Load().Count==1,"temporary tasks never reach disk");
            }finally{Directory.Delete(folder,true);}
        }
        static void CorruptTaskFilePreservesOriginal()
        {
            string folder=Folder();try{
                var store=new ImageGenerationTaskStore(folder);store.Save(new[]{Record("job")});byte[] corrupt={1,2,3};File.WriteAllBytes(FilePath(folder),corrupt);
                Check(store.Load().Count==0 && store.Warning.Length>0,"corruption warns");
                bool refused=false;try{store.Save(new[]{Record("replacement")});}catch(InvalidDataException){refused=true;}
                Check(refused && File.ReadAllBytes(FilePath(folder)).SequenceEqual(corrupt),"corruption blocks overwrite and preserves original");
            }finally{Directory.Delete(folder,true);}
        }
        static void TaskStoreLimitRejectsBeforeOverwrite()
        {
            string folder=Folder();try{
                var store=new ImageGenerationTaskStore(folder);store.Save(new[]{Record("baseline")});byte[] before=File.ReadAllBytes(FilePath(folder));
                var oversized=Record("oversized");oversized.Prompt=new string('x',ImageGenerationTaskStore.MaximumBytes+1);
                bool refused=false;try{store.Save(new[]{oversized});}catch(InvalidDataException){refused=true;}
                Check(refused && File.ReadAllBytes(FilePath(folder)).SequenceEqual(before),"oversized record rejected before overwrite");
                var tooLarge=new List<ImageGenerationTaskRecord>();for(int i=0;i<4300;i++){var item=Record("large-"+i);item.Prompt=new string('p',16000);tooLarge.Add(item);}
                refused=false;try{store.Save(tooLarge);}catch(InvalidDataException){refused=true;}
                Check(refused && File.ReadAllBytes(FilePath(folder)).SequenceEqual(before),"64 MB serialized file rejected before overwrite");
                var items=new List<ImageGenerationTaskRecord>();for(int i=0;i<2002;i++){var item=Record("done-"+i);item.State=ImageGenerationTaskState.Succeeded;item.CompletedUtc=DateTime.UtcNow.AddSeconds(i);items.Add(item);}items.Add(Record("pending"));
                store.Save(items);var loaded=store.Load();Check(loaded.Count==2001 && loaded.Any(i=>i.Id=="pending") && !loaded.Any(i=>i.Id=="done-0") && loaded.Any(i=>i.Id=="done-2001"),"retain newest 2000 terminal tasks and unfinished tasks");
                byte[] persisted=File.ReadAllBytes(FilePath(folder));store.Load();Check(File.ReadAllBytes(FilePath(folder)).SequenceEqual(persisted),"load does not rewrite records");
            }finally{Directory.Delete(folder,true);}
        }
    }
}
