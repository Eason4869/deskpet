# 互动扩展接口

支持以下互动扩展。伙伴悬停时从可用动作中选择 `jumping`、`waving`、`special`、`special2`；缺少第二招牌素材时不提供 `special2`，节日服饰还可追加对应彩蛋。菜单和轮盘显示具体动作名称，内部 ID 保持稳定。`HoverInteraction` 负责“一次移入只触发一次”，动作候选由 `HoverActions.Choices` 生成。

## 节日彩蛋

实现 `IHolidayHoverActionProvider`，再赋给 `PetWindow.HolidayHoverActions`。每次实际悬停触发时，会使用当前伙伴及 `DateTime.Now.Date` 查询；跨日不需要重启。当前没有默认节日提供者，也不包含节日素材或解锁逻辑。

```csharp
public sealed class FestivalHoverActions : IHolidayHoverActionProvider
{
    public CompanionAction GetAction(SkinDefinition skin, DateTime localDate)
    {
        if (localDate.Month != 10 || localDate.Day != 31)
            return null;
        if (!FestivalArtwork.IsAvailable(skin.Id, "pumpkin"))
            return null;
        return new CompanionAction("holiday-pumpkin", "南瓜惊喜",
            pet => FestivalArtwork.Play(pet, "pumpkin"));
    }
}

pet.HolidayHoverActions = new FestivalHoverActions();
```

`FestivalArtwork` 是未来节日模块需要实现的素材加载及播放层，上例只说明接入协议，当前项目没有此类型。返回 `null` 表示日期、解锁条件或素材不满足，仍为四选一；返回一个具有独立 ID 的动作后为五选一。与常规动作重复的 ID 不会重复加入。播放回调应复用宠物动作的暂停、隐藏、聊天优先与取消规则，不能自建不受管理的定时器。菜单／轮盘的节日入口与服饰解锁留待节日功能实现。

## 新增动作资源

咕嘎（`gugugaga`）使用完整 v2 角色包与头像，并有提起、落地、喂食、开心、小憩、摇摆、转圈、踉跄、跳跃共 9 类双套动作。第一套标准动作位于 `assets/bundled-skins/gugugaga/animations/`，第一套扩展位于 `variants/{stumble,special2}-original.png`；第二套位于 `variants/` 的 9 个动作文件。具体路径由 `pet.json` 的 `extra_animations` 与 `alternate_animations` 声明。两套均各 8 帧、8 FPS，随机选择只在动作开始发生，保留拖起到落地及喂食到开心的版本。WPF 跳跃也按本次选择读取相应动作，旧四宠时序不变。

`HasOriginal` 从角色包自己的 manifest 解析双套资源。导入包使用其声明的头像和扩展，不因同名／同 ID 借用内置文件。独立发布与更新规则见 [完整角色包](character-packs.md)，生产、QA 说明见 `previews/gugugaga-production/README.md`。

`assets/companion-animations/{pet}/{action}.png` 为 8 列 × 1 行透明 PNG，每格 480×520。四宠均提供 `lifted`、`landing`、`feeding`、`happy`、`sleeping`、`special`、`special2`、`stumble`，E包另有修复后的 `jumping`，共 33 条、264 帧。每条 1 秒、8 FPS；拎起与小憩循环，其他动作单次播放，投喂完成后接开心。`Build.ps1` 将全部资源嵌入主程序和测试程序，缺少任一资源即构建失败。

每次 imagegen 都附原版预览参考，生成独立的洋红背景动作条。`tools/prepare_companion_animations.py` 适配 Pets create-pet 的原始组件提取、边缘去色和帧检查脚本，仅调整单元尺寸及动作表；拆出完整姿态，整条统一缩放与定位，保留原始垂直运动，仅做一次边缘去色，不混帧、不插帧。原始条、提示词、注册参数、浅／深背景接触表与 GIF 预览保存在 `work/{Ebao,Dafeiyu,Marfu,Xiaobei}-repair`。原有基础图集、摸头与戳一戳不变。

0.5.6 按用户要求同时构建旧 `assets/additional`，供踉跄与第二招牌的旧版选择；其他旧版由原互动图／小贝内置包读取。`AdditionalInteractions.Get(skin, action+"-original")` 返回旧版，`InteractionAnimation.Configure(skin,true)` 配置对应原帧数量与 8 FPS。旧版保持原姿态数量，不能强行切为 8 格。随机选择只在互动开始时进行；投喂→开心不重选，拎起→普通／踉跄落地也不重选。`DropMotion` 独立于素材时钟，四种落地共用相同曲线与距离，新的拖动会立即中断上次未完成的下落。

运行时按实际 clip 帧数配置互动时钟；遇到延迟刷新，每次最多推进 125 毫秒，不追赶跳过关键姿态。悬停触发时先显示第 0 帧，再开始计时。E包跳跃的自然时长同步为 1 秒，其余未重绘的基础动作及自定义外观继续使用原有协议。
