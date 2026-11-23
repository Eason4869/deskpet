using System;
using System.IO;
using System.Linq;
using System.Threading;
using Dafeiyu;

// Opt-in real-package acceptance. Never point this at an existing user profile.
internal static class CompanionPerformancePackages
{
    internal static OfficialCompanionInstallation Install(string data,string indexPath,string id)
    {
        var index=OfficialJson.Object(OfficialJson.File(indexPath,1024*1024));
        var row=OfficialJson.Array(OfficialJson.Required(index,"companions")).Select(OfficialJson.Object).Single(r=>OfficialJson.Text(r,"id",48)==id);
        string file=OfficialJson.Text(row,"asset_name",150),zip=Path.Combine(Path.GetDirectoryName(indexPath)??"",file);
        string hash=OfficialJson.Text(row,"sha256",64);long size=OfficialJson.Integer(row,"size");
        if(new FileInfo(zip).Length!=size||OfficialCompanionStorage.HashFile(zip)!=hash)throw new InvalidDataException("Real performance package does not match its pinned release index.");
        var asset=new OfficialCompanionAsset(id,OfficialJson.Text(row,"name",80),OfficialJson.Text(row,"version",64),OfficialJson.Text(row,"min_app_version",64),"v1.0.3",file,size,hash,
            OfficialJson.Array(OfficialJson.Required(row,"outfits")).Select(OfficialJson.Object).Select(o=>new OfficialCompanionOutfit(OfficialJson.Text(o,"outfit_id",64),OfficialJson.Text(o,"version",64))));
        var store=new OfficialCompanionStorage(data);var existing=store.Installed(id);if(existing!=null){if(existing.Asset.Sha256!=asset.Sha256)throw new InvalidDataException("Owned benchmark root differs from pinned fixture.");return existing;}string role=store.CharacterRoot(id);Directory.CreateDirectory(role);
        string stage=Path.Combine(role,".stage-"+Guid.NewGuid().ToString("N"));
        OfficialCompanionPackageValidation.Extract(zip,stage,CancellationToken.None);
        OfficialCompanionPackageValidation.Validate(stage,asset,CancellationToken.None);
        store.WriteReceipt(stage,asset,CancellationToken.None);
        return store.Publish(stage,asset,CancellationToken.None,()=>{},publish=>publish());
    }
}
