using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public sealed class ImageExportItem
    {
        public string Id { get; internal set; }
        public string Path { get; internal set; }
        public string Error { get; internal set; }
    }
    public sealed class ImageExportResult
    {
        public IList<ImageExportItem> Items { get; internal set; }
        public bool Canceled { get; internal set; }
        public int Completed { get { return Items.Count(i => i.Path != null); } }
        public int Failed { get { return Items.Count(i => i.Error != null); } }
    }
    public sealed class ImageGalleryExport
    {
        readonly GeneratedImageStore store;
        readonly ImageAssetUsageRegistry usage;
        public ImageGalleryExport(GeneratedImageStore store, ImageAssetUsageRegistry usage) { this.store = store; this.usage = usage; }
        public Task<ImageExportResult> ExportAsync(IEnumerable<string> ids, string destination, IProgress<ImageExportItem> progress, CancellationToken token)
        {
            if (ids == null) throw new ArgumentNullException("ids");
            string folder = Path.GetFullPath(destination);
            if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("导出目录无效。");
            string[] selected = ids.Distinct(StringComparer.Ordinal).ToArray();
            return Task.Run(() => Export(selected, folder, progress, token));
        }
        ImageExportResult Export(string[] ids, string folder, IProgress<ImageExportItem> progress, CancellationToken token)
        {
            var rows = new List<ImageExportItem>();
            var summary = new ImageExportResult { Items = rows.AsReadOnly() };
            foreach (string id in ids)
            {
                if (token.IsCancellationRequested) { summary.Canceled = true; break; }
                var item = new ImageExportItem { Id = id }; string temporary = null;
                try
                {
                    using (usage.Acquire(id, "gallery-export"))
                    {
                        var metadata = store.Get(id);
                        if (metadata == null || metadata.IsReference) throw new FileNotFoundException("图片不存在。");
                        byte[] original = store.Read(id);
                        string suffix = metadata.MediaType == "image/jpeg" ? ".jpg" : metadata.MediaType == "image/webp" ? ".webp" : metadata.MediaType == "image/gif" ? ".gif" : ".png";
                        temporary = Path.Combine(folder, ".deskpet-" + Guid.NewGuid().ToString("N") + ".tmp");
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(original, 0, original.Length); output.Flush(true); }
                        for (int number = 0; number < 10000; number++)
                        {
                            token.ThrowIfCancellationRequested();
                            string candidate = Path.Combine(folder, "generated-" + metadata.Id + (number == 0 ? "" : " (" + number + ")") + suffix);
                            try { File.Move(temporary, candidate); temporary = null; item.Path = candidate; break; }
                            catch (IOException) { if (!File.Exists(candidate)) throw; }
                        }
                        if (item.Path == null) throw new IOException("导出文件名已用尽。");
                    }
                }
                catch (OperationCanceledException) { summary.Canceled = true; break; }
                catch (Exception error) { item.Error = error.Message; }
                finally { if (temporary != null && File.Exists(temporary)) try { File.Delete(temporary); } catch (IOException error) { item.Error = "临时文件清理失败：" + error.Message; } catch (UnauthorizedAccessException error) { item.Error = "临时文件清理失败：" + error.Message; } }
                rows.Add(item); if (progress != null) progress.Report(item);
            }
            return summary;
        }
    }
}
