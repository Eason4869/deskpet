# 发布包命名与升级兼容

所有可安装包、运行 ZIP、源码、外观和模板包含版本号；平台包显式区分 Windows 与 macOS，Mac 再区分 CPU。当前 0.9.3 提供 Windows 安装／便携版与 macOS 双架构预览。

运行包、源码及模板使用 0.9.3；第五位伙伴的完整素材包为 `gugugaga-HD-Skin-0.9.3.zip`，其文件名和内置说明读取 `pet.json` 的 `pack_version`。下表列出本版附件命名。

| 用途（推荐下载顺序） | 0.5.6（兼容旧名） | 0.9.3 |
| --- | --- | --- |
| Windows 安装（推荐） | `DeskPet-Setup-0.5.6.exe` | `DeskPet-Windows-Setup-0.9.3.exe` |
| Windows 便携 | `DeskPet-Windows-0.5.6.zip` | `DeskPet-Windows-0.9.3.zip` |
| Mac Apple Silicon 预览 | 无 | `DeskPet-macOS-0.9.3-arm64-preview.dmg` / `.zip` |
| Mac Intel 预览 | 无 | `DeskPet-macOS-0.9.3-x86_64-preview.dmg` / `.zip` |
| 源码 | `DeskPet-Source-0.5.6.zip` | `DeskPet-Source-0.9.3.zip` |
| 外观 | `{Ebao,Dafeiyu,Marfu,Xiaobei}-HD-Skin-0.5.6.zip` | 相同前缀，版本改为 `0.9.3`；新增 `gugugaga-HD-Skin-0.9.3.zip` |
| 创作模板 | `DeskPet-Skin-Template-v2-0.5.6.zip` | `DeskPet-Skin-Template-v2-0.9.3.zip` |
| 校验 | `SHA256.txt` | `SHA256.txt`，覆盖本次所有实际分发包 |

0.5.6 的安装包沿用旧名，程序和更新助手则已接受规范的新旧 Windows 安装名，并绑定目标版本及本机安装模式。0.6.0 起安装器、Python 打包脚本、更新筛选与执行器使用同一规则；不靠附件上传顺序判断平台。

0.5.4 及更早正式版的**安装版**不认识新的 Windows 安装名，不能直接内置升级到 0.9.3。请先手动安装 0.5.6 后再使用内置更新，或直接手动覆盖安装最新版，保留 `data`。便携 ZIP 名称与目录布局保持不变，不受本次改名限制。

Release 说明按 Windows 推荐安装、便携、Mac 两架构、源码、外观、模板和校验文件排列。Mac 预览需标明 ad-hoc、未做 Developer ID 签名及未公证；正式模式不得自动降级为预览。GitHub 的附件区可由站点自行排序，推荐顺序以下载表为准。

附件名称不增加 01／02 等排序前缀，以免破坏平台与版本匹配。发布时按下载表的顺序上传，说明中提供逐项直达链接；SHA256.txt 覆盖本轮全部实际发布包，不包含自身。Mac 的 preview、架构与格式必须同时出现在文件名中，不以一个架构包冒充另一个。

## 下一版的正式／预览通道合同

以下为待发布实现，不修改现有 0.9.3 的标签或附件。

| 渠道 | 分支与标签 | GitHub 标志 | Windows 附件示例 |
| --- | --- | --- | --- |
| 正式版 | main，`v0.9.4` | draft=false、prerelease=false、latest=true | `DeskPet-Windows-0.9.4.zip`、`DeskPet-Windows-Setup-0.9.4.exe` |
| 预览版 | dev，`v0.9.5-dev.1` | draft=false、prerelease=true、latest=false | `DeskPet-Windows-0.9.5-dev.1.zip`、`DeskPet-Windows-Setup-0.9.5-dev.1.exe` |

示例版本供说明，正式发布前应重新核实远端版本，选择未使用的新标签。dev.N 的 N 必须为正整数，旧裸 dev 后缀仅兼容读取，不再新发。正式通道仅取非预发布版本，预览通道取两种渠道中的最高语义版本；不依赖 API 返回顺序或发布时间。同底版 `dev.2 < dev.10 < 正式版`，旧四段数字版本兼容比较。切换偏好不会触发下载，不提供降级。

Windows EXE 的 AssemblyVersion/FileVersion 保持数字四段（例如 `0.9.5.0`），显示及附件名使用完整 `0.9.5-dev.1`；应用和 helper 的 AssemblyInformationalVersion 均为 `完整版本+40位commit`。更新任务协议 2 绑定目标 Release 标签解析出的 commit，覆盖前核对 ZIP 中的应用和 helper；安装器接收 EXPECTEDBUILD，身份不符时在写入前拒绝。新安装器还阻止手动降级，同版允许修复。旧安装器不具备新防护。

必须先发布一个更高的纯数字正式桥接版本，让旧客户端按原命名和 ZIP 根目录完成升级并取得新 helper；随后才能从客户端选择 dev.N。旧客户端/旧 helper 继续使用旧任务协议，新 updater 才使用协议 2。不能要求旧 helper 理解预览后缀，也不能直接把现有 0.9.3 的附件替换成新构建。

运行 ZIP 与 Inno 使用同一精确文件清单：4 个程序/配置文件、运行 README、CHANGELOG 和必要用户教程。不附带开发计划、验证脚本、测试、宣传截图及模板。源码包保留必要验证材料；独立模板 ZIP 同时含 high-fps-v2 和 legacy-v1。升级仅按固定历史路径及 SHA-256 清理旧发行内容；用户修改、额外文件、data 和导入皮肤保留，失败恢复备份。运行教程中未随包的本地引用转成对应版本的仓库链接。

## 构建与发布

`.github/workflows/windows-release.yml` 接收已存在的不可变 tag，默认 `publish=false`：只构建和上传私有 CI artifact，不创建 Release。工作流校验干净源码、HEAD/tag、main/dev 祖先归属、版本数值、应用与 helper 身份、ZIP/安装器及摘要。

用户后续授权发布时才设置 publish=true：共享发布队列串行执行，正式 latest 必须递增；创建草稿、上传完整附件、逐项验证 GitHub digest，最后一次复核 latest 后公开。查询失败即停止，dev 永不设置 latest。`SHA256.txt` 覆盖最终实际附件，不包含自身。Mac 仍使用已有独立构建/签名流程，数字 bundle 版本与完整显示版本分开，不把 Windows 预览通道冒充 Mac 正式签名。

本次实施不提升版本号、不创建标签、不推送、不发布 Release。下一次正式桥接版本及之后的 dev 版本必须按上述顺序另行发布。
