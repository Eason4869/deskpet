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

internal sealed record ClipSpec(string File, int Columns, int Frames, double Fps, bool Loop, int Width = 480, int Height = 520, int Top = 0);

internal sealed class PetSkin
{
    public required string Id { get; set; }
    public required string Name { get; init; }
    public required string Prompt { get; init; }
    public string? Folder { get; init; }
    public bool BuiltIn => Folder == null;
    public string? Atlas { get; init; }
    public string SpecialAction { get; init; } = "伙伴招牌";
    public string SpecialAction2 { get; init; } = "第二招牌";
    public string? PackageId { get; init; }
    public string? PackVersion { get; init; }
    public string? Avatar { get; init; }
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
    private readonly Dictionary<string, Bitmap> images = new();
    private readonly Dictionary<(string, int), byte[]> masks = new();
    public static readonly string[] Interactions = { "lifted", "landing", "petting", "poked", "feeding", "happy", "sleeping", "special" };
    public static Stream OpenAsset(string path) => AssetLoader.Open(new Uri("avares://DeskPet/Assets/" + path.Replace('\\', '/')));
    public static readonly string[] PairedActions = { "lifted", "landing", "feeding", "happy", "sleeping", "special", "special2", "stumble" };
    public static List<PetSkin> LoadSkins(string data, Dictionary<string,string>? aliases = null, bool restoreAliases = false)
    {
        var skins = new List<PetSkin>();
        foreach (var item in new[] { ("e-bao", "E包", "e-bao-spritesheet.png", PersonaProfiles.Ebao), ("dafeiyu", "大肥鱼", "spritesheet.png", PersonaProfiles.Dafeiyu), ("marfu", "马尔芙", "marfu-spritesheet.png", PersonaProfiles.Marfu) })
            skins.Add(new PetSkin { Id = item.Item1, Name = item.Item2, Atlas = item.Item3, Prompt = item.Item4 });
        using (var source = OpenAsset("bundled-skins/catalog.json"))
        using (var registry = JsonDocument.Parse(source)) foreach (var entry in registry.RootElement.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString()!;
            if (!CompanionPackStorage.ValidId(id)) throw new InvalidDataException("内置角色包 ID 无效。");
            using var manifest = OpenAsset($"bundled-skins/{id}/pet.json");
            skins.Add(ReadManifest(manifest, CompanionPackStorage.Identity(data, id), null, $"bundled-skins/{id}/"));
        }
        var supplied = skins.Where(s => s.PackageId != null).ToArray();
        foreach (var pack in CompanionPackStorage.Installed(data))
        {
            try {
                using var source = File.OpenRead(Path.Combine(pack.Value, "pet.json"));
                var skin = ReadManifest(source, CompanionPackStorage.Identity(data, pack.Key), pack.Value);
                if (skin.PackageId != pack.Key) continue;
                ValidateSkin(skin); var index = skins.FindIndex(s => s.PackageId == skin.PackageId);
                if (index >= 0) skins[index] = skin; else skins.Add(skin);
            } catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException) { System.Diagnostics.Trace.WriteLine("Character pack: " + e.GetType().Name); }
        }
        var root = Path.Combine(data, "skins");
        if (Directory.Exists(root)) foreach (var folder in Directory.GetDirectories(root))
        {
            if (Path.GetFileName(folder).StartsWith(".import-", StringComparison.Ordinal)) continue;
            try { using var source = File.OpenRead(Path.Combine(folder, "pet.json")); var skin = ReadManifest(source, Path.GetFileName(folder), folder); ValidateSkin(skin); skins.Add(skin); }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException) { System.Diagnostics.Trace.WriteLine("Skin load: " + e.GetType().Name); }
        }
        foreach (var legacy in skins.Where(s => !s.BuiltIn && s.PackageId == null && s.Gaze != null).ToArray())
        {
            var original = supplied.Concat(skins.Where(s => s.PackageId != null)).FirstOrDefault(p => SameArtwork(legacy, p));
            if (original == null) continue;
            var current = skins.First(s => s.PackageId == original.PackageId);
            current.Id = CompanionPackStorage.RetainIdentity(data, original.PackageId!, legacy.Id);
            if (aliases != null) aliases[legacy.Id] = current.Id;
            if (restoreAliases) CompanionPackStorage.SetHidden(data, current.PackageId!, false);
            skins.Remove(legacy);
        }
        var hidden = CompanionPackStorage.Hidden(data); var fallback = skins[0];
        skins.RemoveAll(s => hidden.Contains(s.PackageId ?? s.Id)); if (skins.Count == 0) skins.Add(fallback);
        return skins;
    }
    private static bool SameArtwork(PetSkin left, PetSkin right)
    {
        if (left.Name != right.Name || left.Prompt != right.Prompt) return false;
        foreach (var action in AnimationPlayer.States.Concat(Interactions))
        {
            var a = left.Clips[action]; var b = right.Clips[action];
            if (a.Frames != b.Frames || a.Fps != b.Fps || a.Loop != b.Loop || !SameFile(left, a.File, right, b.File)) return false;
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
    private static PetSkin ReadManifest(Stream stream, string id, string? folder, string prefix = "")
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
            skin.Clips[property.Name] = new ClipSpec(prefix + a.GetProperty("file").GetString(), columns, frames, fps, a.GetProperty("loop").GetBoolean());
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
                pair.Item2[property.Name] = new ClipSpec(prefix + a.GetProperty("file").GetString(), 8, count, fps, a.GetProperty("loop").GetBoolean());
            }
        }
        foreach (var pair in skin.Alternates)
        {
            if (!skin.Clips.TryGetValue(pair.Key, out var first) && !skin.ExtraClips.TryGetValue(pair.Key, out first)) throw new InvalidDataException("第二套动作缺少第一套。");
            if (pair.Value.Loop != first.Loop || pair.Key == "jumping" && Math.Abs(pair.Value.Frames / pair.Value.Fps - first.Frames / first.Fps) > 1e-8) throw new InvalidDataException("两套动作的循环或跳跃时长不一致。");
        }
        return skin;
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
            CompanionPackStorage.Install(root, data, packageId, references, copied => { using var input = File.OpenRead(Path.Combine(copied, "pet.json")); ValidateSkin(ReadManifest(input, packageId, copied)); });
            CompanionPackStorage.SetHidden(data, packageId, false);
            return LoadSkins(data).Single(s => s.PackageId == packageId);
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
    public Bitmap Get(PetSkin skin, string path)
    {
        var key = ImageKey(skin, path);
        if (!images.TryGetValue(key, out var image))
        {
            using var stream = skin.BuiltIn ? OpenAsset(path) : File.OpenRead(SafePath(skin.Folder!, path, skin.Atlas != null));
            if (stream.Length > 64L * 1024 * 1024) throw new InvalidDataException("单张外观图不能超过 64 MB。");
            Span<byte> header = stackalloc byte[24]; stream.ReadExactly(header); stream.Position = 0;
            if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("外观图像必须是 PNG。");
            var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]); var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
            if (width == 0 || height == 0 || width > 8192 || height > 33280 || (long)width * height > 55000000) throw new InvalidDataException("外观 PNG 尺寸过大或无效。");
            image = new Bitmap(stream); images[key] = image;
        }
        return image;
    }
    private static string ImageKey(PetSkin skin, string path) => (skin.BuiltIn ? "builtin/" + skin.Id : "custom/" + Path.GetFullPath(skin.Folder!)) + "/" + path;
    public static string? AvatarPath(PetSkin skin) => skin.Avatar ?? (skin.BuiltIn && skin.Id is "e-bao" or "dafeiyu" or "marfu" or "xiaobei" ? "avatars/" + skin.Id + ".png" : null);
    public Bitmap? GetAvatar(PetSkin skin) => AvatarPath(skin) is { } path ? Get(skin, path) : null;
    public static bool HasExtra(PetSkin skin,string action) => skin.ExtraClips.ContainsKey(action) || skin.Alternates.ContainsKey(action) || skin.BuiltIn && skin.PackageId == null && PairedActions.Contains(action);
    public static void Remove(PetSkin skin,string data)
    {
        if (LoadSkins(data).Count <= 1) throw new InvalidOperationException("至少保留一位伙伴。");
        CompanionPackStorage.SetHidden(data, skin.PackageId ?? skin.Id, true);
    }
    public ClipSpec Resolve(PetSkin skin, string action, bool original)
    {
        if (!original && skin.Alternates.TryGetValue(action, out var alternate)) return alternate;
        if (skin.ExtraClips.TryGetValue(action, out var extra)) return extra;
        if (skin.Alternates.ContainsKey(action) && skin.Clips.TryGetValue(action, out var first)) return first;
        if (skin.BuiltIn && skin.PackageId == null && PairedActions.Contains(action))
        {
            if (!original) return new ClipSpec($"companion-animations/{skin.Id}/{action}.png", 8, 8, 8, action is "sleeping" or "lifted");
            if (action is "special2" or "stumble") return new ClipSpec($"additional/{skin.Id}.png", 4, 8, 8, false, Top: action == "stumble" ? 0 : 1040);
        }
        if (skin.Clips.TryGetValue(action, out var manifest)) return manifest with { Fps = skin.PackageId == null && Interactions.Contains(action) ? 8 : manifest.Fps };
        if (Interactions.Contains(action) && skin.BuiltIn) return new ClipSpec($"{skin.Id}-interactions.png", 4, 4, 8, action is "sleeping" or "lifted", Top: Array.IndexOf(Interactions, action) * 520);
        var row = Array.IndexOf(AnimationPlayer.States, action); if (row < 0) row = 0;
        if (skin.BuiltIn && skin.Id is "dafeiyu" or "marfu" && row == 2) row = 1;
        var atlas = Get(skin, skin.Atlas!); var w = atlas.PixelSize.Width / 8; var h = atlas.PixelSize.Height / 11;
        return new ClipSpec(skin.Atlas!, 8, AnimationPlayer.Counts[row], 8, action is "idle" or "running" or "running-right" or "running-left", w, h, row * h);
    }
    public ClipSpec Gaze(PetSkin skin)
    {
        if (skin.Gaze != null) return skin.Gaze;
        var atlas = Get(skin, skin.Atlas!); return new ClipSpec(skin.Atlas!, 8, 16, 8, true, atlas.PixelSize.Width / 8, atlas.PixelSize.Height / 11, atlas.PixelSize.Height / 11 * 9);
    }
    public static PixelRect Rect(ClipSpec spec, int frame) => new(frame % spec.Columns * spec.Width, spec.Top + frame / spec.Columns * spec.Height, spec.Width, spec.Height);
    private byte[] Mask(PetSkin skin, ClipSpec spec, int frame)
    {
        var key = (ImageKey(skin, spec.File) + "/" + spec.Top, frame);
        if (masks.TryGetValue(key, out var result)) return result;
        var bytes = new byte[spec.Width * spec.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { Get(skin, spec.File).CopyPixels(Rect(spec, frame), handle.AddrOfPinnedObject(), bytes.Length, spec.Width * 4); }
        finally { handle.Free(); }
        result = new byte[spec.Width * spec.Height];
        for (int i = 0; i < result.Length; i++) result[i] = bytes[i * 4 + 3];
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
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Clip == null) return;
        var r = SpriteAssets.Rect(Clip, Frame);
        using var transform = context.PushTransform(MirrorX ? new Matrix(-1, 0, 0, 1, Bounds.Width, 0) : Matrix.Identity);
        context.DrawImage(Assets.Get(Skin, Clip.File), new Rect(r.X, r.Y, r.Width, r.Height), new Rect(Bounds.Size));
    }
    public bool Hits(Point point) => Clip != null && Bounds.Width > 0 && Bounds.Height > 0 && Assets.Hit(Skin, Clip, Frame, MirrorX ? 1 - point.X / Bounds.Width : point.X / Bounds.Width, point.Y / Bounds.Height);
}
