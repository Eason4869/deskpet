# 发布包命名与升级兼容

所有可安装包、运行 ZIP、源码、外观和模板包含版本号；平台包显式区分 Windows 与 macOS，Mac 再区分 CPU。Windows 提供安装／便携包，macOS 按双架构提供预览包；实际版本与附件以 Release 为准。

运行包、源码及模板使用对应应用发行版本命名；0.11.1 起官方完整伙伴包以独立素材版本命名；包内 `pack_version` 保持实际素材版本，不因应用更新改写。下表以 0.10.0 为例列出附件命名。

| 用途（推荐下载顺序） | 0.5.6（兼容旧名） | 0.10.0 |
| --- | --- | --- |
| Windows 安装（推荐） | `DeskPet-Setup-0.5.6.exe` | `DeskPet-Windows-Setup-0.10.0.exe` |
| Windows 便携 | `DeskPet-Windows-0.5.6.zip` | `DeskPet-Windows-0.10.0.zip` |
| Mac Apple Silicon 预览 | 无 | `DeskPet-macOS-0.10.0-arm64-preview.dmg` / `.zip` |
| Mac Intel 预览 | 无 | `DeskPet-macOS-0.10.0-x86_64-preview.dmg` / `.zip` |
| 源码 | `DeskPet-Source-0.5.6.zip` | `DeskPet-Source-0.10.0.zip` |
| 外观 | `{Ebao,Dafeiyu,Marfu,Xiaobei}-HD-Skin-0.5.6.zip` | 相同前缀，版本改为 `0.10.0`；新增 `gugugaga-HD-Skin-0.10.0.zip` |
| 创作模板 | `DeskPet-Skin-Template-v2-0.5.6.zip` | `DeskPet-Skin-Template-v2-0.10.0.zip` |
| 校验 | `SHA256.txt` | `SHA256.txt`，覆盖本次所有实际分发包 |

开发版按相同规则保留完整后缀，例如 `DeskPet-Windows-Setup-0.10.1-dev.2.exe`、`DeskPet-Windows-0.10.1-dev.2.zip` 和 `DeskPet-macOS-0.10.1-dev.2-arm64-preview.dmg`。独立主程序使用 `DeskPet-Windows-App-0.10.1-dev.2.exe`，用于已有运行目录内替换；首次使用推荐安装包或便携 ZIP。开发版为 prerelease，不替换正式版 latest。

0.5.6 的安装包沿用旧名，程序和更新助手则已接受规范的新旧 Windows 安装名，并绑定目标版本及本机安装模式。0.6.0 起安装器、Python 打包脚本、更新筛选与执行器使用同一规则；不靠附件上传顺序判断平台。

0.5.4 及更早正式版的**安装版**不认识新的 Windows 安装名，不能直接内置升级到 0.10.0。请先手动安装 0.5.6 后再使用内置更新，或直接手动覆盖安装最新版，保留 `data`。便携 ZIP 名称与目录布局保持不变，不受本次改名限制。

Release 说明按 Windows 推荐安装、便携、Mac 两架构、源码、外观、模板和校验文件排列。Mac 预览需标明 ad-hoc、未做 Developer ID 签名及未公证；正式模式不得自动降级为预览。GitHub 的附件区可由站点自行排序，推荐顺序以下载表为准。

附件名称不增加 01／02 等排序前缀，以免破坏平台与版本匹配。发布时按下载表的顺序上传，说明中提供逐项直达链接；SHA256.txt 覆盖本轮全部实际发布包，不包含自身。Mac 的 preview、架构与格式必须同时出现在文件名中，不以一个架构包冒充另一个。

## 正式／预览通道合同

0.10.0 为正式桥接版本，从 main 的固定标签 `v0.10.0` 构建。原 `v0.10.0-dev.1` 源码标签保留；仅创建源码标签不会成为客户端可安装的更新候选。现有 0.9.3 的标签和附件保持不变。

后续随应用打包时，五个伙伴 ZIP 的文件名统一使用完整应用发行版本（如 `gugugaga-HD-Skin-0.10.0-dev.1.zip`）；包内 `pack_version` 继续记录实际素材版本，不因应用升级改写素材。单独运行咕嘎素材打包工具时默认沿用素材版本。

| 渠道 | 分支与标签 | GitHub 标志 | Windows 附件示例 |
| --- | --- | --- | --- |
| 正式版 | main，`v0.10.0` | draft=false、prerelease=false、latest=true | `DeskPet-Windows-0.10.0.zip`、`DeskPet-Windows-Setup-0.10.0.exe` |
| 开发预览版 | dev，`v0.10.1-dev.2` | draft=false、prerelease=true、latest=false | `DeskPet-Windows-0.10.1-dev.2.zip`、`DeskPet-Windows-Setup-0.10.1-dev.2.exe` |

示例版本供说明，正式发布前应重新核实远端版本，选择未使用的新标签。dev.N 的 N 必须为正整数，旧裸 dev 后缀仅兼容读取，不再新发。正式通道仅取非预发布版本，预览通道取两种渠道中的最高语义版本；不依赖 API 返回顺序或发布时间。同底版 `dev.2 < dev.10 < 正式版`，旧四段数字版本兼容比较。切换偏好不会触发下载，不提供降级。

Windows EXE 的 AssemblyVersion/FileVersion 保持数字四段（例如 `0.9.5.0`），显示及附件名使用完整 `0.9.5-dev.1`；应用和 helper 的 AssemblyInformationalVersion 均为 `完整版本+40位commit`。更新任务协议 2 绑定目标 Release 标签解析出的 commit，覆盖前核对 ZIP 中的应用和 helper；安装器接收 EXPECTEDBUILD，身份不符时在写入前拒绝。新安装器还阻止手动降级，同版允许修复。旧安装器不具备新防护。

旧客户端先按原命名和 ZIP 根目录升级到 0.10.0，取得新 helper 后才能从客户端选择更高的 dev.N。旧客户端/旧 helper 继续使用旧任务协议，新 updater 才使用协议 2。同底版 0.10.0-dev.1 低于 0.10.0 正式版，正式版不会倒退到它。不能要求旧 helper 理解预览后缀，也不能直接把现有 0.9.3 的附件替换成新构建。

运行 ZIP 与 Inno 使用同一精确文件清单：4 个程序/配置文件、运行 README、CHANGELOG 和必要用户教程。不附带开发计划、验证脚本、测试、宣传截图及模板。源码包保留必要验证材料；独立模板 ZIP 同时含 high-fps-v2 和 legacy-v1。升级仅按固定历史路径及 SHA-256 清理旧发行内容；用户修改、额外文件、data 和导入皮肤保留，失败恢复备份。运行教程中未随包的本地引用转成对应版本的仓库链接。

## 构建与发布

`.github/workflows/windows-release.yml` 接收已存在的不可变 tag，默认 `publish=false`：只构建和上传私有 CI artifact，不创建 Release。工作流校验干净源码、HEAD/tag、main/dev 祖先归属、版本数值、应用与 helper 身份、ZIP/安装器及摘要。

用户后续授权发布时才设置 publish=true：共享发布队列串行执行，正式 latest 必须递增；创建草稿、上传完整附件、逐项验证 GitHub digest，最后一次复核 latest 后公开。查询失败即停止，dev 永不设置 latest。`SHA256.txt` 覆盖最终实际附件，不包含自身。Mac 仍使用已有独立构建/签名流程，数字 bundle 版本与完整显示版本分开，不把 Windows 预览通道冒充 Mac 正式签名。

0.10.0 发布授权包含 README／CHANGELOG 核对、dev CI 完成后的 main PR、正式版本标签、完整构建验证和 Release；后续预览版仍须使用更高版本及独立发布授权，不覆盖既有标签或附件。


## 0.11.1：独立素材版本

官方目录 JSON 使用发行版本号，角色 ZIP 使用逐角色素材版本（`DeskPet-Companion-<id>-<pack_version>.zip`）。`pack_version`、`min_app_version` 与应用发行号分别记录在 `assets/official/companion-versions.json`；仅应用升级不改变素材版本或 ZIP 字节。最低应用版本按完整安装与动作能力保守设置，不自动降到早期 v2 格式版本。

已验证的相同 ZIP 复用原安装根，不改写安装收据；检查或复用不显示下载提示。远端版本/衣装声明与相同字节冲突时保留原素材并报告异常，不宣称更新完成。历史版本因改写包内字段而产生不同摘要时，可能需要一次完整迁移下载。已发布附件不改写。


## 0.11.1-dev.2：受保护的历史副本

新安装使用独立的 `v2-<素材版本>-<摘要前缀>-<随机标识>` 缓存目录和第 2 版安装收据；ZIP 格式与完整 SHA256 校验不变。旧客户端不能选择这些受管目录。旧 `v-*` 安装仍可读取，但不会自动采用为可删除对象，也不改写原收据。旧应用再次运行时可能需要恢复自己的兼容缓存。

设置中的清理缓存新增“角色包历史副本”。只回收有明确归属、退役超过七天、无使用租约且非当前/前版本的受管目录。当前、一个已验证的恢复前版本、使用中或状态不明的素材及旧格式副本均保留。当前进程保守地持有已使用版本的租约直到退出，因此部分副本要在重启后才可清理；不会声称磁盘始终恰好只有两份。

安装切换先保存事务，再原子提交 current；成功后完成前版本和退役记录。中断状态先保护后恢复，不能因清理失败回滚已经生效的 current。仅从列表移除伙伴不会释放其聊天、配置、关系或可恢复素材；自定义包的历史版本不在本轮回收范围。
