using System;
using System.Collections.Generic;
using System.Linq;

namespace Dafeiyu
{
    public sealed class ImageGalleryQuery
    {
        public string Keyword { get; set; }
        public bool FavoritesOnly { get; set; }
        public bool Trash { get; set; }
        public string ProviderId { get; set; }
        public string Model { get; set; }
        public ImageGenerationSource? Source { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public bool OldestFirst { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }
        public ImageGalleryQuery() { Limit = 40; }
    }
    public sealed class ImageGalleryPage
    {
        public IList<GeneratedImage> Items { get; internal set; }
        public int Total { get; internal set; }
    }
    public sealed class ImageGalleryProviderChoice
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public override string ToString() { return Label; }
    }
    public sealed class ImageGalleryQueryService
    {
        readonly GeneratedImageStore store;
        public ImageGalleryQueryService(GeneratedImageStore store) { this.store = store; }
        public ImageGalleryPage Query(ImageGalleryQuery query)
        {
            if (query == null) throw new ArgumentNullException("query");
            if (query.Offset < 0 || query.Limit < 1 || query.Limit > 20000) throw new ArgumentOutOfRangeException("query");
            IEnumerable<GeneratedImage> found = store.ListIncludingTrash().Where(i => i.TrashedUtc.HasValue == query.Trash);
            if (query.FavoritesOnly) found = found.Where(i => i.IsFavorite);
            if (!String.IsNullOrWhiteSpace(query.Keyword))
            {
                string term = query.Keyword.Trim();
                found = found.Where(i => Match(i.Prompt, term) || Match(i.Model, term));
            }
            if (!String.IsNullOrEmpty(query.ProviderId)) found = found.Where(i => i.ProviderId == query.ProviderId);
            if (!String.IsNullOrEmpty(query.Model)) found = found.Where(i => i.Model == query.Model);
            if (query.Source.HasValue) found = found.Where(i => EffectiveSource(i) == query.Source.Value);
            if (query.FromUtc.HasValue) found = found.Where(i => i.CreatedUtc >= query.FromUtc.Value);
            if (query.ToUtc.HasValue) found = found.Where(i => i.CreatedUtc <= query.ToUtc.Value);
            var ordered = query.OldestFirst ? found.OrderBy(i => i.CreatedUtc).ThenBy(i => i.Id, StringComparer.Ordinal) : found.OrderByDescending(i => i.CreatedUtc).ThenBy(i => i.Id, StringComparer.Ordinal);
            var all = ordered.ToList();
            return new ImageGalleryPage { Total = all.Count, Items = all.Skip(query.Offset).Take(query.Limit).Select(i => i.Copy()).ToList().AsReadOnly() };
        }
        static bool Match(string value, string term) { return (value ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0; }
        static ImageGenerationSource EffectiveSource(GeneratedImage image)
        {
            return image.Source != ImageGenerationSource.Unknown ? image.Source : image.SessionId == "image-workspace" ? ImageGenerationSource.Workspace : ImageGenerationSource.Unknown;
        }
    }
    public sealed class ImageGallerySelection
    {
        readonly HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
        public int Count { get { return selected.Count; } }
        public IList<string> Ids { get { return selected.OrderBy(i => i, StringComparer.Ordinal).ToList().AsReadOnly(); } }
        public bool Contains(string id) { return selected.Contains(id); }
        public void Toggle(string id) { if (!selected.Add(id)) selected.Remove(id); }
        public void Clear() { selected.Clear(); }
        public void SelectLoaded(IEnumerable<GeneratedImage> images) { foreach (var image in images) selected.Add(image.Id); }
    }
}
