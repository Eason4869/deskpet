# 成长反馈与纪念卡

保存成功的成长会合并为短暂的好感度提示。伙伴不可见、免打扰开启、
反馈关闭或已切换伙伴时不补弹提示；已解锁的纪念仍可在伙伴主页查看。
阶段通知先持久领取，再显示无抢焦点卡片。首次见面仅作为主页纪念，
不触发阶段庆祝；真实保存的首次见面收益仍可显示普通轻量提示。

聊天、语音、任务或已有动作忙碌时，卡片可被动显示，庆祝动作不会抢占
已有状态。自动庆祝调用非用户动作路径，不会获得互动分数。

伙伴主页的每张已解锁卡片提供“保存纪念卡”。选择保存位置后导出
800×480 PNG，包含当前伙伴的原色头像、名称、已保存阶段和实际解锁日期。
日期跟随所选界面语言，不受当前系统语言或再次导出日期影响。
原生保存对话框负责重名确认；取消不创建文件。写入失败会提示重试，
同目录临时写入及原子替换避免残留半张图片。导出是用户资料，不注册为缓存。

## 实现与验证入口

- `RelationshipFeedback` 订阅应用级 `RelationshipRuntime.Committed`，400ms 合并，
  所有领取和展示前设置读取均在工作线程执行。异步领取后和最终展示前重查身份与抑制条件。
  `IRelationshipFeedbackScheduler` 可注入，`Dispose` 取消定时器并退订。
- `RelationshipCardData.FromSnapshot` 仅从真实里程碑读取阶段和原始日期。
  `RelationshipCardFile.SaveAsync` 返回 `Cancelled`、`Saved` 或 `Failed`。
- 两个 UI 工程分别通过 WPF `RenderTargetBitmap/PngBitmapEncoder` 与
  Avalonia `RenderTargetBitmap` 渲染；原生保存选择器和图片资源遵循页面生命周期。
- Framework：`./Build.ps1 -Target RelationshipFeedbackTests`；便携测试：
  `--relationship-feedback-only`。Windows CI 和便携默认套件已注册。
  两端 `CompanionHomeUiTests` / `--companion-home-ui=en_US` 验证实际 PNG 解码、
  像素内容、通知和原有主页导航/设置保护，并生成主题和宽窄截图。

Windows WPF 使用真实窗口验证；Avalonia 在 Windows 上使用其实际渲染器和
无头窗口验证。真实原生文件对话框的用户取消/覆盖交互、macOS 桌面交互及
安装包验收仍需要对应人工环境；文件层取消、拒绝写入和原子覆盖已自动验证。
