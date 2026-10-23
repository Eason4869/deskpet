using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Dafeiyu
{
    // Picker provenance and persona ownership are deliberately separate. This
    // helper never moves history or turns a self-declared package ID into trust.
    public static class OfficialCompanionIdentity
    {
        static readonly Regex Alias = new Regex("\\Askin-[a-f0-9]{32}\\z");
        public static string NormalizePickerKey(string key)
        {
            if (String.IsNullOrEmpty(key) || key.Length > 80) return "";
            if (key.StartsWith("official:", StringComparison.Ordinal))
                return OfficialCompanionProfiles.IsOfficialId(key.Substring(9)) ? key : "";
            if (key.StartsWith("custom-pack:", StringComparison.Ordinal)) {
                string id = key.Substring(12);
                return ExactPackageId(id) ? key : "";
            }
            if (key.StartsWith("legacy-skin:", StringComparison.Ordinal))
                return Alias.IsMatch(key.Substring(12)) ? key : "";
            return "";
        }

        public static string ResolveOfficial(string dataRoot, string characterId, IEnumerable<string> untrustedPersonaIds)
        {
            if (!OfficialCompanionProfiles.IsOfficialId(characterId)) throw Invalid();
            var reserved = Reserved(untrustedPersonaIds);
            using (Acquire(dataRoot)) {
                ReserveOfficial(dataRoot, characterId, reserved);
                string path = Sidecar(dataRoot, "official-companions", characterId, "identity.txt");
                string saved = Read(path, characterId);
                if (saved != null) {
                    if (reserved.Contains(saved)) throw Invalid();
                    return saved;
                }
                string preferred = CompanionPackStorage.Identity(dataRoot, characterId);
                if (!ValidPersona(preferred, characterId)) throw Invalid();
                string assigned = reserved.Contains(preferred) ? NewAlias(reserved) : preferred;
                Publish(path, assigned);
                return assigned;
            }
        }

        public static string ResolveCustom(string dataRoot, string packageId, IEnumerable<string> assignedOfficialPersonaIds, bool isNewImport)
        {
            if (!ExactPackageId(packageId)) throw Invalid();
            var reserved = Reserved(assignedOfficialPersonaIds);
            using (Acquire(dataRoot)) {
                ReserveOfficial(dataRoot, null, reserved);
                string path = Sidecar(dataRoot, "companion-packs", packageId, "custom-identity.txt");
                string saved = Read(path, packageId);
                if (saved != null) {
                    if (reserved.Contains(saved)) throw Invalid();
                    return saved;
                }
                string preferred = CompanionPackStorage.Identity(dataRoot, packageId);
                if (!ValidPersona(preferred, packageId)) throw Invalid();
                // Existing entries already own their persona. Catalog discovery
                // reserves these before assigning any new official identities.
                if (!isNewImport) return preferred;
                string assigned = reserved.Contains(preferred) ? NewAlias(reserved) : preferred;
                Publish(path, assigned);
                return assigned;
            }
        }

        static bool ExactPackageId(string id)
        {
            return CompanionPackStorage.ValidId(id) && id.IndexOfAny(new[] {'\r', '\n', '\0'}) < 0;
        }
        static bool ValidPersona(string value, string id) { return value == id || (value != null && Alias.IsMatch(value)); }
        static InvalidDataException Invalid() { return new InvalidDataException(TextResources.Get("officialCompanionIdentity.invalid.text")); }
        static HashSet<string> Reserved(IEnumerable<string> values)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (values != null) {
                int count = 0;
                foreach (string value in values) {
                    if (++count > 4096 || value == null || value.Length > 80 || (!ExactPackageId(value) && !Alias.IsMatch(value))) throw Invalid();
                    result.Add(value);
                }
            }
            return result;
        }
        static string NewAlias(HashSet<string> reserved)
        {
            string value;
            do { value = "skin-" + Guid.NewGuid().ToString("N"); } while (reserved.Contains(value));
            return value;
        }
        static void ReserveOfficial(string dataRoot, string exceptId, HashSet<string> reserved)
        {
            foreach (var profile in OfficialCompanionProfiles.All) {
                if (profile.Id == exceptId) continue;
                string path = CompanionPackStorage.AssetPath(Path.GetFullPath(dataRoot), "official-companions/" + profile.Id + "/identity.txt");
                string value = Read(path, profile.Id);
                if (value != null) reserved.Add(value);
            }
        }
        static string Sidecar(string dataRoot, string store, string id, string file)
        {
            string root = CompanionPackStorage.AssetPath(Path.GetFullPath(dataRoot), store + "/" + id);
            Directory.CreateDirectory(root);
            return CompanionPackStorage.AssetPath(root, file);
        }
        static string Read(string path, string id)
        {
            if (!File.Exists(path)) return null;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (input.Length == 0 || input.Length > 128) throw Invalid();
                var bytes = new byte[(int)input.Length];
                int offset = 0;
                while (offset < bytes.Length) { int n = input.Read(bytes, offset, bytes.Length - offset); if (n == 0) throw Invalid(); offset += n; }
                string value;
                try { value = new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { throw Invalid(); }
                if (!ValidPersona(value, id)) throw Invalid();
                return value;
            }
        }
        static FileStream Acquire(string dataRoot)
        {
            if (String.IsNullOrWhiteSpace(dataRoot)) throw Invalid();
            string root = CompanionPackStorage.AssetPath(Path.GetFullPath(dataRoot), "official-companions");
            Directory.CreateDirectory(root);
            string path = CompanionPackStorage.AssetPath(root, ".identity.lock");
            var timeout = Stopwatch.StartNew();
            while (true) {
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { if (timeout.ElapsedMilliseconds >= 5000) throw new IOException(TextResources.Get("officialCompanionIdentity.acquire.text")); Thread.Sleep(20); }
            }
        }
        static void Publish(string path, string value)
        {
            string temporary = CompanionPackStorage.AssetPath(Path.GetDirectoryName(path), ".identity-" + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(value); output.Write(bytes, 0, bytes.Length); output.Flush(true);
                }
                // A reader sees either no assignment or the whole assignment;
                // an unexpected preexisting sidecar is never overwritten.
                File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
