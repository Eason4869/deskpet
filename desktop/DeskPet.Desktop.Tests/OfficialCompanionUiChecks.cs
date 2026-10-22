using Dafeiyu;
using DeskPet.Desktop;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;

internal static class OfficialCompanionUiChecks
{
    // Opt-in acceptance for authored release candidates. CI transfers only the
    // index and five ZIPs; local authoring can also compare the original files.
    internal static int RunActualMaterials(string? materialRoot, string indexPath, string reportPath)
    {
        if (materialRoot != null) materialRoot = Path.GetFullPath(materialRoot);
        indexPath = Path.GetFullPath(indexPath); reportPath = Path.GetFullPath(reportPath);
        using var json = JsonDocument.Parse(File.ReadAllText(indexPath));
        var records = json.RootElement.GetProperty("companions").EnumerateArray().ToArray();
        if (records.Length != 5 || records.Select(r => r.GetProperty("id").GetString()).Distinct().Count() != 5) throw new InvalidDataException("Expected complete five-role candidate index.");
        var results = new List<object>();
        using var validator = new OfficialCompanionNativeValidator();
        var stagingRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DeskPet-AvaloniaNative-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(stagingRoot);
        try
        {
        foreach (var profile in OfficialCompanionProfiles.All)
        {
            var entry = records.Single(r => r.GetProperty("id").GetString() == profile.Id);
            string file = entry.GetProperty("asset_name").GetString()!;
            string zip = CompanionPackStorage.AssetPath(Path.GetDirectoryName(indexPath)!, file);
            string hash;
            using (var input = File.OpenRead(zip)) hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            if (new FileInfo(zip).Length != entry.GetProperty("size").GetInt64() || hash != entry.GetProperty("sha256").GetString()) throw new InvalidDataException("Candidate ZIP differs from local index.");
            var asset = new OfficialCompanionAsset(profile.Id, entry.GetProperty("name").GetString()!, entry.GetProperty("version").GetString()!, entry.GetProperty("min_app_version").GetString()!, "companions-v" + entry.GetProperty("version").GetString(), file, new FileInfo(zip).Length, hash,
                entry.GetProperty("outfits").EnumerateArray().Select(o => new OfficialCompanionOutfit(o.GetProperty("outfit_id").GetString()!, o.GetProperty("version").GetString()!)));
            string staged = Path.Combine(stagingRoot, profile.Id);
            OfficialCompanionPackageValidation.Extract(zip, staged, CancellationToken.None);
            if (materialRoot != null) foreach (string relative in OfficialCompanionStorage.Inventory(staged))
            {
                using var extracted = File.OpenRead(CompanionPackStorage.AssetPath(staged, relative));
                using var authored = File.OpenRead(CompanionPackStorage.AssetPath(Path.Combine(materialRoot, profile.Id), relative));
                if (extracted.Length != authored.Length || !SHA256.HashData(extracted).SequenceEqual(SHA256.HashData(authored))) throw new InvalidDataException("Authored material differs from candidate ZIP.");
            }
            var installation = new OfficialCompanionInstallation(asset, staged);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            OfficialCompanionPackageValidation.Validate(installation.RootDirectory, asset, CancellationToken.None);
            // Full HD candidates are much larger than the UI fixtures. The
            // validator is entirely off-dispatcher; the CI job bounds its run.
            validator.ValidateAsync(installation, CancellationToken.None).GetAwaiter().GetResult();
            var pngs = Directory.EnumerateFiles(installation.RootDirectory, "*.png", SearchOption.AllDirectories).ToArray();
            results.Add(new { id = profile.Id, pngs = pngs.Length, zipSha256 = hash, milliseconds = clock.ElapsedMilliseconds, native = true });
            Console.WriteLine("PASS actual native " + profile.Id + ": " + pngs.Length + " PNGs");
        }
        }
        finally
        {
            var tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!stagingRoot.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(stagingRoot).StartsWith("DeskPet-AvaloniaNative-", StringComparison.Ordinal)) throw new InvalidDataException("Native QA staging path escaped temporary directory.");
            Directory.Delete(stagingRoot, true);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { materialRoot, indexPath, backend = "Avalonia Skia Headless on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription, results }, new JsonSerializerOptions { WriteIndented = true }));
        return results.Count;
    }
    internal static int RunThin(string directory)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Official thin: " + message); checks++; }
        var data = Path.Combine(directory, "data");
        var settings = new PetSettings { SkinId = "dafeiyu", SelectedCompanionKey = "official:dafeiyu", PersonaProfileVersion = 1, SystemPrompt = "原有大肥鱼人格" };
        settings.PersonaOverrides["dafeiyu"] = "原有大肥鱼人格";
        new SettingsStore(Path.Combine(data, "settings.json")).Save(settings);
        var fake = new FakeSource();
        using (var owner = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(data), officialSourceFactory: _ => fake))
        {
            Check(owner.Skins.Count(s => s.OfficialCharacterId != null) == 5 && owner.Skins.Count(s => s.IsPlaceholder) == 4, "production contains E plus four lightweight placeholders");
            Check(owner.Skin.OfficialCharacterId == "e-bao" && owner.Settings.PendingOfficialCompanionId == "dafeiyu", "missing selected role uses E while preserving pending request");
            Check(owner.Settings.PersonaOverrides["dafeiyu"] == "原有大肥鱼人格" && fake.Calls == 0, "offline startup preserves desired persona and does not start HTTP in constructor");
            using var assets = new SpriteAssets();
            foreach (var entry in owner.Skins)
            {
                Check(assets.GetAvatar(entry) is { PixelSize.Width: > 100 }, "all official placeholders have native decoded avatars");
                if (!entry.IsPlaceholder) foreach (var action in AnimationPlayer.States.Concat(SpriteAssets.Interactions).Concat(new[] { "special2", "stumble" })) foreach (bool original in new[] { false, true }) { var clip = assets.Resolve(entry, action, original); assets.PrepareFrame(entry, clip, 0); checks++; }
            }
            foreach (var missing in new[] { "spritesheet.png", "marfu-spritesheet.png", "bundled-skins/xiaobei/pet.json", "bundled-skins/gugugaga/pet.json", "companion-animations/dafeiyu/feeding.png" })
                Check(!Avalonia.Platform.AssetLoader.Exists(new Uri("avares://DeskPet/Assets/" + missing)), "production omits full non-E resource " + missing);
            Check(new HolidayCatalog().Outfits.Count == 1 && new HolidayCatalog().Outfits.Single().CharacterId == "e-bao", "actual Core has E-only holiday catalog");
            Finish(owner);
        }
        using (var restarted = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(data), officialSourceFactory: _ => new FakeSource()))
        {
            Check(restarted.Settings.PendingOfficialCompanionId == "dafeiyu", "offline pending desired role survives restart");
            Complete(restarted.SelectSkinAsync(restarted.Skin, CompanionSelectionOrigin.PetMenu));
            Check(restarted.Settings.PendingOfficialCompanionId == "", "explicit current E supersedes automatic restore");
            Finish(restarted);
        }
        return checks;
    }
    internal static int RunIdentity(string directory)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Official identity: " + message); checks++; }
        var sourceRoot = Path.Combine(directory, "legacy-custom"); Directory.CreateDirectory(sourceRoot);
        using (var atlas = new WriteableBitmap(new PixelSize(512, 704), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul))
        {
            var pixels = new byte[512 * 704 * 4];
            for (int row = 0; row < 11; row++) for (int col = 0; col < 8; col++) for (int y = 16; y < 48; y++) for (int x = 16; x < 48; x++) { int at = ((row * 64 + y) * 512 + col * 64 + x) * 4; pixels[at] = 140; pixels[at + 3] = 255; }
            using (var buffer = atlas.Lock()) Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
            atlas.Save(Path.Combine(sourceRoot, "atlas.png"), new PngBitmapEncoderOptions());
        }
        File.WriteAllText(Path.Combine(sourceRoot, "pet.json"), "{\"version\":1,\"id\":\"e-bao\",\"name\":\"历史自定义\",\"atlas\":\"atlas.png\",\"system_prompt\":\"custom\"}");
        var data = Path.Combine(directory, "data");
        CompanionPackStorage.Install(sourceRoot, data, "e-bao", new[] { "atlas.png" }, _ => { });
        var runtime = new StableRuntime(data); runtime.InitializeProtection();
        var history = new HistoryStore(Path.Combine(data, "history"));
        history.SaveAsync("e-bao", new[] { new ChatMessage("user", "旧伙伴的记录") }).GetAwaiter().GetResult();
        var settings = new PetSettings { SkinId = "e-bao", SystemPrompt = "已有自定义人格", PersonaProfileVersion = 1, SaveChatHistory = true };
        settings.PersonaOverrides["e-bao"] = "已有自定义人格";
        new SettingsStore(Path.Combine(data, "settings.json")).Save(settings);
        var fake = new FakeSource();
        using (var owner = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), runtime, officialSourceFactory: _ => fake))
        {
            var official = owner.Skins.Single(s => s.PickerKey == "official:e-bao");
            Check(owner.Skin.PickerKey == "custom-pack:e-bao" && owner.Skin.Id == "e-bao" && owner.Settings.SystemPrompt == "已有自定义人格", "ambiguous historical custom remains selected with original persona");
            Check(official.Id.StartsWith("skin-", StringComparison.Ordinal) && official.BuiltinCharacterId == "e-bao" && official.Id != owner.Skin.Id, "official placeholder receives separate stable persona without trusting custom ID");
            Check(owner.Session.History.Messages.Any(m => m.Content == "旧伙伴的记录"), "old custom conversation remains loaded");
            using var assets = new SpriteAssets();
            foreach (var action in new[] { "idle", "feeding", "stumble", "special2", "jumping" })
            {
                var clip = assets.Resolve(official, action, true); assets.PrepareFrame(official, clip, 0);
                Check(true, "embedded E alias decodes " + action);
            }
            var gaze = assets.Gaze(official); assets.PrepareFrame(official, gaze, 15);
            Check(gaze.Frames == 16, "embedded E alias keeps legacy gaze stride");
            var alias = official.Id;
            Check(SpriteAssets.LoadSkins(data).Single(s => s.PickerKey == "official:e-bao").Id == alias, "official alias survives catalog reload");
            Finish(owner);
        }
        Check(history.Load("e-bao").Any(m => m.Content == "旧伙伴的记录"), "collision allocation preserves durable custom history");
        return checks;
    }
    internal static int RunNative(string directory)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Official native: " + message); checks++; }
        using var first = new HolidayReviewChecks.Fixture(Path.Combine(directory, "source-one"), "e-bao", null, false);
        using var second = new HolidayReviewChecks.Fixture(Path.Combine(directory, "source-two"), "e-bao", null, false);
        Paint(Path.Combine(second.Root, "normal.png"), 3840, 520, 8, 210);
        Paint(Path.Combine(second.Root, "preview.png"), 480, 520, 1, 210);
        var next = new HolidayCatalog(second.Root).Outfits.Single();
        var skin = first.Owner.Skin;
        using (var assets = new SpriteAssets())
        {
            var oldClip = SpriteAssets.OutfitClip(first.Outfit, first.Outfit.FindClip("idle", "default"));
            var newClip = SpriteAssets.OutfitClip(next, next.FindClip("idle", "default"));
            Check(oldClip.File != newClip.File, "same id/version roots have distinct native cache keys");
            var oldImage = assets.Get(skin, oldClip); var newImage = assets.Get(skin, newClip);
            Check(!ReferenceEquals(oldImage, newImage) && Pixel(oldImage) != Pixel(newImage), "captured clips keep their immutable original pixels");
            Check(Pixel(assets.GetPreview(first.Outfit)) != Pixel(assets.GetPreview(next)), "preview opener is immutable and source-aware");
            for (int i = 0; i < 15; i++)
            {
                var generation = new HolidayCatalog(i % 2 == 0 ? first.Root : second.Root).Outfits.Single();
                var clip = SpriteAssets.OutfitClip(generation, generation.FindClip("idle", "default"));
                assets.PrepareFrame(skin, clip, 0); _ = assets.GetPreview(generation);
            }
            var images = (System.Collections.IDictionary)typeof(SpriteAssets).GetField("images", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(assets)!;
            var masks = (System.Collections.IDictionary)typeof(SpriteAssets).GetField("masks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(assets)!;
            Check(images.Count <= 5 && masks.Count <= 5, "retired source switches keep bitmap and alpha cache bounded");
        }
        var root = Path.Combine(directory, "package"); Directory.CreateDirectory(root);
        var installation = CreatePackage(root, "e-bao", first.Root);
        var holidays = installation.HolidayRootDirectory;
        OfficialCompanionPackageValidation.Validate(root, installation.Asset, CancellationToken.None);
        Check(true, "native fixture also satisfies complete trusted pure package validation");
        using var validator = new OfficialCompanionNativeValidator();
        Complete(validator.ValidateAsync(installation, CancellationToken.None));
        Check(true, "native callback decodes full default, holiday, gaze and previews off dispatcher");
        var owner = first.Owner; var previous = owner.Skin; var history = owner.Session.History;
        var selectedKey = owner.Settings.SelectedCompanionKey; var persona = owner.Settings.SkinId;
        var newer = SpriteAssets.ReadOfficial(installation, previous.Id); newer.SourceKey += ":newer";
        var older = SpriteAssets.ReadOfficial(installation, previous.Id); older.SourceKey += ":older";
        var newerCatalog = new HolidayCatalog(holidays); var olderCatalog = new HolidayCatalog(first.Root);
        var queue = typeof(DesktopController).GetMethod("QueueAppearance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        owner.ShowGuide(); Until(() => owner.GuideLocked);
        queue.Invoke(owner, new object[] { newer, newerCatalog, previous, 0L, 20L });
        queue.Invoke(owner, new object[] { older, olderCatalog, previous, 0L, 10L });
        Check(ReferenceEquals(owner.Skin, previous), "same-role source refresh waits while guide is open");
        ((GuideWindow?)typeof(DesktopController).GetField("guide", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner))?.Close();
        Until(() => ReferenceEquals(owner.Skin, newer));
        Check(owner.Settings.SelectedCompanionKey == selectedKey && owner.Settings.SkinId == persona && ReferenceEquals(owner.Session.History, history), "same-role safe refresh preserves picker, persona and active conversation");
        queue.Invoke(owner, new object[] { older, olderCatalog, previous, 0L, 10L });
        Check(ReferenceEquals(owner.Skins.Single(s => s.PickerKey == newer.PickerKey), newer) && ReferenceEquals(owner.Holidays.Catalog.Find("e-bao", "halloween-2026"), newerCatalog.Find("e-bao", "halloween-2026")), "late same-token older appearance cannot mix newer skin with stale picker or holiday catalog");
        var autoSource = new FakeSource();
        using (var automatic = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(Path.Combine(directory, "automatic-data")), officialSourceFactory: _ => autoSource))
        {
            var fallback = automatic.Skin;
            using var cancelQuestion = new CancellationTokenSource();
            var question = automatic.Questions.AskAsync(new AgentQuestionRequest { SessionId = "busy", TaskId = "work", CallId = "call", Question = "请保留正在进行的工作" }, cancelQuestion.Token);
            var restore = automatic.SelectSkinAsync(SpriteAssets.ReadOfficial(installation, fallback.Id), CompanionSelectionOrigin.StartupRestore);
            Until(() => autoSource.InstalledCalls > 0);
            Complete(Task.Delay(200));
            Check(!restore.IsCompleted && ReferenceEquals(automatic.Skin, fallback) && automatic.Questions.Requests.Count == 1, "automatic completion waits for current business and keeps work question alive");
            cancelQuestion.Cancel(); try { question.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            Complete(restore);
            Check(automatic.Skin.Folder == root, "automatic restore adopts source only after work becomes safe");
            Finish(automatic);
        }
        var installedData = Path.Combine(directory, "installed-ebao");
        foreach (bool restart in new[] { false, true })
        {
            var installedSource = new FakeSource { InstalledValue = installation };
            using var installedOwner = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(installedData), officialSourceFactory: _ => installedSource);
            installedOwner.Settings.OnboardingState = "completed";
            var embedded = installedOwner.Skin;
            Check(embedded.BuiltIn, "constructor keeps usable embedded E without blocking on full native decode");
            var startup = (Task)typeof(DesktopController).GetMethod("StartOfficialCompanionsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(installedOwner, null)!;
            Until(() => installedOwner.Skin.Folder == root && installedSource.Calls > 0);
            Check(!startup.IsCompleted && !installedSource.Result.Task.IsCompleted, restart ? "restart prefers installed E before metadata completes" : "startup prefers installed E before metadata completes");
            installedSource.Result.TrySetResult(new OfficialCompanionResult(installation, true, false, false, "")); Complete(startup);
            Complete(installedOwner.SelectSkinAsync(embedded, CompanionSelectionOrigin.PetMenu));
            Check(installedOwner.Skin.Folder == root, "reselecting embedded E still prefers its verified installed full package");
            Finish(installedOwner);
        }
        using var bad = new HolidayReviewChecks.Fixture(Path.Combine(directory, "bad-idat"), "e-bao", "feeding", false);
        Check(bad.Outfit.Ready, "correct CRC bad IDAT fixture passes pure structural catalog check");
        foreach (var file in new[] { "bad.png", "outfit.json", "catalog.json" })
        {
            File.Copy(Path.Combine(bad.Root, file), Path.Combine(holidays, file), true);
            if (file.EndsWith(".json", StringComparison.Ordinal)) File.WriteAllText(Path.Combine(holidays, file), File.ReadAllText(Path.Combine(holidays, file)).Replace("\"version\":\"1\"", "\"version\":\"1.0.0\"", StringComparison.Ordinal));
        }
        OfficialCompanionPackageValidation.Validate(root, installation.Asset, CancellationToken.None);
        Check(true, "CRC-correct broken compressed pixels also pass complete pure package gate");
        var failed = Task.Run(() => OfficialCompanionNativeValidator.ValidateRoot(installation, CancellationToken.None));
        Until(() => failed.IsCompleted);
        Check(failed.IsFaulted, "native callback rejects structurally valid broken compressed pixels");
        checks += RunHidden(Path.Combine(directory, "hidden"));
        return checks;
    }
    private static int RunHidden(string directory)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Official hidden: " + message); checks++; }
        using var fixture = new HolidayReviewChecks.Fixture(Path.Combine(directory, "outfit"), "dafeiyu", null, false);
        var installation = CreatePackage(Path.Combine(directory, "package"), "dafeiyu", fixture.Root);
        OfficialCompanionPackageValidation.Validate(installation.RootDirectory, installation.Asset, CancellationToken.None);
        using var owner = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(Path.Combine(directory, "data")), officialSourceFactory: _ => new FakeSource());
        var entry = owner.Skins.Single(s => s.OfficialCharacterId == "dafeiyu");
        var method = typeof(DesktopController).GetMethod("PublishBulkResultAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Task Publish() => (Task)method.Invoke(owner, new object[] { installation, owner.Skin, 0L, 1L, CancellationToken.None })!;
        void Hide() { CompanionPackStorage.SetHidden(owner.DataDirectory, entry.PickerKey, true); owner.Skins.RemoveAll(s => s.PickerKey == entry.PickerKey); }
        Hide(); Complete(Publish());
        Check(!owner.Skins.Any(s => s.PickerKey == entry.PickerKey), "bulk keeps an already hidden official role hidden");
        CompanionPackStorage.SetHidden(owner.DataDirectory, entry.PickerKey, false); owner.Skins.Add(entry);
        var catalogs = typeof(DesktopController).GetField("companionCatalogs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
        Task pending;
        Monitor.Enter(catalogs);
        try { pending = Publish(); Hide(); }
        finally { Monitor.Exit(catalogs); }
        Complete(pending);
        Check(!owner.Skins.Any(s => s.PickerKey == entry.PickerKey) && owner.Holidays.Catalog.Find("dafeiyu", "halloween-2026") == null, "hide during native source preparation cannot resurrect picker or catalog");
        var next = SpriteAssets.ReadOfficial(installation, entry.Id);
        typeof(DesktopController).GetMethod("QueueAppearance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(owner, new object[] { next, new HolidayCatalog(installation.HolidayRootDirectory), entry, 0L, 2L });
        Check(!owner.Skins.Any(s => s.PickerKey == entry.PickerKey), "late inactive refresh also respects hidden membership");
        Finish(owner); return checks;
    }
    private static OfficialCompanionInstallation CreatePackage(string root, string role, string holidayRoot)
    {
        Directory.CreateDirectory(root);
        var profile = OfficialCompanionProfiles.Find(role)!;
        Paint(Path.Combine(root, "normal.png"), 3840, 520, 8, 120);
        Paint(Path.Combine(root, "gaze.png"), 1920, 2080, 4, 120);
        Paint(Path.Combine(root, "preview.png"), 480, 520, 1, 120);
        Paint(Path.Combine(root, "avatar.png"), 480, 480, 1, 120);
        object Clip(string path, bool loop = false) => new { file = path, columns = 8, frames = 8, fps = 8, loop };
        var normal = AnimationPlayer.States.Concat(SpriteAssets.Interactions).ToDictionary(action => action, action => Clip("normal.png", action == "idle"));
        var extras = new[] { "stumble", "special2" }.ToDictionary(action => action, _ => Clip("normal.png"));
        var alternateNames = SpriteAssets.PairedActions.Concat(role is "e-bao" or "gugugaga" ? new[] { "jumping" } : Array.Empty<string>());
        var alternates = alternateNames.ToDictionary(action => action, _ => Clip("normal.png"));
        File.WriteAllText(Path.Combine(root, "pet.json"), JsonSerializer.Serialize(new { version = 2, id = role, pack_version = "0.10.3-dev.1", name = profile.Name, format = "illustrated-atlases-v1", system_prompt = profile.SystemPrompt, description = profile.Description, special_action = profile.SpecialAction, special_action2 = profile.SpecialAction2, voice_presets = new { speech = new[] { "nova" }, live = new[] { "Puck" } }, cell_width = 480, cell_height = 520, animations = normal, extra_animations = extras, alternate_animations = alternates, gaze = new { file = "gaze.png", columns = 4, frames = 16, interpolation = "none", order = "clockwise-from-up" }, avatar = "avatar.png", preview = "preview.png" }));
        File.WriteAllText(Path.Combine(root, "companions.json"), JsonSerializer.Serialize(new { schema_version = 1, id = role, name = profile.Name, version = "0.10.3-dev.1", min_app_version = "0.10.3-dev.1" }));
        var holidays = Path.Combine(root, "holidays"); Directory.CreateDirectory(holidays);
        foreach (var file in new[] { "normal.png", "gaze.png", "preview.png", "outfit.json", "catalog.json" })
        {
            File.Copy(Path.Combine(holidayRoot, file), Path.Combine(holidays, file));
            if (file.EndsWith(".json", StringComparison.Ordinal)) File.WriteAllText(Path.Combine(holidays, file), File.ReadAllText(Path.Combine(holidays, file)).Replace("\"version\":\"1\"", "\"version\":\"1.0.0\"", StringComparison.Ordinal));
        }
        var asset = new OfficialCompanionAsset(role, profile.Name, "0.10.3-dev.1", "0.10.3-dev.1", "v0.10.3-dev.1", "DeskPet-Companion-" + role + "-0.10.3-dev.1.zip", 1, new string('b', 64), new[] { new OfficialCompanionOutfit("halloween-2026", "1.0.0") });
        return new OfficialCompanionInstallation(asset, root);
    }
    private static byte Pixel(Bitmap bitmap)
    {
        var bytes = new byte[4]; var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(70, 90, 1, 1), handle.AddrOfPinnedObject(), 4, 4); return bytes[0]; }
        finally { handle.Free(); }
    }
    private static void Paint(string path, int width, int height, int columns, byte color)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var bytes = new byte[width * height * 4];
        for (int row = 0; row < (height + 519) / 520; row++) for (int column = 0; column < columns; column++)
        for (int y = 80; y < 160; y++) for (int x = 60; x < 110; x++) { int at = ((row * 520 + y) * width + column * 480 + x) * 4; bytes[at] = color; bytes[at + 3] = 255; }
        using (var buffer = bitmap.Lock()) Marshal.Copy(bytes, 0, buffer.Address, bytes.Length);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }
    internal static int RunSelection(string directory)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Official selection: " + message); checks++; }
        var source = new FakeSource();
        using var owner = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(directory), officialSourceFactory: _ => source);
        var ebao = owner.Skin;
        var placeholder = new PetSkin { Id = "dafeiyu", Name = "大肥鱼", Prompt = "", OfficialCharacterId = "dafeiyu", BuiltinCharacterId = "dafeiyu", PickerKey = "official:dafeiyu", IsPlaceholder = true };
        var first = owner.SelectSkinAsync(placeholder, CompanionSelectionOrigin.PetMenu);
        Until(() => source.Calls == 1);
        Check(ReferenceEquals(owner.Skin, ebao), "missing role keeps active source while acquiring");
        Complete(owner.SelectSkinAsync(ebao, CompanionSelectionOrigin.PetMenu));
        var asset = new OfficialCompanionAsset("dafeiyu", "大肥鱼", "0.10.3-dev.1", "0.10.3-dev.1", "v0.10.3-dev.1", "DeskPet-Companion-dafeiyu-0.10.3-dev.1.zip", 1, new string('a', 64), Array.Empty<OfficialCompanionOutfit>());
        source.Result.TrySetResult(new OfficialCompanionResult(new OfficialCompanionInstallation(asset, Path.Combine(directory, "never-read-stale")), false, true, true, ""));
        Complete(first);
        Check(ReferenceEquals(owner.Skin, ebao) && owner.Settings.PendingOfficialCompanionId == "", "explicit current E selection supersedes stale completion before reading its source");
        source.Result = new TaskCompletionSource<OfficialCompanionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Settings.PendingOfficialCompanionId = "dafeiyu";
        var hiddenRequest = owner.SelectSkinAsync(placeholder, CompanionSelectionOrigin.StartupRestore);
        Until(() => source.Calls == 2);
        CompanionPackStorage.SetHidden(owner.DataDirectory, placeholder.PickerKey, true);
        owner.CompanionHidden(placeholder); owner.Skins.RemoveAll(s => s.PickerKey == placeholder.PickerKey);
        source.Result.TrySetResult(new OfficialCompanionResult(new OfficialCompanionInstallation(asset, Path.Combine(directory, "never-read-hidden")), false, true, true, ""));
        Complete(hiddenRequest);
        Check(ReferenceEquals(owner.Skin, ebao) && owner.Skins.All(s => s.PickerKey != placeholder.PickerKey) && owner.Settings.PendingOfficialCompanionId == "", "hiding an in-flight automatic role blocks late activation and clears durable pending restore");
        owner.ShowGuide(); Until(() => owner.GuideLocked);
        var before = source.Selections; var persona = owner.Settings.SkinId; var picker = owner.Settings.SelectedCompanionKey;
        Complete(owner.SelectSkinAsync(placeholder, CompanionSelectionOrigin.PetMenu));
        Complete(owner.DownloadAllOfficialAsync());
        Check(source.Selections == before && source.Calls == 2 && source.BulkCalls == 0, "guide attempts never create selection tokens or downloads");
        Check(owner.Settings.SkinId == persona && owner.Settings.SelectedCompanionKey == picker && owner.Settings.PendingOfficialCompanionId == "", "guide attempt preserves persona and pending settings");
        ((GuideWindow?)typeof(DesktopController).GetField("guide", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner))?.Close();
        Finish(owner);
        return checks;
    }
    private static void Until(Func<bool> ready)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!ready()) { if (clock.ElapsedMilliseconds > 10000) throw new TimeoutException(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Complete(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Finish(DesktopController owner) { owner.Pet.Close(); owner.Dispose(); Complete(owner.ImageServicesShutdown); }
    private sealed class StableRuntime(string directory) : DesktopRuntime
    {
        private readonly TestProtector protector = new();
        public override string DataDirectory => directory;
        public override bool ShowTray => false;
        public override void InitializeProtection() => SecretProtection.Configure(protector);
        public override IReplySpeech CreateSpeech(ApiClient api) => new TestSpeech();
        public override IAudioRecorder CreateRecorder() => new TestRecorder();
        public override IPcmMicrophone CreateMicrophone() => throw new InvalidOperationException();
        public override ILiveAudioOutput CreateLiveOutput() => throw new InvalidOperationException();
    }
    private sealed class FakeSource : IOfficialCompanionSource
    {
        internal int Calls, BulkCalls, Selections, InstalledCalls;
        internal OfficialCompanionInstallation? InstalledValue;
        internal TaskCompletionSource<OfficialCompanionResult> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long BeginSelection() => ++Selections;
        public bool OwnsSelection(long token) => token == Selections;
        public OfficialCompanionInstallation? Installed(string id) { Interlocked.Increment(ref InstalledCalls); return InstalledValue?.Asset.Id == id ? InstalledValue : null; }
        public Task<OfficialCompanionResult> EnsureAsync(string id, OfficialDownloadRouting routing, OfficialCompanionCheckMode mode, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel) { Calls++; return Result.Task; }
        public Task<IList<OfficialCompanionResult>> EnsureAllAsync(OfficialDownloadRouting routing, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel) { BulkCalls++; return Task.FromResult<IList<OfficialCompanionResult>>(Array.Empty<OfficialCompanionResult>()); }
        public void Dispose() { Result.TrySetCanceled(); }
    }
    internal static int RunTiming()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Official timing: " + message); checks++; }
        var clip = new ClipSpec("jump", 8, 5, 1 / .13, false, HoldLastSeconds: .35);
        var player = new InteractionPlayback((action, _) => action == "jumping" ? clip : new ClipSpec("idle", 8, 6, 8, true));
        player.Play("jumping", false);
        for (int i = 0; i < 5; i++) player.Advance(.13);
        Check(player.Action == "jumping" && player.Frame == 4, "five poses enter final dwell");
        player.Advance(.1); player.RefreshAppearance(); player.Advance(.1); player.Advance(.1);
        Check(player.Action == "jumping" && player.Frame == 4, "appearance refresh preserves elapsed hold");
        player.Advance(.05);
        Check(player.Action == "idle", "one second completes jump exactly once");
        var visual = SpriteAssets.MatchSourceTiming(new ClipSpec("holiday", 8, 8, 8, false), clip);
        Check(Math.Abs(visual.Frames / visual.Fps - .65) < 1e-8 && visual.HoldLastSeconds == .35, "holiday pose duration and hold remain separate");
        var alternate = new ClipSpec("alternate", 8, 8, 8, false);
        player = new InteractionPlayback((action, _) => action == "jumping" ? alternate : clip);
        player.Play("jumping", false);
        for (int i = 0; i < 8; i++) player.Advance(.125);
        Check(player.Action == "idle", "old zero-hold clips complete at their pose duration");
        player.Play("jumping", false); player.Advance(60);
        Check(player.Frame == 1, "delayed dispatcher still advances one pose");
        return checks;
    }
}
