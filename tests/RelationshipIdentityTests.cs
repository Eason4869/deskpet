using System;
namespace Dafeiyu.Tests
{
    public static class RelationshipIdentityTests
    {
        public static int Main()
        {
            try { using (var f = new RelationshipFixture()) {
                var official = RelationshipIdentity.Resolve(f.Root, "official:e-bao", "e-bao", null);
                RelationshipFixture.Check(official == "official:e-bao", "verified official identity");
                RelationshipFixture.Check(RelationshipIdentity.Resolve(f.Root, "custom-pack:e-bao", "e-bao", null) == "custom-pack:e-bao", "custom official ID spoof retains custom provenance");
                RelationshipFixture.Check(RelationshipIdentity.Resolve(f.Root, "custom-pack:alice", null, "same name") != RelationshipIdentity.Resolve(f.Root, "custom-pack:bob", null, "same name"), "identical display names do not merge packs");
                RelationshipFixture.Check(RelationshipIdentity.Resolve(f.Root, "official:e-bao", "e-bao", "outfit-path") == official, "outfit reuses parent identity");
                bool rejected = false; try { RelationshipIdentity.Resolve(f.Root, "official:e-bao", null, null); } catch (System.IO.InvalidDataException) { rejected = true; }
                RelationshipFixture.Check(rejected, "unverified official metadata rejected");
                string legacy = RelationshipIdentity.Resolve(f.Root, "", null, "old-persistent-id");
                RelationshipFixture.Check(legacy.StartsWith("legacy-skin:skin-") && legacy == RelationshipIdentity.Resolve(f.Root, "", null, "old-persistent-id"), "legacy mapping survives reload");
                RelationshipFixture.Check(legacy != RelationshipIdentity.Resolve(f.Root, "", null, "another-persistent-id"), "legacy identity remains distinct");
            } Console.WriteLine("RelationshipIdentityTests: 7 passed"); return 0;
            } catch (Exception e) { Console.WriteLine("FAIL RelationshipIdentityTests: " + e); return 1; }
        }
    }
}
