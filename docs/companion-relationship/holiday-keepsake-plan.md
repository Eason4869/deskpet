# H1 实施计划：节日纪念注册、领取、长期保存与完整卡面

最终需求holiday-keepsake-design.md，接口依据builder v2场景契约、v3可扩展注册聚合契约；旧v1肖像契约已废弃。依赖F4a/F4b交付字体、35图资源、场景descriptor/provider和卡面基础，之后H1a→H1b→M5c2→S1→S3→M6，无MSI/上游/真实用户数据操作。

## H1a 共享规则、注册与场景持久化

1. 固定C#5只读registry/definition、encounter、aggregate observation result、record/album/scene/preview DTO。初始仅Halloween2026；复用CalendarId查现有日历，支持重叠测试节日，生产不暴露未来占位活动。
2. 先写真实规则测试：Oct30/31/Nov1、2025/2027、全球观察日期水位/同一次UTCoffset样本、跨伙伴/年度/重启去重、未穿特殊服和关闭反馈仍可领取、失败不报Saved、重复metadata回调不加分。
3. 实现受控scene provider捕获来源、精确服饰/场景版本/hash/尺寸匹配；共享PNG完整结构与两端完整原尺寸native decode。12MiB、near5:3、width800..3200、height≤2000、≤6.4Mpixels，合法不透明图允许，原始字节保存。
4. 独立state/scenes schema、锁、reload、年度与InstanceKey唯一、blob先/JSON后原子发布、未知/坏状态保留。历史结构独立当前registry/art；坏scene只影响单卡。32MiB上限不删旧卡。
5. 一次事件入队前取得维护活动，冻结所有候选来源，顺序验证、有界内存、一次JSON提交；坏候选不阻其他合格项，JSON失败没有任何新Saved。关闭排空、订阅异常隔离、preview完全只读，freshretry不跨日补领。
6. 进程间竞争、两节日同日/一个坏图/提交失败、源替换/版本竞争、unknownschema、坏IDAT、资源和definition移除后可读、维护清理保留全目录等实际测试。报告确切最终API/流与字节所有权，独立审阅。

## H1b 真实入口、节日纪念册与保存

1. 两端只在实际显示伙伴/实际打开主页时提交事件；覆盖跨午夜、隐藏/锁屏/后台排除，inflight/day/sourcegeneration合并和失败重试。切换后旧回调失效但不改已受理归属。
2. 注册表驱动节日预览入口，明确锁定/不可用/已获得；savedcounts不来自registry。消费聚合结果，不复制资格规则。已获记录移除定义后仍显示通用可读卡面。
3. 用保存的scene构成800×480完整卡，复用F4艺术字体/原生装饰/轻柔右侧浅深渐变。保留当时姓名/服饰/场景/日期，标题与文案13语言，cachekey包括动态样式版本。
4. 所有已获节日卡都有“保存纪念卡”，OS picker选择PNG路径，预览/导出同字节，取消无写入、失败可重试、身份切换保护。锁定预览不伪装已获保存。
5. 五角色两端浅深、长语言/窄宽、空/错误/锁定/已获、实际资源移除仍导出、前后台边界和保存流程验证；日期注入fixture，不改主机时钟。实际screenshots登记。独立审阅后进入M6最终包体/安装/保留矩阵。

后续新增节日只增加受审registry定义、必要日历规则、可信服饰场景映射与素材、13语言keys及fontcorpus；验证既有通用路径，不复制事务/显示事件或导出实现。
