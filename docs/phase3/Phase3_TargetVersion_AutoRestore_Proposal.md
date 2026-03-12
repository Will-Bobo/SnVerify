# Phase3 版本号回填方案（评审结论版 v5）

> 实施状态：已按本方案落地（Settings 回填路径），`VerificationParameter` / `ParameterService` / `ProcessCoordinator` / DB schema 均未改动；编译通过，单测 363/363 通过。

## 一、需求目标（最终确认）

本次需求只解决操作员效率问题：

- 在同一产品、同一项目下，如果版本号没有变化，希望系统自动回填上一次输入的版本号，减少重复输入。
- 当前重点场景是 KM001。

典型预期：

- 上一次输入：
  - Android: `1.0.5`
  - Board: `1.0.3`
  - ChargeBoard: `2.0.1`
- 下次打开程序继续 KM001 时，可自动回填以上值。

---

## 二、关键架构决策（最终确认）

本次需求**不修改现有参数架构**，不做系统级改造。

| 项 | 是否修改 |
|---|---|
| `VerificationParameter` 表结构 | ❌ |
| `ParameterService` | ❌ |
| `ProcessCoordinator` | ❌ |
| Session / Order 逻辑 | ❌ |
| 数据库 schema | ❌ |

理由：

- `VerificationParameter` 当前职责是运行时校验参数来源，不是 UI 默认值仓库。
- 为 UI 回填去改 Session 维度、参数查询链路，属于明显过度设计，与本次目标不匹配。

---

## 三、回填实现方案（采用 Settings）

使用 `ApplicationSettings` 存储 UI 默认值（UserScoped）。

新增字段：

- `LastProductCode`
- `LastExpectedAndroidVersion`
- `LastExpectedBoardVersion`
- `LastExpectedChargeBoardVersion`

字段类型与默认值：

- `string`
- `Default = ""`

---

## 四、保存逻辑

保存时机：

- `StartBatchAsync` 成功执行后（现有 `Settings.Save()` 同一位置）。

保存内容：

- `Settings.Default.LastProductCode = SelectedProductCode;`
- `Settings.Default.LastExpectedAndroidVersion = ExpectedAndroidVersion;`
- `Settings.Default.LastExpectedBoardVersion = ExpectedBoardVersion;`
- `Settings.Default.LastExpectedChargeBoardVersion = ExpectedChargeBoardVersion;`
- `Settings.Default.Save();`

约束：

- 仅在 **Phase3 产品** 下保存版本号相关字段。
- 若产品非 Phase3，不覆盖已有 LastExpected*（避免误清空用户可复用值）。

---

## 五、启动回填逻辑

位置：

- `MainViewModel` 初始化阶段。

执行顺序：

1. 读取 Settings。
2. `LoadAvailableProducts()`。
3. 恢复 `LastProductCode`（若存在于产品列表）。
4. 若当前产品为 Phase3，则回填版本号。

建议判定：

```csharp
if (SelectedProductCode == Settings.Default.LastProductCode && IsPhase3Product)
{
    ExpectedAndroidVersion = Settings.Default.LastExpectedAndroidVersion;
    ExpectedBoardVersion = Settings.Default.LastExpectedBoardVersion;
    ExpectedChargeBoardVersion = Settings.Default.LastExpectedChargeBoardVersion;
}
```

---

## 六、为什么不使用 VerificationParameter 做本次回填

| 维度 | 说明 |
|---|---|
| 参数表语义 | 偏运行时参数来源，不是 UI 默认值 |
| Session 生命周期 | 若按批次（Session）严谨建模，会牵涉全链路改造 |
| 本次需求 | 只需要“上一次值”回填 |
| 复杂度 | 改参数架构属于系统级重构，超出本次范围 |

职责边界：

- **UI 默认值**：`Settings`
- **运行参数**：`VerificationParameter`

---

## 七、风险与扩展说明（保留）

已确认风险：

- 后续若新增产品或新增页面参数，若同样需要“上一次值回填”，需要新增对应 Settings 字段与回填逻辑。

评估结论：

- 该风险可接受，且符合“本次最小改动、快速见效”的实施目标。
- 如后续回填需求持续扩展，再统一评估“UI 默认值配置中心”或“可扩展配置存储”方案，不在本次实现。

---

## 八、实施范围（评审通过后执行）

| 类型 | 修改点 |
|---|---|
| Settings | 新增 4 个字段：`LastProductCode`、`LastExpectedAndroidVersion`、`LastExpectedBoardVersion`、`LastExpectedChargeBoardVersion` |
| MainViewModel | 在 `StartBatchAsync` 成功后写入上述 Settings（仅 Phase3）；初始化阶段按顺序恢复并回填 |
| 文档 | 更新本方案落地状态、补充回填行为说明 |
| 测试 | 覆盖保存与回填主路径（Phase3），以及非 Phase3 不回填/不覆盖边界 |

---

评审通过后，按本方案执行代码改造与验证。
