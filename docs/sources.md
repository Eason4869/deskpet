# 素材与平台说明

「大肥鱼」为本会话根据 DeepSeek 同人形象制作的生成式角色及动作图；参考形象项目：[YunYueSama/codex-deepseek-pet](https://github.com/YunYueSama/codex-deepseek-pet)。应用代码独立实现。

「E包」使用本地 E-bao-20261013 已完成的角色和动作原图，形象来自该会话的参考图。原始目录保持原样。本次分别从两套原始动作条重新提取透明人物，处理抠图边色，统一组内缩放与落地基线，并保留跳跃位移；不使用小精灵图作为高清像素来源。

“桌面伙伴”应用 LOGO 参考 E包的白发蓝眸形象单独生成，保存高清透明 PNG，导出 Windows 多尺寸 ICO。图标使用独立品牌素材，角色动画保持原样。

Windows 平台依据：[WPF 透明窗口](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency)、[Windows 录音缓冲管理](https://learn.microsoft.com/en-us/windows/win32/multimedia/managing-waveform-audio-recording)。录音通过显式点击启动，使用默认输入设备，16 kHz 单声道 PCM。

文字与通用语音依据：[Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create)、[语音识别](https://developers.openai.com/api/reference/resources/audio/subresources/transcriptions/methods/create)、[语音合成](https://developers.openai.com/api/reference/resources/audio/subresources/speech/methods/create)、[Anthropic Messages](https://platform.claude.com/docs/en/api/messages/create)。

MiMo 对话音频：[识别](https://mimo.mi.com/docs/zh-CN/quick-start/usage-guide/audio/Speech-Recognition)、[合成](https://mimo.mi.com/docs/zh-CN/quick-start/usage-guide/audio/speech-synthesis-v2.5)。采用 input_audio 与 assistant 合成文本，解析 message.audio.data 的 Base64 WAV。

Gemini Live：[WebSocket 入门](https://ai.google.dev/gemini-api/docs/live-api/get-started-websocket)、[Live API 参考](https://ai.google.dev/api/live)。传输原始 PCM，支持转录、播放与打断。

服务预设参考：[Gemini](https://ai.google.dev/gemini-api/docs/openai)、[DeepSeek](https://api-docs.deepseek.com/)、[Kimi](https://platform.kimi.com/docs/get-api-key)、[MiniMax](https://platform.minimax.io/docs/api-reference/text-openai-api)、[火山](https://docs.volcengine.com/docs/ark/base-url-and-authentication?lang=en)、[千问](https://help.aliyun.com/zh/model-studio/model-calling-in-sub-workspace)、[智谱](https://docs.bigmodel.cn/cn/best-practice/case/ai-search-engine)、[Claude](https://platform.claude.com/docs/en/models/sonnet-4-6/overview)。实际模型权限由账号决定，预设均可编辑。

更新：[GitHub Releases REST API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)。官方仓库固定为 [Eason4869/deskpet](https://github.com/Eason4869/deskpet)。模型与语音协议使用本机模拟验证，未使用真实线上模型密钥验收。

安装器：采用官方 [Inno Setup 6.7.3](https://jrsoftware.org/isdl.php)，下载时验证 GitHub SHA256 和 Pyrsys 数字签名。[当前用户安装](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)、[运行互斥保护](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm)、[.NET 前置检查](https://jrsoftware.org/ishelp/topic_isxfunc_isdotnetinstalled.htm) 按官方规范实现。简体中文向导采用官方源码仓库的 [ChineseSimplified.isl](https://github.com/jrsoftware/issrc/blob/is-6_7_3/Files/Languages/Unofficial/ChineseSimplified.isl)，保留维护者声明。

自动更新安装参数：[Inno Setup 命令行](https://jrsoftware.org/ishelp/topic_setupcmdline.htm)，使用静默安装、原目录、禁止系统重启和自动关闭其他程序的选项。

0.3.3 新增「马尔芙」：根据用户提供的金发绿眼人物与黑绿魔法袍 Q 版参考图生成角色、九组动作及十六个基础视线方向。原始高清帧保存在 `assets/marfu-spritesheet.png`。0.4.0 删除造成重影的光流与混合补帧，直接播放原始帧。音色预设依据见[厂商官方音色说明](voice-presets.md)。

## 服务商 Logo

本地开发互动优化：使用内置 imagegen，每次附四位伙伴各自的原版预览图，按 Pets create-pet 的逐行动作、组件提取和边缘去色流程重绘四技能、开心及拖放动作，另修复 E包跳跃。最终 33 条、264 帧位于 `assets/companion-animations`；精确提示与选定源条校验值见 [素材记录](../assets/companion-animations/README.md)。原有基础图集未改动。

服务商标识采用 [Lobe Icons](https://github.com/lobehub/lobe-icons) 的单色矢量路径，固定来源提交 `8a1b2821bb768c484fe8da20c5e1838b6bf551a9`。小米 MiMo 使用 [Simple Icons 的 Xiaomi 图形](https://github.com/simple-icons/simple-icons/blob/develop/icons/xiaomi.svg) 中的 mi 标志，采用黑色圆底、白色字形；该路径使用 [CC0-1.0](https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md) 许可。标识仅用于辨识服务商，不表示合作或授权背书。图形与许可随程序离线提供，不从网络加载图标。

模型获取依据：[OpenAI Models](https://developers.openai.com/api/reference/resources/models/methods/list)、[Anthropic Models](https://platform.claude.com/docs/en/api/models/list)。列表可见性、对话兼容性与调用权限以实际服务为准。

Lobe Icons MIT 许可：
MIT License

Copyright (c) 2023 LobeHub

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Agent 开源参考

原生 C# 工具循环参考 [OpenManus-Lite（MIT）](https://github.com/Celebez/OpenManus-Lite/tree/1aed3c29ec12be7f68dc5471d48eacd84c891565) 与 [OpenCode（MIT）](https://github.com/anomalyco/opencode/tree/bc5ebf00a4c169ece10f276e6ce02eaf18c326e6) 的工具边界、权限和有界上下文设计，未复制或翻译其代码。交互参考 Codex App 与 Mimo Code Desktop；后者采用 PolyForm Noncommercial，仅参考需求，未复制代码或界面资源。
