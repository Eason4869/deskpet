using System.Runtime.InteropServices;
using System.Buffers.Binary;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed record ClipSpec(string File, int Columns, int Frames, double Fps, bool Loop, int Width = 480, int Height = 520, int Top = 0, double OffsetX = 0, double OffsetY = 0, double HoldLastSeconds = 0, OutfitClipDefinition? HolidaySource = null);

internal sealed class PetSkin
{
    public required string Id { get; set; }
    public string PickerKey { get; set; } = "";
    public string? OfficialCharacterId { get; set; }
    public bool IsPlaceholder { get; set; }
    public string SourceKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? BuiltinCharacterId { get; set; }
    public required string Name { get; init; }
    public required string Prompt { get; init; }
    public string? Folder { get; init; }
    public bool BuiltIn => Folder == null;
    public string? Atlas { get; init; }
    public string SpecialAction { get; init; } = "伙伴招牌";
    public string SpecialAction2 { get; init; } = "第二招牌";
    public string? PackageId { get; set; }
    public string? PackVersion { get; init; }
    public string? Avatar { get; set; }
    public string? Preview { get; init; }
    public string Description { get; init; } = "你的专属桌面伙伴";
    public VoiceRecommendations? Voices { get; init; }
    public Dictionary<string, ClipSpec> ExtraClips { get; } = new();
    public Dictionary<string, ClipSpec> Alternates { get; } = new();
    public Dictionary<string, ClipSpec> Clips { get; } = new();
    public ClipSpec? Gaze { get; set; }
    public override string ToString() => Name;
}

internal sealed class SpriteAssets : IDisposable
{
    private readonly Func<string, Stream>? openHoliday;
    internal SpriteAssets(Func<string, Stream>? holidaySource = null) => openHoliday = holidaySource;
    private readonly Dictionary<string, Bitmap> images = new();
    private readonly Dictionary<(string, int), byte[]> masks = new();
    public static readonly string[] Interactions = { "lifted", "landing", "petting", "poked", "feeding", "happy", "sleeping", "special" };
    public static Stream OpenAsset(string path) => AssetLoader.Open(new Uri("avares://DeskPet/Assets/" + path.Replace('\\', '/')));
    public static readonly string[] PairedActions = { "lifted", "landing", "feeding", "happy", "sleeping", "special", "special2", "stumble" };
    public static List<PetSkin> LoadSkins(string data, Dictionary<string,string>? aliases = null, bool restoreAliases = false)
    {
        var custom = new List<PetSkin>();
        var canonical = new Dictionary<string, PetSkin>(StringComparer.Ordinal);
        var legacyRoles = new Dictionary<string, string>(StringComparer.Ordinal);
        void Discover(string manifest, string persona, string folder, string key, string? packageId)
        {
            try
            {
                using var source = File.OpenRead(manifest);
                var skin = ReadManifest(source, persona, folder);
                if (packageId != null && skin.PackageId != packageId) return;
                ValidateSkin(skin); skin.PickerKey = key;
                foreach (var profile in OfficialCompanionProfiles.All)
                {
                    var matched = OfficialCompanionLegacy.MatchImport(folder, profile.Id, CancellationToken.None);
                    if (matched == null) continue;
                    if (persona.StartsWith("skin-", StringComparison.Ordinal))
                    {
                        CompanionPackStorage.RetainIdentity(data, profile.Id, persona);
                        legacyRoles[persona] = profile.Id;
                    }
                    skin.OfficialCharacterId = skin.BuiltinCharacterId = profile.Id;
                    skin.PickerKey = "official:" + profile.Id; skin.SourceKey = matched.SourceKey;
                    skin.PackageId = profile.Id; canonical[profile.Id] = skin;
                    if (restoreAliases) CompanionPackStorage.SetHidden(data, skin.PickerKey, false);
                    return;
                }
                custom.Add(skin);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
            { System.Diagnostics.Trace.WriteLine("Character source: " + e.GetType().Name); }
        }
        foreach (var pack in CompanionPackStorage.Installed(data))
        {
            try { Discover(Path.Combine(pack.Value, "pet.json"), OfficialCompanionIdentity.ResolveCustom(data, pack.Key, Array.Empty<string>(), false), pack.Value, "custom-pack:" + pack.Key, pack.Key); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine("Custom identity: " + error.GetType().Name); }
        }
        var root = Path.Combine(data, "skins");
        if (Directory.Exists(root)) foreach (var folder in Directory.GetDirectories(root))
        {
            var id = Path.GetFileName(folder);
            if (!id.StartsWith("skin-", StringComparison.Ordinal)) continue;
            Discover(Path.Combine(folder, "pet.json"), id, folder, "legacy-skin:" + id, null);
        }
        var reserved = custom.Select(s => s.Id).ToArray();
        var skins = new List<PetSkin>();
        foreach (var profile in OfficialCompanionProfiles.All)
        {
            var persona = OfficialCompanionIdentity.ResolveOfficial(data, profile.Id, reserved);
            if (aliases != null) foreach (var pair in legacyRoles.Where(p => p.Value == profile.Id)) aliases[pair.Key] = persona;
            PetSkin skin;
            if (canonical.TryGetValue(profile.Id, out var existing)) { skin = existing; skin.Id = persona; }
            else
            {
                var atlas = profile.Id switch { "e-bao" => "e-bao-spritesheet.png", "dafeiyu" => "spritesheet.png", "marfu" => "marfu-spritesheet.png", _ => null };
                var manifest = "bundled-skins/" + profile.Id + "/pet.json";
                if (AssetLoader.Exists(new Uri("avares://DeskPet/Assets/" + manifest)))
                {
                    using var input = OpenAsset(manifest);
                    skin = ReadManifest(input, persona, null, "bundled-skins/" + profile.Id + "/");
                }
                else if (atlas != null && AssetLoader.Exists(new Uri("avares://DeskPet/Assets/" + atlas)))
                    skin = new PetSkin { Id = persona, Name = profile.Name, Prompt = profile.SystemPrompt, Atlas = atlas, Description = profile.Description, Voices = profile.VoicePresets, SpecialAction = profile.SpecialAction, SpecialAction2 = profile.SpecialAction2 };
                else
                {
                    var legacy = OfficialCompanionLegacy.FindBundled(data, profile.Id, CancellationToken.None);
                    if (legacy != null)
                    {
                        using var input = File.OpenRead(Path.Combine(legacy.RootDirectory, "pet.json"));
                        skin = ReadManifest(input, persona, legacy.RootDirectory); ValidateSkin(skin); skin.SourceKey = legacy.SourceKey;
                    }
                    else skin = new PetSkin { Id = persona, Name = profile.Name, Prompt = profile.SystemPrompt, Description = profile.Description, Voices = profile.VoicePresets, SpecialAction = profile.SpecialAction, SpecialAction2 = profile.SpecialAction2, IsPlaceholder = true };
                }
            }
            skin.OfficialCharacterId = skin.BuiltinCharacterId = profile.Id; skin.PickerKey = "official:" + profile.Id;
            skin.Avatar = skin.Folder == null ? skin.Avatar ?? "avatars/" + profile.Id + ".png" : skin.Avatar;
            skins.Add(skin);
        }
        var hidden = CompanionPackStorage.Hidden(data);
        skins.AddRange(custom.Where(s => !hidden.Contains(s.PackageId ?? s.Id) && !hidden.Contains(s.PickerKey)));
        skins.RemoveAll(s => s.OfficialCharacterId != "e-bao" && hidden.Contains(s.PickerKey));
        return skins;
    }
    private static bool SameArtwork(PetSkin left, PetSkin right)
    {
        if (left.Name != right.Name || left.Prompt != right.Prompt) return false;
        foreach (var action in AnimationPlayer.States.Concat(Interactions))
        {
            var a = left.Clips[action]; var b = right.Clips[action];
            if (a.Frames != b.Frames || a.Fps != b.Fps || a.HoldLastSeconds != b.HoldLastSeconds || a.Loop != b.Loop || !SameFile(left, a.File, right, b.File)) return false;
        }
        return SameFile(left, left.Gaze!.File, right, right.Gaze!.File) &&
            (left.Preview == null || !File.Exists(Path.Combine(left.Folder!, left.Preview)) || right.Preview != null && SameFile(left, left.Preview, right, right.Preview));
    }
    private static bool SameFile(PetSkin left, string a, PetSkin right, string b)
    {
        using var x = left.BuiltIn ? OpenAsset(a) : File.OpenRead(SafePath(left.Folder!, a));
        using var y = right.BuiltIn ? OpenAsset(b) : File.OpenRead(SafePath(right.Folder!, b));
        return x.Length == y.Length && System.Security.Cryptography.SHA256.HashData(x).SequenceEqual(System.Security.Cryptography.SHA256.HashData(y));
    }
    internal static PetSkin ReadManifest(Stream stream, string id, string? folder, string prefix = "")
    {
        if (stream.Length > 65536) throw new InvalidDataException("外观说明文件过大。");
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        var version = root.GetProperty("version").GetInt32();
        var name = root.GetProperty("name").GetString() ?? "自定义伙伴";
        if (name.Length is < 1 or > 60) throw new InvalidDataException("外观名称应为 1～60 字。");
        var prompt = root.TryGetProperty("system_prompt", out var p) ? p.GetString() ?? "" : PersonaProfiles.Generic(name);
        if (prompt.Length > 8000) throw new InvalidDataException("人格最多 8000 字。");
        string? Text(string field) => root.TryGetProperty(field, out var value) ? value.GetString() : null;
        var packageId = Text("id"); if (packageId != null && !CompanionPackStorage.ValidId(packageId)) throw new InvalidDataException("角色包 ID 无效。");
        var second = Text("special_action2"); if ((second?.Length ?? 0) > 32 || (Text("pack_version")?.Length ?? 0) > 32) throw new InvalidDataException("角色包名称或版本过长。");
        VoiceRecommendations? voices = null;
        if (root.TryGetProperty("voice_presets", out var voice))
        {
            string[]? Choices(string kind) => voice.TryGetProperty(kind, out var values) ? values.EnumerateArray().Select(v => v.GetString()!).ToArray() : null;
            voices = new VoiceRecommendations { Speech = Choices("speech"), Live = Choices("live"), Description = voice.TryGetProperty("description", out var d) ? d.GetString() : null, SampleText = voice.TryGetProperty("sample_text", out var t) ? t.GetString() : null };
            if ((voices.Description?.Length ?? 0) > 280 || (voices.SampleText?.Length ?? 0) > 200 || new[] { voices.Speech, voices.Live }.Any(v => v != null && (v.Length > 16 || v.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 64)))) throw new InvalidDataException("角色包音色预设无效。");
        }
        var skin = new PetSkin { Id = id, Name = name, Folder = folder, Prompt = prompt, Atlas = version == 1 ? root.GetProperty("atlas").GetString() : null,
            SpecialAction = Text("special_action") ?? "伙伴招牌", SpecialAction2 = second ?? "第二招牌", PackageId = packageId, PackVersion = Text("pack_version"),
            Avatar = Text("avatar") is { } avatar ? prefix + avatar : null, Preview = Text("preview") is { } preview ? prefix + preview : null,
            Description = Text("description") ?? "你的专属桌面伙伴", Voices = voices };
        if (version == 1) return skin;
        if (version != 2 || root.GetProperty("cell_width").GetInt32() != 480 || root.GetProperty("cell_height").GetInt32() != 520) throw new InvalidDataException("外观需要 v1 或 480 × 520 的 v2 格式。");
        foreach (var property in root.GetProperty("animations").EnumerateObject())
        {
            var a = property.Value;
            var columns = a.GetProperty("columns").GetInt32(); var frames = a.GetProperty("frames").GetInt32(); var fps = a.GetProperty("fps").GetDouble();
            if (columns is < 1 or > 16 || frames is < 1 or > 64 || !double.IsFinite(fps) || fps is <= 0 or > 60) throw new InvalidDataException("外观帧数或速度无效。");
            var loop = a.GetProperty("loop").GetBoolean();
            skin.Clips[property.Name] = new ClipSpec(prefix + a.GetProperty("file").GetString(), columns, frames, fps, loop, HoldLastSeconds: ReadHold(a, property.Name, loop));
        }
        foreach (var required in AnimationPlayer.States.Concat(Interactions)) if (!skin.Clips.ContainsKey(required)) throw new InvalidDataException("缺少动作：" + required);
        var gaze = root.GetProperty("gaze");
        if (gaze.GetProperty("columns").GetInt32() != 4 || gaze.GetProperty("frames").GetInt32() != 16) throw new InvalidDataException("视线图需要 4 列、16 帧。");
        skin.Gaze = new ClipSpec(prefix + gaze.GetProperty("file").GetString(), 4, 16, 8, true);
        foreach (var pair in new[] { ("extra_animations", skin.ExtraClips), ("alternate_animations", skin.Alternates) })
        {
            if (!root.TryGetProperty(pair.Item1, out var dictionary)) continue;
            foreach (var property in dictionary.EnumerateObject())
            {
                if (!(pair.Item1 == "extra_animations" ? new[] { "stumble", "special2" } : PairedActions.Append("jumping")).Contains(property.Name)) throw new InvalidDataException("扩展动作名称无效。");
                var a = property.Value; var count = a.GetProperty("frames").GetInt32(); var fps = a.GetProperty("fps").GetDouble();
                if (a.GetProperty("columns").GetInt32() != 8 || count is < 1 or > 128 || !double.IsFinite(fps) || fps is < 1 or > 60) throw new InvalidDataException("扩展动作帧数或速度无效。");
                var loop = a.GetProperty("loop").GetBoolean();
                pair.Item2[property.Name] = new ClipSpec(prefix + a.GetProperty("file").GetString(), 8, count, fps, loop, HoldLastSeconds: ReadHold(a, property.Name, loop));
            }
        }
        foreach (var pair in skin.Alternates)
        {
            if (!skin.Clips.TryGetValue(pair.Key, out var first) && !skin.ExtraClips.TryGetValue(pair.Key, out first)) throw new InvalidDataException("第二套动作缺少第一套。");
            if (pair.Value.Loop != first.Loop || pair.Key == "jumping" && Math.Abs(pair.Value.Frames / pair.Value.Fps + pair.Value.HoldLastSeconds - first.Frames / first.Fps - first.HoldLastSeconds) > 1e-8) throw new InvalidDataException("两套动作的循环或跳跃时长不一致。");
        }
        return skin;
    }
    private static double ReadHold(JsonElement clip, string action, bool loop)
    {
        var hold = clip.TryGetProperty("hold_last_seconds", out var value) ? value.GetDouble() : 0;
        if (!double.IsFinite(hold) || hold < 0 || hold > 2 || hold != 0 && (action != "jumping" || loop)) throw new InvalidDataException("最终停留仅适用于非循环跳跃，范围为 0～2 秒。");
        return hold;
    }
    internal static PetSkin ReadOfficial(OfficialCompanionInstallation installation, string personaId)
    {
        using var stream = File.OpenRead(installation.PetManifestPath);
        var skin = ReadManifest(stream, personaId, installation.RootDirectory);
        skin.OfficialCharacterId = skin.BuiltinCharacterId = installation.Asset.Id;
        skin.PickerKey = "official:" + installation.Asset.Id;
        skin.SourceKey = installation.RootDirectory + ":" + installation.Asset.Sha256;
        return skin;
    }
    internal static void ValidateNativeDefault(PetSkin skin, CancellationToken cancel)
    {
        foreach (var spec in skin.Clips.Values.Concat(skin.ExtraClips.Values).Concat(skin.Alternates.Values).Append(skin.Gaze!))
        {
            cancel.ThrowIfCancellationRequested();
            using var assets = new SpriteAssets();
            var image = assets.Get(skin, spec);
            if (image.PixelSize.Width != spec.Columns * spec.Width || image.PixelSize.Height != (int)Math.Ceiling((double)spec.Frames / spec.Columns) * spec.Height) throw new InvalidDataException("外观图像尺寸与说明不一致。");
            for (int frame = 0; frame < spec.Frames; frame++) { cancel.ThrowIfCancellationRequested(); assets.ValidateFrame(skin, spec, frame); }
        }
        using var portraits = new SpriteAssets();
        if (skin.Avatar is { } avatar)
        {
            var size = portraits.Get(skin, avatar).PixelSize;
            if (size.Width != size.Height || size.Width is < 64 or > 2048) throw new InvalidDataException("角色头像需要正方形 PNG。");
        }
        if (skin.Preview is { } preview && portraits.Get(skin, preview).PixelSize != new PixelSize(480, 520)) throw new InvalidDataException("角色预览尺寸无效。");
    }
    public static string SafePath(string root, string relative, bool legacy = false)
    {
        if (!legacy) return CompanionPackStorage.AssetPath(root, relative);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("外观资源路径无效。");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("外观资源目录不接受符号链接。");
        var target = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(fullRoot, StringComparison.Ordinal)) throw new InvalidDataException("外观资源不能越过所在目录。");
        var current = target;
        while (current.Length >= fullRoot.Length)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("外观资源不接受符号链接。");
            current = Path.GetDirectoryName(current)!;
        }
        return target;
    }
    private static void ValidateSkin(PetSkin skin)
    {
        using var assets = new SpriteAssets();
        if (skin.Atlas != null)
        {
            var image = assets.Get(skin, skin.Atlas);
            var size = image.PixelSize;
            if (size.Width % 8 != 0 || size.Height % 11 != 0 || size.Width / 8 is < 64 or > 768 || size.Height / 11 is < 64 or > 832 || (long)size.Width * size.Height > 55000000) throw new InvalidDataException("精灵图需要 8 列 × 11 行透明 PNG。");
            for (int row = 0; row < 11; row++) for (int col = 0; col < (row < 9 ? AnimationPlayer.Counts[row] : 8); col++) assets.ValidateFrame(skin, new ClipSpec(skin.Atlas, 8, 88, 8, true, size.Width / 8, size.Height / 11), row * 8 + col);
        }
        else foreach (var spec in skin.Clips.Values.Concat(skin.ExtraClips.Values).Concat(skin.Alternates.Values).Append(skin.Gaze!))
        {
            var image = assets.Get(skin, spec.File);
            if (image.PixelSize.Width != spec.Columns * spec.Width || image.PixelSize.Height != (int)Math.Ceiling((double)spec.Frames / spec.Columns) * spec.Height) throw new InvalidDataException("外观图像尺寸与说明不一致：" + spec.File);
            for (int i = 0; i < spec.Frames; i++) assets.ValidateFrame(skin, spec, i);
        }
        if (skin.Avatar is { } avatar)
        {
            var size = assets.Get(skin, avatar).PixelSize;
            if (size.Width != size.Height || size.Width is < 64 or > 2048) throw new InvalidDataException("角色头像需要 64–2048 像素的正方形 PNG。");
        }
        if (skin.Preview is { } preview && (skin.BuiltIn || skin.PackageId != null || File.Exists(Path.Combine(skin.Folder!, preview))) && assets.Get(skin, preview).PixelSize != new PixelSize(480, 520)) throw new InvalidDataException("角色预览需要 480 × 520 像素 PNG。");
        if (skin.Clips.Values.Concat(skin.ExtraClips.Values).Concat(skin.Alternates.Values).Sum(c => c.Frames) > 1024) throw new InvalidDataException("角色包姿态数量过多。");
    }
    public static PetSkin Import(string manifestPath, string data)
    {
        if (string.Equals(Path.GetExtension(manifestPath), ".zip", StringComparison.OrdinalIgnoreCase)) return CompanionPackStorage.ReadZip(manifestPath, data, path => Import(path, data));
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        if (new FileInfo(manifestPath).Length > 65536) throw new InvalidDataException("外观说明文件过大。");
        var id = "skin-" + Guid.NewGuid().ToString("N");
        using var stream = File.OpenRead(manifestPath);
        var skin = ReadManifest(stream, id, root);
        ValidateSkin(skin);
        var references = skin.Atlas != null ? new[] { skin.Atlas } : skin.Clips.Values.Concat(skin.ExtraClips.Values).Concat(skin.Alternates.Values).Append(skin.Gaze!).Select(c => c.File).Concat(new[] { skin.Avatar, skin.Preview }.OfType<string>()).Distinct();
        if (skin.PackageId is { } packageId)
        {
            if (!string.Equals(Path.GetFileName(manifestPath), "pet.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("完整角色包说明请命名为 pet.json。");
            var before = LoadSkins(data);
            bool canonical = OfficialCompanionLegacy.MatchImport(root, packageId, CancellationToken.None) != null;
            if (!canonical) OfficialCompanionIdentity.ResolveCustom(data, packageId, before.Where(s => s.OfficialCharacterId != null).Select(s => s.Id), !before.Any(s => s.PickerKey == "custom-pack:" + packageId));
            CompanionPackStorage.Install(root, data, packageId, references, copied => { using var input = File.OpenRead(Path.Combine(copied, "pet.json")); ValidateSkin(ReadManifest(input, packageId, copied)); });
            CompanionPackStorage.SetHidden(data, packageId, false);
            CompanionPackStorage.SetHidden(data, (canonical ? "official:" : "custom-pack:") + packageId, false);
            return LoadSkins(data).Single(s => s.PickerKey == (canonical ? "official:" : "custom-pack:") + packageId);
        }
        var staging = Path.Combine(data, "skins", ".import-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(data, "skins", id);
        Directory.CreateDirectory(staging);
        try
        {
            var files = references;
            long total = 0;
            foreach (var relative in files)
            {
                var source = SafePath(root, relative, skin.Atlas != null); total += new FileInfo(source).Length;
                if (total > 256L * 1024 * 1024) throw new InvalidDataException("外观资源总大小不能超过 256 MB。");
                var target = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
            }
            File.Copy(manifestPath, Path.Combine(staging, "pet.json"));
            using (var copied = File.OpenRead(Path.Combine(staging, "pet.json"))) ValidateSkin(ReadManifest(copied, id, staging));
            Directory.Move(staging, destination);
            var aliases = new Dictionary<string,string>(); var loaded = LoadSkins(data, aliases, true);
            return loaded.First(s => s.Id == (aliases.TryGetValue(id, out var retained) ? retained : id));
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    public Bitmap Get(PetSkin skin, string path) => GetImage(ImageKey(skin, path), () => path.StartsWith("holiday|", StringComparison.Ordinal)
        ? OpenHoliday(path.Split('|')[2]) : skin.BuiltIn ? OpenAsset(path) : File.OpenRead(SafePath(skin.Folder!, path, skin.Atlas != null)));
    private Stream OpenHoliday(string resource) => openHoliday != null ? openHoliday(resource) : typeof(HolidayCatalog).Assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException("节日服饰资源不存在。");
    public Bitmap Get(PetSkin skin, ClipSpec clip) => clip.HolidaySource is { } source
        ? GetImage(clip.File, () => openHoliday != null ? openHoliday(source.ResourceName) : source.OpenResource() ?? throw new InvalidDataException("节日服饰资源不存在。"))
        : Get(skin, clip.File);
    internal Bitmap GetPreview(OutfitDefinition outfit) => GetImage("preview|" + outfit.SourceKey + "|" + outfit.Preview,
        () => openHoliday != null ? openHoliday("Dafeiyu.Holidays." + outfit.Preview.Replace('/', '.')) : outfit.OpenPreview() ?? throw new InvalidDataException("服饰预览不存在。"));
    private Bitmap GetImage(string key, Func<Stream> open)
    {
        if (!images.TryGetValue(key, out var image))
        {
            using var stream = open();
            if (stream.Length > 64L * 1024 * 1024) throw new InvalidDataException("单张外观图不能超过 64 MB。");
            Span<byte> header = stackalloc byte[24]; stream.ReadExactly(header); stream.Position = 0;
            if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("外观图像必须是 PNG。");
            var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]); var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
            if (width == 0 || height == 0 || width > 8192 || height > 33280 || (long)width * height > 55000000) throw new InvalidDataException("外观 PNG 尺寸过大或无效。");
            image = new Bitmap(stream);
            if (images.Count >= 5)
            {
                var oldest = images.Keys.First(); images[oldest].Dispose(); images.Remove(oldest);
                foreach (var mask in masks.Keys.Where(k => k.Item1.StartsWith(oldest + "/", StringComparison.Ordinal)).ToArray()) masks.Remove(mask);
            }
            images[key] = image;
        }
        return image;
    }
    private static string ImageKey(PetSkin skin, string path) => path.StartsWith("holiday|", StringComparison.Ordinal) ? path : skin.SourceKey + "/" + path;
    public static ClipSpec OutfitClip(OutfitDefinition outfit, OutfitClipDefinition clip) => new("holiday|" + outfit.CharacterId + ":" + outfit.OutfitId + ":" + outfit.Version + ":" + clip.SourceKey + "|" + clip.ResourceName, clip.Columns, clip.FrameCount, clip.Fps, clip.Loop, clip.FrameWidth, clip.FrameHeight, OffsetX: clip.OffsetX, OffsetY: clip.OffsetY, HolidaySource: clip);
    internal static bool IsHolidayClip(ClipSpec clip) => clip.File.StartsWith("holiday|", StringComparison.Ordinal);
    internal void PrepareFrame(PetSkin skin, ClipSpec clip, int frame)
    {
        var image = Get(skin, clip); var rect = Rect(clip, frame);
        if (frame < 0 || frame >= clip.Frames || rect.Right > image.PixelSize.Width || rect.Bottom > image.PixelSize.Height) throw new InvalidDataException("服饰帧超出图像范围。");
        if (IsHolidayClip(clip)) _ = Mask(skin, clip, frame);
    }
    internal static ClipSpec MatchSourceTiming(ClipSpec visual, ClipSpec source) => visual with { Fps = visual.Frames * source.Fps / source.Frames, Loop = source.Loop, HoldLastSeconds = source.HoldLastSeconds };
    public static string? AvatarPath(PetSkin skin) => skin.Avatar ?? (skin.OfficialCharacterId != null ? "@official/" + skin.OfficialCharacterId : skin.BuiltIn && skin.Id is "e-bao" or "dafeiyu" or "marfu" or "xiaobei" ? "avatars/" + skin.Id + ".png" : null);
    public Bitmap? GetAvatar(PetSkin skin) => AvatarPath(skin) is { } path ? path.StartsWith("@official/", StringComparison.Ordinal) ? GetImage(path, () => OpenAsset("avatars/" + path.Substring(10) + ".png")) : Get(skin, path) : null;
    public static bool HasExtra(PetSkin skin,string action) => skin.ExtraClips.ContainsKey(action) || skin.Alternates.ContainsKey(action) || skin.BuiltIn && skin.PackageId == null && PairedActions.Contains(action);
    public static void Remove(PetSkin skin,string data)
    {
        if (skin.OfficialCharacterId == "e-bao") throw new InvalidOperationException("默认伙伴用于离线启动，不能删除。");
        if (LoadSkins(data).Count <= 1) throw new InvalidOperationException("至少保留一位伙伴。");
        CompanionPackStorage.SetHidden(data, skin.OfficialCharacterId != null ? skin.PickerKey : skin.PackageId ?? skin.Id, true);
    }
    public ClipSpec Resolve(PetSkin skin, string action, bool original, OutfitDefinition? outfit = null)
    {
        if (skin.IsPlaceholder) throw new InvalidOperationException("请先下载完整角色包。");
        if (outfit != null && skin.BuiltinCharacterId == outfit.CharacterId)
        {
            var useAlternate = !original && outfit.FindClip(action, "alternate") != null;
            var clip = outfit.FindClip(action, useAlternate ? "alternate" : "default") ?? throw new InvalidDataException("节日服饰缺少当前动作。");
            return MatchSourceTiming(OutfitClip(outfit, clip), Resolve(skin, action, original));
        }
        if (!original && skin.Alternates.TryGetValue(action, out var alternate)) return alternate;
        if (!original && action == "jumping" && skin.BuiltIn && (skin.OfficialCharacterId ?? skin.Id) == "e-bao")
            return new ClipSpec("companion-animations/e-bao/jumping.png", 8, 8, 8, false);
        if (skin.ExtraClips.TryGetValue(action, out var extra)) return extra;
        if (skin.Alternates.ContainsKey(action) && skin.Clips.TryGetValue(action, out var first)) return first;
        if (skin.BuiltIn && skin.PackageId == null && PairedActions.Contains(action))
        {
            var assetId = skin.OfficialCharacterId ?? skin.Id;
            if (!original) return new ClipSpec($"companion-animations/{assetId}/{action}.png", 8, 8, 8, action is "sleeping" or "lifted");
            if (action is "special2" or "stumble") return new ClipSpec($"additional/{assetId}.png", 4, 8, 8, false, Top: action == "stumble" ? 0 : 1040);
        }
        if (skin.Clips.TryGetValue(action, out var manifest)) return manifest with { Fps = skin.PackageId == null && Interactions.Contains(action) ? 8 : manifest.Fps };
        if (Interactions.Contains(action) && skin.BuiltIn) return new ClipSpec($"{skin.OfficialCharacterId ?? skin.Id}-interactions.png", 4, 4, 8, action is "sleeping" or "lifted", Top: Array.IndexOf(Interactions, action) * 520);
        var row = Array.IndexOf(AnimationPlayer.States, action); if (row < 0) row = 0;
        if (skin.BuiltIn && (skin.OfficialCharacterId ?? skin.Id) is "dafeiyu" or "marfu" && row == 2) row = 1;
        var atlas = Get(skin, skin.Atlas!); var w = atlas.PixelSize.Width / 8; var h = atlas.PixelSize.Height / 11;
        return new ClipSpec(skin.Atlas!, 8, AnimationPlayer.Counts[row], action == "jumping" ? 1 / .13 : 8, action is "idle" or "running" or "running-right" or "running-left", w, h, row * h, HoldLastSeconds: action == "jumping" ? skin.OfficialCharacterId == "e-bao" || skin.Id == "e-bao" ? .35 : .05 : 0);
    }
    public ClipSpec Gaze(PetSkin skin, OutfitDefinition? outfit = null)
    {
        if (outfit != null && skin.BuiltinCharacterId == outfit.CharacterId) return OutfitClip(outfit, outfit.Gaze);
        if (skin.Gaze != null) return skin.Gaze;
        var atlas = Get(skin, skin.Atlas!); return new ClipSpec(skin.Atlas!, 8, 16, 8, true, atlas.PixelSize.Width / 8, atlas.PixelSize.Height / 11, atlas.PixelSize.Height / 11 * 9);
    }
    public static PixelRect Rect(ClipSpec spec, int frame) => new(frame % spec.Columns * spec.Width, spec.Top + frame / spec.Columns * spec.Height, spec.Width, spec.Height);
    private byte[] Mask(PetSkin skin, ClipSpec spec, int frame)
    {
        var key = (ImageKey(skin, spec.File) + "/" + spec.Top + "/" + spec.Width + "/" + spec.Height + "/" + spec.Columns, frame);
        if (masks.TryGetValue(key, out var result)) return result;
        var bytes = new byte[spec.Width * spec.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { Get(skin, spec).CopyPixels(Rect(spec, frame), handle.AddrOfPinnedObject(), bytes.Length, spec.Width * 4); }
        finally { handle.Free(); }
        result = new byte[spec.Width * spec.Height];
        for (int i = 0; i < result.Length; i++) result[i] = bytes[i * 4 + 3];
        // Some native decoders accept broken IDAT as empty pixels rather than
        // throwing. A holiday frame must still have a character and transparency.
        if (IsHolidayClip(spec) && (!result.Any(b => b > 16) || !result.Any(b => b == 0))) throw new InvalidDataException("节日服饰帧无法解码为有效透明人物。");
        masks[key] = result; return result;
    }
    private void ValidateFrame(PetSkin skin, ClipSpec spec, int frame)
    {
        var mask = Mask(skin, spec, frame);
        if (!mask.Any(b => b > 16) || !mask.Any(b => b == 0)) throw new InvalidDataException("外观每帧需要可见人物和透明背景。");
    }
    public bool Hit(PetSkin skin, ClipSpec spec, int frame, double x, double y)
    {
        var px = (int)(x * spec.Width); var py = (int)(y * spec.Height);
        return px >= 0 && py >= 0 && px < spec.Width && py < spec.Height && Mask(skin, spec, frame)[py * spec.Width + px] > 16;
    }
    public void Dispose() { foreach (var image in images.Values) image.Dispose(); images.Clear(); masks.Clear(); }
}

internal sealed class SpriteView : Control
{
    public required SpriteAssets Assets { get; init; }
    public required PetSkin Skin { get; set; }
    public new ClipSpec? Clip { get; set; }
    public int Frame { get; set; }
    public bool MirrorX { get; set; }
    public event Action? FrameRendered;
    public event Action<Exception>? HolidayFrameFailed;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Clip == null) return;
        try { DrawFrame(context); }
        catch (Exception error) when (CanRecover(error)) { HolidayFrameFailed!(error); if (Clip == null || SpriteAssets.IsHolidayClip(Clip)) return; DrawFrame(context); }
        FrameRendered?.Invoke();
    }
    private bool CanRecover(Exception error) => Clip != null && SpriteAssets.IsHolidayClip(Clip) && HolidayFrameFailed != null && (error is InvalidDataException or IOException or ArgumentException or InvalidOperationException or ExternalException);
    private void DrawFrame(DrawingContext context)
    {
        var clip = Clip!; Assets.PrepareFrame(Skin, clip, Frame); var r = SpriteAssets.Rect(clip, Frame);
        using var transform = context.PushTransform(MirrorX ? new Matrix(-1, 0, 0, 1, Bounds.Width, 0) : Matrix.Identity);
        context.DrawImage(Assets.Get(Skin, clip), new Rect(r.X, r.Y, r.Width, r.Height), new Rect(clip.OffsetX / clip.Width * Bounds.Width, clip.OffsetY / clip.Height * Bounds.Height, Bounds.Width, Bounds.Height));
    }
    public bool Hits(Point point)
    {
        try { return HitFrame(point); }
        catch (Exception error) when (CanRecover(error)) { HolidayFrameFailed!(error); return Clip != null && !SpriteAssets.IsHolidayClip(Clip) && HitFrame(point); }
    }
    private bool HitFrame(Point point) => Clip != null && Bounds.Width > 0 && Bounds.Height > 0 && Assets.Hit(Skin, Clip, Frame, (MirrorX ? 1 - point.X / Bounds.Width : point.X / Bounds.Width) - Clip.OffsetX / Clip.Width, point.Y / Bounds.Height - Clip.OffsetY / Clip.Height);
}
