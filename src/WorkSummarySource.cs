using System;
using System.Collections.Generic;
using System.Linq;

namespace Dafeiyu
{
    internal sealed class WorkSummarySource
    {
        internal string Title { get; private set; }
        internal ChatAttachment File { get; private set; }
        internal Uri Link { get; private set; }

        internal static bool MayContribute(ChatMessage message)
        {
            return (message.Attachments != null && message.Attachments.Count > 0) ||
                (message.Role == "assistant" && message.Status == "complete" && (message.Content ?? "").IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static WorkSummarySource[] FromHistory(IList<ChatMessage> history)
        {
            var sources = new List<WorkSummarySource>();
            var seenFiles = new HashSet<string>(StringComparer.Ordinal);
            var seenLinks = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in history.Reverse().Where(MayContribute).Take(24))
            {
                if (message.Attachments != null)
                {
                    foreach (var file in message.Attachments)
                    {
                        if (file.ImportedFile != null && !seenFiles.Add(file.ImportedFile.Id)) continue;
                        sources.Add(new WorkSummarySource { Title = file.Name, File = file });
                    }
                }
                if (message.Role != "assistant" || message.Status != "complete") continue;
                string markdown = message.Content ?? "";
                if (markdown.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (markdown.Length > 18000) markdown = markdown.Substring(markdown.Length - 18000);
                foreach (var link in Links(MarkdownContent.Parse(markdown)).Take(12))
                {
                    Uri address;
                    if (link.Url.Length > 2048 || !MarkdownContent.TryLink(link.Url, out address) ||
                        (address.Scheme != "http" && address.Scheme != "https") || !String.IsNullOrEmpty(address.UserInfo) || !seenLinks.Add(address.AbsoluteUri)) continue;
                    string title = MarkdownContent.PlainText(link.Children).Trim();
                    if (String.IsNullOrWhiteSpace(title) || String.Equals(title, address.AbsoluteUri, StringComparison.OrdinalIgnoreCase)) title = address.Host;
                    sources.Add(new WorkSummarySource { Title = title.Length > 240 ? title.Substring(0, 240) : title, Link = address });
                }
            }
            return sources.Take(100).ToArray();
        }

        static IEnumerable<MarkdownNode> Links(IEnumerable<MarkdownNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Kind == "link") yield return node;
                else foreach (var link in Links(node.Children)) yield return link;
            }
        }
    }
}
