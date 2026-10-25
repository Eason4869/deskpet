# Stable identity, local schemas and optional content

[使用说明](user-guide.md) · [User guide](user-guide.en.md)

本文件说明当前已实现的格式；不是允许角色包导入任意数据、执行动作或注册节日的接口。The formats below describe the implemented contract, not permission for packs to import arbitrary state, execute actions or register holidays.

## Identity / 稳定身份

`RelationshipIdentity.Resolve` uses the verified picker provenance. Official identities use `official:<character-id>`. Imported/custom packages retain their own stable picker identity; a package’s claimed official ID does not confer trust. Legacy identities deterministically map the persisted stable skin ID into the `legacy-skin` namespace. Display names, artwork paths, outfit IDs and package versions are not relationship IDs.

官方角色换装和更新沿用同一伙伴关系；同名自定义角色不会共享官方账本。衣装 `id/version` 与伙伴身份分开。更换资源版本不改写已保存节日卡的历史版本、名称或场景。旧 v1 角色包经导入、重新发现和保留转换后仍保存其原始 atlas 来源、插画/身份标志和可选关系内容，不靠重建 v2 身份冒充升级。

## Optional manifest extension / 可选清单扩展

The optional top-level `relationship` object is supported by the existing skin-manifest readers, including retained v1 imports. It does not grant scores, identities, outfits, holiday definitions or executable code.

```json
{
  "relationship": {
    "fallback_locale": "en_US",
    "stages": {
      "first_meeting": {
        "welcome": { "zh_CN": "很高兴认识你。", "en_US": "Nice to meet you." },
        "interaction": { "zh_CN": "你好呀！", "en_US": "Hello there!" }
      },
      "best_friend": {
        "welcome": { "en_US": "Another day together." }
      }
    }
  }
}
```

- Stable stage IDs remain `first_meeting`, `familiar`, `close`, `trusted`, `heartfelt`, `best_friend`. Application resource segments use explicit `firstMeeting` / `bestFriend` mapping; never rename saved stage IDs to match JSON resource keys.
- Only `fallback_locale` and `stages` are accepted in this object. A stage accepts only `welcome`, `interaction` and optional `action`. Text dictionaries accept the 13 built-in locale codes, at most 280 characters per value, no blank values or control characters other than newline. No more than six stage entries or thirteen locale entries per dictionary are accepted.
- `action`, if provided, must already be supported by that actual loaded skin/host. It cannot invent a new action or bypass validation. Omitting it uses normal supported behavior.
- Lookup uses the explicitly requested locale, then the pack’s declared fallback, then the built-in generic text in that requested locale. It does not silently switch the process UI language. Explicit persona/preferences remain authoritative.

## Local relationship store / 本地关系数据

`data/relationships/state.json` has `SchemaVersion: 1`, `Settings` and an identity-keyed `Companions` map. Shared settings are `GrowthEnabled`, `FeedbackEnabled` and `AiContextEnabled` (defaults: enabled). A companion stores score, first/last UTC instants and local dates, offset/budget epoch/high-water date, active dates, category/day counters, logical-event receipts and earned milestones. UTC instants are persisted as ticks so submillisecond rollback and reward intervals survive restart. Milestones retain stable stage IDs, earned dates and notification claims.

`pending.json` is the runtime’s save-first outbox, not extra earned history. Preserve it with the ledger when backing up. Successfully committed receipts, milestone claims and pending event attribution must survive cache cleaning, history clearing, preference reset and restart. Unknown/corrupt/newer schema is a warning state with original bytes preserved, not an invitation to initialize an empty first meeting. Do not hand-edit counters, erase receipts or delete pending data to bypass a warning.

## Holiday store / 节日纪念数据

`data/commemorations/holiday-state.json` uses `Schema: 1`, `DateHighWater` and `Records`. Identity is `(CompanionId, HolidayId, InstanceKey)`; the instance is the calendar’s annual occurrence, not just a day number or outfit. Records retain `CharacterId`, captured `CompanionName`, holiday/calendar/year/date range, earned UTC ticks/local offset/date, outfit/scene IDs and versions, dimensions, byte length and `SceneSha256`. Original scene bytes are retained separately as `commemorations/scenes/<sha256>.png`.

已拥有记录先于当前衣装和资源可用性判断。更换当前图片、包版本或名称不会重写旧纪念。读取场景失败时继续保留记录及其历史事实；整本状态不可读时不能把未知数量显示成零。

Future acquisition is a compiled, controlled extension: `HolidayKeepsakeDefinition` binds holiday/calendar IDs, first enabled year, title/message/style resource keys, `SceneSetId` and acquisition enablement. Currently `SceneSetId == HolidayId`, the built-in registry enables only Halloween from 2026, and the five-argument `IRelationshipSceneProvider.Capture(companionId, characterId, holidayId, outfitId, outfitVersion)` must capture the exact reviewed 800×480 scene/version/hash. Original PNG structure, decompression completeness, dimensions and hashes are validated before durable publication. A new calendar entry without matching registry/resources is not collectible, and introducing a definition does not backfill past dates.

## Review and compatibility / 审阅与兼容

All original 4507 values in each locale are protected against the fixed `fde04835ffce544c334205d881a232c579ded4df` base. New feature copy has separate independent text/format fixtures, exact literal callsite and committed-source provenance. Assistant-authored translations have not received native-human proofreading. Technical source/format checks do not establish natural-language or real-device acceptance. Native save pickers, macOS hardware and final candidate package behavior remain separately reported.
