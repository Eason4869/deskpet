using Avalonia.LogicalTree;
using Avalonia.Controls;
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
        // macOS's system temp directory may traverse /var -> /private/var.
        // Keep the extractor's strict no-links policy and use our test output.
        var stagingParent = Path.GetFullPath(AppContext.BaseDirectory);
        OfficialCompanionStorage.SafeDirectory(stagingParent);
        var stagingRoot = Path.Combine(stagingParent, "DeskPet-AvaloniaNative-" + Guid.NewGuid().ToString("N"));
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
            var parentPrefix = stagingParent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!stagingRoot.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(stagingRoot).StartsWith("DeskPet-AvaloniaNative-", StringComparison.Ordinal)) throw new InvalidDataException("Native QA staging path escaped test output directory.");
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
            Check(owner.Skin.OfficialCharacterId == "e-bao" && owner.Settings.PendingOfficialCompanionId == "", "missing selected role uses offline E without durable pending request");
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
            Check(restarted.Settings.PendingOfficialCompanionId == "", "restart never restores a stale pending request");
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
        foreach (var format in new[] { PixelFormat.Bgra8888, PixelFormat.Rgba8888 })
        {
            uint Sample(byte blue)
            {
                using var bitmap = new WriteableBitmap(new PixelSize(96, 96), new Vector(96, 96), format, AlphaFormat.Unpremul);
                var pixels = new byte[96 * 96 * 4]; int at = (90 * 96 + 70) * 4;
                pixels[at + (format.Equals(PixelFormat.Bgra8888) ? 0 : 2)] = blue; pixels[at + 3] = 255;
                using (var framebuffer = bitmap.Lock()) Marshal.Copy(pixels, 0, framebuffer.Address, pixels.Length);
                return Pixel(bitmap);
            }
            uint before = Sample(180), after = Sample(210);
            Check(before != after, "complete pixel distinguishes blue in " + format + ": " + before.ToString("X8") + " -> " + after.ToString("X8"));
        }
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
            Check(assets.RetainedPixelBytes <= 64L*1024*1024, "retired source switches keep bitmap and alpha cache bounded");
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
            Complete(restore);
            Check(autoSource.InstalledCalls==1&&ReferenceEquals(automatic.Skin,fallback)&&automatic.Questions.Requests.Count==1,"busy selection is refused without interrupting or queueing business");
            cancelQuestion.Cancel();try{question.GetAwaiter().GetResult();}catch(OperationCanceledException){}
            Check(ReferenceEquals(automatic.Skin,fallback),"business completion never replays a refused selection");
            Complete(automatic.SelectSkinAsync(SpriteAssets.ReadOfficial(installation,fallback.Id),CompanionSelectionOrigin.PetMenu));
            Check(automatic.Skin.Folder==root,"user may explicitly select again after work becomes safe");
            Finish(automatic);
        }
        var installedData = Path.Combine(directory, "installed-ebao");
        foreach (bool restart in new[] { false, true })
        {
            var installedSource = new FakeSource { InstalledValue = installation };
            using var installedOwner = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(installedData), officialSourceFactory: _ => installedSource);
            installedOwner.Settings.OnboardingState = "completed";
            var embedded = installedOwner.Skin;
            Check(embedded.Folder==root, "constructor restores compatible installed E with bounded default preparation");
            var startup = (Task)typeof(DesktopController).GetMethod("StartOfficialCompanionsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(installedOwner, null)!;
            Complete(startup);
            Check(installedOwner.Skin.Folder==root&&installedSource.Calls==0,"installed E startup remains offline across restart");
            installedSource.Result.TrySetResult(new OfficialCompanionResult(installation,true,false,false,""));
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
        using var owner = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(Path.Combine(directory, "data")), officialSourceFactory: _ => new FakeSource{InstalledValue=installation});
        var entry = owner.Skins.Single(s => s.OfficialCharacterId == "dafeiyu");
        var method = typeof(DesktopController).GetMethod("PublishBulkResultAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Task Publish() => (Task)method.Invoke(owner, new object[] { installation, owner.Skin, 0L, 1L, CancellationToken.None })!;
        void Hide() { CompanionPackStorage.SetHidden(owner.DataDirectory, entry.PickerKey, true); owner.Skins.RemoveAll(s => s.PickerKey == entry.PickerKey); }
        Hide(); Complete(Publish());
        Check(!owner.Skins.Any(s => s.PickerKey == entry.PickerKey), "bulk keeps an already hidden official role hidden");
        CompanionPackStorage.SetHidden(owner.DataDirectory, entry.PickerKey, false); owner.Skins.Add(entry);
        var catalogs = typeof(DesktopController).GetField("preparedCompanions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
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
    private static OfficialCompanionInstallation CreatePackage(string root, string role, string holidayRoot,bool coldAction=false)
    {
        Directory.CreateDirectory(root);
        var profile = OfficialCompanionProfiles.Find(role)!;
        Paint(Path.Combine(root, "normal.png"), 3840, 520, 8, 120);
        Paint(Path.Combine(root, "gaze.png"), 1920, 2080, 4, 120);
        Paint(Path.Combine(root, "preview.png"), 480, 520, 1, 120);
        Paint(Path.Combine(root, "avatar.png"), 480, 480, 1, 120);
        object Clip(string path, bool loop = false) => new { file = path, columns = 8, frames = 8, fps = 8, loop };
        var normal = AnimationPlayer.States.Concat(SpriteAssets.Interactions).ToDictionary(action => action, action => Clip("normal.png", action == "idle"));
        if(coldAction){File.Copy(Path.Combine(root,"normal.png"),Path.Combine(root,"cold.png"));normal["running"]=Clip("cold.png",true);}
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
    internal static int RunStartupRestore(string directory)
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("Companion startup: "+label);checks++;}
        using var fixture=new HolidayReviewChecks.Fixture(Path.Combine(directory,"holiday"),"dafeiyu",null,false);
        var installation=CreatePackage(Path.Combine(directory,"installed"),"dafeiyu",fixture.Root);
        string data=Path.Combine(directory,"data");Directory.CreateDirectory(data);
        var store=new SettingsStore(Path.Combine(data,"settings.json"));
        var saved=new PetSettings{SkinId="dafeiyu",SelectedCompanionKey="official:dafeiyu",OnboardingState="skipped",SystemPrompt="保留已保存人格",PersonaProfileVersion=1};
        saved.PersonaOverrides["dafeiyu"]="保留已保存人格";store.Save(saved);
        foreach(bool restart in new[]{false,true})
        {
            var source=new FakeSource{InstalledValue=installation};
            using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(data),officialSourceFactory:_=>source);
            Check(owner.Skin.OfficialCharacterId=="dafeiyu","saved role selected before Start");
            if(owner.Skins.Any(s=>s.IsPlaceholder))Check(owner.Skin.Folder==installation.RootDirectory,"thin application rehydrates installed package before fallback");
            Check(owner.Settings.SelectedCompanionKey=="official:dafeiyu"&&owner.Settings.PendingOfficialCompanionId=="","saved selection survives constructor and restart");
            Check(owner.Settings.SystemPrompt=="保留已保存人格","custom persona survives cached restoration");
            Check(source.Calls==0&&source.BulkCalls==0,"restoration makes no HTTP/update calls");
            owner.Save();Finish(owner);
        }
        // Startup ignores a stale in-flight request and performs no update.
        string startupData=Path.Combine(directory,"startup-ebao");Directory.CreateDirectory(startupData);
        new SettingsStore(Path.Combine(startupData,"settings.json")).Save(new PetSettings{SkinId="e-bao",SelectedCompanionKey="official:e-bao",PendingOfficialCompanionId="dafeiyu",OnboardingState="skipped"});
        var offline=new FakeSource();
        using(var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(startupData),officialSourceFactory:_=>offline)){
            var previous=owner.Skin;
            Complete((Task)typeof(DesktopController).GetMethod("StartOfficialCompanionsAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(owner,null)!);
            Check(offline.Calls==0&&ReferenceEquals(previous,owner.Skin)&&owner.Skin.OfficialCharacterId=="e-bao","startup remains fully offline on last successful selection");
            Check(owner.Settings.PendingOfficialCompanionId=="","stale pending intent cannot auto-select after restart");Finish(owner);
        }
        string upgradeData=Path.Combine(directory,"upgrade-data");Directory.CreateDirectory(upgradeData);
        new SettingsStore(Path.Combine(upgradeData,"settings.json")).Save(new PetSettings{SkinId="dafeiyu",SelectedCompanionKey="official:dafeiyu",LastCompanionUpgradeVersion="1.0.2",OnboardingState="skipped"});
        for(int attempt=0;attempt<3;attempt++){
            var upgrade=new FakeSource{InstalledValue=installation};
            using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(upgradeData),officialSourceFactory:_=>upgrade);
            owner.Settings.OnboardingState="completed";var selected=owner.Skin;var events=new List<CompanionSelectionUpdate>();owner.CompanionSelectionChanged+=events.Add;
            Check(selected.OfficialCharacterId=="dafeiyu"&&upgrade.Calls==0,"upgrade restores prior selected role locally before first visible frame");
            owner.Pet.Show();var sprite=(SpriteView)typeof(PetWindow).GetField("sprite",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner.Pet)!;
            using(var target=new RenderTargetBitmap(new PixelSize(192,208)))using(var context=target.CreateDrawingContext())sprite.Render(context);
            if(attempt<2){
                Until(()=>upgrade.Calls==1);
                Check(upgrade.RequestedIds.SequenceEqual(new[]{"dafeiyu"})&&upgrade.Progress==null&&ReferenceEquals(owner.Skin,selected),"upgrade checks only visible selected role silently after rendering");
                upgrade.Result.SetResult(new OfficialCompanionResult(installation,attempt==0,true,false,""));
                if(attempt==0){Complete(Task.Delay(100));Check(owner.Settings.LastCompanionUpgradeVersion=="1.0.2","offline fallback does not mark failed upgrade installation complete");}
                else Until(()=>owner.Settings.LastCompanionUpgradeVersion==ReleaseInfo.DisplayVersion);
            }else{Complete(Task.Delay(100));Check(upgrade.Calls==0,"successful upgrade check is deduplicated on ordinary restart");}
            Check(owner.Settings.SelectedCompanionKey=="official:dafeiyu"&&!events.Any(e=>e.Stage=="completed"),"upgrade preserves selected identity and emits no selection completion cue");Finish(owner);
        }
        // Cached manual selection must finish while the metadata response is held.
        string switchData=Path.Combine(directory,"switch-data");Directory.CreateDirectory(switchData);
        var switchSettings=new PetSettings{SkinId="e-bao",SelectedCompanionKey="official:e-bao",OnboardingState="skipped"};
        new SettingsStore(Path.Combine(switchData,"settings.json")).Save(switchSettings);
        var switchSource=new FakeSource{InstalledValue=installation};
        using(var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(switchData),officialSourceFactory:_=>switchSource))
        {
            var requested=owner.Skins.Single(s=>s.OfficialCharacterId=="dafeiyu");
            Complete(owner.SelectSkinAsync(requested,CompanionSelectionOrigin.PetMenu));
            Check(owner.Skin.Folder==installation.RootDirectory,"cached old package is selected without awaiting metadata");
            Until(()=>switchSource.Calls>0);
            Check(!switchSource.Result.Task.IsCompleted,"new metadata may remain pending after selection completes");
            var oldSnapshot=owner.Skin.Snapshot;
            var next=CreatePackage(Path.Combine(directory,"updated"),"dafeiyu",fixture.Root);
            switchSource.InstalledValue=next;
            string persona=owner.Skin.Id,key=owner.Settings.SelectedCompanionKey;
            switchSource.Result.TrySetResult(new OfficialCompanionResult(next,false,true,true,""));
            Until(()=>owner.Skin.Folder==next.RootDirectory);
            Check(owner.Skin.Id==persona&&owner.Settings.SelectedCompanionKey==key,"silent update reloads the same role identity");
            owner.ApplyPendingCompanionAppearance();bool retired=false;try{oldSnapshot!.ReadVerifiedBytes("pet.json");}catch(ObjectDisposedException){retired=true;}
            Check(retired,"safe appearance replacement releases retired snapshot lease");
            Complete(owner.SelectSkinAsync(owner.Skins.Single(role=>role.OfficialCharacterId=="e-bao"),CompanionSelectionOrigin.PetMenu));
            owner.Settings.OnboardingState="completed";owner.ShowGuide();Until(()=>owner.GuideLocked);
            var guide=(GuideWindow)typeof(DesktopController).GetField("guide",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
            void GuideClick(string name){guide.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name==name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();}
            GuideClick("GuideNext");GuideClick("GuideNext");GuideClick("GuideNext");GuideClick("GuideNext");GuideClick("GuideNext");switchSource.BulkResult=new TaskCompletionSource<IList<OfficialCompanionResult>>(TaskCreationOptions.RunContinuationsAsynchronously);GuideClick("GuidePreparePacks");var bulk=owner.OfficialPreparation.StartAsync();Until(()=>switchSource.BulkCalls==1);
            Complete(owner.SelectSkinAsync(owner.Skins.Single(role=>role.OfficialCharacterId=="dafeiyu"),CompanionSelectionOrigin.Onboarding));
            Check(owner.Skin.Folder==next.RootDirectory&&guide.IsVisible&&!bulk.IsCompleted,"cached last-choice role is usable while guide-owned full metadata is pending");
            var latest=CreatePackage(Path.Combine(directory,"bulk-updated"),"dafeiyu",fixture.Root);switchSource.InstalledValue=latest;var selectedHistory=owner.Session.History;
            switchSource.BulkResult.SetResult(new[]{new OfficialCompanionResult(latest,false,true,true,"")});Complete(bulk);
            Until(()=>owner.Skin.Folder==latest.RootDirectory);
            Check(owner.OfficialPreparation.State==OfficialCompanionPreparationState.Completed&&owner.Skin.Folder==latest.RootDirectory&&owner.Skin.Id==persona&&owner.Settings.SelectedCompanionKey==key&&ReferenceEquals(owner.Session.History,selectedHistory),"bulk completion hotloads the cached role chosen during metadata wait without changing persona or history");guide.Close();

            Finish(owner);
        }
        var staleSource=new FakeSource{InstalledValue=installation};
        string staleData=Path.Combine(directory,"stale-data");Directory.CreateDirectory(staleData);
        new SettingsStore(Path.Combine(staleData,"settings.json")).Save(switchSettings);
        using(var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(staleData),officialSourceFactory:_=>staleSource))
        {
            Complete(owner.SelectSkinAsync(owner.Skins.Single(s=>s.OfficialCharacterId=="dafeiyu"),CompanionSelectionOrigin.Settings));
            Until(()=>staleSource.Calls>0);
            Check(staleSource.Progress==null,"background update has no visible progress sink");
            Complete(owner.SelectSkinAsync(owner.Skins.Single(s=>s.OfficialCharacterId=="e-bao"),CompanionSelectionOrigin.PetMenu));
            Check(owner.Skin.OfficialCharacterId=="e-bao","new selection remains active while previous background update is pending");
            staleSource.Result.TrySetResult(new OfficialCompanionResult(installation,false,false,true,""));Dispatcher.UIThread.RunJobs();
            Finish(owner);
        }
        // The lightweight notice is lazy: metadata/cache reuse never opens it.
        using(var cancellation=new CancellationTokenSource())
        using(var progress=new OfficialCompanionProgressView(true,null,false,cancellation,lazy:true))
        {
            var field=typeof(OfficialCompanionProgressView).GetField("window",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var window=(Window)field.GetValue(progress)!;
            progress.Report(new OfficialCompanionProgress("dafeiyu","metadata","",0,100,0));Dispatcher.UIThread.RunJobs();
            Check(!window.IsVisible,"metadata check has no waiting bubble");
            progress.Report(new OfficialCompanionProgress("dafeiyu","install","",100,100,100));Dispatcher.UIThread.RunJobs();
            Check(!window.IsVisible,"unchanged cached package has no waiting bubble");
            progress.Report(new OfficialCompanionProgress("dafeiyu","download","",0,100,0));Dispatcher.UIThread.RunJobs();
            Check(window.IsVisible&&window.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text=="正在更新角色包，请稍后..."),"missing package download shows the requested waiting copy");
        }
        // Main-window/settings progress keeps its normal controls, but only
        // appears when package work actually begins. Disposal ignores queued reports.
        using(var cancellation=new CancellationTokenSource())
        {
            var progress=new OfficialCompanionProgressView(false,null,false,cancellation,lazy:true);
            var field=typeof(OfficialCompanionProgressView).GetField("window",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var window=(Window)field.GetValue(progress)!;
            progress.Report(new OfficialCompanionProgress("dafeiyu","metadata","",0,100,0));Dispatcher.UIThread.RunJobs();
            Check(!window.IsVisible,"settings metadata check has no dialog");
            progress.Report(new OfficialCompanionProgress("dafeiyu","install","",100,100,100));Dispatcher.UIThread.RunJobs();
            Check(!window.IsVisible,"settings cache reuse has no dialog");
            progress.Report(new OfficialCompanionProgress("dafeiyu","download","",25,100,25));Dispatcher.UIThread.RunJobs();
            Check(window.IsVisible&&window.Name=="OfficialCompanionProgressDialog"&&window.GetLogicalDescendants().OfType<Button>().Any(b=>b.Content?.ToString()==TextResources.Get("dialog.cancelDownload")),"settings actual update retains normal cancellable progress dialog");
            progress.Dispose();
            Check(!window.IsVisible&&!cancellation.IsCancellationRequested,"completed progress closes without cancelling the operation");
            progress.Report(new OfficialCompanionProgress("dafeiyu","download","",50,100,50));Dispatcher.UIThread.RunJobs();
            Check(!window.IsVisible,"late progress cannot reopen a completed dialog");
        }
        return checks;
    }
    internal static int RunReviewFailures(string directory)
    {
        int checks=0;void Check(bool value,string label){if(!value)throw new Exception("Review failures: "+label);checks++;}
        using var fixture=new HolidayReviewChecks.Fixture(Path.Combine(directory,"holiday"),"dafeiyu",null,false);
        foreach(var relative in new[]{"catalog.json","outfit.json"}){
            var installation=CreatePackage(Path.Combine(directory,relative,"installed"),"dafeiyu",fixture.Root);var source=new FakeSource{InstalledValue=installation};
            using(var snapshot=source.CaptureLocalSnapshot("dafeiyu",CancellationToken.None)){}
            var file=Path.Combine(installation.HolidayRootDirectory,relative);var bytes=File.ReadAllBytes(file);bytes[0]^=1;File.WriteAllBytes(file,bytes);
            using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(Path.Combine(directory,relative,"data")),officialSourceFactory:_=>source);
            var old=owner.Skin;var sprite=(SpriteView)typeof(PetWindow).GetField("sprite",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner.Pet)!;var frame=sprite.Clip;var pixels=frame==null?null:sprite.Assets.Get(sprite.Skin,frame);string key=owner.Settings.SelectedCompanionKey,persona=owner.Settings.SystemPrompt,id=owner.Settings.SkinId;owner.Save();
            source.Result.TrySetException(new ApiException("offline repair"));Complete(owner.SelectSkinAsync(owner.Skins.Single(s=>s.OfficialCharacterId=="dafeiyu")));
            Check(ReferenceEquals(owner.Skin,old)&&sprite.Clip?.File==frame?.File&&ReferenceEquals(sprite.Skin,old)&&ReferenceEquals(sprite.Assets.Get(sprite.Skin,sprite.Clip!),pixels)&&owner.Settings.SelectedCompanionKey==key&&owner.Settings.SystemPrompt==persona&&owner.Settings.SkinId==id&&new SettingsStore(Path.Combine(owner.DataDirectory,"settings.json")).Load().SelectedCompanionKey==key,"corrupt "+relative+" retains visual pixels persona and persisted selection");Finish(owner);
        }
        var installed=CreatePackage(Path.Combine(directory,"lazy"),"dafeiyu",fixture.Root,true);var lazySource=new FakeSource{InstalledValue=installed};var assets=new SpriteAssets();
        using(var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(Path.Combine(directory,"lazy-data")),spriteAssetsFactory:()=>assets,officialSourceFactory:_=>lazySource)){
            Complete(owner.SelectSkinAsync(owner.Skins.Single(s=>s.OfficialCharacterId=="dafeiyu"),CompanionSelectionOrigin.StartupRestore));
            var active=owner.Skin;lazySource.Result.SetResult(new OfficialCompanionResult(installed,false,true,false,""));
            Complete((Task)typeof(DesktopController).GetMethod("RefreshOfficialAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(owner,new object?[]{active,(long)lazySource.Selections,OfficialCompanionCheckMode.Cached,CompanionSelectionOrigin.StartupRestore,null})!);
            lazySource.Result=new(TaskCreationOptions.RunContinuationsAsynchronously);var cold=Path.Combine(installed.RootDirectory,"cold.png");var bytes=File.ReadAllBytes(cold);bytes[0]^=1;File.WriteAllBytes(cold,bytes);
            var clip=assets.Resolve(active,"running",true);for(int n=0;n<8;n++)assets.Ready(active,clip);
            Until(()=>lazySource.Repairs==1);Check(lazySource.Repairs==1&&lazySource.RepairBackground&&ReferenceEquals(owner.Skin,active),"cold default failure starts one background repair with working source retained");
            lazySource.Result.SetException(new ApiException("offline"));var repairs=(System.Collections.IDictionary)typeof(DesktopController).GetField("companionRepairs",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;Until(()=>repairs.Count==0);
            for(int n=0;n<8;n++)assets.Ready(active,clip);Dispatcher.UIThread.RunJobs();Check(lazySource.Repairs==1&&ReferenceEquals(owner.Skin,active),"offline lazy repair throttles repeated frames without discarding active source");
            var replacement=CreatePackage(Path.Combine(directory,"repaired"),"dafeiyu",fixture.Root,true);lazySource.InstalledValue=replacement;lazySource.Result=new(TaskCreationOptions.RunContinuationsAsynchronously);
            ((System.Collections.IDictionary)typeof(DesktopController).GetField("companionRepairAfter",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner)!)[active.Snapshot!.Generation]=DateTime.MinValue;
            assets.Ready(active,clip);Until(()=>lazySource.Repairs==2);
            var question=owner.Questions.AskAsync(new AgentQuestionRequest{SessionId="repair-busy",TaskId="task",CallId="call",Question="wait"},CancellationToken.None);
            lazySource.Result.SetResult(new OfficialCompanionResult(replacement,false,true,true,""));Until(()=>repairs.Count==0);Check(ReferenceEquals(owner.Skin,active),"successful repair waits for safe idle without interrupting business");
            ((System.Collections.IDictionary)typeof(DesktopController).GetField("companionRepairAfter",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner)!)[active.Snapshot!.Generation]=DateTime.MinValue;
            for(int n=0;n<8;n++)assets.Ready(active,clip);Dispatcher.UIThread.RunJobs();
            Check(lazySource.Repairs==2&&ReferenceEquals(owner.Skin,active),"pending successful repair suppresses repeated transfers after retry deadline expires");
            owner.Questions.CancelSession("repair-busy");owner.ApplyPendingCompanionAppearance();Until(()=>!ReferenceEquals(owner.Skin,active));
            Check(owner.Skin.Folder==replacement.RootDirectory&&owner.Settings.SelectedCompanionKey==active.PickerKey&&owner.CurrentCompanionSelection?.Stage!="completed","later repair silently applies same-role source at idle");assets.PrepareFrame(owner.Skin,assets.Resolve(owner.Skin,"running",true),0);checks++;
            Complete(owner.SelectSkinAsync(owner.Skins.Single(s=>s.OfficialCharacterId=="e-bao"),CompanionSelectionOrigin.StartupRestore));int settled=lazySource.Repairs;assets.Ready(active,clip);Dispatcher.UIThread.RunJobs();
            Check(owner.Skin.OfficialCharacterId=="e-bao"&&lazySource.Repairs==settled,"superseded source failure cannot start repair or reclaim latest selection");Finish(owner);
        }
        return checks;
    }
    internal static int RunInstallationBubble(string directory)
    {
        int checks=0;void Check(bool value,string label){if(!value)throw new Exception("Installation bubble: "+label);checks++;}
        var source=new FakeSource();using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(directory),officialSourceFactory:_=>source);
        var old=owner.Skin;var placeholder=new PetSkin{Id="dafeiyu",Name="D",Prompt="",OfficialCharacterId="dafeiyu",PickerKey="official:dafeiyu",IsPlaceholder=true};
        var operation=owner.SelectSkinAsync(placeholder,CompanionSelectionOrigin.Settings);Until(()=>source.Calls==1);
        var bubble=(CompanionInstallationBubble)typeof(DesktopController).GetField("companionInstallationBubble",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
        Check(bubble!=null&&bubble.Status.CanCancel&&ReferenceEquals(owner.Skin,old),"missing selection retains current pet and owns status");
        Check(!bubble!.IsVisible&&owner.CompanionSelectionNotice==bubble.Status.Text&&owner.CompanionSelectionNotice.Length>0,"hidden pet suppresses window but selection immediately updates initiating status");owner.Pet.Show();Dispatcher.UIThread.RunJobs();
        owner.CancelCompanionSelection(owner.CurrentCompanionSelection!.Request);source.Result.SetCanceled();Complete(operation);Check(!bubble.IsVisible&&owner.CompanionSelectionNotice=="","cancel immediately removes status without manually rendering");
        source.Result=new(TaskCreationOptions.RunContinuationsAsynchronously);operation=owner.SelectSkinAsync(placeholder,CompanionSelectionOrigin.Settings);Until(()=>source.Calls==2);
        Check(bubble.IsVisible&&!bubble.ShowActivated&&owner.CompanionSelectionNotice==bubble.Status.Text,"normal selection immediately displays passive window and selector notice");
        var message=bubble.GetLogicalDescendants().OfType<TextBlock>().First(t=>t.Name=="CompanionInstallationMessage");string? last=message.Text;int refreshes=0,sequence=0;var clock=System.Diagnostics.Stopwatch.StartNew();
        while(clock.ElapsedMilliseconds<1050){source.Progress!.Report(new OfficialCompanionProgress("dafeiyu","download","",++sequence,1000,sequence%90));Dispatcher.UIThread.RunJobs();if(message.Text!=last){refreshes++;last=message.Text;}Thread.Sleep(5);}
        Check(refreshes>=1&&refreshes<=4,"continuous progress is coalesced to at most four updates per second");
        var close=bubble.GetLogicalDescendants().OfType<Button>().First(b=>b.Name=="DismissCompanionInstallation");close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        source.Progress!.Report(new OfficialCompanionProgress("dafeiyu","download","",10,20,50));Dispatcher.UIThread.RunJobs();Check(!bubble.IsVisible&&!operation.IsCompleted&&bubble.Status.CanCancel,"close hides only UI and progress cannot reopen it");
        source.Result.SetException(new IOException("fixture failure"));Complete(operation);Check(bubble.Status.CanRetry&&!bubble.IsVisible,"failed dismissed request stays hidden and remains retryable");
        bubble.Close();source.Result=new(TaskCreationOptions.RunContinuationsAsynchronously);var retry=owner.SelectSkinAsync(placeholder,CompanionSelectionOrigin.Settings);Until(()=>source.Calls==3);Check(bubble.IsVisible&&!bubble.Status.Dismissed,"explicit retry immediately starts a new visible intent");
        owner.CancelCompanionSelection(owner.CurrentCompanionSelection!.Request);source.Result.SetCanceled();Complete(retry);Check(!bubble.IsVisible&&owner.CompanionSelectionNotice==""&&ReferenceEquals(owner.Skin,old),"cancel keeps current pet and immediately closes status");
        long token=owner.CurrentCompanionSelection!.Request+1;bubble.Accept(new CompanionSelectionUpdate(token,"official:dafeiyu","Settings","requested",0));bubble.Accept(new CompanionSelectionUpdate(token,"official:dafeiyu","Settings","installing",0));Check(bubble.IsVisible,"Accept shows first active state synchronously");bubble.Accept(new CompanionSelectionUpdate(token,"official:dafeiyu","Settings","committed",0));Check(!bubble.IsVisible&&owner.CompanionSelectionNotice=="","commit immediately removes presentation without a timer tick");Finish(owner);return checks;
    }
    internal static int RunBundledEbao(string directory)
    {
        int checks=0;void Check(bool value,string label){if(!value)throw new Exception("Bundled Ebao: "+label);checks++;}
        var bundle=OfficialCompanionBundle.Ebao;Check(bundle!=null,"production Core contains complete pinned bundle");
        using var fixture=new HolidayReviewChecks.Fixture(Path.Combine(directory,"holiday"),"e-bao",null,false);
        var installed=CreatePackage(Path.Combine(directory,"installed"),"e-bao",fixture.Root);var source=new FakeSource{InstalledValue=installed};var assets=new SpriteAssets();
        using(var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(Path.Combine(directory,"data")),spriteAssetsFactory:()=>assets,officialSourceFactory:_=>source)){
            Check(owner.Skin.Bundle!=null&&owner.Skin.PackageHolidayCatalog!.Outfits.Count==10&&owner.Holidays.Catalog.Outfits.Count(o=>o.CharacterId=="e-bao")==10,"startup rejects older installed E before cache and retains full embedded wardrobe");
            Complete(owner.SelectSkinAsync(owner.Skin,CompanionSelectionOrigin.StartupRestore));Check(owner.Skin.Bundle!=null&&assets.RetainedPixelBytes<64L*1024*1024,"explicit E reselect uses small prepared idle gaze resources");
            var read=typeof(DesktopController).GetMethod("ReadPublished",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
            var next=((PetSkin Skin,HolidayCatalog Catalog))read.Invoke(owner,new object[]{installed,owner.Skin.Id})!;Check(next.Skin.Bundle!=null,"background publication cannot downgrade bundled E");
            source.NoNewerBundle=true;long token=source.BeginSelection();
            var refresh=(Task)typeof(DesktopController).GetMethod("RefreshOfficialAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(owner,new object?[]{owner.Skin,token,OfficialCompanionCheckMode.AfterApplicationUpdate,CompanionSelectionOrigin.StartupRestore,null})!;Complete(refresh);
            Check(source.AvailableVersion==bundle!.Version&&owner.Settings.LastCompanionUpgradeVersion==ReleaseInfo.DisplayVersion,"Avalonia background passes bundle baseline and records metadata-only upgrade success");Finish(owner);
        }
        string manifest=Path.Combine(installed.RootDirectory,"pet.json");File.WriteAllText(manifest,File.ReadAllText(manifest).Replace("0.10.3-dev.1","9.0.0"));
        var asset=new OfficialCompanionAsset("e-bao","E","9.0.0","0.10.3-dev.1","v9.0.0","new.zip",1,new string('a',64),new[]{new OfficialCompanionOutfit("halloween-2026","1.0.0")});
        var newer=new FakeSource{InstalledValue=new OfficialCompanionInstallation(asset,installed.RootDirectory)};
        using(var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(Path.Combine(directory,"newer-data")),officialSourceFactory:_=>newer)){
            Check(owner.Skin.Snapshot!=null&&owner.Skin.PackVersion=="9.0.0","newer compatible local E wins at startup");Finish(owner);
        }
        return checks;
    }
    internal static int RunPreparedSwitch(string directory)
    {
        int checks=0;void Check(bool value,string label){if(!value)throw new Exception("Prepared switch: "+label);checks++;}
        using var fixture=new HolidayReviewChecks.Fixture(Path.Combine(directory,"holiday"),"dafeiyu",null,false);
        var installed=CreatePackage(Path.Combine(directory,"installed"),"dafeiyu",fixture.Root);
        var source=new FakeSource{InstalledValue=installed};var assets=new SpriteAssets();
        using(var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(Path.Combine(directory,"data")),spriteAssetsFactory:()=>assets,officialSourceFactory:_=>source)){
            var updates=new List<CompanionSelectionUpdate>();owner.CompanionSelectionChanged+=updates.Add;
            var e=owner.Skin;var d=owner.Skins.Single(s=>s.OfficialCharacterId=="dafeiyu");
            Complete(owner.SelectSkinAsync(d,CompanionSelectionOrigin.PetMenu));
            Check(owner.Skin.Snapshot!=null&&source.Calls==1&&!source.Result.Task.IsCompleted,"local activation precedes unresolved network update");
            Check(updates.Any(x=>x.Stage=="committed")&&!updates.Any(x=>x.Stage=="completed"),"commit is not an actual render completion");
            var sprite=(SpriteView)typeof(PetWindow).GetField("sprite",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner.Pet)!;
            using(var target=new RenderTargetBitmap(new PixelSize(192,208)))using(var context=target.CreateDrawingContext())sprite.Render(context);
            Check(updates.Count(x=>x.Stage=="completed")==1&&updates.Last().Milliseconds>0,"first rendered target emits one measured completion");
            using(var target=new RenderTargetBitmap(new PixelSize(192,208)))using(var context=target.CreateDrawingContext())sprite.Render(context);
            Check(updates.Count(x=>x.Stage=="completed")==1,"repeated frames do not duplicate completion");
            int captures=source.InstalledCalls;
            Complete(owner.SelectSkinAsync(e,CompanionSelectionOrigin.PetMenu));
            int native=assets.DecodeCount;
            Complete(owner.SelectSkinAsync(d,CompanionSelectionOrigin.PetMenu));
            Check(source.InstalledCalls==captures+1,"warm retained generation avoids recapturing pointer behind background lifecycle work");
            Check(assets.DecodeCount==native,"A to B to A reuses prepared native default pixels");
            Complete(owner.SelectSkinAsync(e,CompanionSelectionOrigin.PetMenu));
            var normal=Path.Combine(installed.RootDirectory,"normal.png");var bytes=File.ReadAllBytes(normal);bytes[^1]^=1;File.WriteAllBytes(normal,bytes);
            var corrupt=owner.SelectSkinAsync(d,CompanionSelectionOrigin.PetMenu);Until(()=>updates.Any(u=>u.Stage=="installing"));
            Check(owner.Skin.PickerKey==e.PickerKey&&!corrupt.IsCompleted,"same-length tampering forces repair and keeps the old visible role");
            owner.CancelCompanionSelection(owner.CurrentCompanionSelection!.Request);source.Result.TrySetCanceled();Complete(corrupt);
            Check(owner.Skin.PickerKey==e.PickerKey&&updates.Any(u=>u.Stage=="cancelled"),"cancelled repair cannot select damaged default");
            Finish(owner);
        }
        var arrived=CreatePackage(Path.Combine(directory,"arrival"),"dafeiyu",fixture.Root);var missing=new FakeSource();
        using(var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(Path.Combine(directory,"missing-data")),officialSourceFactory:_=>missing)){
            var old=owner.Skin;var placeholder=new PetSkin{Id="dafeiyu",Name="D",Prompt="",OfficialCharacterId="dafeiyu",PickerKey="official:dafeiyu",IsPlaceholder=true};
            var selection=owner.SelectSkinAsync(placeholder,CompanionSelectionOrigin.PetMenu);Until(()=>missing.Calls==1);
            var question=owner.Questions.AskAsync(new AgentQuestionRequest{SessionId="busy",TaskId="task",CallId="call",Question="wait"},CancellationToken.None);
            missing.InstalledValue=arrived;missing.Result.SetResult(new OfficialCompanionResult(arrived,false,true,true,""));Complete(selection);
            Check(ReferenceEquals(owner.Skin,old)&&owner.CurrentCompanionSelection?.Stage=="reselect-required"&&owner.Questions.Requests.Count==1,"installation arriving during business revokes auto-selection without stopping work");
            owner.Questions.CancelSession("busy");Dispatcher.UIThread.RunJobs();Check(ReferenceEquals(owner.Skin,old),"idle does not replay the revoked selection");Finish(owner);
        }
        return checks;
    }
    private static uint Pixel(Bitmap bitmap)
    {
        var bytes = new byte[4]; var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        // Native decode may expose BGRA or RGBA. Compare every channel rather
        // than assuming the first byte contains the differing fixture blue.
        try { bitmap.CopyPixels(new PixelRect(70, 90, 1, 1), handle.AddrOfPinnedObject(), 4, 4); return BitConverter.ToUInt32(bytes); }
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
        Until(() => source.Calls == 3);
        CompanionPackStorage.SetHidden(owner.DataDirectory, placeholder.PickerKey, true);
        owner.CompanionHidden(placeholder); owner.Skins.RemoveAll(s => s.PickerKey == placeholder.PickerKey);
        source.Result.TrySetResult(new OfficialCompanionResult(new OfficialCompanionInstallation(asset, Path.Combine(directory, "never-read-hidden")), false, true, true, ""));
        Complete(hiddenRequest);
        Check(ReferenceEquals(owner.Skin, ebao) && owner.Skins.All(s => s.PickerKey != placeholder.PickerKey) && owner.Settings.PendingOfficialCompanionId == "", "hiding an in-flight automatic role blocks late activation and clears durable pending restore");
        owner.Questions.AskAsync(new AgentQuestionRequest{SessionId="busy",TaskId="task",CallId="call",Question="waiting"},CancellationToken.None);
        var beforeBusy=source.Selections;var active=owner.Skin;
        Complete(owner.SelectSkinAsync(active,CompanionSelectionOrigin.PetMenu));
        Check(source.Selections==beforeBusy&&owner.Questions.Requests.Count==1&&ReferenceEquals(owner.Skin,active)&&owner.CompanionSelectionNotice.Length>0,"business refusal leaves pending agent question and active companion untouched");
        owner.Questions.CancelSession("busy");
        owner.ShowGuide(); Until(() => owner.GuideLocked);
        var before = source.Selections; var persona = owner.Settings.SkinId; var picker = owner.Settings.SelectedCompanionKey;
        Complete(owner.SelectSkinAsync(placeholder, CompanionSelectionOrigin.PetMenu));
        Complete(owner.DownloadAllOfficialAsync());
        Check(source.Selections == before && source.Calls == 3 && source.BulkCalls == 0, "guide attempts never create selection tokens or downloads");
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
        internal int Calls, BulkCalls, Selections, InstalledCalls, Repairs;
        internal bool RepairBackground, NoNewerBundle;
        internal string? AvailableVersion;
        public async Task<OfficialCompanionResult?> UpdateAsync(string id,OfficialDownloadRouting routing,CancellationToken cancel,OfficialCompanionCheckMode mode=OfficialCompanionCheckMode.Cached,string? availableVersion=null){AvailableVersion=availableVersion;if(NoNewerBundle)return null;return await EnsureAsync(id,routing,mode,null,cancel);}
        public Task<OfficialCompanionResult> RepairAsync(string id,OfficialDownloadRouting routing,IProgress<OfficialCompanionProgress>? progress,CancellationToken cancel,bool background=false){Repairs++;RepairBackground=background;return EnsureAsync(id,routing,OfficialCompanionCheckMode.Cached,progress,cancel);}
        internal readonly List<string> RequestedIds=new();
        internal OfficialCompanionInstallation? InstalledValue;
        internal TaskCompletionSource<IList<OfficialCompanionResult>>? BulkResult;
        internal TaskCompletionSource<OfficialCompanionResult> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long BeginSelection() => ++Selections;
        public bool OwnsSelection(long token) => token == Selections;
        public OfficialCompanionInstallation? Installed(string id) { Interlocked.Increment(ref InstalledCalls); return InstalledValue?.Asset.Id == id ? InstalledValue : null; }
        private readonly Dictionary<string,Dictionary<string,OfficialCompanionFile>> snapshots=new(StringComparer.Ordinal);
        public OfficialCompanionSnapshot? CaptureLocalSnapshot(string id,CancellationToken cancel)
        {
            var installed=Installed(id);if(installed==null)return null;
            lock(snapshots){
                if(!snapshots.TryGetValue(installed.RootDirectory,out var files)){
                    files=Directory.GetFiles(installed.RootDirectory,"*",SearchOption.AllDirectories).ToDictionary(path=>Path.GetRelativePath(installed.RootDirectory,path).Replace('\\','/'),path=>{var bytes=File.ReadAllBytes(path);return new OfficialCompanionFile(bytes.Length,OfficialCompanionSnapshot.Digest(bytes));},StringComparer.Ordinal);
                    snapshots[installed.RootDirectory]=files;
                }
                return new OfficialCompanionSnapshot(installed,files,installed.RootDirectory,installed.RootDirectory,null);
            }
        }
        internal IProgress<OfficialCompanionProgress>? Progress;
        public Task<OfficialCompanionResult> EnsureAsync(string id, OfficialDownloadRouting routing, OfficialCompanionCheckMode mode, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel) { Calls++; RequestedIds.Add(id);Progress=progress; return Result.Task; }
        public Task<IList<OfficialCompanionResult>> EnsureAllAsync(OfficialDownloadRouting routing, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel) { BulkCalls++; return BulkResult?.Task??Task.FromResult<IList<OfficialCompanionResult>>(Array.Empty<OfficialCompanionResult>()); }
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
        var reviewed=new HolidayCatalog(Path.GetFullPath("assets/holidays")).Outfits.First(o=>o.CharacterId=="e-bao"&&o.HolidayId=="christmas");
        var visual=SpriteAssets.OutfitClip(reviewed,reviewed.FindClip("jumping","default"));
        Check(visual.Frames==8&&Math.Abs(visual.Frames/visual.Fps-.9)<1e-8&&visual.HoldLastSeconds==0,"holiday jump cadence stays independent of the default skin dwell");
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
