## 字符串资源管理架构与流程（Phase3）

> 目的：统一说明 SnVerify 当前的字符串资源管理方式，作为后续新增 / 调整资源字符串时的操作指引与约束。

---

## 一、总体架构

- **集中管理位置**：  
  - 所有 UI 与业务用到的本地化字符串，集中维护在 `Properties/Resources.resx` 中。  
  - C# 侧通过自动生成的 `Properties.Resources` 强类型类访问。

- **使用场景**：
  - **UI 标签 / 提示**：窗口标题、按钮文本、字段标签等（如 `Label_DeviceSn`、`Label_AndroidVersionNo`）。
  - **错误提示文案**：规则失败原因、ADB 相关错误等（如 `Err_AdbCommandEmpty`、`Err_SnNotMatch`）。
  - **导出列头**：KM001 等产品的导出表头（如 `Export_Km001_StickerSn`、`Export_Summary_Total`）。

- **多语言预留**：
  - 当前仅维护一套中文资源，但通过 `.resx` + `Properties.Resources` 的模式，已为将来增加其他语言（`Resources.en.resx` 等）预留了扩展点。

---

## 二、资源命名约定

### 2.1 UI 标签类（`Label_*`）

- **用途**：各类界面标签与字段名。
- **命名规则**：
  - 前缀：`Label_`
  - 示例：
    - `Label_DeviceSn` → “设备SN”
    - `Label_AndroidVersionNo` → “Android版本号”
    - `Label_ChipId` → “芯片ID”

### 2.2 错误文案类（`Err_*`）

- **用途**：规则失败原因、ADB 异常等错误文本。
- **命名规则**：
  - 前缀：`Err_`
  - Key 对应业务错误语义，而非 UI 位置。
  - 示例：
    - `Err_AdbCommandEmpty` → “ADB命令为空”
    - `Err_AndroidVersionMismatch` → “设备Android版本号与目标值不匹配”
    - `Err_BoardVersionMismatch` → “芯片版本号与目标值不匹配”
    - `Err_ChargeBoardVersionMismatch` → “充电板版本号与目标值不匹配”

- **使用方式**：
  - 规则执行层产出 **错误代码**（如 `ADB_READ_FAIL`、`ANDROID_VERSION_MISMATCH`），写入 `TestRecord.FailReason`。
  - UI / 导出层通过 `FailReasonTextResolver.Resolve(code)` 将代码映射为 `Err_*` 资源中文案：
    - 空 / null → 空字符串；
    - 已知 code → 对应 `Err_*` 文案；
    - 未知 code → 原样返回 code。

### 2.3 导出列头类（`Export_*`）

- **用途**：Excel 导出中的表头文案（包括明细 Sheet 与 Summary Sheet）。
- **命名规则**：
  - KM001 明细列：`Export_Km001_*`
  - Summary 列：`Export_Summary_*`
  - 示例：
    - `Export_Km001_RowNumber` → “序号”
    - `Export_Km001_StickerSn` → “条形码SN”
    - `Export_Km001_DeviceSn` → “设备SN”
    - `Export_Km001_ExpectedBoardVersion` → “目标芯片版本”
    - `Export_Km001_ActualBoardVersion` → “设备芯片版本”
    - `Export_Km001_ExpectedVersion` → “Android目标版本号”
    - `Export_Km001_ActualVersion` → “Android实际版本号”
    - `Export_Summary_Total` → “Total”
    - `Export_Summary_PassRate` → “PassRate”

- **使用方式**：
  - KM001 导出（`Km001SessionExporter`）中，通过 `ProductExportProfile.RecordColumns` 携带 `HeaderResourceKey`，在运行时调用：
    - `Resources.ResourceManager.GetString(resourceKey) ?? resourceKey`
  - 若资源缺失，则回退显示资源 Key 本身，避免抛异常。

---

## 三、字符串使用模式

### 3.1 代码侧访问模式

- 所有资源最终通过 `Properties.Resources` 强类型类或 `ResourceManager` 访问：
  - 直接属性访问（适合固定标签）：
    - `Properties.Resources.Label_DeviceSn`
  - 动态按 Key 获取（适合导出列头、错误码映射等）：
    - `Resources.ResourceManager.GetString("Export_Km001_DeviceSn")`

### 3.2 FailReason → 用户可读错误文案

- **规则输出**：  
  - 规则执行链路写入 `TestRecord.FailReason = 错误代码`（如 `ADB_READ_FAIL`）。

- **解析器**：`FailReasonTextResolver.Resolve(string failReasonCode)`：
  - 通过 `switch` 将业务错误代码映射为内部 `Err_*` Key，再从 `Resources` 中取文案。
  - 保证 UI 与导出在显示错误信息时行为一致。

- **导出层集成**（Phase3 之后的实现）：
  - 在 `DefaultExportValueResolver` 中，`ExportFieldId.ErrorDetail` 使用：
    - `FailReasonTextResolver.Resolve(record.FailReason)`
  - 这样 PASS/FAIL Sheet 中的「错误详细」列显示的就是与 UI 相同的中文解释。

### 3.3 导出列头资源化

- `ProductExportRegistry` 为每个产品维护 `ProductExportProfile`：
  - 列字段：`ExportFieldId`
  - 表头 Key：`HeaderResourceKey`（如 `Export_Km001_StickerSn`）
- `Km001SessionExporter` 在写表头时：
  - 遍历 `RecordColumns`，调用统一方法 `GetHeaderText(resourceKey)`：
    - 先尝试从 `Resources` 获取；
    - 失败时回退到 `resourceKey` 文本本身。

---

## 四、添加 / 修改字符串资源的标准流程

> 以下流程适用于新增 UI 标签、错误提示、导出表头等所有字符串资源。

1. **确定命名与分组前缀**：
   - UI 标签：使用 `Label_*`；
   - 错误文案：使用 `Err_*`，并确保有对应的错误 code 与 `FailReasonTextResolver` 映射；
   - 导出列头：根据产品 / Sheet 使用 `Export_{ProductCode}_*` 或 `Export_Summary_*`。

2. **在 `Resources.resx` 中新增条目**：
   - 使用 VS 的资源编辑器或直接编辑 XML：
     - `<data name="Export_Km001_NewField" xml:space="preserve"><value>新字段说明</value></data>`

3. **重新生成强类型资源类（如有需要）**：
   - 项目设置为 `EmbeddedResource + ResXFileCodeGenerator` 时，保存 `.resx` 通常会自动更新 `Resources.Designer.cs`。
   - 若使用命名访问（`Properties.Resources.SomeKey`），务必确保编译后该属性存在。

4. **在代码中使用新资源**：
   - UI：通过 `Properties.Resources.Label_*` 或绑定到 ViewModel。
   - 规则 / 导出错误文案：
     - 在 `FailReasonTextResolver` 的 `switch` 中增加对应 case，将错误 code 映射到新的 `Err_*` Key。
   - 导出列头：
     - 在 `ProductExportRegistry` 中为对应产品的 `ProductExportProfile` 增加 `ExportColumnDefinition`，使用新 `Export_*` Key。

5. **添加 / 更新单元测试**：
   - 对新错误码：在 `FailReasonTextResolverTests` / 相关测试中增加断言，确保 code → 文案映射正确。
   - 对导出列头：在 `ProductExportRegistryTests` 中验证列顺序与 `ExportFieldId` / `HeaderResourceKey` 一致。

---

## 五、注意事项与约束

- **不要在业务逻辑中硬编码中文字符串**：
  - 所有对用户可见的文本应来自 `Resources`，便于统一调整与未来多语言支持。

- **错误代码与文案分离**：
  - `TestRecord.FailReason` 始终存储错误代码，而非中文文案。
  - 任何展示（UI / 导出 / 日志）需要人类可读信息时，通过 `FailReasonTextResolver + Resources` 获取。

- **导出列头的兼容性**：
  - 对已有 Excel 消费方，优先保持列数与列顺序不变，仅在必要时新增或重命名字段；
  - 若必须调整列名 / 列顺序，应在文档中明确标注版本变化，并更新对应测试用例。

---

## 六、后续演进建议

- **多语言支持**：
  - 可以在未来为不同语言新增 `Resources.{culture}.resx`，保持 Key 不变，仅更换 `value`。
  - 由 `Thread.CurrentUICulture` 决定实际加载的资源文件，无需修改业务代码。

- **资源清理**：
  - 定期梳理不再使用的 `Label_*` / `Err_*` / `Export_*`，减少历史包袱；
  - 对于废弃 Key，可在文档中标记为“仅兼容旧版本，不再使用”，避免误用。

本指南适用于 Phase3 及之后阶段，后续若引入新的资源管理机制（如独立 JSON / 数据库配置），应在此文档基础上补充或更新约定。

