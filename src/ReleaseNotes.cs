using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Dafeiyu
{
    internal sealed class ReleaseNotes
    {
        internal sealed class Entry
        {
            internal ReleaseVersion Version { get; private set; }
            internal List<string> Items { get; private set; }
            internal Uri Details { get; private set; }

            internal Entry(ReleaseVersion version, Uri details)
            {
                Version = version;
                Details = details;
                Items = new List<string>();
            }
        }

        readonly string markerPath;
        internal ReleaseVersion Current { get; private set; }
        internal ReleaseVersion Previous { get; private set; }

        ReleaseNotes(string markerPath, ReleaseVersion current, ReleaseVersion previous)
        {
            this.markerPath = markerPath;
            Current = current;
            Previous = previous;
        }

        internal static ReleaseNotes ForStartup(string dataDirectory, string version, bool hasPreviousInstall)
        {
            var current = ReleaseVersion.Parse(version);
            var path = Path.Combine(dataDirectory, "release-notes-version.txt");
            var recorded = ReadVersion(path);
            if (recorded != null)
                return current.CompareTo(recorded) > 0 ? new ReleaseNotes(path, current, recorded) : null;

            if (hasPreviousInstall) return new ReleaseNotes(path, current, null);

            // A fresh installation establishes the baseline for future upgrades.
            new ReleaseNotes(path, current, null).MarkSeen();
            return null;
        }

        static ReleaseVersion ReadVersion(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 128) return null;
            ReleaseVersion version;
            return ReleaseVersion.TryParse(File.ReadAllText(path).Trim(), out version) ? version : null;
        }

        internal void MarkSeen()
        {
            var recorded = ReadVersion(markerPath);
            if (recorded != null && Current.CompareTo(recorded) <= 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath));
            var temporary = markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, Current.Canonical, new UTF8Encoding(false));
                AtomicFile.Commit(temporary, markerPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        static string LogFile(string language) { return language.StartsWith("zh_", StringComparison.Ordinal) ? "CHANGELOG.md" : "CHANGELOG.en.md"; }

        internal Uri FullLog(string language)
        {
            var identity = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(typeof(ReleaseNotes).Assembly, typeof(AssemblyInformationalVersionAttribute));
            var commit = Regex.Match(identity == null ? "" : identity.InformationalVersion, @"\+([a-f0-9]{40})\z");
            string source = commit.Success ? commit.Groups[1].Value : Current.IsPreview ? "dev" : "main";
            return new Uri(ReleaseInfo.RepositoryUrl + "/blob/" + source + "/" + LogFile(language));
        }

        internal List<Entry> Entries(string language)
        {
            var entries = new List<Entry>();
            using (var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("Dafeiyu.ReleaseNotes." + LogFile(language)))
            {
                if (stream == null) return entries;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    Entry section = null;
                    foreach (var block in MarkdownContent.Parse(reader.ReadToEnd()))
                    {
                        if (block.Kind == "heading" && block.Level <= 2)
                        {
                            section = null;
                            if (block.Level != 2) continue;
                            var title = MarkdownContent.PlainText(block.Children).Trim();
                            var versionText = title.Split(new[] { ' ', '\t', '·' }, 2)[0];
                            ReleaseVersion version;
                            if (ReleaseVersion.TryParse(versionText, out version) && version.IsPreview == Current.IsPreview &&
                                version.CompareTo(Current) <= 0 && (Previous == null ? version.CompareTo(Current) == 0 : version.CompareTo(Previous) > 0))
                            {
                                string releaseUrl = ReleaseInfo.RepositoryUrl + "/releases/tag/v" + version.Canonical;
                                bool published = block.Children.Any(child => child.Kind == "link" && child.Url == releaseUrl);
                                section = new Entry(version, published ? new Uri(releaseUrl) : null);
                                entries.Add(section);
                            }
                        }
                        else if (section != null && (block.Kind == "list" || block.Kind == "ordered"))
                        {
                            foreach (var item in block.Children)
                            {
                                var text = MarkdownContent.PlainText(item.Children).Trim();
                                if (text.Length != 0) section.Items.Add(text);
                            }
                        }
                    }
                }
            }
            return entries.Where(entry => entry.Items.Count > 0).OrderByDescending(entry => entry.Version).ToList();
        }

        internal string Title { get { return TextResources.Format("releaseNotes.updated", Current.Canonical); } }
        internal string Subtitle
        {
            get { return Previous == null ? TextResources.Get("releaseNotes.latest") : TextResources.Format("releaseNotes.since", Previous.Canonical); }
        }
        internal string ChannelName { get { return Current.IsPreview ? TextResources.Get("releaseNotes.preview") : TextResources.Get("releaseNotes.stable"); } }
    }
}
