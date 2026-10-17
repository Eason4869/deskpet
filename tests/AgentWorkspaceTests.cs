using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Dafeiyu.Tests
{
    public static class AgentWorkspaceTests
    {
        private static int assertions;
        private static string Call(AgentWorkspace workspace, string name, object args)
        {
            return workspace.ExecuteAsync(name, new JavaScriptSerializer { MaxJsonLength = 3000000 }.Serialize(args), CancellationToken.None).GetAwaiter().GetResult();
        }
        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }
        private static bool Error(string result) { return result.StartsWith("错误：", StringComparison.Ordinal); }

        public static int Main()
        {
            string scratch = Path.Combine(Directory.GetCurrentDirectory(), "work", "agent-tools-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(scratch, "project"), outside = Path.Combine(scratch, "outside");
            string junction = Path.Combine(root, "linked");
            try
            {
                Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
                File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside-secret", new UTF8Encoding(false));
                var workspace = new AgentWorkspace(root);
                Check(workspace.Root == root, "Root is explicit");
                Check(workspace.OpenAITools.Length >= 7 && workspace.AnthropicTools.Length >= 7, "native schemas include directory tools");
                Check(workspace.RestrictToRoot && workspace.BackupEnabled && workspace.ContextTokenLimit == 32768, "strict legacy constructor and execution defaults");
                workspace.ReadOnly=true;
                Check(workspace.OpenAITools.Length>=3 && !new JavaScriptSerializer().Serialize(workspace.OpenAITools).Contains("write_file"),"planning schemas omit write and edit tools");
                Check(Error(Call(workspace,"write_file",new {path="blocked.txt",content="no"})) && !File.Exists(Path.Combine(root,"blocked.txt")),"planning mode refuses writes even if model invents call");
                workspace.ReadOnly=false;
                string schemas = new JavaScriptSerializer().Serialize(workspace.OpenAITools);
                Check(schemas.Contains("\"function\"") && schemas.Contains("old_text"), "OpenAI schema shape");
                Check(new JavaScriptSerializer().Serialize(workspace.AnthropicTools).Contains("input_schema"), "Anthropic schema shape");
                Check(!Error(Call(workspace, "write_file", new { path = "nested/hello.txt", content = "第一行\nsecond\nthird" })), "write nested UTF8");
                Check(File.ReadAllText(Path.Combine(root, "nested/hello.txt")) == "第一行\nsecond\nthird", "real file content");
                Check(Call(workspace, "list_files", new { }).Contains("nested"), "list default path");
                string read = Call(workspace, "read_file", new { path = "nested/hello.txt", offset = 1, limit = 1 });
                Check(read.Contains("2: second") && !read.Contains("第三行") && !read.Contains("3: third"), "read offset and limit");
                Check(!Error(Call(workspace, "edit_file", new { path = "nested/hello.txt", old_text = "second", new_text = "changed" })), "edit unique text");
                Check(File.ReadAllText(Path.Combine(root, "nested/hello.txt")).Contains("changed"), "edit written");
                string[] backups = Directory.GetFiles(Path.Combine(root, ".deskpet-agent-backups"));
                Check(backups.Length == 1 && File.ReadAllText(backups[0]).Contains("second"), "backup preserves old bytes");
                Check(Call(workspace, "search_files", new { query = "CHANGED" }).Contains("nested/hello.txt:2"), "recursive case insensitive search");
                Check(Error(Call(workspace, "edit_file", new { path = "nested/hello.txt", old_text = "absent", new_text = "x" })), "missing edit refused");
                File.WriteAllText(Path.Combine(root, "twice.txt"), "same same");
                Check(Error(Call(workspace, "edit_file", new { path = "twice.txt", old_text = "same", new_text = "x" })), "ambiguous edit refused");
                Check(File.ReadAllText(Path.Combine(root, "twice.txt")) == "same same", "ambiguous edit unchanged");
                Check(Error(Call(workspace, "read_file", new { path = "../outside/secret.txt" })), "dotdot escape refused");
                Check(Error(Call(workspace, "read_file", new { path = Path.Combine(outside, "secret.txt") })), "external absolute refused");
                Check(Error(Call(workspace, "write_file", new { path = "../outside/new.txt", content = "bad" })), "external write refused");
                Check(!File.Exists(Path.Combine(outside, "new.txt")), "external file never created");
                Check(Error(Call(workspace, "read_file", new { path = "nested/hello.txt:secret" })), "ADS refused");
                Check(Error(Call(workspace, "read_file", new { path = "\\\\localhost\\c$\\secret.txt" })), "UNC refused");
                Check(Error(Call(workspace, "read_file", new { path = "\\\\?\\" + root + "\\twice.txt" })), "device path refused");
                Check(Error(Call(workspace, "write_file", new { path = "CON", content = "bad" })), "DOS device refused");
                Check(Error(Call(workspace, "write_file", new { path = ".git/config", content = "bad" })), ".git refused");
                Check(Error(Call(workspace, "read_file", new { path = ".deskpet-agent-backups/" + Path.GetFileName(backups[0]) })), "backup internal access refused");
                Check(!Call(workspace, "list_files", new { }).Contains(".deskpet-agent-backups"), "backups hidden");
                File.WriteAllBytes(Path.Combine(root, "invalid.txt"), new byte[] { 0xc3, 0x28 });
                Check(Call(workspace, "read_file", new { path = "invalid.txt" }).Contains("UTF-8"), "invalid UTF8 explicit");
                Check(Error(Call(workspace, "write_file", new { path = "invalid.txt", content = "overwrite" })), "binary overwrite refused");
                File.WriteAllBytes(Path.Combine(root, "large.txt"), new byte[1024 * 1024 + 1]);
                Check(Error(Call(workspace, "read_file", new { path = "large.txt" })), "read size bound");
                Check(Error(Call(workspace, "write_file", new { path = "too-large.txt", content = new string('x', 1024 * 1024 + 1) })), "write size bound");
                Check(!File.Exists(Path.Combine(root, "too-large.txt")), "size rejected before mutation");
                Check(Error(Call(workspace, "read_file", new { path = "twice.txt", limit = 1001 })), "line limit bound");
                Check(Error(Call(workspace, "read_file", new { path = "twice.txt", offset = -1 })), "negative offset refused");
                Check(Error(workspace.ExecuteAsync("read_file", "{bad", CancellationToken.None).GetAwaiter().GetResult()), "invalid JSON safe result");
                string hardlink = Path.Combine(root, "hardlink.txt");
                Check(CreateHardLink(hardlink, Path.Combine(outside, "secret.txt"), IntPtr.Zero), "real hardlink fixture");
                Check(Error(Call(workspace, "read_file", new { path = "hardlink.txt" })), "hardlinked external content refused");
                Check(Error(Call(workspace, "write_file", new { path = "hardlink.txt", content = "bad" })), "hardlink overwrite refused");
                Check(File.ReadAllText(Path.Combine(outside, "secret.txt")) == "outside-secret", "hardlink target unchanged");
                File.WriteAllText(Path.Combine(root, "long-lines.txt"), new string('x', 50000));
                Check(Call(workspace, "read_file", new { path = "long-lines.txt" }).Length <= 24000, "result bound");
                var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                bool didCancel = false;
                try { workspace.ExecuteAsync("write_file", "{\"path\":\"cancelled.txt\",\"content\":\"bad\"}", cancelled.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { didCancel = true; }
                Check(didCancel && !File.Exists(Path.Combine(root, "cancelled.txt")), "cancellation prevents mutation");
                CreateJunction(junction, outside);
                Check((File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0, "real junction fixture");
                Check(Error(Call(workspace, "read_file", new { path = "linked/secret.txt" })), "junction read refused");
                Check(Error(Call(workspace, "write_file", new { path = "linked/secret.txt", content = "bad" })), "junction write refused");
                Check(File.ReadAllText(Path.Combine(outside, "secret.txt")) == "outside-secret", "junction target unchanged");
                Check(!Call(workspace, "search_files", new { query = "outside-secret" }).Contains("secret.txt"), "search never traverses junction");
                bool badRoot = false;
                try { new AgentWorkspace(junction); } catch (UnauthorizedAccessException) { badRoot = true; }
                Check(badRoot, "junction root refused");
                Directory.CreateDirectory(Path.Combine(outside, "child"));
                badRoot = false;
                try { new AgentWorkspace(Path.Combine(junction, "child")); } catch (UnauthorizedAccessException) { badRoot = true; }
                Check(badRoot, "junction ancestor refused");
                bool volumeRoot = false;
                try { new AgentWorkspace(Path.GetPathRoot(root)); } catch (ArgumentException) { volumeRoot = true; }
                Check(volumeRoot, "volume root refused");
                string crowded = Path.Combine(root, "crowded"); Directory.CreateDirectory(crowded);
                for (int i = 0; i < 210; i++) File.WriteAllText(Path.Combine(crowded, "f" + i + ".txt"), "match");
                Check(Call(workspace, "list_files", new { path = "crowded" }).Contains("已截断"), "directory enumeration capped");
                Check(Call(workspace, "search_files", new { path = "crowded", query = "match" }).Contains("已截断"), "search match limit capped");
                string bom = Path.Combine(root, "bom.txt");
                File.WriteAllText(bom, "before", new UTF8Encoding(true));
                byte[] bomBefore = File.ReadAllBytes(bom);
                Check(!Error(Call(workspace, "write_file", new { path = "bom.txt", content = "after" })), "overwrite BOM file");
                bool bomBackupFound = false;
                foreach (string backup in Directory.GetFiles(Path.Combine(root, ".deskpet-agent-backups")))
                {
                    byte[] data = File.ReadAllBytes(backup);
                    if (Convert.ToBase64String(data) == Convert.ToBase64String(bomBefore)) bomBackupFound = true;
                }
                Check(bomBackupFound, "backup preserves UTF8 BOM bytes");
                Check(!Error(Call(workspace, "create_directory", new { path = "strict/new-folder" })) && Directory.Exists(Path.Combine(root, "strict/new-folder")), "strict directory creation within root");
                string opened = null;
                workspace.DirectoryOpener = delegate(string path) { opened = path; };
                Check(!Error(Call(workspace, "open_directory", new { path = "strict/new-folder" })) && opened == Path.Combine(root, "strict", "new-folder"), "directory opener receives validated path");
                Check(Error(Call(workspace, "create_directory", new { path = "../outside/strict-refused" })) && !Directory.Exists(Path.Combine(outside, "strict-refused")), "strict creation remains within root");
                opened = null;
                Check(Error(Call(workspace, "open_directory", new { path = outside })) && opened == null, "strict external directory opening refused");
                Check(Error(Call(workspace, "open_directory", new { path = "twice.txt" })), "only directories can be opened");

                var full = new AgentWorkspace(root, false);
                Check(!full.RestrictToRoot && full.Root == root, "explicit full access constructor");
                var disk = new AgentWorkspace(Path.GetPathRoot(root), false);
                Check(disk.Root == Path.GetPathRoot(root) && Path.IsPathRooted(disk.Root), "full access preserves volume root separator");
                string desktop = Path.Combine(outside, "Desktop", "测试项目", "子目录");
                Check(!Error(Call(full, "create_directory", new { path = desktop })) && Directory.Exists(desktop), "full access creates external desktop-style nested directory");
                string fullFile = Path.Combine(desktop, "new.txt");
                Check(!Error(Call(full, "write_file", new { path = fullFile, content = "external-before" })), "full access creates external absolute file");
                Check(Call(full, "read_file", new { path = "../outside/Desktop/测试项目/子目录/new.txt" }).Contains("external-before"), "full access normalizes parent-relative path");
                Check(Call(full, "search_files", new { path = desktop, query = "external-before" }).Contains(fullFile + ":1"), "external search results use safe absolute path");
                Check(!Error(Call(full, "edit_file", new { path = fullFile, old_text = "external-before", new_text = "external-after" })) && File.ReadAllText(fullFile) == "external-after", "full access edits across root");
                string externalBackup = Path.Combine(desktop, ".deskpet-agent-backups");
                Check(Directory.GetFiles(externalBackup).Length == 1 && File.ReadAllText(Directory.GetFiles(externalBackup)[0]) == "external-before", "full access backups stay alongside external file");
                full.BackupEnabled = false;
                Check(!Error(Call(full, "write_file", new { path = fullFile, content = "no-more-backups" })) && Directory.GetFiles(externalBackup).Length == 1, "backup off skips additional backups");
                string noBackup = Path.Combine(desktop, "NoBackup", "note.txt");
                Call(full, "write_file", new { path = noBackup, content = "first" });
                Check(!Error(Call(full, "write_file", new { path = noBackup, content = "second" })) && !Directory.Exists(Path.Combine(Path.GetDirectoryName(noBackup), ".deskpet-agent-backups")), "backup off never creates backup directory");
                Check(Error(Call(full, "write_file", new { path = hardlink, content = "bad" })), "full access with backup off still refuses hardlink");
                Check(Error(Call(full, "read_file", new { path = Path.Combine(junction, "secret.txt") })), "full access still refuses junction traversal");
                Check(Error(Call(full, "write_file", new { path = fullFile + ":ads", content = "bad" })), "full access still refuses ADS");
                Check(Error(Call(full, "write_file", new { path = "NUL", content = "bad" })), "full access still refuses device names");
                string fullGit = Path.Combine(outside, ".git", "config");
                Check(!Error(Call(full, "write_file", new { path = fullGit, content = "fixture git metadata" })) && File.ReadAllText(fullGit) == "fixture git metadata", "full access removes artificial git restriction");
                Check(Call(full, "list_files", new { path = outside }).Contains(".git"), "full access lists git directory");
                Check(Call(full, "search_files", new { path = Path.GetDirectoryName(fullGit), query = "fixture git metadata" }).Contains("config"), "full access searches git directory");
                string systemNamed = Path.Combine(outside, "System Volume Information", "note.txt");
                Check(!Error(Call(full, "write_file", new { path = systemNamed, content = "ordinary fixture path" })), "full access removes artificial system-name restriction");
                full.DirectoryOpener = delegate(string path) { opened = path; }; opened = null;
                Check(!Error(Call(full, "open_directory", new { path = desktop })) && opened == desktop, "full access opens external selected directory using validated path");
                full.PlanMode = true; opened = null;
                Check(full.ReadOnly && full.OpenAITools.Length >= 3 && !new JavaScriptSerializer().Serialize(full.OpenAITools).Contains("write_file"), "plan alias preserves readonly behavior and schemas");
                Check(Error(Call(full, "create_directory", new { path = Path.Combine(desktop, "blocked") })) && !Directory.Exists(Path.Combine(desktop, "blocked")), "plan execution refuses directory creation");
                Check(Error(Call(full, "open_directory", new { path = desktop })) && opened == null, "plan execution refuses directory opening");
                Check(Error(Call(full, "edit_file", new { path = fullFile, old_text = "no-more-backups", new_text = "blocked" })) && File.ReadAllText(fullFile) == "no-more-backups", "plan execution refuses external edit");
                Check(Call(full, "read_file", new { path = fullFile }).Contains("no-more-backups"), "plan allows external read under configured full access");
                full.ReadOnly = false;
                Check(!full.PlanMode && full.OpenAITools.Length >= 7, "readonly alias restores all tools");
                File.SetAttributes(fullFile, FileAttributes.ReadOnly);
                try { Check(Error(Call(full, "write_file", new { path = fullFile, content = "forbidden" })) && File.ReadAllText(fullFile) == "no-more-backups", "full access respects OS readonly file permission"); }
                finally { File.SetAttributes(fullFile, FileAttributes.Normal); }
                var copy = new AgentToolEvent { Id = "1", Name = "read_file", Summary = "x", Status = "complete", ElapsedMilliseconds = 5 }.Copy();
                Check(copy.Id == "1" && copy.ElapsedMilliseconds == 5, "tool event copy");
                Console.WriteLine("AgentWorkspaceTests: " + assertions + " assertions passed.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                // Delete the junction itself before recursively removing only our known fixture directory.
                if (Directory.Exists(junction)) Directory.Delete(junction);
                if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            }
        }

        private static void CreateJunction(string path, string target)
        {
            Directory.CreateDirectory(path);
            byte[] substitute = Encoding.Unicode.GetBytes("\\??\\" + target);
            byte[] printable = Encoding.Unicode.GetBytes(target);
            byte[] data = new byte[16 + substitute.Length + 2 + printable.Length + 2];
            Array.Copy(BitConverter.GetBytes(0xA0000003u), 0, data, 0, 4);
            Array.Copy(BitConverter.GetBytes((ushort)(data.Length - 8)), 0, data, 4, 2);
            Array.Copy(BitConverter.GetBytes((ushort)substitute.Length), 0, data, 10, 2);
            Array.Copy(BitConverter.GetBytes((ushort)(substitute.Length + 2)), 0, data, 12, 2);
            Array.Copy(BitConverter.GetBytes((ushort)printable.Length), 0, data, 14, 2);
            Array.Copy(substitute, 0, data, 16, substitute.Length);
            Array.Copy(printable, 0, data, 18 + substitute.Length, printable.Length);
            using (SafeFileHandle handle = CreateFile(path, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
            {
                uint returned;
                if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4, data, data.Length, IntPtr.Zero, 0, out returned, IntPtr.Zero))
                    throw new Exception("Cannot create junction fixture: " + Marshal.GetLastWin32Error());
            }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputLength, IntPtr output, int outputLength, out uint returned, IntPtr overlapped);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string path, string existing, IntPtr security);
    }
}
