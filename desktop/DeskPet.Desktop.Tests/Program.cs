using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

AppBuilder.Configure<TestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
int assertions = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); assertions++; }
void Until(Func<bool> ready) { var started = System.Diagnostics.Stopwatch.StartNew(); while (!ready()) { if (started.ElapsedMilliseconds > 10000) throw new TimeoutException("UI operation did not complete"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } Dispatcher.UIThread.RunJobs(); }
void Complete(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
T Result<T>(Task<T> task) { Complete(task); return task.GetAwaiter().GetResult(); }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is InvalidDataException or ArgumentException or IOException or JsonException) { assertions++; return; } throw new Exception("FAIL: accepted " + name); }
object Asset(string name, string? url = null) => new { name, browser_download_url = url ?? "https://github.com/Eason4869/deskpet/releases/download/v0.6.0/" + Uri.EscapeDataString(name), size = 1024L };
JsonDocument Release(params object[] assets) => JsonDocument.Parse(JsonSerializer.Serialize(new { tag_name = "v0.6.0", assets }));
var directory = Path.Combine(AppContext.BaseDirectory, "DeskPet-UI-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    if (args.Contains("--image-ui-only")) { var imageChecks = ImageUiChecks.Run(Path.Combine(directory, "image-ui")); Console.WriteLine("PASS: " + imageChecks + " image workspace / gallery / conversation UI assertions"); return; }
    var skins = SpriteAssets.LoadSkins(directory);
    Check(skins.Count == 5 && skins.Any(s => s.Id == "gugugaga"), "five bundled companions including Guga");
    using (var gugaAssets = new SpriteAssets())
    {
        var guga = skins.Single(s => s.Id == "gugugaga");
        foreach (var action in SpriteAssets.PairedActions.Append("jumping"))
        {
            var first = gugaAssets.Resolve(guga, action, true);
            var second = gugaAssets.Resolve(guga, action, false);
            Check(first.File != second.File && first.Frames == 8 && second.Frames == 8, "Guga independent variants " + action);
            Check(gugaAssets.Get(guga, first.File).PixelSize.Width == 3840 && gugaAssets.Get(guga, second.File).PixelSize.Width == 3840, "Guga own variant resources " + action);
        }
        Check(gugaAssets.Resolve(guga, "idle", false).Fps == 4, "Guga keeps manifest idle cadence");
        Check(SpriteAssets.AvatarPath(new PetSkin { Id = "gugugaga", Name = "import", Prompt = "", Folder = directory }) == null, "imported same ID cannot borrow Guga avatar");
        var packSource = Path.Combine(directory, "guga-complete-source"); Directory.CreateDirectory(packSource);
        const string prefix = "bundled-skins/gugugaga/";
        using (var input = SpriteAssets.OpenAsset(prefix + "pet.json")) using (var output = File.Create(Path.Combine(packSource, "pet.json"))) input.CopyTo(output);
        foreach (var path in guga.Clips.Values.Concat(guga.ExtraClips.Values).Concat(guga.Alternates.Values).Select(c => c.File).Concat(new[] { guga.Gaze!.File, guga.Avatar!, guga.Preview! }).Distinct())
        {
            var target = Path.Combine(packSource, path[prefix.Length..]); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = SpriteAssets.OpenAsset(path); using var output = File.Create(target); input.CopyTo(output);
        }
        var archive = Path.Combine(directory, "guga-complete.zip"); System.IO.Compression.ZipFile.CreateFromDirectory(packSource, archive);
        var installed = SpriteAssets.Import(archive, directory);
        Check(SpriteAssets.HasExtra(installed, "special2") && SpriteAssets.HasExtra(installed, "stumble"), "installed package enables second signature and stumble capabilities");
        Check(installed.Id == guga.Id && installed.PackageId == "gugugaga" && installed.Folder != null && SpriteAssets.LoadSkins(directory).Count == skins.Count, "ZIP replaces bundled resource without duplicate companion");
        Check(gugaAssets.GetAvatar(installed) is { PixelSize.Width: 512 }, "installed resource package supplies its own portrait");
        foreach (var action in SpriteAssets.PairedActions.Append("jumping"))
        {
            var first = gugaAssets.Resolve(installed, action, true); var second = gugaAssets.Resolve(installed, action, false);
            Check(first.File != second.File && gugaAssets.Get(installed, first.File).PixelSize.Width == 3840 && gugaAssets.Get(installed, second.File).PixelSize.Width == 3840, "external package keeps both action variants " + action);
        }
        var packSettings = new PetSettings(); PersonaProfiles.Activate(packSettings, installed.Id, installed.Prompt, installed.Voices);
        Check(PersonaProfiles.VoiceFor(packSettings, installed.Id, "speech", new ConnectionSettings { BaseUrl = "https://api.openai.com/v1", Model = "tts-1", Protocol = "openai" }) == "shimmer", "external package supplies supported speech recommendation");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(packSource, "pet.json")))!;
        json["pack_version"] = "update-test"; json["name"] = "咕嘎 · 更新"; File.WriteAllText(Path.Combine(packSource, "pet.json"), json.ToJsonString());
        var updated = SpriteAssets.Import(Path.Combine(packSource, "pet.json"), directory);
        Check(updated.Id == installed.Id && updated.Folder != installed.Folder && SpriteAssets.LoadSkins(directory).Count == skins.Count, "new same-ID version atomically replaces resource selection");
        var legacySource = Path.Combine(directory, "guga-legacy-source"); Directory.CreateDirectory(legacySource);
        foreach (var path in Directory.GetFiles(packSource, "*.png", SearchOption.AllDirectories)) { var target = Path.Combine(legacySource, Path.GetRelativePath(packSource, path)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target); }
        foreach (var key in new[] { "id", "pack_version", "avatar", "special_action2", "extra_animations", "alternate_animations", "voice_presets" }) json.AsObject().Remove(key);
        json["name"] = guga.Name; File.WriteAllText(Path.Combine(legacySource, "pet.json"), json.ToJsonString());
        var migrated = SpriteAssets.Import(Path.Combine(legacySource, "pet.json"), directory);
        Check(migrated.Id.StartsWith("skin-") && migrated.PackageId == "gugugaga" && SpriteAssets.LoadSkins(directory).Count == skins.Count, "legacy original keeps personal identity with one installed companion");
        Check(SpriteAssets.Import(Path.Combine(legacySource, "pet.json"), directory).Id == migrated.Id, "repeated legacy import returns retained identity");
        var previewBytes = File.ReadAllBytes(Path.Combine(legacySource, "preview.png"));
        using (var previewStream = new MemoryStream(previewBytes))
        using (var bitmap = new Bitmap(previewStream)) bitmap.Save(Path.Combine(legacySource, "changed-preview.png"), new PngBitmapEncoderOptions());
        File.Copy(Path.Combine(legacySource, "changed-preview.png"), Path.Combine(legacySource, "preview.png"), true);
        var custom = SpriteAssets.Import(Path.Combine(legacySource, "pet.json"), Path.Combine(directory, "modified-preview-data"));
        Check(custom.PackageId == null, "custom legacy preview is preserved as a separate companion");
        SpriteAssets.Remove(migrated, directory);
        Check(SpriteAssets.LoadSkins(directory).Count == skins.Count - 1, "removing package persists across catalog reload");
        Check(SpriteAssets.Import(archive, directory).Id == migrated.Id && SpriteAssets.LoadSkins(directory).Count == skins.Count, "reimport restores removed package with retained personal identity");
        foreach (var candidate in SpriteAssets.LoadSkins(directory).Skip(1)) SpriteAssets.Remove(candidate, directory);
        bool lastBlocked = false; try { SpriteAssets.Remove(SpriteAssets.LoadSkins(directory).Single(), directory); } catch (InvalidOperationException) { lastBlocked = true; }
        Check(lastBlocked, "last companion cannot be removed"); CompanionPackStorage.RestoreHidden(directory);
    }
    using (var avatars = new SpriteAssets()) foreach (var skin in skins)
    {
        var avatar = avatars.GetAvatar(skin);
        Check(SpriteAssets.AvatarPath(skin) == (skin.Avatar ?? "avatars/" + skin.Id + ".png") && avatar is { PixelSize.Width: > 100, PixelSize.Height: > 100 } && avatar.PixelSize.Width == avatar.PixelSize.Height, "bundled companion uses its dedicated square portrait " + skin.Id);
        Check(ReferenceEquals(avatar, avatars.GetAvatar(skin)), "portrait bitmap is cached per companion " + skin.Id);
    }
    foreach (var skin in skins)
    {
        using var assets = new SpriteAssets();
        foreach (var action in SpriteAssets.PairedActions)
        foreach (var original in new[] { false, true })
        {
            var clip = assets.Resolve(skin, action, original); var bitmap = assets.Get(skin, clip.File);
            Check(clip.Fps == 8, skin.Id + "/" + action + " runs at 8FPS");
            for (int frame = 0; frame < clip.Frames; frame++)
            {
                var rect = SpriteAssets.Rect(clip, frame);
                Check(rect.Right <= bitmap.PixelSize.Width && rect.Bottom <= bitmap.PixelSize.Height, "frame inside atlas");
                Check(!assets.Hit(skin, clip, frame, -1, 0), "outside alpha is transparent");
                bool visible = false, transparent = false;
                for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++) { var hit = assets.Hit(skin, clip, frame, x / 20.0, y / 20.0); visible |= hit; transparent |= !hit; }
                Check(visible && transparent, skin.Id + "/" + action + " has visible alpha and transparent area");
            }
        }
        foreach (var original in new[] { false, true })
        {
            var player = new InteractionPlayback((action, version) => assets.Resolve(skin, action, version));
            player.Play("feeding", original);
            int length = player.Clip.Frames;
            player.Advance(60); Check(player.Frame == 1, "delayed clock displays next frame only");
            for (int i = 1; i < length; i++) player.Advance(.125);
            Check(player.Action == "happy" && player.Original == original && player.Frame == 0, "feeding keeps version into happy");
            player.Play("lifted", original); player.Land(false);
            Check(player.Action == "landing" && player.Original == original, "lift and normal landing paired");
            player.Play("lifted", original); player.Land(true);
            Check(player.Action == "stumble" && player.Original == original, "lift and stumble paired");
        }
    }
    var source = Path.Combine(directory, "source"); Directory.CreateDirectory(source);
    using (var bitmap = new WriteableBitmap(new PixelSize(512, 704), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul))
    {
        var pixels = new byte[512 * 704 * 4];
        for (int row = 0; row < 11; row++) for (int col = 0; col < 8; col++) for (int y = 16; y < 48; y++) for (int x = 16; x < 48; x++) { var i = ((row * 64 + y) * 512 + col * 64 + x) * 4; pixels[i] = 180; pixels[i + 3] = 255; }
        using (var buffer = bitmap.Lock()) Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        bitmap.Save(Path.Combine(source, "spritesheet.png"), new PngBitmapEncoderOptions());
    }
    var manifest = Path.Combine(source, "pet.json");
    File.WriteAllText(manifest, "{\"version\":1,\"name\":\"大肥鱼\",\"atlas\":\"spritesheet.png\"}");
    var imported = SpriteAssets.Import(manifest, directory);
    Check(!imported.BuiltIn && imported.Id.StartsWith("skin-"), "same-name skin gets independent identity");
    using (var assets = new SpriteAssets()) Check(assets.Resolve(imported, "feeding", false).File == "spritesheet.png", "custom skin never uses bundled interaction");
    using (var isolatedAvatars = new SpriteAssets())
    {
        var customSameId = new PetSkin { Id = "e-bao", Name = "导入伙伴", Prompt = "", Folder = imported.Folder, Atlas = imported.Atlas };
        Check(SpriteAssets.AvatarPath(customSameId) == null && isolatedAvatars.GetAvatar(customSameId) == null && isolatedAvatars.Resolve(customSameId, "idle", false).File == imported.Atlas, "imported companion sharing an ID retains its own idle preview instead of a bundled portrait");
        Check(!ReferenceEquals(isolatedAvatars.Get(skins[0], skins[0].Atlas!), isolatedAvatars.Get(customSameId, customSameId.Atlas!)), "custom and bundled sprite caches remain separate even for an identical ID");
    }
    File.WriteAllText(manifest, "{\"version\":1,\"name\":\"bad\",\"atlas\":\"../escape.png\"}");
    Reject(() => SpriteAssets.Import(manifest, directory), "path traversal");
    Check(SpriteAssets.LoadSkins(directory).Count == skins.Count + 1, "rejected import preserves existing skins");
    var abandoned = Path.Combine(directory, "skins", ".import-abandoned"); Directory.CreateDirectory(abandoned);
    File.Copy(Path.Combine(source, "spritesheet.png"), Path.Combine(abandoned, "spritesheet.png"));
    File.WriteAllText(Path.Combine(abandoned, "pet.json"), "{\"version\":1,\"name\":\"interrupted\",\"atlas\":\"spritesheet.png\"}");
    Check(SpriteAssets.LoadSkins(directory).Count == skins.Count + 1, "interrupted staging import never appears as a companion");
    Check(AboutWindow.MatchesArchitecture("DeskPet-macOS-0.6.0-x86_64-preview.zip", "x64"), "Intel release packaging name matches current architecture");
    Check(!AboutWindow.MatchesArchitecture("DeskPet-macOS-0.6.0-arm64-preview.zip", "x64"), "Intel query excludes arm64 archive");
    Check(!AboutWindow.MatchesArchitecture("DeskPet-Windows-macos-notes-x64-template.zip", "x64"), "platform words inside an unrelated filename are not a Mac package");
    Check(!AboutWindow.MatchesArchitecture("DeskPet-macOS-0.6.0-universal-x64.zip", "x64"), "universal or unknown CPU package is not an Intel package");
    const string intelDmg = "DeskPet-macOS-0.6.0-x86_64-preview.dmg", intelZip = "DeskPet-macOS-0.6.0-x86_64-preview.zip", armDmg = "DeskPet-macOS-0.6.0-arm64-preview.dmg";
    using (var mixed = Release(Asset("DeskPet-Setup-0.6.0-x64.exe"), Asset("DeskPet-Windows-0.6.0-x64.zip"), Asset("DeskPet-Windows-macos-notes-x64-template.zip"), Asset("DeskPet-macOS-0.6.0-universal.zip"), Asset("DeskPet-macOS-0.6.0-x64.zip"), Asset(armDmg), Asset(intelZip), Asset(intelDmg)))
    {
        var intel = AboutWindow.SelectMacAsset(mixed.RootElement, "Eason4869/deskpet", "X64");
        Check(intel?.Name == intelDmg && intel.Preview, "mixed platform and CPU release chooses Intel DMG before ZIP");
        Check(intel?.DownloadUrl.AbsoluteUri == "https://github.com/Eason4869/deskpet/releases/download/v0.6.0/" + intelDmg, "download target is the exact official compatible attachment");
        Check(AboutWindow.SelectMacAsset(mixed.RootElement, "Eason4869/deskpet", "Arm64")?.Name == armDmg, "Apple Silicon excludes Intel and Windows assets");
        Check(AboutWindow.SelectMacAsset(mixed.RootElement, "Eason4869/deskpet", "X86") == null, "unsupported CPU never falls back to another architecture");
    }
    foreach (var invalidUrl in new[]
    {
        "http://github.com/Eason4869/deskpet/releases/download/v0.6.0/" + intelDmg,
        "https://github.com/other/deskpet/releases/download/v0.6.0/" + intelDmg,
        "https://github.com/Eason4869/other/releases/download/v0.6.0/" + intelDmg,
        "https://github.com/Eason4869/deskpet/releases/download/v0.5.6/" + intelDmg,
        "https://github.com/Eason4869/deskpet/releases/download/v0.6.0/" + armDmg,
        "https://github.com.evil.test/Eason4869/deskpet/releases/download/v0.6.0/" + intelDmg,
        "https://github.com/Eason4869/deskpet/releases/download/v0.6.0/" + intelDmg + "?redirect=other",
        "https://github.com/Eason4869/deskpet/releases/download/v0.6.0/" + intelDmg + "#fragment",
        "https://account@github.com/Eason4869/deskpet/releases/download/v0.6.0/" + intelDmg,
        "https://github.com:444/Eason4869/deskpet/releases/download/v0.6.0/" + intelDmg
    })
    {
        using var bad = Release(Asset(intelDmg, invalidUrl));
        Check(AboutWindow.SelectMacAsset(bad.RootElement, "Eason4869/deskpet", "x64") == null, "reject unrelated or modified official download URL: " + invalidUrl);
    }
    using (var wrongVersion = Release(Asset("DeskPet-macOS-0.5.6-x86_64-preview.dmg"))) Check(AboutWindow.SelectMacAsset(wrongVersion.RootElement, "Eason4869/deskpet", "x64") == null, "package version must match release tag");
    using (var onlyWindows = Release(Asset("DeskPet-Setup-0.6.0-x64.exe"), Asset(armDmg))) Check(AboutWindow.SelectMacAsset(onlyWindows.RootElement, "Eason4869/deskpet", "x64") == null, "no suitable Mac CPU package leaves update download unavailable");
    using (var signed = Release(Asset(intelDmg), Asset("DeskPet-macOS-0.6.0-x86_64.dmg"))) Check(AboutWindow.SelectMacAsset(signed.RootElement, "Eason4869/deskpet", "x64") is { Preview: false }, "prefer signed release over preview of the same format");
    using (var zipFallback = Release(Asset(intelZip))) Check(AboutWindow.SelectMacAsset(zipFallback.RootElement, "Eason4869/deskpet", "x86_64")?.Name == intelZip, "ZIP remains valid when no compatible DMG exists");

    var runningVersion = ReleaseVersion.Parse(ReleaseInfo.DisplayVersion).AssemblyBase;
    var runningSeries = runningVersion.Major + "." + runningVersion.Minor;
    var followingSeries = runningVersion.Major + "." + (runningVersion.Minor + 1);
    var migrationDirectory = Path.Combine(directory, "upgrade-from-0.5.6");
    var legacy = new PetSettings { OnboardingState = "completed", OnboardingStep = 12, LastOnboardingSeries = "0.5" };
    Check(!OnboardingUpgrade.Prepare(legacy, "0.5.6").ShowGuide, "actual 0.5.6 completed configuration is already claimed in 0.5 series");
    new TestRuntime(migrationDirectory).InitializeProtection();
    var migrationStore = new SettingsStore(Path.Combine(migrationDirectory, "settings.json")); migrationStore.Save(legacy);
    using (var upgraded = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(migrationDirectory)))
    {
        Check(upgraded.ShouldShowAutomaticGuide && upgraded.Settings.OnboardingState == "new" && upgraded.Settings.OnboardingStep == 0, "actual saved 0.5.6 configuration triggers current-version UI guide at first step");
        Check(migrationStore.Load().LastOnboardingSeries == runningSeries, "current series upgrade is persisted before opening UI guide");
        var upgradedGuide = new GuideWindow(upgraded); upgradedGuide.Show(); upgradedGuide.Close(); Dispatcher.UIThread.RunJobs();
        Check(migrationStore.Load().OnboardingState == "deferred", "closing upgraded UI guide persists the deferred state");
        upgraded.Pet.Close();
    }
    using (var reopened = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(migrationDirectory)))
    {
        Check(!reopened.ShouldShowAutomaticGuide && reopened.Settings.LastOnboardingSeries == runningSeries, "restarting current version after closing upgrade guide never opens it again");
        reopened.Pet.Close();
    }
    var runtime = new TestRuntime(Path.Combine(directory, "data"));
    using var controller = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), runtime);
    assertions += ImageUiChecks.Run(Path.Combine(directory, "image-ui"));
    const string richMarkdown = "# 原生标题\n\n普通 **粗体**、*斜体*、~~删除~~、`inline()` 和 [文档](https://example.com)。\n\n> 引用文本\n\n3. 有序项目\n4. 第二项\n\n- 无序项目\n\n```csharp\nvar answer = 42;\n```\n\n| 名称 | 值 |\n| --- | --- |\n| Markdown | **已渲染** |";
    var richLog = new ConversationLog(); var richReply = richLog.BeginTurn(richMarkdown, "text", out var richUser);
    var richTools = new[] { new AgentToolEvent { Id = "tool-1", Name = "read_file", Summary = "查看 README.md", Status = "running" } };
    richLog.StartProcessing(richReply.Id, true);
    richReply = richLog.Update(richReply.Id, "", "pending", "先检查仓库\n再确认结果", 2500, richTools);
    var richHost = new Window { Width = 660, Height = 720 };
    var richView = new ChatMessageView(richReply, controller, richHost); richHost.Content = richView; richHost.Show(); Dispatcher.UIThread.RunJobs();
    var thought = richView.GetLogicalDescendants().OfType<Expander>().Single(e => e.Name == "ReasoningDetails");
    Check(thought.Header is TextBlock { Text: "处理中" }, "process status has its own stable header, independent of streaming preview");
    var toolDetails = richView.GetLogicalDescendants().OfType<Expander>().Single(e => e.Name == "AgentToolDetails");
    var footer = richView.GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "ReplyActions");
    richHost.MouseMove(new Point(640, 690)); Dispatcher.UIThread.RunJobs();
    var processToggle = thought.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Name == "ProcessToggle");
    var processChevron = processToggle.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "ProcessChevron");
    Check(processChevron.Opacity == 0, "process expander has no idle arrow or default circular chrome");
    var processPoint = processToggle.TranslatePoint(new Point(10, 10), richHost)!.Value;
    richHost.MouseMove(processPoint); Dispatcher.UIThread.RunJobs(); Check(processChevron.Opacity == 1, "hover reveals the trailing gray process chevron");
    richHost.MouseDown(processPoint, MouseButton.Left); richHost.MouseUp(processPoint, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    Check(thought.IsExpanded && processChevron.RenderTransform is RotateTransform { Angle: 90 }, "clicking process header expands details and rotates its trailing chevron");
    richHost.MouseMove(new Point(640, 690)); Dispatcher.UIThread.RunJobs(); Check(processChevron.Opacity == 0, "process chevron hides again after pointer leaves even when expanded"); thought.IsExpanded = false;
    var processPreview = richView.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "ProcessPreview");
    Check(processPreview.Text == "查看 README.md" && processPreview.TextWrapping == TextWrapping.NoWrap && processPreview.TranslatePoint(default, richHost)!.Value.Y >= processToggle.TranslatePoint(new Point(0, processToggle.Bounds.Height), richHost)!.Value.Y, "latest tool preview occupies one separate row below the fixed status header");
    double chevronX = processChevron.TranslatePoint(default, richHost)!.Value.X;
    richTools[0].Summary = "读取 " + new string('x', 500) + ".md\n下一行工具输出";
    richReply = richLog.Update(richReply.Id, "", "pending", tools: richTools); richView.Update(richReply); Dispatcher.UIThread.RunJobs();
    Check(Math.Abs(processChevron.TranslatePoint(default, richHost)!.Value.X - chevronX) < 1 && processPreview.Bounds.Width < richHost.Width && !processPreview.Text!.Contains('\n'), "long streaming file summaries cannot move the header chevron or wrap the collapsed preview");
    foreach (var stage in new[] { ("edit_file", "编辑中"), ("open_directory", "运行中"), ("read_file", "处理中") })
    {
        richTools[0].Name = stage.Item1; richTools[0].Summary = stage.Item1 + " README.md";
        richReply = richLog.Update(richReply.Id, "", "pending", tools: richTools); richView.Update(richReply);
        Check(((TextBlock)thought.Header!).Text == stage.Item2, "process status follows active tool: " + stage.Item1);
    }
    richTools[0].Status = "complete"; richReply = richLog.Update(richReply.Id, "", "pending", tools: richTools); richView.Update(richReply);
    richReply = richLog.Update(richReply.Id, "", "pending", "继续思考\n新的推理内容"); richView.Update(richReply);
    Check(((TextBlock)thought.Header!).Text == "思考中" && processPreview.Text == "新的推理内容" && ((ISolidColorBrush)processPreview.Foreground!).Color == Colors.Black, "new reasoning replaces a completed tool preview and restores the thinking stage");
    richReply = richLog.Update(richReply.Id, "", "pending", "", tools: richTools); richView.Update(richReply);
    Check(thought.IsVisible, "tool-only work retains the same expandable process header");
    richTools[0].Status = "running"; richTools[0].Summary = "查看 README.md";
    richReply = richLog.Update(richReply.Id, "", "pending", "先检查仓库\n再确认结果", tools: richTools); richView.Update(richReply);
    processToggle.Focus(); richHost.KeyPress(Key.Space, default, default, null); richHost.KeyRelease(Key.Space, default, default, null); Dispatcher.UIThread.RunJobs();
    Check(thought.IsExpanded && !processPreview.Parent!.GetValue(Visual.IsVisibleProperty), "keyboard space opens the unified process and hides its collapsed preview"); thought.IsExpanded = false;
    Check(richView.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "ReplyElapsedTime").Text!.StartsWith("处理时间 "), "reply header renders request-scoped work timer");
    Check(thought.IsVisible && !thought.IsExpanded && toolDetails.IsVisible && !footer.IsVisible, "pending reasoning and tool logs precede hidden reply actions");
    Check(thought.GetLogicalDescendants().OfType<SelectableTextBlock>().Any(t => t.FontSize == 14 && t.Foreground is ISolidColorBrush reasoningBrush && reasoningBrush.Color == Colors.Black), "important thinking explanation uses normal black body text");
    Check(toolDetails.Header is TextBlock { Foreground: ISolidColorBrush toolBrush } && toolBrush.Color != Colors.Black, "tool activity remains gray and separate from the explanation");
    richView.Update(richReply, true); Check(thought.IsExpanded && toolDetails.IsExpanded, "detailed process setting expands current reasoning and tool details");
    thought.IsExpanded = false; toolDetails.IsExpanded = false; richView.Update(richReply, true);
    Check(!thought.IsExpanded && !toolDetails.IsExpanded, "detailed streaming updates preserve a user-collapsed process view");
    richView.Update(richReply, false);
    thought.IsExpanded = true; toolDetails.IsExpanded = true;
    richReply = richLog.Update(richReply.Id, "", "pending", "先检查仓库\n再确认结果\n继续处理", 3300, richTools); richView.Update(richReply);
    Check(ReferenceEquals(thought, richView.GetLogicalDescendants().OfType<Expander>().Single(e => e.Name == "ReasoningDetails")) && thought.IsExpanded && toolDetails.IsExpanded, "stream updates preserve reasoning and tool expansion state");
    richReply = richLog.Update(richReply.Id, richMarkdown, "pending"); richView.Update(richReply);
    Check(!thought.IsVisible && !toolDetails.Parent!.GetValue(Visual.IsVisibleProperty) && !footer.IsVisible, "starting final Markdown hides thinking and keeps actions hidden until completion");
    richReply = richLog.Update(richReply.Id, richMarkdown, "complete"); richView.Update(richReply); Dispatcher.UIThread.RunJobs();
    var markdownBody = richView.GetLogicalDescendants().OfType<MarkdownView>().Single();
    var richText = markdownBody.GetLogicalDescendants().OfType<SelectableTextBlock>().ToList();
    IEnumerable<Inline> DescendInline(IEnumerable<Inline> source) { foreach (var inline in source) { yield return inline; if (inline is Span span) foreach (var child in DescendInline(span.Inlines)) yield return child; } }
    var richInlines = richText.SelectMany(t => DescendInline(t.Inlines!)).ToList();
    Check(richText.All(t => t.ContextMenu!.Items.OfType<MenuItem>().Select(i => i.Header as string).SequenceEqual(new[] { "复制", "全选" })), "all Markdown text and code expose only Chinese read-only copy/select-all actions");
    var selectableHeading = richText.First(t => t.Name == "MarkdownHeading"); selectableHeading.ContextMenu!.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Check(selectableHeading.CanCopy && selectableHeading.SelectedText == "原生标题", "Markdown Chinese select-all delegates to the native selectable text control");
    Check(richText.Any(t => t.Name == "MarkdownHeading" && t.FontSize == 26 && t.FontWeight == FontWeight.Bold), "Markdown heading uses native heading typography");
    Check(richInlines.OfType<Span>().Any(s => s.FontWeight == FontWeight.Bold) && richInlines.OfType<Span>().Any(s => s.FontStyle == FontStyle.Italic) && richInlines.OfType<Span>().Any(s => s.TextDecorations == TextDecorations.Strikethrough), "Markdown emphasis and strikethrough render as native spans");
    Check(richText.Any(t => t.Name == "MarkdownCode" && t.Text!.Contains("var answer = 42;") && t.FontFamily.Name.Contains("Menlo")), "fenced code uses selectable monospace control");
    Check(markdownBody.GetLogicalDescendants().OfType<Grid>().Any(g => g.Name == "MarkdownTable" && g.RowDefinitions.Count == 2 && g.ColumnDefinitions.Count == 2), "Markdown table has real native rows and columns");
    Check(markdownBody.GetLogicalDescendants().OfType<Border>().Any(b => b.Name == "MarkdownQuote") && markdownBody.GetLogicalDescendants().OfType<StackPanel>().Count(p => p.Name == "MarkdownList") == 2, "Markdown quotes and both list forms have native layout");
    Check(richInlines.OfType<InlineUIContainer>().Any(i => i.Child is Button { Name: "MarkdownLink" }), "Markdown URL has a clickable inline link");
    Check(footer.IsVisible && footer.Children.OfType<Button>().Select(b => b.Name).SequenceEqual(new[] { "CopyReply", "ReadReply", "MoreReply" }), "complete reply exposes exactly three icon actions");
    var moreReply = footer.Children.OfType<Button>().Single(b => b.Name == "MoreReply");
    Check(moreReply.ContextMenu!.Items.OfType<MenuItem>().Select(i => i.Header as string).Contains("导出本条 Markdown") && moreReply.ContextMenu.Items.OfType<MenuItem>().Count() == 4, "more menu includes regenerate, plain text, Markdown export and reasoning history");
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var markdownFrame = richHost.CaptureRenderedFrame())
    {
        Check(markdownFrame != null && markdownFrame.PixelSize.Width > 500, "native Markdown conversation renders to bitmap");
        var markdownArtifacts = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(markdownArtifacts);
        markdownFrame!.Save(Path.Combine(markdownArtifacts, "chat-markdown.png"), new PngBitmapEncoderOptions());
    }
    moreReply.ContextMenu.Items.OfType<MenuItem>().Single(i => i.Header as string == "查看思考过程").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Check(thought.IsVisible && thought.IsExpanded, "hidden thinking can be reopened without losing expansion state");
    richReply = richLog.Update(richReply.Id, richMarkdown, "pending"); richView.Update(richReply);
    Check(!thought.IsVisible && !footer.IsVisible, "a regenerated reply resets explicit thinking history and hides actions while streaming");
    var richUserView = new ChatMessageView(richUser, controller, richHost);
    Check(richUserView.IsUser && richUserView.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right && richUserView.GetLogicalDescendants().OfType<MarkdownView>().Any() && !richUserView.GetLogicalDescendants().OfType<StackPanel>().Any(p => p.Name == "ReplyActions"), "user Markdown retains right-aligned bubble without assistant actions");
    richHost.Close();
    var sentImagePath = Path.Combine(directory, "头像预览.png"); using (var avatarInput = SpriteAssets.OpenAsset("avatars/e-bao.png")) using (var avatarOutput = File.Create(sentImagePath)) avatarInput.CopyTo(avatarOutput);
    var sentLog = new ConversationLog(); sentLog.BeginTurn("帮我看看这张图片。", "text", out var sentUser); sentLog.Attach(sentUser.Id, new[] { ChatAttachment.Read(sentImagePath), ChatAttachment.FromText("调查笔记.txt", "notes") }); sentUser = sentLog.Messages.First();
    var sentHost = new Window { Width = 640, Height = 350, Background = Brushes.White };
    var sentView = new ChatMessageView(sentUser, controller, sentHost) { MaxWidth = 304 }; sentHost.Content = new Border { Padding = new Thickness(16), Child = sentView }; sentHost.Show(); Dispatcher.UIThread.RunJobs();
    var sentArea = sentView.GetLogicalDescendants().OfType<WrapPanel>().Single(p => p.Name == "SentAttachments"); var bubble = sentView.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "UserBubble");
    var sentPicture = sentArea.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "SentImageCard");
    var userActions = sentView.GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "UserMessageActions");
    sentHost.MouseMove(new Point(1, 1)); Dispatcher.UIThread.RunJobs(); Check(userActions.Opacity == 0, "user copy and timestamp are hidden by default");
    var userPoint = bubble.TranslatePoint(new Point(12, 12), sentHost)!.Value; sentHost.MouseMove(userPoint); Dispatcher.UIThread.RunJobs();
    Check(userActions.Opacity == 1 && userActions.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "UserMessageTime").Text == sentUser.Timestamp.ToLocalTime().ToString("HH:mm"), "hovering sent bubble reveals local send time and copy action");
    userActions.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CopyUserMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    Check(Result(sentHost.Clipboard!.TryGetTextAsync()) == sentUser.Content, "user copy writes the original message through the isolated headless clipboard");
    sentHost.MouseMove(new Point(1, 1)); Dispatcher.UIThread.RunJobs(); Check(userActions.Opacity == 0, "user actions hide again without changing the bubble layout");
    Check(sentPicture.Width == 80 && sentPicture.Height == 80 && !sentPicture.GetLogicalDescendants().OfType<TextBlock>().Any() && sentPicture.GetLogicalDescendants().OfType<Image>().Single().Source != null, "sent images are independent 80 pixel thumbnails with no repeated filename");
    Check(!bubble.GetLogicalDescendants().OfType<Border>().Any(b => b.Name is "SentImageCard" or "SentFileCard") && sentArea.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right && bubble.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right && bubble.Bounds.Width <= 304, "sent attachment cards sit outside the right-aligned half-width blue text bubble");
    Check(sentArea.TranslatePoint(new Point(0, sentArea.Bounds.Height), sentHost)!.Value.Y <= bubble.TranslatePoint(new Point(0, 0), sentHost)!.Value.Y && sentArea.GetLogicalDescendants().OfType<TextBlock>().Single().Text == "调查笔记.txt", "sent attachment row is above the message and file cards retain their filename");
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var sentFrame = sentHost.CaptureRenderedFrame()) { var artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(artifacts); sentFrame!.Save(Path.Combine(artifacts, "chat-sent-attachments.png"), new PngBitmapEncoderOptions()); }
    sentHost.Close();
    using (var portraitAssets = new SpriteAssets())
    {
        var portraitRows = Ui.Row(); foreach (var skin in skins) portraitRows.Children.Add(Ui.Column(new AvatarView { Assets = portraitAssets, Skin = skin, Width = 64, Height = 64 }, Ui.Text(skin.Name, 12)));
        var hoverButton = Ui.Button("灰色悬停", () => { }); var keyboardButton = Ui.Button("键盘焦点", () => { });
        var sendSample = Ui.Button("↑", () => { }); sendSample.Name = "PrimaryChatAction"; sendSample.Width = sendSample.Height = 32; sendSample.Padding = new Thickness(0); sendSample.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center; sendSample.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center; sendSample.CornerRadius = new CornerRadius(16); sendSample.Background = new SolidColorBrush(Color.Parse("#357FF5")); sendSample.Foreground = Brushes.White;
        var linkSample = Ui.Button("蓝色语义链接", () => { }); linkSample.Foreground = new SolidColorBrush(Color.Parse("#246ED8"));
        var hoverCombo = new ComboBox { Width = 200, ItemsSource = new[] { "模型下拉框", "第二个模型" }, SelectedIndex = 0 };
        var hoverCheck = new CheckBox { Content = "开关/复选框", IsChecked = true }; var hoverSwitch = new ToggleSwitch { OffContent = "关闭" };
        var hoverMenu = new MenuItem { Header = "菜单悬停 / 键盘选择", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control), IsSelected = true };
        var statesHost = new Window { Width = 620, Height = 360, Background = Brushes.White, Content = new Border { Padding = new Thickness(20), Child = Ui.Column(portraitRows, Ui.Row(hoverButton, keyboardButton, linkSample, sendSample), Ui.Row(hoverCombo, hoverCheck, hoverSwitch), hoverMenu) } };
        statesHost.Show(); Dispatcher.UIThread.RunJobs();
        ((IPseudoClasses)hoverButton.Classes).Set(":pointerover", true); ((IPseudoClasses)sendSample.Classes).Set(":pointerover", true); ((IPseudoClasses)linkSample.Classes).Set(":pointerover", true); ((IPseudoClasses)hoverCombo.Classes).Set(":pointerover", true); ((IPseudoClasses)hoverCheck.Classes).Set(":pointerover", true); ((IPseudoClasses)hoverSwitch.Classes).Set(":pointerover", true);
        keyboardButton.Focus(NavigationMethod.Tab); Dispatcher.UIThread.RunJobs();
        ContentPresenter Presenter(Control control) => control.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        bool Gray(IBrush? brush) => brush is ISolidColorBrush solid && solid.Color == DesktopAppearance.HoverColor;
        Check(Gray(Presenter(hoverButton).Background) && Gray(Presenter(keyboardButton).Background) && hoverButton.FocusAdorner == null && keyboardButton.FocusAdorner == null && Presenter(hoverButton).CornerRadius == new CornerRadius(8), "button hover and keyboard focus use the same rounded gray fill without a blue focus frame");
        Check(Presenter(sendSample).Background is ISolidColorBrush blueSend && blueSend.Color == Color.Parse("#357FF5") && Presenter(linkSample).Foreground is ISolidColorBrush blueLink && blueLink.Color == Color.Parse("#246ED8"), "gray interaction styles preserve the send button and semantic link blues");
        Check(Gray(hoverCombo.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Background").Background) && Gray(hoverCheck.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Border").Background) && Gray(hoverSwitch.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "OuterBorder").Background), "dropdown and switch interactions use native template gray fills");
        Check(Gray(hoverMenu.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_LayoutRoot").Background), "keyboard-selected menu entry uses rounded gray interaction fill");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (var statesFrame = statesHost.CaptureRenderedFrame()) statesFrame!.Save(Path.Combine(AppContext.BaseDirectory, "artifacts", "avatars-and-interaction-states.png"), new PngBitmapEncoderOptions());
        statesHost.Close();
    }
    Check(controller.ShouldShowAutomaticGuide, "fresh install opens initial guide once");
    var scrollingRows = Ui.Column(); for (int row = 0; row < 50; row++) scrollingRows.Children.Add(Ui.Text("滚动条验证内容 " + (row + 1)));
    var narrowScroll = new ScrollViewer { Height = 230, Width = 320, Content = scrollingRows, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    var scrollHost = new Window { Width = 360, Height = 270, Background = Brushes.White, Content = new Border { Padding = new Thickness(16), Child = narrowScroll } }; scrollHost.Show(); Dispatcher.UIThread.RunJobs();
    var verticalBar = narrowScroll.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
    var dragThumb = verticalBar.GetVisualDescendants().OfType<Thumb>().Single(); var slimThumb = dragThumb.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SlimScrollThumb");
    Check(slimThumb.Bounds.Width == 6 && dragThumb.Bounds.Width >= 12 && verticalBar.GetVisualDescendants().OfType<RepeatButton>().Where(b => b.Name is "PART_LineUpButton" or "PART_LineDownButton").All(b => !b.IsVisible), "scrollbar renders a six pixel thumb with a twelve pixel grab area and no arrow buttons");
    Check(slimThumb.Background is ISolidColorBrush normalScroll && normalScroll.Color == Color.Parse("#C5C9D0") && verticalBar.Background is ISolidColorBrush trackScroll && trackScroll.Color.A == 0, "scrollbar rests in light gray on a transparent track");
    ((IPseudoClasses)verticalBar.Classes).Set(":pointerover", true); Dispatcher.UIThread.RunJobs();
    Check(slimThumb.Background is ISolidColorBrush hoverScroll && hoverScroll.Color == Color.Parse("#3B3B3B"), "scrollbar hover darkens the narrow thumb toward black");
    ((IPseudoClasses)dragThumb.Classes).Set(":pressed", true); Dispatcher.UIThread.RunJobs();
    Check(slimThumb.Background is ISolidColorBrush pressedScroll && pressedScroll.Color == Color.Parse("#222222"), "scrollbar drag state darkens the thumb further");
    ((IPseudoClasses)dragThumb.Classes).Set(":pressed", false); ((IPseudoClasses)verticalBar.Classes).Set(":pointerover", false);
    var thumbCenter = dragThumb.TranslatePoint(new Point(1, dragThumb.Bounds.Height / 2), scrollHost)!.Value;
    scrollHost.MouseMove(thumbCenter); scrollHost.MouseDown(thumbCenter, MouseButton.Left); scrollHost.MouseMove(thumbCenter + new Vector(0, 30)); scrollHost.MouseUp(thumbCenter + new Vector(0, 30), MouseButton.Left); Dispatcher.UIThread.RunJobs();
    Check(narrowScroll.Offset.Y > 0, "slim scrollbar remains draggable from the invisible padded grab area beside its six pixel thumb");
    AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using (var scrollbarFrame = scrollHost.CaptureRenderedFrame()) scrollbarFrame!.Save(Path.Combine(AppContext.BaseDirectory, "artifacts", "slim-scrollbar.png"), new PngBitmapEncoderOptions()); scrollHost.Close();
    controller.Settings.OnboardingState = "completed"; controller.Save();
    using (var restarted = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(Path.Combine(directory, "data")))) { Check(!restarted.ShouldShowAutomaticGuide, "same-series restart does not reopen completed guide"); restarted.Pet.Close(); }
    Check(!OnboardingUpgrade.Prepare(controller.Settings, runningSeries + ".99").ShowGuide, "patch upgrade does not show UI guide");
    var upgrade = OnboardingUpgrade.Prepare(controller.Settings, followingSeries + ".0"); Check(upgrade.ShowGuide && controller.Settings.OnboardingStep == 0, "next series upgrade resets actual UI guide state");
    Check(!OnboardingUpgrade.Prepare(controller.Settings, followingSeries + ".1").ShowGuide, "claimed next series guide not retriggered");
    Check(!OnboardingUpgrade.Prepare(controller.Settings, "0.6.0").ShowGuide, "downgrade does not reopen UI guide");
    var windows = new Window[] { new ChatWindow(controller, true), new ChatWindow(controller, false), new SettingsWindow(controller), new AboutWindow(controller), new LiveWindow(controller) };
    foreach (var window in windows) { window.Show(); window.Measure(new Size(900, 800)); window.Arrange(new Rect(0, 0, 900, 800)); Dispatcher.UIThread.RunJobs(); Check(window.IsVisible && window.Bounds.Width > 0, window.GetType().Name + " opens with actual controls"); }
    var composerChat = (ChatWindow)windows[0];
    Check(composerChat.GetLogicalDescendants().OfType<AvatarView>().Single(a => a.Name == "ChatPartnerAvatar").Skin.Id == controller.Skin.Id && windows[4].GetLogicalDescendants().OfType<AvatarView>().Any(), "chat and Live use the current companion portrait");
    var partnerSettings = (SettingsWindow)windows[2];
    var initialTheme = controller.Settings.ThemeMode;
    partnerSettings.SelectSection("behavior"); Dispatcher.UIThread.RunJobs();
    var themePicker = partnerSettings.GetLogicalDescendants().OfType<ThemeModePicker>().Distinct().Single();
    void ChooseTheme(string mode) => themePicker.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Name == "ThemeChoice" + mode).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(themePicker.GetLogicalDescendants().OfType<Button>().Distinct().Count() == 3, "desktop settings expose three theme preview cards");
    ChooseTheme("dark"); Dispatcher.UIThread.RunJobs();
    Check(Application.Current!.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark && controller.Settings.ThemeMode == "dark", "desktop theme selection applies dark immediately");
    Check(new SettingsStore(Path.Combine(controller.DataDirectory, "settings.json")).Load().ThemeMode == "dark", "desktop theme selection persists dark");
    var themedInk = DesktopTheme.Brush("#203754"); var themedSurface = DesktopTheme.Brush("#FFFFFF");
    Check(themedInk.Color.R > 160 && themedSurface.Color.R < 60 && composerChat.Background == themedSurface, "existing desktop chat follows dark palette");
    Check(DesktopTheme.Caret is SolidColorBrush darkCaret && darkCaret.Color == Colors.White, "desktop dark caret is white");
    var selectedThemeCard = themePicker.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Name == "ThemeChoicedark");
    foreach (var cardState in new[] { ":pointerover", ":pressed", ":focus-visible" })
    {
        ((IPseudoClasses)selectedThemeCard.Classes).Set(cardState, true); Dispatcher.UIThread.RunJobs();
        var cardPresenter = selectedThemeCard.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        Check(cardPresenter.BorderBrush is ISolidColorBrush outline && outline.Color == DesktopTheme.Brush("#456DE6").Color, "selected theme blue outline survives " + cardState);
        ((IPseudoClasses)selectedThemeCard.Classes).Set(cardState, false);
    }
    ChooseTheme("light"); Dispatcher.UIThread.RunJobs();
    Check(themedInk.Color.R < 100 && themedSurface.Color.R == 255, "existing desktop brushes return to light");
    ChooseTheme("system"); Dispatcher.UIThread.RunJobs();
    Check(Application.Current.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Default && controller.Settings.ThemeMode == "system", "desktop system choice inherits OS theme");
    ChooseTheme(initialTheme);
    partnerSettings.SelectSection("partner"); Dispatcher.UIThread.RunJobs();
    Check(partnerSettings.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(c => ReferenceEquals(c.ItemsSource, controller.Skins)).ItemTemplate != null && controller.Pet.ContextMenu!.Items.OfType<MenuItem>().Count(i => i.Icon is AvatarView) == controller.Skins.Count, "partner selector and every partner menu entry use avatar views");
    Check(!controller.Settings.AgentEnabled, "new Avalonia client defaults to pure conversation without file Agent");
    var modeButton = composerChat.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ChatModeAction");
    var modeItems = modeButton.ContextMenu!.Items.OfType<MenuItem>().ToList();
    Check(modeItems[0].Icon is Control { Name: "ConversationBubbleGlyph" } && modeItems[1].Icon is Control { Name: "WorkPencilGlyph" } && modeButton.GetLogicalDescendants().OfType<Avalonia.Controls.Shapes.Path>().Any(p => p.Name == "ConversationBubbleGlyph"), "mode menu distinguishes the chat bubble from the work pencil and defaults to the chat glyph");
    Check(modeItems.Select(i => i.Name).SequenceEqual(new[] { "ConversationMode", "WorkMode" }) && modeItems.All(i => i.ToggleType == MenuItemToggleType.Radio), "mode menu contains exactly two exclusive conversation and work choices");
    Check(modeItems[0].IsChecked && !modeItems[1].IsChecked && modeItems[1].IsEnabled == OperatingSystem.IsWindows(), "conversation is initially selected and unsupported Mac work mode is disabled");
    var chatDraft = composerChat.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "ChatDraft");
    composerChat.Activate(); chatDraft.Focus(); Dispatcher.UIThread.RunJobs();
    Check(chatDraft.IsFocused && !controller.Settings.AgentEnabled, "keyboard mode test starts focused in pure conversation");
    // Test our handler's consumption separately: the full Avalonia route also
    // sets Handled when normal Tab focus navigation runs on an unsupported OS.
    var modeHandler = typeof(ChatWindow).GetMethod("DraftKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    var handlerTab = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = KeyModifiers.None };
    modeHandler.Invoke(composerChat, new object[] { chatDraft, handlerTab });
    Check(controller.Settings.AgentEnabled == OperatingSystem.IsWindows() && handlerTab.Handled == OperatingSystem.IsWindows(), "mode handler consumes bare Tab only on supported Windows runtime");
    modeItems[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); chatDraft.Focus();
    var toggleMode = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = KeyModifiers.None }; chatDraft.RaiseEvent(toggleMode);
    Dispatcher.UIThread.RunJobs();
    Check(controller.Settings.AgentEnabled == OperatingSystem.IsWindows(), "routed bare Tab switches to work only on a supported Windows runtime");
    Check(toggleMode.Handled && chatDraft.IsFocused == OperatingSystem.IsWindows(), "Windows mode shortcut retains draft focus while unsupported platforms navigate normally");
    Check(modeButton.GetLogicalDescendants().OfType<Avalonia.Controls.Shapes.Path>().Any(p => p.Name == (OperatingSystem.IsWindows() ? "WorkPencilGlyph" : "ConversationBubbleGlyph")), "Tab immediately swaps the visible mode glyph to the pencil only when work mode is available");
    var handlerShiftTab = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = KeyModifiers.Shift };
    modeHandler.Invoke(composerChat, new object[] { chatDraft, handlerShiftTab });
    Check(!handlerShiftTab.Handled && controller.Settings.AgentEnabled == OperatingSystem.IsWindows(), "mode handler leaves Shift Tab to native navigation without changing mode");
    chatDraft.Focus();
    var shiftTab = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = KeyModifiers.Shift }; chatDraft.RaiseEvent(shiftTab); Dispatcher.UIThread.RunJobs();
    Check(controller.Settings.AgentEnabled == OperatingSystem.IsWindows() && shiftTab.Handled && !chatDraft.IsFocused, "Shift Tab bypasses mode shortcut and is consumed by normal focus navigation");
    modeItems[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
    Check(!controller.Settings.AgentEnabled && !new SettingsStore(Path.Combine(controller.DataDirectory, "settings.json")).Load().AgentEnabled, "conversation selection persists without starting work");
    Check(modeButton.GetLogicalDescendants().OfType<Avalonia.Controls.Shapes.Path>().Any(p => p.Name == "ConversationBubbleGlyph"), "returning to conversation restores its bubble glyph");
    var toolbar = composerChat.GetLogicalDescendants().OfType<Grid>().Single(g => g.Name == "ComposerToolbar");
    var modelSelect = composerChat.GetLogicalDescendants().OfType<ComboBox>().Single(b => b.Name == "ChatModels");
    Check(modelSelect.RenderTransform is TranslateTransform { Y: 4 }, "model dropdown has the requested final four pixel vertical adjustment");
    var primaryAction = composerChat.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "PrimaryChatAction");
    var micAction = composerChat.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ChatDictation");
    Check(toolbar.ColumnDefinitions.Count == 4 && Grid.GetColumn(modeButton) == 1 && Grid.GetColumn(modelSelect) == 2 && toolbar.ColumnDefinitions[2].Width.IsStar && modelSelect.MinWidth == 0 && primaryAction.Width == 32 && micAction.Width == 32, "mode occupies a fixed left slot while model shrinks and right actions stay at 32 pixels");
    var attachmentButton = composerChat.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AddAttachment");
    var attachmentMenu = attachmentButton.ContextMenu!.Items.OfType<MenuItem>().ToList();
    Check(attachmentMenu.Select(i => i.Name).Where(n => n != null).SequenceEqual(new[] { "AttachmentFilesMenu", "AttachApplicationSnapshot", "SetAgentGoal", "AgentPlanMode" }), "add menu exposes file/folder, application snapshot, goal and plan mode actions");
    var attachmentFiles = attachmentMenu.Single(i => i.Name == "AttachmentFilesMenu");
    Check(attachmentFiles.Items.Count == 0 && attachmentMenu.All(i => i.Items.Count == 0), "every add menu action is a single direct item without nested menus");
    var applicationSnapshotItem = attachmentMenu.Single(i => i.Name == "AttachApplicationSnapshot");
    Check(!applicationSnapshotItem.IsEnabled, "application snapshot is disabled when no eligible application target exists");
    composerChat.SetSnapshotTarget(new ApplicationSnapshotTarget { AppName = "测试编辑器", Title = "编辑器窗口", IconPng = File.ReadAllBytes(sentImagePath) });
    Check(OperatingSystem.IsWindows() ? applicationSnapshotItem.Header as string == "附加 测试编辑器" && applicationSnapshotItem.Icon is Image && applicationSnapshotItem.IsEnabled : !applicationSnapshotItem.IsEnabled, "eligible application target appears directly with its name and logo on Windows");
    composerChat.SetSnapshotTarget(null);
    var planAction = attachmentMenu.Single(i => i.Name == "AgentPlanMode"); var messagesBeforeGoal = controller.Session.History.Messages.Count;
    planAction.IsChecked = true; planAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Check(controller.Settings.AgentPlanMode && new SettingsStore(Path.Combine(controller.DataDirectory, "settings.json")).Load().AgentPlanMode && controller.Session.History.Messages.Count == messagesBeforeGoal, "plan check persists without sending a model request");
    planAction.IsChecked = false; planAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    attachmentMenu.Single(i => i.Name == "SetAgentGoal").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
    var goalDialog = composerChat.OwnedWindows.Single(w => w.Name == "AgentGoalDialog");
    goalDialog.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "AgentGoalInput").Text = "持续检查发布构建";
    goalDialog.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "SaveAgentGoal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    Check(controller.Settings.AgentGoal == "持续检查发布构建" && new SettingsStore(Path.Combine(controller.DataDirectory, "settings.json")).Load().AgentGoal == "持续检查发布构建" && controller.Session.History.Messages.Count == messagesBeforeGoal && !controller.Session.IsBusy, "goal save persists the goal without auto-submitting work");
    attachmentMenu.Single(i => i.Name == "SetAgentGoal").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
    var clearGoalDialog = composerChat.OwnedWindows.Single(w => w.Name == "AgentGoalDialog");
    clearGoalDialog.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ClearAgentGoal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    clearGoalDialog.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "SaveAgentGoal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    Check(controller.Settings.AgentGoal == "", "goal can be cleared from its dialog");
    var previewImage = ChatAttachment.Read(Path.Combine(source, "spritesheet.png")); var previewText = ChatAttachment.FromText("用于验证省略显示的很长的附件文件名称.txt", "attachment text");
    composerChat.AddAttachments(new[] { previewImage, previewText }); Dispatcher.UIThread.RunJobs();
    var attachmentCards = composerChat.GetLogicalDescendants().OfType<Border>().Where(b => b.Name == "AttachmentCard").ToList();
    Check(attachmentCards.Count == 2 && attachmentCards[0].Width == 120 && attachmentCards[1].Width == 150 && attachmentCards.All(c => c.Height == 100), "image and file attachments use screenshot-sized preview cards");
    Check(attachmentCards[0].GetLogicalDescendants().OfType<Image>().Single().Source != null && attachmentCards[1].GetLogicalDescendants().OfType<TextBlock>().Any(t => t.TextTrimming == TextTrimming.CharacterEllipsis), "image thumbnail decodes and long file name uses ellipsis");
    try { composerChat.AddAttachments(new[] { previewText, previewText, previewText }); throw new Exception("FAIL: attachment count accepted"); } catch (ApiException) { assertions++; }
    Check(composerChat.GetLogicalDescendants().OfType<Border>().Count(b => b.Name == "AttachmentCard") == 2, "invalid attachment addition preserves existing cards");
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var attachmentFrame = composerChat.CaptureRenderedFrame())
    {
        Check(attachmentFrame != null, "attachment composer renders to bitmap");
        var attachmentArtifacts = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(attachmentArtifacts);
        attachmentFrame!.Save(Path.Combine(attachmentArtifacts, "chat-attachments.png"), new PngBitmapEncoderOptions());
    }
    composerChat.Width = 390; composerChat.Height = 620; composerChat.Measure(new Size(390, 620)); composerChat.Arrange(new Rect(0, 0, 390, 620)); Dispatcher.UIThread.RunJobs();
    var modeRight = modeButton.TranslatePoint(new Point(modeButton.Bounds.Width, 0), composerChat)!.Value.X;
    var modelLeft = modelSelect.TranslatePoint(new Point(0, 0), composerChat)!.Value.X;
    var modelRight = modelSelect.TranslatePoint(new Point(modelSelect.Bounds.Width, 0), composerChat)!.Value.X;
    var micLeft = micAction.TranslatePoint(new Point(0, 0), composerChat)!.Value.X;
    Check(modeRight <= modelLeft + 1 && modelRight <= micLeft + 1 && primaryAction.Bounds.Width == 32 && micAction.Bounds.Width == 32, "minimum-width composer keeps mode, model and fixed right actions from overlapping");
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var narrowFrame = composerChat.CaptureRenderedFrame()) narrowFrame!.Save(Path.Combine(AppContext.BaseDirectory, "artifacts", "chat-composer-narrow.png"), new PngBitmapEncoderOptions());
    attachmentCards[0].GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "RemoveAttachment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(composerChat.GetLogicalDescendants().OfType<Border>().Count(b => b.Name == "AttachmentCard") == 1 && !composerChat.GetLogicalDescendants().OfType<Image>().Any(i => i.Name == "AttachmentThumbnail"), "removing an image rebuilds cards and releases its preview");
    var uploadWindow = new ChatWindow(controller, true); uploadWindow.Show(); Dispatcher.UIThread.RunJobs();
    IStorageFile StorageFile(string path) => Result(uploadWindow.StorageProvider.TryGetFileFromPathAsync(path)) ?? throw new Exception("Headless storage provider did not resolve test file");
    var uploadDraft = uploadWindow.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "ChatDraft");
    uploadDraft.Text = ""; uploadDraft.Focus(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    var watermarkVisible = uploadDraft.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == uploadDraft.PlaceholderText && t.IsEffectivelyVisible);
    Check(uploadDraft.IsFocused && watermarkVisible, "focused empty chat editor preserves its watermark");
    Check(uploadDraft.GetVisualDescendants().OfType<Border>().All(b => b.Background is not ISolidColorBrush brush || brush.Color.A == 0), "focused chat editor has no gray fill over its watermark");
    using (var focusedFrame = uploadWindow.CaptureRenderedFrame()) focusedFrame!.Save(Path.Combine(AppContext.BaseDirectory, "artifacts", "chat-focused-placeholder.png"), new PngBitmapEncoderOptions());
    var inputMenu = uploadDraft.ContextMenu!.Items.OfType<MenuItem>().ToList();
    Check(inputMenu.Select(i => i.Header as string).SequenceEqual(new[] { "剪切", "复制", "粘贴", "全选" }) && inputMenu.Select(i => i.InputGesture!.Key).SequenceEqual(new[] { Key.X, Key.C, Key.V, Key.A }), "draft context menu retains native editing with Chinese labels and shortcut hints");
    int UploadCards() => uploadWindow.GetLogicalDescendants().OfType<Border>().Count(b => b.Name == "AttachmentCard");
    var uploadTextPath = Path.Combine(directory, "粘贴文字文件.txt"); File.WriteAllText(uploadTextPath, "file content");
    var invalidUploadPath = Path.Combine(directory, "invalid.bin"); File.WriteAllText(invalidUploadPath, "invalid");
    uploadDraft.Text = "保留草稿";
    Complete(uploadWindow.AddAttachmentPathsAsync(new[] { uploadTextPath, Path.Combine(source, "spritesheet.png") }));
    Check(UploadCards() == 2 && uploadDraft.Text == "保留草稿" && !controller.Settings.AgentEnabled, "shared picker/drop/paste file loader adds image and text atomically without changing draft or Agent mode");
    try { Complete(uploadWindow.AddAttachmentPathsAsync(new[] { uploadTextPath, invalidUploadPath })); throw new Exception("FAIL: unsupported upload accepted"); } catch (ApiException) { assertions++; }
    Check(UploadCards() == 2 && uploadDraft.Text == "保留草稿", "partly invalid upload adds no files and preserves existing draft and attachments");
    var oversizedUpload = Path.Combine(directory, "oversized.txt"); using (var oversized = File.Create(oversizedUpload)) oversized.SetLength(ChatAttachment.MaximumBytes + 1L);
    try { Complete(uploadWindow.AddAttachmentPathsAsync(new[] { oversizedUpload })); throw new Exception("FAIL: oversized upload accepted"); } catch (ApiException) { assertions++; }
    Check(UploadCards() == 2, "four megabyte limit also applies to shared upload paths");
    foreach (var remove in uploadWindow.GetLogicalDescendants().OfType<Button>().Where(b => b.Name == "RemoveAttachment").ToList()) remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(UploadCards() == 0 && DragDrop.GetAllowDrop(uploadWindow), "chat window accepts external file drag while clearing test attachments");
    var dragData = new DataTransfer(); var dragFile = DataTransferItem.CreateFile(StorageFile(uploadTextPath)); dragFile.SetText("不应把拖拽的文件路径插入草稿"); dragData.Add(dragFile);
    var dragOver = new DragEventArgs(DragDrop.DragOverEvent, dragData, uploadDraft, new Point(10, 10), KeyModifiers.None); uploadDraft.RaiseEvent(dragOver);
    Check(dragOver.Handled && dragOver.DragEffects == DragDropEffects.Copy, "file drag over input routes to window and advertises copy");
    var dropped = new DragEventArgs(DragDrop.DropEvent, dragData, uploadDraft, new Point(10, 10), KeyModifiers.None); uploadDraft.RaiseEvent(dropped); Until(() => UploadCards() == 1);
    Check(dropped.Handled && uploadDraft.Text == "保留草稿", "synthetic native drop route imports its file without replacing draft text");
    uploadWindow.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "RemoveAttachment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var folderOne = Path.Combine(directory, "folder-one"); var folderTwo = Path.Combine(directory, "folder-two"); Directory.CreateDirectory(folderOne); Directory.CreateDirectory(folderTwo);
    var priorWorkspace = controller.Settings.AgentWorkspace;
    try { Complete(uploadWindow.AddAttachmentPathsAsync(new[] { folderOne, folderTwo })); throw new Exception("FAIL: multiple folders accepted"); } catch (ApiException) { assertions++; }
    Check(UploadCards() == 0 && controller.Settings.AgentWorkspace == priorWorkspace, "multiple directory upload is rejected before adding files or changing workspace");
    Complete(uploadWindow.AddAttachmentPathsAsync(new[] { folderOne }));
    Check(UploadCards() == 1 && !controller.Settings.AgentEnabled && controller.Settings.AgentWorkspace == priorWorkspace, "single folder adds a path reference without changing Agent mode or work access scope");
    uploadWindow.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "RemoveAttachment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    controller.Settings.AgentWorkspace = priorWorkspace; controller.Save();
    var clipboard = uploadWindow.Clipboard!;
    var isolatedClipboard = clipboard.GetType().Name.Contains("Headless") || clipboard.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Any(f => f.GetValue(clipboard)?.GetType().Name.Contains("Headless") == true);
    Check(isolatedClipboard, "clipboard integration test is isolated in Avalonia HeadlessClipboardImplStub and never writes Windows clipboard");
    var savedClipboard = Result(clipboard.TryGetDataAsync());
    try
    {
        Complete(clipboard.SetTextAsync("中文粘贴")); uploadDraft.Text = "前后"; uploadDraft.SelectionStart = uploadDraft.SelectionEnd = 1;
        uploadDraft.Paste(); Until(() => uploadDraft.Text == "前中文粘贴后");
        Check(UploadCards() == 0, "ordinary native paste preserves selection insertion and adds no attachment");
        uploadDraft.Text = "剪切测试"; uploadDraft.SelectionStart = 0; uploadDraft.SelectionEnd = 2;
        inputMenu.Single(i => i.Header as string == "剪切").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Until(() => uploadDraft.Text == "测试");
        Check(Result(clipboard.TryGetTextAsync()) == "剪切", "Chinese cut delegates selected text to native TextBox editing");
        inputMenu.Single(i => i.Header as string == "全选").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); inputMenu.Single(i => i.Header as string == "复制").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(Result(clipboard.TryGetTextAsync()) == "测试", "Chinese select-all and copy retain native text semantics");
        Complete(clipboard.SetFilesAsync(new[] { StorageFile(uploadTextPath) })); uploadDraft.Text = "不应被文件粘贴覆盖";
        // Headless has its own platform configuration, regardless of its host OS.
        // Native macOS Command+V is covered separately by the real UI smoke run.
        var pasteGesture = uploadWindow.GetPlatformSettings()!.HotkeyConfiguration.Paste.First();
        var pasteKey = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = pasteGesture.Key, KeyModifiers = pasteGesture.KeyModifiers }; uploadDraft.RaiseEvent(pasteKey); Until(() => UploadCards() == 1);
        Check(uploadDraft.Text == "不应被文件粘贴覆盖", "native paste shortcut imports clipboard files without inserting their paths as text");
        using var clipboardImage = new Bitmap(Path.Combine(source, "spritesheet.png")); Complete(clipboard.SetBitmapAsync(clipboardImage));
        uploadDraft.ContextMenu.Open(uploadDraft); Dispatcher.UIThread.RunJobs();
        Check(inputMenu.Single(i => i.Header as string == "粘贴").IsEnabled, "paste menu stays enabled for an image-only clipboard"); uploadDraft.ContextMenu.Close();
        inputMenu.Single(i => i.Header as string == "粘贴").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Until(() => UploadCards() == 2);
        Check(uploadWindow.GetLogicalDescendants().OfType<Image>().Count(i => i.Name == "AttachmentThumbnail") == 1 && uploadDraft.Text == "不应被文件粘贴覆盖", "image-only clipboard becomes a PNG preview card through the Chinese paste menu");
        Complete(uploadWindow.PasteClipboardAsync()); Check(UploadCards() == 3, "repeated image paste leaves the source clipboard bitmap usable");
        var mixedClipboard = new DataTransfer(); var mixedItem = DataTransferItem.CreateFile(StorageFile(uploadTextPath)); mixedItem.SetText("不应混入草稿"); mixedClipboard.Add(mixedItem); Complete(clipboard.SetDataAsync(mixedClipboard));
        Complete(uploadWindow.PasteClipboardAsync()); Check(UploadCards() == 4 && uploadDraft.Text == "不应被文件粘贴覆盖", "file representation takes priority over clipboard fallback text");
        try { Complete(uploadWindow.PasteClipboardAsync()); throw new Exception("FAIL: paste count limit accepted"); } catch (ApiException) { assertions++; }
        Check(UploadCards() == 4 && uploadDraft.Text == "不应被文件粘贴覆盖", "failed fifth attachment paste keeps all prior cards and draft");
        var lateWindow = new ChatWindow(controller, true); lateWindow.Show(); Dispatcher.UIThread.RunJobs();
        var delayedTransfer = new DelayedFileTransfer(); Complete(clipboard.SetDataAsync(delayedTransfer)); var latePaste = lateWindow.PasteClipboardAsync();
        lateWindow.Close(); delayedTransfer.Value.SetResult(StorageFile(uploadTextPath)); Complete(latePaste);
        Check(!lateWindow.GetLogicalDescendants().OfType<Border>().Any(b => b.Name == "AttachmentCard"), "clipboard data arriving after window close cannot create attachments");
        var personaWindow = new ChatWindow(controller, true); personaWindow.Show(); Dispatcher.UIThread.RunJobs();
        var personaTransfer = new DelayedFileTransfer(); Complete(clipboard.SetDataAsync(personaTransfer)); var personaPaste = personaWindow.PasteClipboardAsync();
        var originalSkin = controller.Skin; Complete(controller.SelectSkinAsync(controller.Skins.First(s => s.Id != originalSkin.Id))); Dispatcher.UIThread.RunJobs();
        personaTransfer.Value.SetResult(StorageFile(uploadTextPath)); Complete(personaPaste);
        Check(!personaWindow.GetLogicalDescendants().OfType<Border>().Any(b => b.Name == "AttachmentCard"), "clipboard data arriving after companion change cannot appear in another companion draft");
        Complete(controller.SelectSkinAsync(originalSkin)); Dispatcher.UIThread.RunJobs(); personaWindow.Close();
    }
    finally
    {
        if (savedClipboard != null) Complete(clipboard.SetDataAsync(savedClipboard)); else Complete(clipboard.ClearAsync());
    }
    uploadWindow.Close(); Dispatcher.UIThread.RunJobs();
    var settings = (SettingsWindow)windows[2];
    foreach (var section in new[] { "partner", "behavior", "text", "image", "asr", "speech", "live", "work", "voice", "storage" }) { settings.SelectSection(section); Dispatcher.UIThread.RunJobs(); Check(settings.GetLogicalDescendants().OfType<TabControl>().First().SelectedItem != null, "settings section " + section); }
    var settingsFile = Path.Combine(controller.DataDirectory, "settings.json"); var beforeWorkOpen = File.GetLastWriteTimeUtc(settingsFile);
    _ = new WorkSettingsView(controller, settings); Dispatcher.UIThread.RunJobs();
    Check(File.GetLastWriteTimeUtc(settingsFile) == beforeWorkOpen, "creating work settings does not save or dirty initial values");
    settings.SelectSection("work"); Dispatcher.UIThread.RunJobs();
    var workView = settings.GetLogicalDescendants().OfType<WorkSettingsView>().Distinct().Single();
    T WorkControl<T>(string name) where T : Control => workView.GetLogicalDescendants().OfType<T>().Distinct().Single(c => c.Name == name);
    var accessSetting = WorkControl<ComboBox>("AgentAccess"); var contextSetting = WorkControl<NumericUpDown>("AgentContextTokenLimit");
    Check(accessSetting.Items.Count == 2 && contextSetting.Minimum == 8192 && contextSetting.Maximum == 2000000 && WorkControl<Button>("OpenAgentBackups").IsEnabled == OperatingSystem.IsWindows(), "work settings expose permission choices, valid budgets and platform-aware backup actions");
    var workBefore = controller.Settings.Copy(); var historyBeforeWork = controller.Session.History.Messages.Count;
    if (OperatingSystem.IsWindows())
    {
        accessSetting.SelectedIndex = 1; WorkControl<CheckBox>("WorkPlanMode").IsChecked = true; WorkControl<CheckBox>("AgentBackupFiles").IsChecked = false;
        contextSetting.Value = 16384; WorkControl<TextBox>("WorkGoal").Text = "检查当前项目并给出执行计划";
        var selectedWorkDir = Path.Combine(directory, "work-settings-directory"); Directory.CreateDirectory(selectedWorkDir); WorkControl<TextBox>("AgentWorkspaceInput").Text = selectedWorkDir;
        WorkControl<Button>("SaveAgentWorkspace").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var savedWork = new SettingsStore(settingsFile).Load();
        Check(!savedWork.AgentFullAccess && savedWork.AgentPlanMode && !savedWork.AgentBackupFiles && savedWork.AgentContextTokenLimit == 16384 && savedWork.AgentGoal == "检查当前项目并给出执行计划" && savedWork.AgentWorkspace == selectedWorkDir, "all work fields persist without changing current conversation or starting a task");
        WorkControl<TextBox>("AgentWorkspaceInput").Text = Path.Combine(directory, "does-not-exist"); WorkControl<Button>("SaveAgentWorkspace").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(controller.Settings.AgentWorkspace == selectedWorkDir, "invalid work directory leaves the previous valid selection unchanged"); WorkControl<TextBox>("AgentWorkspaceInput").Text = selectedWorkDir;
        var defaultWorkDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "agent-workspace"); bool defaultWorkExisted = Directory.Exists(defaultWorkDir);
        WorkControl<Button>("ResetAgentWorkspace").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Check(controller.Settings.AgentWorkspace == defaultWorkDir && new SettingsStore(settingsFile).Load().AgentWorkspace == defaultWorkDir && Directory.Exists(defaultWorkDir) == defaultWorkExisted, "reset saves the default work location without creating it before a task runs");
        WorkControl<TextBox>("AgentWorkspaceInput").Text = selectedWorkDir; WorkControl<Button>("SaveAgentWorkspace").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    else Check(!accessSetting.IsEnabled && !WorkControl<TextBox>("AgentWorkspaceInput").IsEnabled && !contextSetting.IsEnabled, "Mac disables Windows file Agent controls while retaining visible settings");
    WorkControl<CheckBox>("AgentDetailedProcess").IsChecked = true; Dispatcher.UIThread.RunJobs();
    Check(new SettingsStore(settingsFile).Load().AgentDetailedProcess && controller.Session.History.Messages.Count == historyBeforeWork && !controller.Session.IsBusy && workView.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("下一次任务") == true), "detailed process preference persists with a next-task notice and never submits a model request");
    AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using (var workFrame = settings.CaptureRenderedFrame()) workFrame!.Save(Path.Combine(AppContext.BaseDirectory, "artifacts", "work-settings.png"), new PngBitmapEncoderOptions());
    controller.Settings.AgentFullAccess = workBefore.AgentFullAccess; controller.Settings.AgentWorkspace = workBefore.AgentWorkspace; controller.Settings.AgentPlanMode = workBefore.AgentPlanMode; controller.Settings.AgentGoal = workBefore.AgentGoal; controller.Settings.AgentBackupFiles = workBefore.AgentBackupFiles; controller.Settings.AgentContextTokenLimit = workBefore.AgentContextTokenLimit; controller.Settings.AgentDetailedProcess = workBefore.AgentDetailedProcess; controller.Save();
    settings.SelectSection("text"); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var rendered = settings.CaptureRenderedFrame())
    {
        Check(rendered != null && rendered.PixelSize.Width > 500, "actual themed settings window renders to bitmap");
        var artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(artifacts);
        rendered!.Save(Path.Combine(artifacts, "settings.png"), new PngBitmapEncoderOptions());
    }
    var editor = new ProviderEditor(controller, "text");
    var editorHost = new Window { Content = editor }; editorHost.Show(); Dispatcher.UIThread.RunJobs();
    var add = editor.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "新增厂商");
    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(controller.Settings.TextProviders.Count == 1, "provider drafts stay isolated before Save");
    var save = editor.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "保存并启用此厂商 / 模型");
    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(controller.Settings.TextProviders.Count == 2, "provider Save applies real business settings");
    var permissionChat = new ChatWindow(controller, true); permissionChat.Show();
    runtime.PendingPermission = new TaskCompletionSource<bool>();
    controller.Settings.Transcription.BaseUrl = "https://api.example.test/v1"; controller.Settings.Transcription.Model = "whisper-1"; controller.Settings.Transcription.ApiKey = "test-key";
    var record = permissionChat.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ChatDictation");
    record.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); permissionChat.Close(); runtime.PendingPermission.SetResult(true); Dispatcher.UIThread.RunJobs();
    Check(runtime.RecorderCreations == 0 && !controller.Dictation.IsBusy, "late microphone permission after chat close cannot open device");
    var guide = new GuideWindow(controller); guide.Show(); Dispatcher.UIThread.RunJobs();
    var guideExit = guide.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Name == "GuideSkip");
    Check(guideExit.FontSize >= 14 && guideExit.FontWeight == Avalonia.Media.FontWeight.SemiBold && guideExit.Foreground == DesktopTheme.Brush("#203754"), "desktop guide exit is larger semibold and theme aware");
    var later = guide.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "稍后继续"); later.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(controller.Settings.OnboardingState == "deferred", "guide deferred state persists");
    var nextGuide = new GuideWindow(controller); nextGuide.Show();
    var skip = nextGuide.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "跳过全部引导"); skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(controller.Settings.OnboardingState == "skipped", "guide skip releases window");
    controller.Settings.OnboardingState = "new"; controller.Settings.OnboardingStep = 14;
    var workGuide = new GuideWindow(controller); workGuide.Show(); Dispatcher.UIThread.RunJobs();
    Check(workGuide.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "15 / 17 · 对话模式与附件"), "work guide revision starts at new conversation and attachment lesson");
    var guideNext = workGuide.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "下一步");
    guideNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    workGuide.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "打开此步实际界面").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    var guideSettings = (SettingsWindow)typeof(DesktopController).GetField("settingsWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(controller)!;
    Check(guideSettings.GetLogicalDescendants().OfType<TabControl>().First().SelectedItem is TabItem { Header: "工作设置" }, "work lesson opens actual work settings tab");
    guideSettings.Close(); guideNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    Check(guideNext.Content as string == "完成引导" && workGuide.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "17 / 17 · 更新与开始使用"), "seventeenth lesson exposes reachable completion button");
    guideNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    Check(controller.Settings.OnboardingState == "completed" && !workGuide.IsVisible, "completing new guide persists completion and closes guide");
    editor.Dispose(); editorHost.Close();
    foreach (var window in windows) window.Close();
    controller.Pet.Close(); Dispatcher.UIThread.RunJobs();
    Console.WriteLine("PASS: " + assertions + " Desktop UI/resource/animation assertions");
}
finally
{
    // Only remove this run's newly created GUID directory.
    if (Directory.Exists(directory)) Directory.Delete(directory, true);
}

sealed class DelayedFileTransfer : IAsyncDataTransfer, IAsyncDataTransferItem
{
    public TaskCompletionSource<object?> Value { get; } = new();
    public IReadOnlyList<DataFormat> Formats => new[] { DataFormat.File };
    public IReadOnlyList<IAsyncDataTransferItem> Items => new[] { this };
    public Task<object?> TryGetRawAsync(DataFormat format) => Value.Task;
    public void Dispose() { }
}
sealed class TestRuntime(string directory) : DesktopRuntime
{
    public TaskCompletionSource<bool>? PendingPermission { get; set; }
    public int RecorderCreations { get; private set; }
    public override string DataDirectory => directory;
    public override bool ShowTray => false;
    public override void InitializeProtection() => SecretProtection.Configure(new TestProtector());
    public override IReplySpeech CreateSpeech(ApiClient api) => new TestSpeech();
    public override IAudioRecorder CreateRecorder() { RecorderCreations++; return new TestRecorder(); }
    public override Task<bool> RequestMicrophoneAccessAsync() => PendingPermission?.Task ?? Task.FromResult(false);
    public override IPcmMicrophone CreateMicrophone() => throw new InvalidOperationException("UI smoke does not open real audio devices");
    public override ILiveAudioOutput CreateLiveOutput() => throw new InvalidOperationException("UI smoke does not open real audio devices");
}
sealed class TestRecorder : IAudioRecorder
{
    public bool IsRecording { get; private set; }
    public void Start() => IsRecording = true;
    public Task<byte[]> FinishAsync() => Task.FromResult(WaveData.EncodePcm(new byte[320]));
    public void Dispose() => IsRecording = false;
}
sealed class TestApp : Application
{
    public override void Initialize() { Styles.Add(new Avalonia.Themes.Fluent.FluentTheme()); DesktopAppearance.Install(this); }
}
sealed class TestSpeech : IReplySpeech
{
    public void Dispose() { }
    public void SetVolume(int volume) { }
    public void Stop() { }
    public Task SpeakAsync(string reply, PetSettings settings, CancellationToken cancel) => Task.CompletedTask;
}
sealed class TestProtector : ISecretProtector
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    public byte[] Protect(byte[] plain)
    {
        var bytes = new byte[28 + plain.Length]; RandomNumberGenerator.Fill(bytes.AsSpan(0, 12));
        using var aes = new AesGcm(key, 16); aes.Encrypt(bytes.AsSpan(0, 12), plain, bytes.AsSpan(28), bytes.AsSpan(12, 16)); return bytes;
    }
    public byte[] Unprotect(byte[] cipher)
    {
        var plain = new byte[cipher.Length - 28]; using var aes = new AesGcm(key, 16); aes.Decrypt(cipher.AsSpan(0, 12), cipher.AsSpan(28), cipher.AsSpan(12, 16), plain); return plain;
    }
}
