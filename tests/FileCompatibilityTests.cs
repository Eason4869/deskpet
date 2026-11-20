using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Dafeiyu.Tests
{
    public static class FileCompatibilityTests
    {
        static void Require(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
        }

        public static int Main()
        {
            FileCompatibilityDependencies.Ensure();
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deskpet-file-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string source = Path.Combine(directory, "原始记录.txt");
                string data = Path.Combine(directory, "data");
                File.WriteAllText(source, "first\nsecond", new UTF8Encoding(false));
                var store = new AttachmentFileStore(data);
                var reference = store.Import(source, ".txt", 1024, true, CancellationToken.None);
                File.WriteAllText(source, "changed", new UTF8Encoding(false));
                using (var read = store.Open(reference.Id))
                using (var document = new StreamReader(read.Stream, Encoding.UTF8))
                    Require(document.ReadToEnd() == "first\nsecond", "The snapshot must retain the imported bytes.");
                Require(reference.Name == "原始记录.txt" && reference.Size == 12, "The reference retains text data file name and size.");
                Require(!new AttachmentFileStore(Path.Combine(directory, "other")).Exists(reference.Id), "References are only valid for their data root.");
                Require(!store.Exists("p-../../record.txt"), "A persisted reference cannot traverse directories.");
                string owner=Guid.NewGuid().ToString("N");
                var scoped=store.Import(source,".txt",1024,false,CancellationToken.None,ownerId:owner);
                using(var pin=store.Open(scoped))
                {
                    Require(pin.Path.Contains(owner) && scoped.OwnerId==owner,"A snapshot follows its conversation data directory.");
                    store.Discard(scoped);
                    Require(File.Exists(pin.Path),"A deletion cannot invalidate an active read lease.");
                }
                try{store.Open(scoped).Dispose();throw new Exception("Released snapshot was not removed.");}
                catch(FileNotFoundException){ }
                try
                {
                    store.Import(source, ".txt", 2, false, CancellationToken.None);
                    throw new Exception("Input byte limit did not reject the file.");
                }
                catch (IOException) { }
                try
                {
                    store.Import(source, ".txt", 1024, false, new CancellationToken(true));
                    throw new Exception("Canceled import succeeded.");
                }
                catch (OperationCanceledException) { }
                Require(Directory.GetFiles(Path.Combine(data, AttachmentFileStore.FolderName)).Length == 1, "Unsuccessful imports must leave no file.");
                File.WriteAllText(source, "source\nsecond", new UTF8Encoding(false));
                var attachment = FileContentService.Import(source, data, true, CancellationToken.None);
                Require(attachment.ImportedFile != null && attachment.Text.Contains("second"), "Non-image attachment import reads a bounded snapshot.");
                var range = FileContentService.Read(attachment.ImportedFile, data,
                    new FileReadRequest { Start=2, Count=1 }, CancellationToken.None);
                Require(range.Text.Contains("second") && !range.Text.Contains("source"), "Selected range must exclude unread lines.");
                var message = new ChatMessage("user", "read the attachment");
                message.SetAttachments(new[] { attachment });
                var wire = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(ChatAttachment.Content(message, false));
                Require(wire.Contains(attachment.ImportedFile.Id) && !wire.Contains(directory), "The model sees a reference and no local path.");
                string exported=Path.Combine(directory,"export.txt");
                FileContentService.Export(attachment.ImportedFile,data,exported);
                Require(File.ReadAllText(exported)=="source\nsecond","Export retains original bytes.");
                var archive=new Dafeiyu.Maintenance.AttachmentArchive(Path.Combine(data,"preserved-attachments"));
                archive.Preserve(new[]{attachment});
                archive.ReleaseImported();
                Require(!store.Exists(attachment.ImportedFile.Id),"History cleanup releases the duplicate live snapshot.");
                Require(FileContentService.Read(attachment.ImportedFile,data,new FileReadRequest(),CancellationToken.None).Text.Contains("source"),"Preserved original is still readable after live snapshot release.");
                TabularCompatibilityChecks.Run(directory);
                DocumentCompatibilityChecks.Run(directory);
                ContainerCompatibilityChecks.Run(directory);
                MediaCompatibilityChecks.Run(directory);
                MediaHeaderCompatibilityChecks.Run(directory);
#if !NET10_0
                string language=Environment.GetEnvironmentVariable("DESKPET_ATTACHMENT_OCR_TEST_LANGUAGE");
                if(!String.IsNullOrWhiteSpace(language))WindowsFileOcrChecks.Run(directory,language);
#endif
                Console.WriteLine("File compatibility: attachment snapshot and reader checks passed.");
                return 0;
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
