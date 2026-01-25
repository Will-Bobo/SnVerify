# 03_Dev_Rules_TDD_and_AI.md

## 一、TDD 原则

* 先写测试，再写实现
* 规则逻辑必须有测试覆盖

## 二、AI 协作规则

* AI 不允许新增未在文档中定义的功能
* AI 不允许重写架构分层
* 复杂逻辑必须拆解为可测试单元

## 三、提交纪律

* 一次提交只解决一个问题
* 提交信息必须描述“为什么”


## 四、AI 生成代码的可维护性规范（长期规则）

### 1. 注释规范（强烈建议）

- AI 生成的所有 public / internal 方法：
  - 必须包含 XML 注释（summary / param / return）
  - 注释需描述「语义目的」，而非代码表面行为
- 关键业务方法需说明：
  - 输入前提
  - 核心假设
  - 异常或边界行为

禁止：
- 空注释
- 与方法名重复的无意义注释

---

### 2. AI 生成文件标识规范（长期规则）

- 所有由 AI 新生成的代码文件：
  - 建议在文件头部注明作者信息
  - 统一使用如下格式：

```csharp
/// <author>AI Assistant</author>
/// <remarks>
/// This file is generated or initially scaffolded by AI.
/// Human review and refinement may follow.
/// </remarks>


---