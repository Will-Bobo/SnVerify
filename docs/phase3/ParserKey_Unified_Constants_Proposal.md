# Parser Key 统一常量方案（评审结论更新版）

## 一、评审结论

**本方案 评审通过，建议实施。**

理由：

- 解决 Parser Key 魔法字符串分散问题  
- 提供 **单一事实源（Single Source of Truth）**  
- 保持 **现有接口与运行时行为完全不变**  
- 为 **即将扩展的 Parser（尤其是 Aggregate Parser）** 提供统一命名约定  

同时，为适应未来 Parser 扩展，在当前方案基础上加入 **结构化 Key 分类设计**，避免 Parser 数量增长后再次出现混乱。

---

## 二、问题确认

当前 Parser Key 以字符串分散在多处：

| 位置 | 示例 |
|------|------|
| ProductRegistry | `"Trim"` |
| ServiceFactory | `"Trim"` |
| Parser 注释 | `"Trim"` |

风险：

- 修改 Key 容易漏改  
- IDE 无法统一引用  
- 扩展 Parser 时缺乏统一规范  

尤其在 **即将引入聚合 Parser（Aggregate Parser）** 时，Key 数量将增长，若不统一管理，会再次产生魔法字符串问题。

---

## 三、方案（更新版）

### 3.1 新增 Parser Key 常量类

- **路径**：`Domain/DeviceAccess/Parsing/ParserKeys.cs`  
- **命名空间**：`SnVerify.Domain.DeviceAccess`

### 3.2 Key 分类结构（推荐）

考虑未来 Parser 扩展，采用分类结构：

```csharp
/// <summary>
/// Parser Key 常量定义。
///
/// 设计约束：
/// - Key 为 ParserFactory 的注册契约
/// - DeviceInfoCommand.ParserKey 必须引用此处常量
/// - 不允许在其它位置直接使用字符串
/// </summary>
public static class ParserKeys
{
    /// <summary>
    /// 单字段 Parser Key。
    /// </summary>
    public static class Field
    {
        /// <summary>
        /// 去除字符串首尾空白。
        /// </summary>
        public const string Trim = "Trim";
    }

    /// <summary>
    /// 聚合 Parser Key。
    /// </summary>
    public static class Aggregate
    {
        // 示例（未来扩展）
        // public const string Soltag = "Soltag";
    }
}
```

### 3.3 设计约束注释（必须保留）

为便于未来扩展时避免重新引入魔法字符串，**ParserKeys** 类上须保留以下摘要注释，作为设计约束对后续开发者的明确约定：

- Key 为 ParserFactory 的注册契约  
- DeviceInfoCommand.ParserKey / AggregateDeviceInfoCommand.ParserKey **必须**引用此处常量  
- **不允许**在 ProductRegistry、ServiceFactory 或其它位置直接使用 Parser Key 字符串  

实施时该注释应写入 `ParserKeys.cs` 中，与 3.2 节示例一致。

### 3.4 设计优势

| 优势 | 说明 |
|------|------|
| 单一事实源 | 所有 ParserKey 集中管理 |
| IDE 支持 | Rename / Find References 可追踪 |
| 结构清晰 | Field Parser 与 Aggregate Parser 分离 |
| 易扩展 | 新 Parser 只需新增常量 |
| 约束可见 | 类注释明确禁止魔法字符串，降低误用风险 |

---

## 四、使用方式调整

### 4.1 ProductRegistry

修改：

- `ParserKey = "Trim"`  
改为：  
- `ParserKey = ParserKeys.Field.Trim`

### 4.2 ServiceFactory

注册 Parser：

- `{ ParserKeys.Field.Trim, trimParser }`  
替代：  
- `{ "Trim", trimParser }`

### 4.3 Parser 注释（可选）

TrimParser 中注明：

- `// 注册 Key：ParserKeys.Field.Trim`

---

## 五、不修改的部分

保持以下接口与行为不变：

| 项 | 原因 |
|----|------|
| IParserFactory.Get(string key) | 与现有配置模型兼容 |
| DeviceInfoCommand.ParserKey | 配置驱动 |
| AggregateDeviceInfoCommand.ParserKey | 同上 |
| AdbDeviceService | 无需改动 |

因此：

- 不引入 Enum  
- 不改变 ParserFactory 设计  
- 不影响运行逻辑  

---

## 六、扩展约定（重要）

### 新增 Field Parser

1. 在 **ParserKeys.Field** 中新增 Key：`public const string Xxx = "Xxx";`  
2. ServiceFactory 注册时使用：`ParserKeys.Field.Xxx`  
3. ProductRegistry（或其它配置）中：`ParserKey = ParserKeys.Field.Xxx`

### 新增 Aggregate Parser

1. 在 **ParserKeys.Aggregate** 中新增 Key：`public const string Soltag = "Soltag";`  
2. ServiceFactory 注册时使用：`ParserKeys.Aggregate.Soltag`  
3. AggregateDeviceInfoCommand 使用：`ParserKey = ParserKeys.Aggregate.Soltag`

---

## 七、目录结构建议

推荐调整为：

```
Domain
 └─ DeviceAccess
     └─ Parsing
         └─ ParserKeys.cs
```

原因：ParserKeys 属于 **Parsing 子域契约**，与 DeviceAccess 核心模型（如 DeviceInfoCommand、DeviceAdbConfig）区分，便于后续在 Parsing 下扩展更多解析相关类型（如未来若有 IFieldParser / IAggregateParser 等命名或文件组织需求，可同目录扩展）。  
本次实施仅新增 **ParserKeys.cs**，不移动或重命名现有接口文件。

---

## 八、实施步骤（评审通过后执行）

1. **新增文件**：`Domain/DeviceAccess/Parsing/ParserKeys.cs`，实现上述 ParserKeys + Field + Aggregate 结构，定义 `ParserKeys.Field.Trim`。  
2. **修改 ProductRegistry**：两处 `"Trim"` → `ParserKeys.Field.Trim`。  
3. **修改 ServiceFactory**：`"Trim"` → `ParserKeys.Field.Trim`。  
4. **更新 Parser 注释**（可选）：TrimParser 中注明注册 Key 为 `ParserKeys.Field.Trim`。  
5. **主项目**：若 csproj 显式列出文件，将 `Parsing/ParserKeys.cs` 加入 Domain 项目编译项。  
6. **编译并运行现有单元测试**，确保通过。

---

## 九、兼容性与风险

| 项 | 影响 |
|----|------|
| 运行行为 | 无变化 |
| 接口 | 无变化 |
| 配置 | 无变化（仅赋值来源改为常量） |
| 测试 | 无变化 |

属于 **纯重构（Refactoring）**。

---

## 十、最终结论

本方案：

- 解决 Parser Key 魔法字符串问题  
- 提供统一契约管理  
- 为未来 Parser 扩展提供 **结构化 Key 管理**（Field / Aggregate）  
- 不改变现有系统行为  

**评审已通过，按第八节实施步骤执行。**
