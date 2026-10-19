# 发布包命名与升级兼容

所有可安装包、运行 ZIP、源码、外观和模板包含版本号；平台包显式区分 Windows 与 macOS，Mac 再区分 CPU。当前 0.9.1 提供 Windows 安装／便携版与 macOS 双架构预览。

| 用途（推荐下载顺序） | 0.5.6（兼容旧名） | 0.9.1 |
| --- | --- | --- |
| Windows 安装（推荐） | `DeskPet-Setup-0.5.6.exe` | `DeskPet-Windows-Setup-0.9.1.exe` |
| Windows 便携 | `DeskPet-Windows-0.5.6.zip` | `DeskPet-Windows-0.9.1.zip` |
| Mac Apple Silicon 预览 | 无 | `DeskPet-macOS-0.9.1-arm64-preview.dmg` / `.zip` |
| Mac Intel 预览 | 无 | `DeskPet-macOS-0.9.1-x86_64-preview.dmg` / `.zip` |
| 源码 | `DeskPet-Source-0.5.6.zip` | `DeskPet-Source-0.9.1.zip` |
| 外观 | `{Ebao,Dafeiyu,Marfu,Xiaobei}-HD-Skin-0.5.6.zip` | 相同前缀，版本改为 `0.9.1` |
| 创作模板 | `DeskPet-Skin-Template-v2-0.5.6.zip` | `DeskPet-Skin-Template-v2-0.9.1.zip` |
| 校验 | `SHA256.txt` | `SHA256.txt`，覆盖本次所有实际分发包 |

0.5.6 的安装包沿用旧名，程序和更新助手则已接受规范的新旧 Windows 安装名，并绑定目标版本及本机安装模式。0.6.0 起安装器、Python 打包脚本、更新筛选与执行器使用同一规则；不靠附件上传顺序判断平台。

0.5.4 及更早正式版的**安装版**不认识新的 Windows 安装名，不能直接内置升级到 0.9.1。请先手动安装 0.5.6 后再使用内置更新，或直接手动覆盖安装最新版，保留 `data`。便携 ZIP 名称与目录布局保持不变，不受本次改名限制。

Release 说明按 Windows 推荐安装、便携、Mac 两架构、源码、外观、模板和校验文件排列。Mac 预览需标明 ad-hoc、未做 Developer ID 签名及未公证；正式模式不得自动降级为预览。GitHub 的附件区可由站点自行排序，推荐顺序以下载表为准。

附件名称不增加 01／02 等排序前缀，以免破坏平台与版本匹配。发布时按下载表的顺序上传，说明中提供逐项直达链接；SHA256.txt 覆盖本轮全部实际发布包，不包含自身。Mac 的 preview、架构与格式必须同时出现在文件名中，不以一个架构包冒充另一个。
