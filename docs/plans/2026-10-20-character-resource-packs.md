# 完整角色资源包

用户确认：本次一次适配完整资源包机制，以后新增／更新伙伴只发布 ZIP。

## 数据与兼容性

沿用标准 v2 的 17 动作，新增可选 `id`、`pack_version`、`avatar`、`special_action2`、`extra_animations`、`alternate_animations`、`voice_presets`。旧 v2 引擎忽略新增字段后仍可导入标准动作；新版从同包读取全部功能，不运行脚本、不下载依赖。

`extra_animations` 允许 stumble / special2；`alternate_animations` 允许既有 8 类互动及 jumping。每个完整动作声明文件、帧数、帧率及循环。第一套优先标准动作或 extra；第二套来自 alternate，全部真实 PNG。音色推荐是 speech/live 的候选 ID 列表，仅选择当前模型已有目录中的 ID；历史手动值优先。预览台词和声音目标随包提供。

## 安装与更新

设置支持 ZIP 和 pet.json。显式 id 包安装到数据目录的 `companion-packs/<id>/v-<guid>`，原子更新 `current.txt` 指针。保留旧版本，失败不会改当前版本；同 id 只展示一项，现有个人人格、音色与聊天仍使用原 id。无 id 的旧格式继续使用 skin-GUID，互不覆盖。内置注册从资源 catalog.json 读取，已安装包优先于相同 id 的随附版本。

只解压到本次创建的目录；拒绝路径越界、重复路径、链接、异常大小、过多条目和目录重解析点。校验全部引用图像和字段后再更新指针。更新后的同 id、新 Source 必须可以重新选择。

## 执行与验证

1. 先扩展导入／双套／头像／音色回归，观察失败。
2. 共享纯文件包存储、可选 manifest 与资源校验；WPF 接入导入更新及播放。
3. Avalonia 同步 ZIP、覆盖和资源解析；两端 UI 刷新当前伙伴及推荐音色。
4. 咕嘎迁移到完整包，不保留新增的角色专用硬编码；更新 ZIP、应用预览和文档。
5. 运行安全／更新回归、已有互动、声音控件与 headless；独立审查增量后交付。

本次只做本地导入／更新，不自动访问远程账号或发布 Release。用户以后可以单独发布此 ZIP，应用用户从设置导入即可更新。
