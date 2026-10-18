using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    // Reference-matched clips are isolated from imported skin contracts.
    public sealed class AdditionalInteractions
    {
        readonly Dictionary<string, MotionClip> clips = new Dictionary<string, MotionClip>();
        readonly LinkedList<string> recent = new LinkedList<string>();
        static readonly HashSet<string> resources = new HashSet<string>(Assembly.GetExecutingAssembly().GetManifestResourceNames(), StringComparer.Ordinal);
        SkinDefinition currentSkin;
        public static readonly string[] Actions = { "lifted", "landing", "feeding", "happy", "sleeping", "special", "special2", "stumble" };
        static string Resource(SkinDefinition skin, string action) { return "Dafeiyu.Companion." + skin.Id + "." + action; }
        public static bool Supports(SkinDefinition skin)
        {
            return skin != null && (skin.Embedded || skin.Bundled) &&
                resources.Contains(Resource(skin, "stumble"));
        }
        public static bool Has(SkinDefinition skin, string action) { return Supports(skin) && resources.Contains(Resource(skin, action)); }
        public static bool HasOriginal(SkinDefinition skin, string action) { return Supports(skin) && Array.IndexOf(Actions, action) >= 0; }
        public static int OriginalFrameCount(SkinDefinition skin, string action) { return skin.Id == "xiaobei" || action == "stumble" || action == "special2" ? 8 : 4; }
        public static string SpecialName(SkinDefinition skin)
        {
            switch (skin.Id) { case "e-bao": return "比心鼓励"; case "dafeiyu": return "泡泡环游"; case "marfu": return "荧光咒语"; case "xiaobei": return "全息键盘"; default: return ""; }
        }
        public MotionClip Get(SkinDefinition skin, string action)
        {
            if (!ReferenceEquals(currentSkin, skin)) { clips.Clear(); recent.Clear(); currentSkin = skin; }
            if (String.IsNullOrEmpty(action)) return null;
            bool original = action.EndsWith("-original", StringComparison.Ordinal);
            string name = original ? action.Substring(0, action.Length - 9) : action;
            if (original ? !HasOriginal(skin, name) : !Has(skin, action)) return null;
            MotionClip clip; if (clips.TryGetValue(action, out clip))
            {
                if (recent.Last.Value != action) { var node = recent.Find(action); recent.Remove(node); recent.AddLast(node); }
                return clip;
            }
            if (original) clip = LoadOriginal(skin, name);
            else using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource(skin, action)))
            {
                var image = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                if (image.PixelWidth != 3840 || image.PixelHeight != 520) throw new InvalidDataException("伙伴动作需要 8 列 × 1 行、480 × 520 单帧。");
                clip = new MotionClip(image, 8, 1.0, action == "lifted" || action == "sleeping", 480, 520, 8);
            }
            // Each clip owns full-size pixels and hit masks. Keep the current
            // action and two recent ones rather than every action ever played.
            if (clips.Count == 3) { clips.Remove(recent.First.Value); recent.RemoveFirst(); }
            clips.Add(action, clip); recent.AddLast(action); return clip;
        }
        private static MotionClip LoadOriginal(SkinDefinition skin, string action)
        {
            string resource; int count = OriginalFrameCount(skin, action), columns = count; int top = 0, height = 520;
            if (action == "stumble" || action == "special2")
            { resource = "Dafeiyu.Additional." + skin.Id; columns = 4; top = action == "stumble" ? 0 : 1040; height = 1040; }
            else if (skin.Bundled)
            { resource = "Dafeiyu.Bundled.Xiaobei." + skin.Manifest.Animations[action].File.Replace('/', '.').Replace('\\', '.'); }
            else
            {
                resource = skin.Id == "e-bao" ? "Dafeiyu.EbaoInteractions" : skin.Id == "marfu" ? "Dafeiyu.MarfuInteractions" : "Dafeiyu.DafeiyuInteractions";
                top = Array.IndexOf(new[] { "lifted", "landing", "petting", "poked", "feeding", "happy", "sleeping", "special" }, action) * 520;
            }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidDataException("旧版互动素材缺失：" + skin.Id + " / " + action);
                var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                var crop = new CroppedBitmap(image, new Int32Rect(0, top, columns * 480, height));
                return new MotionClip(crop, count, count / 8.0, action == "lifted" || action == "sleeping", 480, 520, columns);
            }
        }
    }
}
