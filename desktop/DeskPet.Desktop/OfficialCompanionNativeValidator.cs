using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Dafeiyu;

namespace DeskPet.Desktop;

// Both receipt-checked startup installs and service staging callbacks use this
// gate. Never retain a staging skin, catalog, bitmap or opener beyond validation.
internal sealed class OfficialCompanionNativeValidator : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> validated = new(StringComparer.Ordinal);
    internal Task ValidateAsync(OfficialCompanionInstallation installation, CancellationToken cancel) => Task.Run(async () =>
    {
        await gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            var digest = installation.Asset.Id + ":" + installation.Asset.Sha256;
            if (validated.Contains(digest)) return;
            ValidateRoot(installation, cancel);
            cancel.ThrowIfCancellationRequested(); validated.Add(digest);
        }
        finally { gate.Release(); }
    }, cancel);

    internal static void ValidateRoot(OfficialCompanionInstallation installation, CancellationToken cancel)
    {
        var skin = SpriteAssets.ReadOfficial(installation, installation.Asset.Id);
        SpriteAssets.ValidateNativeDefault(skin, cancel);
        var catalog = new HolidayCatalog(installation.HolidayRootDirectory);
        if (!string.IsNullOrEmpty(catalog.Warning) || catalog.Outfits.Any(o => !o.Ready)) throw new InvalidDataException(TextResources.Get("officialCompanionNativeValidator.validateRoot.text"));
        foreach (var outfit in catalog.Outfits)
        {
            foreach (var definition in outfit.Clips.Concat(new[] { outfit.Gaze, outfit.EasterEgg }))
            {
                using var assets = new SpriteAssets();
                var clip = SpriteAssets.OutfitClip(outfit, definition);
                for (int frame = 0; frame < clip.Frames; frame++) { cancel.ThrowIfCancellationRequested(); assets.PrepareFrame(skin, clip, frame); }
            }
            using var previews = new SpriteAssets(); _ = previews.GetPreview(outfit);
        }
        // Force decode even extra PNGs and avatar/preview rows. Some codecs only
        // fail when pixels are requested, rather than at Bitmap construction.
        foreach (var path in Directory.EnumerateFiles(installation.RootDirectory, "*.png", SearchOption.AllDirectories))
        {
            cancel.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(installation.RootDirectory, path).Replace('\\', '/');
            using var source = File.OpenRead(SpriteAssets.SafePath(installation.RootDirectory, relative));
            using var bitmap = new Bitmap(source);
            var bytes = new byte[checked(bitmap.PixelSize.Width * 4)];
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { for (int y = 0; y < bitmap.PixelSize.Height; y++) { cancel.ThrowIfCancellationRequested(); bitmap.CopyPixels(new PixelRect(0, y, bitmap.PixelSize.Width, 1), pinned.AddrOfPinnedObject(), bytes.Length, bytes.Length); } }
            finally { pinned.Free(); }
        }
    }
    internal static CompanionPackageInventory ValidateLocalPackage(string root, CancellationToken cancel)
    {
        var inventory = CompanionPackageInventory.Read(root, cancel);
        using var input = File.OpenRead(Path.Combine(root, "pet.json"));
        var skin = SpriteAssets.ReadManifest(input, inventory.PackageId, root);
        SpriteAssets.ValidateNativeDefault(skin, cancel);
        if (inventory.HolidayCatalog != null) foreach (var outfit in inventory.HolidayCatalog.Outfits)
        {
            foreach (var definition in outfit.Clips.Concat(new[] { outfit.Gaze, outfit.EasterEgg }).GroupBy(c => c.Path).Select(g => g.OrderByDescending(c => c.FrameCount).First()))
            {
                using var assets = new SpriteAssets(); var clip = SpriteAssets.OutfitClip(outfit, definition);
                for (int frame = 0; frame < clip.Frames; frame++) { cancel.ThrowIfCancellationRequested(); assets.PrepareFrame(skin, clip, frame); }
            }
            using var previews = new SpriteAssets(); _ = previews.GetPreview(outfit);
        }
        return inventory;
    }
    // Semaphore remains alive until outstanding cancelled validators release it.
    public void Dispose() { }
}
