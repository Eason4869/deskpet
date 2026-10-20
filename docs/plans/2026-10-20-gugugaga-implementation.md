# 咕嘎内置伙伴实现计划

> 实现已完成，含 240 姿态及两端 UI。后续用户选定通用完整资源包机制，最终字段、安装覆盖与移除语义见 [完整角色包](../character-packs.md)。以下保留实施时的任务拆解；验证记录见 `work/gugugaga-implementation-progress.md`。

> **For agentic workers:** 使用 superpowers:executing-plans 在当前会话逐项实现，执行记录保存在 work/gugugaga-implementation-progress.md。

**Goal:** 将已确认的咕嘎形象、完整 240 姿态动作及人格作为第五位内置伙伴接入 Windows 和 Avalonia。
**Architecture:** 沿用 v2 的 17 动作与 16 方向视线格式，使用通用内置缓存加载；第二招牌和踉跄由现有扩展机制加载。按用户最新要求，9 类互动／动作各有两套，现有四宠保留双版本。
**Tech Stack:** Windows PowerShell、.NET Framework 4.8 WPF、现有 .NET 10 / Avalonia 项目、内置 imagegen、Pillow / NumPy 做确定性的拆格和打包。
**Spec:** docs/plans/2026-10-20-gugugaga-companion-design.md 及 2026-10-20-gugugaga-motion-references.md。

## Global Constraints

- 名字咕嘎，稳定 ID gugugaga；工作模式共享提示词保持现状。
- 单帧 480×520；17 标准动作、2 扩展各 8 帧，16 方向视线；提起、落地、喂食、开心、睡觉、两项专属、踉跄、跳跃额外各 8 帧，共 240 姿态。
- 确定采用完整档，第一批样片 32 姿态优先检查；所有艺术姿态必须由 imagegen 绘制。
- 双鳍斜向下、起跳、腹部前顶；已提供的摇摆参考及母版为约束，参考来源的唯一身份仍未确认，不宣称原版复刻。
- 独立左行，发夹及红扣不镜像；同一组统一缩放，保留跳跃垂直运动。
- 只在 work/gugugaga-dev、codex/gugugaga 开发；不触及主工作区已有 README 工具改动。
- 不推送、不发 Release，不改本机正在运行的正式安装。

## Review Focus

- 新 bundled 伙伴不能错误读取 Xiaobei 资源。
- 咕嘎两套动作各自使用自己的图；旧四宠继续工作。
- 同 ID 用户导入不能获取预设头像或扩展。
- 组内缩放不能抹去跳跃高度或造成每帧大小闪烁。
- 两端的动作循环、帧率、喂食后开心及暂停恢复应正常。

## Task 1: 素材规范与生产

- [ ] 为确定性拆格器编写测试：完整透明人物、边界检查、整组统一变换保留垂直位移、唯一姿态及数量检查。先运行得到缺失模块失败。
- [ ] 新增 tools/prepare_gugugaga.py，提供 pack_sheet(source, output, columns, rows, frames, baseline) 与 inspect_pack(root)；只拆分、统一缩放、定位和生成 QA 预览，不合成新姿态。
- [ ] 按母版分别调用内置 imagegen 生产 19 条八姿态与 16 方向视线；先检查待机、右行、跳跃、摇摆。
- [ ] 保存精确提示、生成源、处理参数及图像哈希；导出 v2 包、avatar、preview 和浅深色预览。
- [ ] 验证 python tests/GugugagaAssetTests.py，全部有效格透明且有可见角色，不使用重复姿态凑数。

## Task 2: Windows 注册与单版本能力

- [ ] 先扩展 tests/BundledSkinTests.cs：第五宠稳定 ID、人格切换／重置、缓存隔离与损坏修复、专用头像，运行得到未注册失败。
- [ ] 将 BundledSkinCache.LoadXiaobei 委托到 Load(dataDirectory,id,resourcePrefix)，保持现有缓存验证并提供 LoadGugugaga。
- [ ] SkinCatalog 加载第五宠；Build.ps1 打包咕嘎完整 v2、头像与两项扩展。
- [ ] tests/InteractionTests.cs 先加入双版本和导入同 ID 不套用资源的回归，再按真实资源改 HasOriginal，补第二招牌名称。
- [ ] 运行 BundledSkinTests、InteractionTests、ImportedSkinTests、PersonaTests（如存在的实际目标）、VoicePresetTests。

## Task 3: Avalonia 同步

- [ ] 扩展 desktop/DeskPet.Desktop.Tests/Program.cs：第五宠、avatar、17 标准动作使用自身 manifest、两项扩展在 original=true/false 均可解析且不借用旧图。
- [ ] SpriteAssets.LoadSkins、AvatarPath、Resolve 接入咕嘎两套动作；保留 manifest 的帧率。
- [ ] csproj 包含咕嘎 v2 资源，同步动作菜单可读名称。
- [ ] 使用本机 work/dotnet SDK 运行现有桌面测试；此项只证明 Windows 主机上的 Avalonia/headless，不宣称 macOS 实机。

## Task 4: 集成、验收和交付

- [ ] 制作全部动作浅深背景预览与 Windows 隔离数据目录的渲染预览；检查实际 192×208 的造型。
- [ ] 运行相关测试、构建隔离 Windows 可执行程序、导出可导入 v2 包，核对 22 PNG + manifest。
- [ ] 更新伙伴列表及素材文档，记录人格、提示词、验证结果和剩余视觉限制。
- [ ] 按 requesting-code-review 做一次独立全改动审查，修复实质问题后再验证。
- [ ] 提供开发分支、可执行文件／素材包／预览路径及验证边界。暂不合入 main，不对外发布。
