# macOS 预览与正式发布准备

目标：macOS 14+；分别支持 Apple Silicon（arm64）与 Intel（x86_64）。当前用户没有 Apple Developer 签名证书，默认构建 **preview**：仅做 ad-hoc 完整性签名，不是 Developer ID 签名、不经过 Apple 公证、不自动发布 GitHub Release。

预览包提供双架构 DMG 与 ZIP。发布预览包不等于通过正式签名、公证及全部人工验收。平台支持范围见 [项目说明](../../README.md#平台与升级)；图像工作区与 Windows 文件 Agent 的区别见 [功能说明](../../docs/image-generation.md#对话中的图片与工具)。

## 在 Mac 构建预览

生产构建需要先校验完整鹅宝来源。使用同一源码在 Windows 通过 `tools/build_official_companions.py` 生成的鹅宝 ZIP；素材版本和内容必须匹配 `assets/official/companion-versions.json`。准备脚本会核对完整 payload pin，再生成仅含全部常规动作、视线、头像和元数据的 33 项默认 ZIP 与逐文件校验清单。完整 ZIP 仅作为 `e-bao.canonical.zip` 构建证明保存在 `work/bundled-ebao/`，不会嵌入应用或安装包；节日装扮在选中鹅宝后静默补齐，已有装扮保持可用。随后再执行下方构建。CI 会在所有 .NET 编译前下载、准备同一提交生成的素材。

```bash
python3 -m pip install Pillow
python3 tools/prepare_bundled_ebao.py --package "path/to/DeskPet-Companion-e-bao-MATERIAL_VERSION.zip"
```

这些文件只用于编译期嵌入程序集；成品 App 离线按需读取，不在启动时解压完整角色包。


需要 Xcode Command Line Tools、仓库 `desktop/global.json` 指定的 .NET SDK 和 Python 3。无需 Docker。

```bash
bash installer/macos/build.sh arm64 preview work/macos-arm64
```

Intel 对应 `x86_64`。输出目录必须是新的或为空；脚本不删除原有制品。输出标准 `DeskPet.app`、`DeskPet-macOS-版本-架构-preview.dmg`、ZIP、SHA256 和构建报告。拖入 Applications 后运行，用户数据保存在 Application Support，应用包不写入配置。

两架构 workflow 运行共享业务、平台原生 self-test、Avalonia Headless、真实 app smoke 和磁盘映像挂载检查。无声播放/设备缺失的路径不能代替真实麦克风、实际听感、权限弹窗、多屏和登录启动的人工验收。

## 正式发布门槛

正式 `release` 模式需要环境变量 `MACOS_SIGN_IDENTITY`（Developer ID Application 完整身份）与 `MACOS_NOTARY_PROFILE`（已配置的 Keychain 公证 profile 名），以及 `--release-evidence` 指向的两架构功能实测记录。缺少任一项直接失败，不会自动生成“无签名正式包”。证书、密码、私钥不能写进仓库或聊天。

脚本从内层 Mach-O 到 app 签名；使用 Hardened Runtime、JIT 与麦克风用途配置；通过 `notarytool` 检查 Accepted 后 staple/validate，再制作并检查 DMG/ZIP。`codesign --deep` 仅用于验证，不用于自动签名。两架构实测记录模板见 `release-evidence.example.json`；默认全部未通过，不能仅改文件为 true 代替实际验收。

`build.sh` 默认不创建 GitHub Release，也不修改版本号或现有标签。
