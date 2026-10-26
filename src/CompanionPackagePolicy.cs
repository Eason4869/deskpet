using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace Dafeiyu
{
    // Compiled application resource: an imported package cannot supply this policy.
    internal static class CompanionPackagePolicy
    {
        static readonly Dictionary<string,object> policy=Load();
        static Dictionary<string,object> Load()
        {
            using(var input=typeof(CompanionPackagePolicy).Assembly.GetManifestResourceStream("Dafeiyu.CompanionPackagePolicy")) {
                if(input==null)throw new InvalidDataException("Missing compiled companion package policy.");
                using(var reader=new StreamReader(input,Encoding.UTF8)) {
                    var data=OfficialJson.Object(OfficialJson.Parse(Encoding.UTF8.GetBytes(reader.ReadToEnd()),65536));
                    if(OfficialJson.Integer(data,"schema_version")!=1)throw new InvalidDataException("Invalid companion package policy.");
                    return data;
                }
            }
        }
        static long Limit(string key){long value=OfficialJson.Integer(policy,key);if(value<=0)throw new InvalidDataException("Invalid package limit.");return value;}
        internal static long MaximumArchiveBytes {get{return Limit("maximum_archive_bytes");}}
        internal static long MaximumExpandedBytes {get{return Limit("maximum_expanded_bytes");}}
        internal static int MaximumEntries {get{return checked((int)Limit("maximum_entries"));}}
        internal static long MaximumPngBytes {get{return Limit("maximum_png_bytes");}}
        internal static long MaximumManifestBytes {get{return Limit("maximum_manifest_bytes");}}
        internal static long MaximumMetadataBytes {get{return Limit("maximum_metadata_bytes");}}
        internal static long MaximumTextBytes {get{return Limit("maximum_text_bytes");}}
        internal static long MaximumOfficialTextBytes {get{return Limit("maximum_official_text_bytes");}}
        internal static long MaximumAppearanceBytes {get{return Limit("maximum_appearance_bytes");}}
        internal static int MaximumDefaultFrames {get{return checked((int)Limit("maximum_default_frames"));}}
        internal static int MaximumHolidayOutfits {get{return checked((int)Limit("maximum_holiday_outfits"));}}
        internal static void CheckArchive(long bytes,int entries)
        {if(bytes<0||bytes>MaximumArchiveBytes||entries<0||entries>MaximumEntries)throw new InvalidDataException(TextResources.Get("companionPackStorage.readZip.text.alternativeC"));}
        internal static void CheckExpanded(long bytes)
        {if(bytes<0||bytes>MaximumExpandedBytes)throw new InvalidDataException(TextResources.Get("companionPackStorage.install.text.alternativeA"));}
        internal static long FileLimit(string relative,bool official)
        {
            if(relative=="pet.json")return MaximumManifestBytes;
            string extension=Path.GetExtension(relative);
            if(extension.Equals(".png",StringComparison.OrdinalIgnoreCase))return MaximumPngBytes;
            if(extension.Equals(".json",StringComparison.OrdinalIgnoreCase))return MaximumMetadataBytes;
            if(extension.Equals(".txt",StringComparison.OrdinalIgnoreCase)||extension.Equals(".md",StringComparison.OrdinalIgnoreCase)||relative=="LICENSE")return official?MaximumOfficialTextBytes:MaximumTextBytes;
            throw new InvalidDataException(TextResources.Get("companionPackStorage.readZip.text.alternativeB"));
        }
    }
}
