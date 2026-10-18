# macOS 预览与正式发布准备

目标：macOS 14+；分别支持 Apple Silicon（arm64）与 Intel（x86_64）。当前用户没有 Apple Developer 签名证书，默认构建 **preview**：仅做 ad-hoc 完整性签名，不是 Developer ID 签名、不经过 Apple 公证、不自动发布 GitHub Release。

0.7.0 Release 提供双架构 preview DMG 与 ZIP；发布预览包不等于通过正式签名、公证及全部人工验收。Windows 新版侧栏、富文本输入、118 步引导和文件 Agent 不代表已在 Mac 实现。基础功能与验证边界见 [0.6.0 就绪矩阵](../../docs/macos-readiness-0.6.0.md)。

## 在 Mac 构建预览

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
