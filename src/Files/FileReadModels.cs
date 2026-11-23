using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Dafeiyu
{
    internal interface IFileContentReader
    {
        FileManifest Inspect(string path, CancellationToken token);
        FileReadResult Read(string path, FileReadRequest request, CancellationToken token);
    }

    internal sealed class FileManifest
    {
        internal FileManifest(string kind, IEnumerable<FileSection> sections, string detail = null)
        {
            Kind = kind;
            Sections = (sections ?? Enumerable.Empty<FileSection>()).ToList().AsReadOnly();
            Detail = detail;
        }

        internal string Kind { get; private set; }
        internal IList<FileSection> Sections { get; private set; }
        internal string Detail { get; private set; }
    }

    internal sealed class FileSection
    {
        internal FileSection(string id, string label, int? count = null)
        {
            Id = id;
            Label = label;
            Count = count;
        }

        internal string Id { get; private set; }
        internal string Label { get; private set; }
        internal int? Count { get; private set; }
        public override string ToString() { return Label; }
    }

    internal sealed class FileReadRequest
    {
        internal FileReadRequest()
        {
            Start = 1;
            Count = 100;
            StartColumn = 1;
            ColumnCount = 32;
        }

        internal string Section { get; set; }
        internal string NestedSection { get; set; }
        internal int Start { get; set; }
        internal int Count { get; set; }
        internal int StartColumn { get; set; }
        internal int ColumnCount { get; set; }
        internal string Operation { get; set; }
        internal string EncodingName { get; set; }
        internal string Language { get; set; }
        internal string Query { get; set; }
        internal string Column { get; set; }
        internal string FilterColumn { get; set; }
        internal string FilterValue { get; set; }
        internal string FilterOperator { get; set; }
        internal string SortColumn { get; set; }
        internal long TimestampMs { get; set; }
    }

    internal sealed class FileReadResult
    {
        internal FileReadResult(string text, string range, int? nextStart = null, byte[] image = null, string imageMediaType = null)
        {
            Text = text ?? "";
            Range = range ?? "";
            NextStart = nextStart;
            Image = image;
            ImageMediaType = imageMediaType;
        }

        internal string Text { get; private set; }
        internal string Range { get; private set; }
        internal int? NextStart { get; private set; }
        internal byte[] Image { get; private set; }
        internal string ImageMediaType { get; private set; }
    }
}
