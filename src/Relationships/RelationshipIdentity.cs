using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Dafeiyu
{
    public static class RelationshipIdentity
    {
        // Picker provenance is authoritative. A package's self-declared official ID is not trust.
        // Outfits pass these fields from their parent companion, never from appearance paths.
        public static string Resolve(string dataDirectory, string pickerKey, string verifiedOfficialCharacterId, string legacyStableId)
        {
            string normalized = OfficialCompanionIdentity.NormalizePickerKey(pickerKey);
            if (!String.IsNullOrEmpty(normalized)) {
                if (normalized.StartsWith("official:", StringComparison.Ordinal) && normalized != "official:" + verifiedOfficialCharacterId)
                    throw new InvalidDataException("Official relationship identity requires verified provenance.");
                return normalized;
            }
            if (!String.IsNullOrEmpty(pickerKey)) throw new InvalidDataException("Invalid relationship picker identity.");
            if (String.IsNullOrWhiteSpace(legacyStableId) || legacyStableId.Length > 512 || legacyStableId.IndexOfAny(new[] {'\0','\r','\n'}) >= 0)
                throw new InvalidDataException("A persistent legacy identifier is required.");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("deskpet-relationship-v1:" + legacyStableId))).Replace("-", "").ToLowerInvariant();
            // Deterministic namespace mapping survives upgrades without display names or sidecar races.
            return "legacy-skin:skin-" + hash.Substring(0, 32);
        }
        public static bool IsValid(string id) { return !String.IsNullOrEmpty(id) && OfficialCompanionIdentity.NormalizePickerKey(id) == id; }
    }
}
