using System;
using System.IO;
using Dafeiyu;

namespace Dafeiyu.Tests
{
    internal static class RelationshipTestEvents
    {
        internal static RelationshipResult RecordChecked(this RelationshipService service, string id, RelationshipEventType type, string token, RelationshipTime time)
        {
            var result = service.Record(id, type, token, time);
            RelationshipFixture.Check(result.Success, "Event " + token + " failed: " + result.Error);
            return result;
        }
    }
    internal sealed class RelationshipFixture : IDisposable
    {
        // The Windows sandbox temp redirect denies atomic rename; use isolated workspace fixtures.
        readonly string fixtureParent = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "work"));
        public readonly string Root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "work", "deskpet-relationships-" + Guid.NewGuid().ToString("N")));
        public RelationshipStore Store { get { return new RelationshipStore(Root); } }
        public RelationshipService Service { get { return new RelationshipService(Store); } }
        public static RelationshipTime Time(string utc, int offsetMinutes = 0) { return new RelationshipTime(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal), TimeSpan.FromMinutes(offsetMinutes)); }
        public static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public void Dispose() { if (!Root.StartsWith(fixtureParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup."); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
