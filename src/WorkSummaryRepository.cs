using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Dafeiyu
{
    internal sealed class WorkSummaryRepository
    {
        static readonly Regex OriginSection = new Regex(@"^\[\s*(?i:remote)\s+""origin""\s*\]\s*(?:[;#].*)?$", RegexOptions.CultureInvariant);
        static readonly Regex OriginUrl = new Regex(@"^url\s*=\s*(?:""(?<url>[^""\r\n]*)""|(?<url>[^\s;#]+))\s*(?:[;#].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        internal string Repository { get; private set; }
        internal string Branch { get; private set; }

        internal WorkSummaryRepository(string origin, string branch)
        {
            var address = origin.Trim();
            const string sshPrefix = "git@github.com:";
            if (address.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
                address = "https://github.com/" + address.Substring(sshPrefix.Length);
            Uri uri;
            if (Uri.TryCreate(address, UriKind.Absolute, out uri) && uri.Scheme == "ssh" && uri.Host == "github.com" &&
                (uri.IsDefaultPort || uri.Port == 22) && uri.UserInfo == "git" && uri.Query.Length == 0 && uri.Fragment.Length == 0)
                address = "https://github.com" + uri.AbsolutePath;
            Repository = Normalize(address);
            var name = branch.Trim();
            Branch = name.Length <= 240 && !name.Contains("\n") && !name.Contains("\r") ? name : "";
        }

        internal static WorkSummaryRepository Read(string workspace, CancellationToken cancel)
        {
            string root = Path.GetFullPath(workspace);
            string drive = Path.GetPathRoot(root);
            if (!IsLocalDrive(drive)) return null;
            string marker = Path.Combine(root, ".git"), metadata = marker, common = marker;
            var markerAttributes = LocalAttributes(marker);
            if (!markerAttributes.HasValue) return null;
            if ((markerAttributes.Value & FileAttributes.Directory) == 0)
            {
                string pointer = ReadSmallFile(marker, 4096);
                const string prefix = "gitdir: ";
                if (!pointer.StartsWith(prefix, StringComparison.Ordinal)) return null;
                metadata = Resolve(root, pointer.Substring(prefix.Length), drive);
                if (metadata == null) return null;
                string parent = Path.GetDirectoryName(metadata);
                if (parent == null || !String.Equals(Path.GetFileName(parent), "worktrees", StringComparison.OrdinalIgnoreCase)) return null;
                common = Path.GetDirectoryName(parent);
                if (common == null || !String.Equals(Path.GetFileName(common), ".git", StringComparison.OrdinalIgnoreCase)) return null;
                string backlink = Resolve(metadata, ReadSmallFile(Path.Combine(metadata, "gitdir"), 4096), drive);
                string shared = Resolve(metadata, ReadSmallFile(Path.Combine(metadata, "commondir"), 4096), drive);
                if (!String.Equals(backlink, marker, StringComparison.OrdinalIgnoreCase) || !String.Equals(shared, common, StringComparison.OrdinalIgnoreCase)) return null;
            }
            cancel.ThrowIfCancellationRequested();
            const string branchPrefix = "ref: refs/heads/";
            string head = ReadSmallFile(Path.Combine(metadata, "HEAD"), 4096);
            if (head.Length == 0) return null;
            string branch = head.StartsWith(branchPrefix, StringComparison.Ordinal) ? head.Substring(branchPrefix.Length) : "";
            string origin = ReadOrigin(ReadSmallFile(Path.Combine(common, "config"), 256 * 1024));
            cancel.ThrowIfCancellationRequested();
            return new WorkSummaryRepository(origin, branch);
        }

        static string ReadOrigin(string config)
        {
            string origin = "";
            bool inOrigin = false;
            using (var lines = new StringReader(config))
            {
                string line;
                while ((line = lines.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.StartsWith("[", StringComparison.Ordinal)) inOrigin = OriginSection.IsMatch(line);
                    else if (inOrigin)
                    {
                        var url = OriginUrl.Match(line);
                        if (url.Success) origin = url.Groups["url"].Value;
                    }
                }
            }
            return origin;
        }

        static bool IsLocalDrive(string drive) { return drive != null && drive.Length == 3 && Char.IsLetter(drive[0]) && drive[1] == ':'; }

        static string Resolve(string directory, string path, string drive)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string resolved = Path.GetFullPath(Path.Combine(directory, path));
            return String.Equals(Path.GetPathRoot(resolved), drive, StringComparison.OrdinalIgnoreCase) && resolved.IndexOf(':', drive.Length) < 0 ? resolved.TrimEnd('\\', '/') : null;
        }

        static string ReadSmallFile(string path, int limit)
        {
            var attributes = LocalAttributes(path);
            if (!attributes.HasValue || (attributes.Value & FileAttributes.Directory) != 0) return "";
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                int length = (int)Math.Min(limit + 1L, stream.Length);
                if (length > limit) return "";
                var bytes = new byte[length];
                int offset = 0, read;
                while (offset < length && (read = stream.Read(bytes, offset, length - offset)) > 0) offset += read;
                return new UTF8Encoding(false, true).GetString(bytes, 0, offset).Trim().TrimStart('\uFEFF');
            }
        }

        static FileAttributes? LocalAttributes(string path)
        {
            var ancestors = new Stack<string>();
            for (string current = path; current != null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
            FileAttributes attributes = 0;
            while (ancestors.Count > 0)
            {
                try { attributes = File.GetAttributes(ancestors.Pop()); }
                catch (FileNotFoundException) { return null; }
                catch (DirectoryNotFoundException) { return null; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) return null;
            }
            return attributes;
        }

        internal static string Normalize(string address)
        {
            address = (address ?? "").Trim();
            if (address.Length == 0 || address.Length > 300) return "";
            if (!address.Contains("://")) address = "https://github.com/" + address;
            try { return GitHubUpdates.Repository(address); }
            catch (ApiException) { return ""; }
        }

        internal static Uri GitHubPage(string repository)
        {
            return new Uri("https://github.com/" + repository);
        }
    }
}
