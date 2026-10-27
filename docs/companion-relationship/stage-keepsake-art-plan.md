# F4实施计划：专属场景与多语言艺术标题

需求见stage-keepsake-art-design.md最新用户范围。顺序F3修复审阅通过→F4a→F4b→H1a→H1b→M5c2→S1→S3→M6。每步独立审阅，单产品实施者；原有功能和安全门禁不变，无MSI或上游发布。

## F4a 字体资源与真实标题加载

1. 按builder报告固定Noto Serif Latin/CJK SC TC JP KR/Devanagari官方来源提交和OFL许可，记录原文件hash；生成六阶段及通用标题所需的静态子集，包含完整shaping依赖并使用独立family名。资产预处理工具不成为运行依赖，不安装到系统字体。
2. 先做两端实际字体加载证明：WPF csc输出正确命名.g.resources字体Resource，Avalonia显式avares资源；验证解析到自有family且中文字形存在，避免fallback蒙混过关。再纳入生产及所需测试构建。
3. 提供局部locale→标题字体选择API及style版本，覆盖13语言。真实标题的字符/区域字形与Hindi shaping验证；缺资源/未来未覆盖字符有安全UI字体回退，保留文本。
4. 检查许可随资源/包复制、子集可再生和覆盖检查；记录字节增量。无全局font改动/关系规则/UI改版。报告确切API、构建接法、测试和限制，经独立审阅交接F4b。

## F4b 场景资产、标题艺术与完整卡片

1. 逐张检查30张生成场景身份、动作、可用文本区、无意外文字、水印和画面边界；复制选定原始图到assets/relationships/<id>/<stage>.png，记录builtinimagegen提示词/参考/哈希/尺寸。5张万圣节场景存独立assets/relationships/holidays/halloween/<id>.png供H1后续使用，不伪造解锁。
2. 精确可信official身份与stage allowlist，包装两端资源并用实际程序资源解码验证；不根据显示名或外来manifest路径选图。坏/缺图回退本伙伴F3立绘卡。
3. 复用800×480冻结PNG，把场景铺满、右侧浅深阅读底板控制对比；人物不整体染黑。只缓存当前伙伴六张800宽图，native清理所有权和source版本明确。
4. 使用F4a字体，标题最多两行，独立叶枝/缎带等装饰与完整正文日期不重叠；字体/装饰版本进入capture key。纪念日期与天数仍来自保存事实，导出与打开预览同字节。
5. 至少30×2主题×2端的实际卡片和中/长德完整布局，13语言六标题×2主题×2端局部边界/字形验证，抽查日/韩/印地文整卡。共享selector隔离、资源损坏/寿命、no-preview-writes、保存/切换身份和包内资源覆盖。
6. 形成实际审核页和完整结果；5张节日图只是资源准备，H1领取/持久化接入另行验证。最终EXE/ZIP资源与体积验证归M6，不宣称Mac硬件通过。
