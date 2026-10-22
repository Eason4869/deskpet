using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.Tests
{
    public static class ImageGenerationBackendTests
    {
        // A real 1x1 RGBA PNG: mutations must fail validation, not merely identify its prefix.
        static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=");
        static int passed;
        static string directory;
        public static int Main()
        {
            directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"deskpet-image-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{Providers();Storage();Promotion();Protocol().GetAwaiter().GetResult();Console.WriteLine("ImageGenerationBackendTests: "+passed+" passed");return 0;}
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{Directory.Delete(directory,true);}
        }
        static void Check(bool condition,string name){if(!condition)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        static void Throws<T>(Action action,string name) where T:Exception {try{action();}catch(T){Check(true,name);return;}throw new Exception("Expected "+typeof(T).Name+": "+name);}
        static ImageGenerationProvider Provider(){return new ImageGenerationProvider{Name="Pictures",Connection=new ConnectionSettings{BaseUrl="https://images.example/v1",Model="custom-image",ApiKey="image-private-secret"}};}
        static void Providers()
        {
            var settings=new PetSettings();settings.Normalize();Check(settings.ImageProviders.Count==0 && settings.SelectedImageProvider()==null,"legacy configuration leaves image generation unconfigured");
            var first=Provider();var second=Provider();second.Connection.ApiKey="second-image-secret";second.Connection.UseProxy=true;second.Connection.ProxyAddress="http://127.0.0.1:17890";
            settings.ImageProviders.Add(first);settings.ImageProviders.Add(second);settings.ActiveImageProviderId=second.Id;settings.Text.ApiKey="text-secret";
            var store=new SettingsStore(Path.Combine(directory,"settings.json"));store.Save(settings);var loaded=store.Load();
            Check(loaded.ImageProviders.Count==2 && loaded.SelectedImageProvider().Connection.ApiKey=="second-image-secret" && loaded.SelectedImageProvider().TimeoutSeconds==300,"image providers and active selection survive settings reload");
            Check(loaded.Text.ApiKey=="text-secret" && loaded.ImageProviders[0].Connection.ApiKey=="image-private-secret" && loaded.ImageProviders[1].Connection.UseProxy,"image credentials and proxy remain independent of text");
            Check(!File.ReadAllText(Path.Combine(directory,"settings.json")).Contains("image-private-secret"),"image keys are encrypted in persisted settings");
            var snapshot=loaded.SelectedImageProvider();snapshot.Connection.Model="changed";var copy=loaded.Copy();copy.ImageProviders[0].Connection.Model="copy-change";
            Check(loaded.SelectedImageProvider().Connection.Model=="custom-image" && loaded.ImageProviders[0].Connection.Model=="custom-image","selection and settings copy isolate mutable connections");
            loaded.ImageProviders[0].Connection.EncryptedKey="broken-key";store.Save(loaded);store.Load();Check(store.Warning.Length>0,"unreadable image keys produce settings warning");
            File.WriteAllText(Path.Combine(directory,"legacy.json"),"{}");Check(new SettingsStore(Path.Combine(directory,"legacy.json")).Load().SelectedImageProvider()==null,"old JSON loads without enabling an image provider");
        }
        static void Storage()
        {
            string root=Path.Combine(directory,"store");string savedPath;string id;var metadata=new GeneratedImage{Prompt="private test prompt",SessionId="origin-session",TaskId="origin-task"};
            using(var store=new GeneratedImageStore(root)){
                int changes=0;store.Changed+=delegate{changes++;};var image=store.Save(Png,metadata,true);id=image.Id;savedPath=store.PathFor(id);
                Check(image.Width==1 && image.Height==1 && image.Bytes==Png.Length && image.MediaType=="image/png" && image.SessionId=="origin-session","store validates real format dimensions and preserves origin");
                Check(File.Exists(savedPath) && store.Read(id).Length==Png.Length && changes==1,"assets are committed before save reports completion");
                image.Prompt="mutated";Check(store.Get(id).Prompt=="private test prompt","metadata returned from store is an isolated snapshot");
                var temporary=store.Save(Png,new GeneratedImage{Prompt="session-only"},false);Check(temporary.Temporary,"history-disabled output is temporary");
                Throws<InvalidDataException>(delegate{store.Save(new byte[50*1024*1024+1],metadata,true);},"store enforces independent 50 MB limit");
                Throws<InvalidDataException>(delegate{store.Save(new byte[]{137,80,78,71,13,10,26,10},metadata,true);},"signature-only corrupt image rejected");
                var corrupt=(byte[])Png.Clone();corrupt[45]^=1;Throws<InvalidDataException>(delegate{store.Save(corrupt,metadata,true);},"PNG pixel mutation with damaged checksum is rejected");
                byte[] webp=Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");var validWebp=GeneratedImageValidation.Validate(webp);Check(validWebp.MediaType=="image/webp"&&validWebp.Width==1&&validWebp.Height==1,"valid WebP compressed frame is accepted");
                byte[] webpHeader=new byte[30];Array.Copy(webp,webpHeader,30);webpHeader[4]=22;webpHeader[16]=10;Throws<InvalidDataException>(delegate{store.Save(webpHeader,metadata,true);},"header-only WebP cannot become a completed asset");
                var badPartition=(byte[])webp.Clone();badPartition[20]=240;badPartition[21]=255;badPartition[22]=255;Throws<InvalidDataException>(delegate{GeneratedImageValidation.Validate(badPartition);},"WebP compressed partition cannot extend beyond frame payload");
                Throws<ArgumentException>(delegate{store.PathFor("../outside");},"asset IDs cannot escape storage directory");
            }
            using(var store=new GeneratedImageStore(root)){Check(store.List().Count==1 && store.Get(id)!=null && File.Exists(savedPath),"restart restores durable metadata and removes temporary output");}
            string index=Path.Combine(root,"generated-images","index.dat");Check(!Encoding.UTF8.GetString(File.ReadAllBytes(index)).Contains("private test prompt"),"asset index encrypts prompts and identifiers");
            File.WriteAllText(index,"broken-index");
            using(var store=new GeneratedImageStore(root)){
                Check(File.Exists(savedPath) && store.Warning.Length>0,"corrupt index preserves image bytes and exposes recovery warning");
                Check(store.Recover()>0 && store.Get(id)!=null && File.Exists(savedPath),"explicit recovery reconstructs preserved orphaned assets");
            }
            using(var store=new GeneratedImageStore(root)){Check(store.Get(id)!=null,"recovered index survives a second restart");}
            string referenceId;using(var store=new GeneratedImageStore(root)){var reference=store.SaveReference(Png,new GeneratedImage{Prompt="private edit reference"},true);referenceId=reference.Id;Check(store.List().Count==1 && store.Read(referenceId).Length==Png.Length,"saved reference can be read without appearing as generated gallery result");}
            using(var store=new GeneratedImageStore(root)){Check(store.Get(referenceId)!=null && store.Read(referenceId).Length==Png.Length && store.List().Count==1,"private edit reference survives restart without gallery entry");}
            using(var store=new GeneratedImageStore(Path.Combine(directory,"large-image"))){byte[] large=LargePng();var image=store.Save(large,new GeneratedImage(),true);Check(image.Bytes>4*1024*1024 && store.Read(image.Id).Length==large.Length,"generated image storage accepts images above chat attachment 4 MB ceiling");}
            string failedRoot=Path.Combine(directory,"failed-index");using(var store=new GeneratedImageStore(failedRoot)){
                string failedIndex=Path.Combine(failedRoot,"generated-images","index.dat");Directory.CreateDirectory(failedIndex);
                Throws<IOException>(delegate{store.Save(Png,new GeneratedImage{Prompt="not-complete"},true);},"index commit failure does not report completed asset");
                Check(store.List().Count==0 && Directory.GetFiles(Path.Combine(failedRoot,"generated-images"),"*.png").Length==1,"failed index commit retains orphan bytes without false gallery entry");
                Directory.Delete(failedIndex);Check(store.Recover()==1 && store.List().Count==1,"orphan bytes recover after index storage failure is resolved");
            }
        }
        static void Promotion()
        {
            string root=Path.Combine(directory,"promotion");string id,referenceId;
            using(var store=new GeneratedImageStore(root)){
                var reference=store.SaveReference(Png,new GeneratedImage{Prompt="original reference"},false);referenceId=reference.Id;
                var image=store.Save(Png,new GeneratedImage{Prompt="temporary result",ReferenceImageId=referenceId,SessionId="original-session"},false);id=image.Id;
                string previousImagePath=store.PathFor(id),previousReferencePath=store.PathFor(referenceId);int changes=0;store.Changed+=delegate{changes++;};
                var promoted=store.Promote(id);
                Check(promoted.Id==id && !promoted.Temporary && promoted.SessionId=="original-session" && promoted.ReferenceImageId==referenceId,"promotion preserves result identity ownership and reference ID");
                Check(!store.Get(referenceId).Temporary && store.Get(referenceId).IsReference && store.List().Count==1,"promotion atomically makes original private reference durable without gallery entry");
                Check(!File.Exists(previousImagePath) && !File.Exists(previousReferencePath) && store.Read(id).Length==Png.Length && store.Read(referenceId).Length==Png.Length,"temporary originals are removed only after promotion commits durable copies");
                Check(store.Promote(id).Id==id && changes==1,"repeated promotion is idempotent and emits one committed change");
            }
            using(var store=new GeneratedImageStore(root)){Check(store.Read(id).Length==Png.Length && store.Read(referenceId).Length==Png.Length && !store.Get(id).Temporary,"promoted result and private reference survive disposal and restart");}
            string failedRoot=Path.Combine(directory,"failed-promotion");string failedId,failedReferenceId;
            using(var store=new GeneratedImageStore(failedRoot)){
                var reference=store.SaveReference(Png,new GeneratedImage(),false);failedReferenceId=reference.Id;var image=store.Save(Png,new GeneratedImage{ReferenceImageId=reference.Id},false);failedId=image.Id;
                string imagePath=store.PathFor(image.Id),referencePath=store.PathFor(reference.Id);string failedIndex=Path.Combine(failedRoot,"generated-images","index.dat");Directory.CreateDirectory(failedIndex);
                Throws<IOException>(delegate{store.Promote(image.Id);},"failed promotion index commit cannot report durable completion");
                Check(store.Get(image.Id).Temporary && store.Get(reference.Id).Temporary && File.Exists(imagePath) && File.Exists(referencePath) && store.Read(image.Id).Length==Png.Length,"failed promotion preserves original temporary metadata and readable source bytes");
                Check(File.Exists(Path.Combine(failedRoot,"generated-images",image.Id+".png")) && File.Exists(Path.Combine(failedRoot,"generated-images","references",reference.Id+".png")),"failed promotion preserves both durable orphan copies for recovery");
                Directory.Delete(failedIndex);var retry=store.Promote(image.Id);Check(!retry.Temporary && retry.Id==image.Id && !store.Get(reference.Id).Temporary,"retry commits existing promotion copies with stable IDs after storage repair");
            }
            using(var store=new GeneratedImageStore(failedRoot)){Check(store.Get(failedId)!=null && store.Get(failedReferenceId)!=null && store.Read(failedId).Length==Png.Length,"retried promotion restores both stable asset references on restart");}
            string lockedSource;using(var store=new GeneratedImageStore(Path.Combine(directory,"promotion-cleanup"))){
                var image=store.Save(Png,new GeneratedImage(),false);lockedSource=store.PathFor(image.Id);
                using(var input=new FileStream(lockedSource,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    var promoted=store.Promote(image.Id);Check(!promoted.Temporary && promoted.Id==image.Id && store.Read(image.Id).SequenceEqual(Png),"open temporary source does not invalidate committed durable promotion");
                    // Windows denies deletion without FileShare.Delete; Unix can unlink an open file.
                    bool deletionBlocked=Environment.OSVersion.Platform==PlatformID.Win32NT;
                    Check(File.Exists(lockedSource)==deletionBlocked && !String.IsNullOrEmpty(store.Warning)==deletionBlocked && input.ReadByte()==Png[0],"promotion source cleanup follows native open-file deletion semantics");
                }
            }
            Check(!File.Exists(lockedSource),"dispose retries cleanup of temporary sources locked during promotion");
        }
        static async Task Protocol()
        {
            using(var store=new GeneratedImageStore(Path.Combine(directory,"protocol"))){
                string body="";string endpoint="";string authorization="";
                var handler=new Handler(async delegate(HttpRequestMessage request,CancellationToken token){endpoint=request.RequestUri.AbsolutePath;authorization=request.Headers.Authorization.ToString();body=await request.Content.ReadAsStringAsync();return Json("{\"data\":[{\"b64_json\":\""+Convert.ToBase64String(Png)+"\"}]}");});
                using(var client=new ImageGenerationClient(store,handler,new Handler(delegate{return Task.FromResult(Json("{}"));}))){
                    var result=await client.GenerateAsync(new ImageGenerationRequest{Provider=Provider(),Prompt="a moon",SessionId="session-one",TaskId="task-one",MessageId="message-one"},CancellationToken.None);
                    Check(endpoint=="/v1/images/generations" && authorization=="Bearer image-private-secret" && body.Contains("custom-image") && !body.Contains("response_format") && !body.Contains("quality") && !body.Contains("size"),"generation uses selected model and authentication while omitting service defaults");
                    Check(result.SessionId=="session-one" && result.MessageId=="message-one" && result.TaskId=="task-one" && store.Get(result.Id)!=null,"generation completes with committed asset and original ownership");
                    var editProvider=Provider();editProvider.SupportsEdits=true;
                    var edited=await client.GenerateAsync(new ImageGenerationRequest{Provider=editProvider,Prompt="blue moon",ReferenceImageBytes=Png,ReferenceMediaType="image/png",Size="1024x1024",Quality="high"},CancellationToken.None);
                    Check(endpoint=="/v1/images/edits" && body.Contains("image") && body.Contains("blue moon") && body.Contains("1024x1024") && body.Contains("high"),"reference images use multipart edits and explicit parameters");
                    Check(!String.IsNullOrEmpty(edited.ReferenceImageId) && store.Read(edited.ReferenceImageId).Length==Png.Length && store.List().Count==2,"successful edit retains its original reference privately for regeneration");
                    var temporaryEdit=await client.GenerateAsync(new ImageGenerationRequest{Provider=editProvider,Prompt="temporary edit",ReferenceImageBytes=Png,Persistent=false},CancellationToken.None);
                    var persistentEdit=await client.GenerateAsync(new ImageGenerationRequest{Provider=editProvider,Prompt="saved edit",ReferenceImageBytes=Png,ReferenceImageId=temporaryEdit.ReferenceImageId,Persistent=true},CancellationToken.None);
                    Check(!store.Get(persistentEdit.ReferenceImageId).Temporary,"durable regeneration promotes temporary reference instead of retaining expiring ID");
                    await Fails(client,new ImageGenerationRequest{Provider=Provider(),Prompt="unsupported",ReferenceImageBytes=Png},"参考图","unsupported edits rejected before sending");
                }
                foreach(var entry in new[]{new[]{"{\"data\":[{\"b64_json\":\"badbase64\"}]}","图片"},new[]{"{\"data\":[{\"url\":\"file:///etc/passwd\"}]}","地址"},new[]{"{\"data\":[{\"url\":\"http://127.0.0.1/private\"}]}","地址"},new[]{"{\"data\":[{\"url\":\"http://169.254.169.254/metadata\"}]}","地址"},new[]{"not-json","响应"}}){
                    using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json(entry[0]));}),null))await Fails(client,new ImageGenerationRequest{Provider=Provider(),Prompt="invalid"},entry[1],"reject malformed response or unsafe returned URL");
                }
                bool downloaded=false;
                using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json("{\"data\":[{\"url\":\"https://93.184.216.34/image.png\"}]}"));}),new Handler(delegate(HttpRequestMessage request,CancellationToken token){Check(request.Headers.Authorization==null && request.Headers.CookieAbsent(),"download excludes API authorization and cookies");downloaded=true;var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Png)};response.Content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");return Task.FromResult(response);}))){
                    var result=await client.GenerateAsync(new ImageGenerationRequest{Provider=Provider(),Prompt="remote"},CancellationToken.None);Check(downloaded && result.MediaType=="image/png","public URL output is downloaded and committed locally");
                }
                foreach(var status in new[]{HttpStatusCode.Forbidden,(HttpStatusCode)429}){
                    int calls=0;using(var client=new ImageGenerationClient(store,new Handler(delegate{calls++;return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent("image-private-secret upstream error")});}),null)){
                        await Fails(client,new ImageGenerationRequest{Provider=Provider(),Prompt="error"},status==HttpStatusCode.Forbidden?"鉴权":"限流","API errors are readable and redact server secrets");Check(calls==1,"generation failures are never retried");
                    }
                }
                using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json("{\"data\":[{\"url\":\"https://93.184.216.34/image.png\"}]}"));}),new Handler(delegate{var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri("http://127.0.0.1/private");return Task.FromResult(response);}))){await Fails(client,new ImageGenerationRequest{Provider=Provider(),Prompt="redirect"},"重定向","image redirects are rejected before accessing target");}
                foreach(string mime in new[]{"text/html","image/jpeg"})using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json("{\"data\":[{\"url\":\"https://93.184.216.34/image.png\"}]}"));}),new Handler(delegate{var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Png)};response.Content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue(mime);return Task.FromResult(response);})))await Fails(client,new ImageGenerationRequest{Provider=Provider(),Prompt="wrong mime"},"类型","download requires allowed MIME matching actual file signature");
                using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json("{\"data\":[{\"url\":\"https://93.184.216.34/image.png\"}]}"));}),new Handler(delegate{var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Png)};response.Content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");response.Content.Headers.ContentLength=50*1024*1024+1;return Task.FromResult(response);})))await Fails(client,new ImageGenerationRequest{Provider=Provider(),Prompt="oversized"},"大小","oversized image download rejected before storage");
                var local=Provider();local.Connection.BaseUrl="http://192.168.1.3:18432/v1";
                using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json("{\"data\":[{\"url\":\"http://192.168.1.3:18432/output/image.png\"}]}"));}),new Handler(delegate(HttpRequestMessage request,CancellationToken token){Check(request.Headers.Authorization==null,"explicit local service output is downloaded without key");var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Png)};response.Content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");return Task.FromResult(response);}))){Check((await client.GenerateAsync(new ImageGenerationRequest{Provider=local,Prompt="local service"},CancellationToken.None)).Width==1,"configured local service exact origin may return local image URL");}
                using(var client=new ImageGenerationClient(store,new Handler(delegate{return Task.FromResult(Json("{\"data\":[{\"url\":\"http://192.168.1.3:18433/private\"}]}"));}),null))await Fails(client,new ImageGenerationRequest{Provider=local,Prompt="wrong port"},"地址","local origin exception excludes different ports");
                var timeoutProvider=Provider();timeoutProvider.TimeoutSeconds=1;
                using(var client=new ImageGenerationClient(store,new Handler(async delegate(HttpRequestMessage request,CancellationToken token){await Task.Delay(10000,token);return Json("{}");}),null))await Fails(client,new ImageGenerationRequest{Provider=timeoutProvider,Prompt="timeout"},"超时","provider deadline produces readable timeout instead of hanging");
                using(var cancel=new CancellationTokenSource())using(var client=new ImageGenerationClient(store,new Handler(async delegate(HttpRequestMessage request,CancellationToken token){await Task.Delay(10000,token);return Json("{}");}),null)){
                    cancel.CancelAfter(30);bool canceled=false;try{await client.GenerateAsync(new ImageGenerationRequest{Provider=Provider(),Prompt="cancel"},cancel.Token);}catch(OperationCanceledException){canceled=true;}Check(canceled,"user cancellation stops in-flight request");
                }
            }
        }
        static async Task Fails(ImageGenerationClient client,ImageGenerationRequest request,string fragment,string name){try{await client.GenerateAsync(request,CancellationToken.None);}catch(Exception e){Check(e.Message.Contains(fragment) && !e.ToString().Contains("image-private-secret"),name);return;}throw new Exception("Expected failure: "+name);}
        static HttpResponseMessage Json(string json){return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};}
        static byte[] LargePng()
        {
            // Add a valid private ancillary chunk; pixels remain the independent 1x1 fixture.
            byte[] payload=new byte[4*1024*1024+64];using(var stream=new MemoryStream()){
                stream.Write(Png,0,33);WriteBig(stream,payload.Length);byte[] type=Encoding.ASCII.GetBytes("npAD");stream.Write(type,0,4);stream.Write(payload,0,payload.Length);
                uint crc=0xffffffff;foreach(byte value in type)crc=UpdateCrc(crc,value);foreach(byte value in payload)crc=UpdateCrc(crc,value);WriteBig(stream,unchecked((int)(crc^0xffffffff)));stream.Write(Png,33,Png.Length-33);return stream.ToArray();}
        }
        static uint UpdateCrc(uint crc,byte value){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?0xedb88320^(crc>>1):crc>>1;return crc;}
        static void WriteBig(Stream stream,int value){stream.WriteByte((byte)(value>>24));stream.WriteByte((byte)(value>>16));stream.WriteByte((byte)(value>>8));stream.WriteByte((byte)value);}
        sealed class Handler:HttpMessageHandler {readonly Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send;public Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send){this.send=send;}protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){return send(request,token);}}
        static bool CookieAbsent(this System.Net.Http.Headers.HttpRequestHeaders headers){return !headers.Contains("Cookie");}
    }
}
