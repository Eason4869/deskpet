using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    // Explicit opt-in only. Credentials must be Windows-protected and are never printed.
    public static class ImageGenerationLiveTests
    {
        [STAThread] public static int Main(string[] args)
        {
            if(args.Length==1&&args[0]=="--compile-only")return 0;
            if(args.Length!=1&&!(args.Length==2&&args[1]=="--edit")){Console.Error.WriteLine("Pass the path to an encrypted live-service configuration; optional --edit tests the saved first image.");return 2;}
            try{if(args.Length==2)Edit(args[0]).GetAwaiter().GetResult();else Run(args[0]).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error.GetType().Name+": "+(error is ApiException||error is InvalidOperationException?error.Message:"Live validation failed; check local report."));return 1;}
        }
        static async Task Edit(string path)
        {
            string directory=Path.GetDirectoryName(Path.GetFullPath(path));string data=Path.Combine(directory,"data");var cfg=new SettingsStore(Path.Combine(data,"settings.json")).Load();
            using(var store=new GeneratedImageStore(data))using(var client=new ImageGenerationClient(store))
            {
                var original=store.List().First();var provider=cfg.ImageProviders.First(p=>p.Id==original.ProviderId);Console.WriteLine("Editing existing image with "+provider.Connection.Model+"...");
                var edit=await client.GenerateAsync(new ImageGenerationRequest{Provider=provider,Prompt="Change the blue cup to a warm yellow ceramic cup. Preserve the simple white background and composition.",ReferenceImageBytes=store.Read(original.Id),ReferenceMediaType=original.MediaType,SessionId="live-edit",TaskId="manual-reference-test",Persistent=true},CancellationToken.None);
                if(String.IsNullOrEmpty(edit.ReferenceImageId)||store.Get(edit.ReferenceImageId)==null)throw new InvalidOperationException("Reference image was not retained.");File.WriteAllBytes(Path.Combine(directory,"edit-"+edit.Id+".png"),store.Read(edit.Id));File.WriteAllText(Path.Combine(directory,"edit-report.json"),new JavaScriptSerializer().Serialize(new{model=edit.Model,status="passed",assetId=edit.Id,width=edit.Width,height=edit.Height,bytes=edit.Bytes,referenceImageId=edit.ReferenceImageId}));Console.WriteLine("PASS actual multipart reference edit + private retained reference: "+edit.Width+"x"+edit.Height);
            }
            using(var restored=new GeneratedImageStore(data)){var edit=restored.List().First(i=>i.SessionId=="live-edit");if(restored.Read(edit.ReferenceImageId).Length==0)throw new InvalidOperationException("Reference missing after restart.");Console.WriteLine("PASS edited image and reference restore after restart");}
        }
        static async Task Run(string path)
        {
            string directory=Path.GetDirectoryName(Path.GetFullPath(path));var json=new JavaScriptSerializer();var service=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(path));string endpoint=(string)service["BaseUrl"];string encrypted=(string)service["EncryptedKey"];
            var models=((object[])service["Models"]).Cast<string>().ToArray();string data=Path.Combine(directory,"data");Directory.CreateDirectory(data);var cfg=new PetSettings{SaveChatHistory=true};cfg.ImageProviders=new List<ImageGenerationProvider>();
            foreach(string model in models)cfg.ImageProviders.Add(new ImageGenerationProvider{Name=model,Connection=new ConnectionSettings{BaseUrl=endpoint,Model=model,EncryptedKey=encrypted,UseProxy=false},SupportsEdits=true});
            cfg.ActiveImageProviderId=cfg.ImageProviders[0].Id;var settings=new SettingsStore(Path.Combine(data,"settings.json"));settings.Save(cfg);cfg=settings.Load();if(cfg.SelectedImageProvider()==null||cfg.SelectedImageProvider().Connection.ApiKey.Length==0)throw new InvalidOperationException("Encrypted service settings could not be restored.");
            var log=new ConversationLog();var reports=new List<object>();GeneratedImage first=null;
            using(var store=new GeneratedImageStore(data))using(var client=new ImageGenerationClient(store))
            {
                foreach(var provider in cfg.ImageProviders)
                {
                    ChatMessage user;var reply=log.BeginTurn("Generate a test image.","image",out user);var watch=System.Diagnostics.Stopwatch.StartNew();Console.WriteLine("Generating with "+provider.Connection.Model+"...");
                    try
                    {
                        var image=await client.GenerateAsync(new ImageGenerationRequest{Provider=provider,Prompt="A simple blue ceramic teacup on a white background, soft studio light, no text.",SessionId="live-validation",TaskId="manual-test",MessageId=reply.Id,Persistent=true},CancellationToken.None);
                        log.AttachGeneratedImage(reply.Id,image);log.Update(reply.Id,"图片已生成。","complete");var export=Path.Combine(directory,"export-"+image.Id+(image.MediaType=="image/jpeg"?".jpg":image.MediaType=="image/webp"?".webp":".png"));File.WriteAllBytes(export,store.Read(image.Id));
                        reports.Add(new{model=provider.Connection.Model,status="passed",assetId=image.Id,width=image.Width,height=image.Height,bytes=image.Bytes,seconds=(int)watch.Elapsed.TotalSeconds,export=export});if(first==null)first=image;Console.WriteLine("PASS actual generation + original export: "+provider.Connection.Model+" "+image.Width+"x"+image.Height);
                    }
                    catch(Exception error){log.Update(reply.Id,"生成失败。","error");reports.Add(new{model=provider.Connection.Model,status="failed",error=error is ApiException||error is InvalidOperationException?error.Message:error.GetType().Name});Console.WriteLine("FAIL actual generation: "+provider.Connection.Model+" / "+error.GetType().Name);}
                    File.WriteAllText(Path.Combine(directory,"report.json"),json.Serialize(reports));
                }
                var history=new HistoryStore(Path.Combine(data,"history"));await history.SaveAsync("live",log.Messages);if(history.Warning.Length>0)throw new InvalidOperationException("Live history could not be saved.");
            }
            using(var restored=new GeneratedImageStore(data))
            {
                var history=new HistoryStore(Path.Combine(data,"history"));var messages=history.Load("live");int restoredCount=0;foreach(var message in messages)foreach(var image in message.GeneratedImages??new List<GeneratedImage>()){if(restored.Read(image.Id).Length!=image.Bytes)throw new InvalidOperationException("Restored image bytes differ.");restoredCount++;}
                Console.WriteLine("PASS reopened settings + gallery + encrypted history: "+restoredCount+" images");if(restoredCount==0)throw new InvalidOperationException("No service generated a valid image.");
            }
        }
    }
}
