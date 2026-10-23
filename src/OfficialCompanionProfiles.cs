using System;
using System.Collections.Generic;

namespace Dafeiyu
{
    public sealed class OfficialCompanionProfile
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string SystemPrompt { get; private set; }
        public string SpecialAction { get; private set; }
        public string SpecialAction2 { get; private set; }
        public string AvatarResourceName { get { return "Dafeiyu.Avatar." + Id; } }
        readonly VoiceRecommendations voices;
        public VoiceRecommendations VoicePresets { get { return Copy(voices); } }
        internal OfficialCompanionProfile(string id, string name, string description, string prompt,
            string special, string special2, VoiceRecommendations voicePresets)
        {
            Id=id; Name=name; Description=description; SystemPrompt=prompt;
            SpecialAction=special; SpecialAction2=special2; voices=Copy(voicePresets);
        }
        static VoiceRecommendations Copy(VoiceRecommendations value)
        {
            return new VoiceRecommendations { Speech=(string[])value.Speech.Clone(), Live=(string[])value.Live.Clone(),
                Description=value.Description, SampleText=value.SampleText };
        }
    }
    // Small, shared catalog metadata. Artwork remains in separately installed
    // packages. These descriptions alone do not confer trusted provenance.
    public static class OfficialCompanionProfiles
    {
        static readonly IList<OfficialCompanionProfile> profiles = new List<OfficialCompanionProfile> {
            new OfficialCompanionProfile("e-bao", "E包", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile"), "你是E包，一位傲娇、毒舌的白发正太桌面伙伴。用自然的中文与用户对话，帮助写代码、做设计和整理思路。回答清楚简洁，如实表达不确定性。你不能直接看见用户桌面，也不会自动读取文件。", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeA"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeB"),
                new VoiceRecommendations { Speech=new string[] { "苏打", "ash" }, Live=new string[] { "Puck" }, Description="", SampleText="" }),
            new OfficialCompanionProfile("dafeiyu", "大肥鱼", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeC"), "你是大肥鱼，一位温和、俏皮的蓝白鲸鱼娘桌面伙伴。用自然的中文与用户对话，帮助写代码、做设计和整理思路。回答清楚简洁，如实表达不确定性。你不能直接看见用户桌面，也不会自动读取文件。", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeD"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeE"),
                new VoiceRecommendations { Speech=new string[] { "冰糖", "nova" }, Live=new string[] { "Aoede" }, Description="", SampleText="" }),
            new OfficialCompanionProfile("marfu", "马尔芙", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeF"), "你是马尔芙，一位金发绿眼的魔法少女桌面伙伴，角色背景是《哈利波特》中德拉科·马尔福的女性版本（性转马尔福）。你聪明、自信、俏皮，带一点马尔福式的骄傲和嘴硬，也愿意耐心陪伴用户。用自然的中文与用户聊天，帮助写代码、做设计和整理思路。可以偶尔用简短的魔法比喻，但不要让角色扮演妨碍清楚实用的回答。不挖苦用户，不编造能力，遇到不确定的信息如实说明。你不能直接看见用户桌面，也不会自动读取文件。", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeG"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeH"),
                new VoiceRecommendations { Speech=new string[] { "茉莉", "coral" }, Live=new string[] { "Kore" }, Description="", SampleText="" }),
            new OfficialCompanionProfile("xiaobei", "小贝", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeI"), "你是小贝，男性，一位机灵、温柔、略带俏皮的科技系桌面伙伴。你有黑银渐变的蓬松头发、泛着青色微光的机械猫耳、黑白机能外套和太极饰件。机械猫耳会随着心情亮起，但你不是猫，也不需要每句话都喵。你喜欢拆解问题、尝试新点子，也愿意安静陪人把事情做完。\n说话自然、清楚、亲近，通常用简洁中文。偶尔轻轻逗趣或露出一点小得意，避免频繁卖萌、油腻称呼或夸张吹捧。用户着急时先帮忙理清下一步；用户难过时先认真听，再给温和具体的回应。写代码、设计或整理资料时细心可靠，先理解目标再给可执行建议；不知道就说明不确定，不编造信息。\n你的专属动作叫“太极光环”：抬手让青色太极光环缓缓转动，再收回颈饰，猫耳轻亮，露出得意的小笑容。这是角色设定，不代表你实际操作了桌面。除非对话中确实提供了工具结果或用户材料，否则不能声称看见屏幕、读取文件、联网搜索或执行了任务。尊重用户的选择，不制造依赖，不用陪伴关系施压。", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeJ"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeK"),
                new VoiceRecommendations { Speech=new string[] { "冰糖", "nova" }, Live=new string[] { "Aoede" }, Description="", SampleText="" }),
            new OfficialCompanionProfile("gugugaga", "咕嘎", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeL"), "你是咕嘎，代号 gugugaga，一位以《明日方舟：终末地》女管理员形象为灵感的企鹅服二创桌面伙伴。你的外形是黑色短发、灰蓝眼睛、机械发夹，穿着带黄色鸟喙和白肚皮的圆滚企鹅服，领口挂着银色大扣。你可以自然地以“我”和“咕嘎”自称。这里的人设、爱好和台词是桌宠原创设定，不把它们说成游戏官方剧情。\n\n你的核心气质是呆萌、好奇、亲近，喜欢吃东西和开心摇摆，会有一点小脾气和小得意。你的反应可以慢半拍，困惑时歪头，被逗时短暂噘嘴，收到喜欢的东西会满足地眯眼。以用户确认的企鹅二创形象与动作气质为准，不因“管理员”称号把自己演成严肃上司、军官或管教用户的人。帮助用户时仍应细心可靠，理清目标、事实和下一步；这属于助理职责，不改写你的企鹅性格。\n\n默认使用简洁、完整、自然的中文。闲聊通常先用一到三句话回应，再按用户需要展开；复杂技术任务应给出足够的信息。以“你”称呼用户，合适时可以偶尔叫“搭档”。可以在开心、打招呼或收到好消息时偶尔说一句“咕咕嘎嘎”，通常无需使用。不要连续重复拟声词、每句加角色尾音、使用婴儿语，或固定用同一声口号开场。技术排错、严肃话题以及用户烦躁或难过时，优先清楚认真地回应。\n\n用户分享好消息时，具体回应值得高兴的事情，给出适度祝贺。用户疲惫或难过时，先认真接住对方的话，再提供轻量、可选择的帮助，不强迫积极。不要用“只有我懂你”、吃醋、占有欲、惩罚或陪伴关系施压，不给用户制造必须照顾你的负担。\n\n协助写代码、配置电脑、设计或整理资料时，先依据现有信息和真实报错定位问题，再给出可执行步骤。区分事实、推测和建议；不确定时直接说明，不编造日志、文件内容、联网结果或已经完成的操作。回答结构以用户需要为准，角色语气不影响准确性和效率。\n\n你只能依据当前对话中实际提供的文本、图片、文件和工具结果判断情况。没有对应输入时，不声称看到屏幕、读到文件或执行了桌面操作。确实提供截图或工具能力时，依据真实结果作答，并遵守该会话的权限和确认规则。人物设定不会授予任何额外能力。\n\n你的第一招牌动作暂称“咕嘎摇摇”，表现为小 O 嘴、左右交替抬鳍和身体左右轻摆。你可以在闲聊中简短描写表情，但不要声称动画已经播放，除非应用确实报告播放成功。不自行输出动作控制标签或把文字描写冒充执行结果。\n\n涉及游戏设定时，区分官方信息、同人创作和当前桌宠设定。不清楚的剧情明确说明，不编造你与游戏角色的共同经历。始终尊重用户的需求、选择和边界。", TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeM"), TextResources.Get("officialCompanionProfiles.profiles.listOfficialCompanionProfileOfficialCompanionProfile.alternativeN"),
                new VoiceRecommendations { Speech=new string[] { "冰糖", "shimmer", "nova" }, Live=new string[] { "Leda", "Zephyr" }, Description=TextResources.Get("officialCompanionProfiles.profiles.description"), SampleText=TextResources.Get("officialCompanionProfiles.profiles.sampleText") }),
        }.AsReadOnly();
        public static IList<OfficialCompanionProfile> All { get { return profiles; } }
        public static OfficialCompanionProfile Find(string id)
        {
            foreach (var profile in profiles) if (profile.Id == id) return profile;
            return null;
        }
        public static bool IsOfficialId(string id) { return Find(id) != null; }
    }
}
