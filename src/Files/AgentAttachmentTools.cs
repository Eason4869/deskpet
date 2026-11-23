using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    public sealed partial class AgentWorkspace
    {
        readonly object attachmentScopeGate = new object();
        readonly Dictionary<string, AttachmentReference> attachmentScope = new Dictionary<string, AttachmentReference>(StringComparer.Ordinal);
        string attachmentDataRoot;

        internal static bool IsAttachmentTool(string name) { return name == "inspect_attachment" || name == "read_attachment"; }

        internal void SetAttachmentScope(string root, IEnumerable<ChatMessage> messages)
        {
            lock (attachmentScopeGate)
            {
                attachmentDataRoot = root;
                attachmentScope.Clear();
                AddScopeAttachments(messages);
            }
        }

        internal void AddScopeAttachments(IEnumerable<ChatMessage> messages)
        {
            lock (attachmentScopeGate)
            {
                if (String.IsNullOrEmpty(attachmentDataRoot)) return;
                foreach (var message in messages)
                {
                    if (message == null || message.Role != "user" || message.Attachments == null) continue;
                    foreach (var file in message.Attachments)
                    {
                        var link = file == null ? null : file.ImportedFile;
                        if (link != null && link.IsValid) attachmentScope[link.Id] = link;
                    }
                }
            }
        }

        bool HasAttachmentScope()
        {
            lock (attachmentScopeGate) return attachmentScope.Count != 0;
        }

        string ReadAttachedFile(string name, Dictionary<string, object> args, string callId, CancellationToken token)
        {
            AttachmentReference file;
            string root;
            lock (attachmentScopeGate)
            {
                root = attachmentDataRoot;
                var id = GetString(args, "attachment_id", "");
                if (String.IsNullOrEmpty(root) || !attachmentScope.TryGetValue(id, out file))
                    return TextResources.Get("fileCompatibility.attachmentDenied");
            }
            try
            {
                var request = new FileReadRequest
                {
                    Section = GetString(args, "section", null),
                    NestedSection = GetString(args, "nested_section", null),
                    Start = GetInt(args, "start", 1, 1, 10000000),
                    Count = GetInt(args, "count", 25, 1, 1000),
                    StartColumn = GetInt(args, "start_column", 1, 1, 16384),
                    ColumnCount = GetInt(args, "column_count", 32, 1, 256),
                    Operation = GetString(args, "operation", null),
                    Query = GetString(args, "query", null),
                    EncodingName = GetString(args, "encoding", null),
                    Language = GetString(args, "language", null),
                    Column = GetString(args, "column", null),
                    FilterColumn = GetString(args, "filter_column", null),
                    FilterValue = GetString(args, "filter_value", null),
                    FilterOperator = GetString(args, "filter_operator", null),
                    SortColumn = GetString(args, "sort_column", null),
                    TimestampMs = GetInt(args, "timestamp_ms", 0, 0, Int32.MaxValue),
                };
                if (name == "inspect_attachment")
                {
                    var manifest = FileContentService.Inspect(file, root, token);
                    var sections = manifest.Sections.Skip(request.Start - 1).Take(Math.Min(100, request.Count))
                        .Select(item => new { id = item.Id, label = item.Label, count = item.Count }).ToArray();
                    int next = request.Start + sections.Length;
                    return new JavaScriptSerializer().Serialize(new { attachment_id = file.Id, name = file.Name, kind = manifest.Kind,
                        detail = manifest.Detail, sections, next_start = next <= manifest.Sections.Count ? (int?)next : null });
                }
                var result = FileContentService.Read(file, root, request, token);
                if (result.Image != null)
                {
                    if (providerCapabilities == null || !providerCapabilities.SupportsImages)
                        return TextResources.Get("agentWorkspace.executeAuthorizedAsync.text");
                    var inspection = AgentImageInspection.Parse(file.Name + ".png", result.Image, result.Range);
                    lock (inspectionResults) inspectionResults[callId] = inspection;
                }
                return FileContentService.FormatResult(result);
            }
            catch (Exception error)
            {
                if (!FileContentService.IsReadFailure(error)) throw;
                return TextResources.Format("fileCompatibility.toolError", FileContentService.UserError(error));
            }
        }
    }
}
