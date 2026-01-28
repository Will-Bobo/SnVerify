# 02_Architecture_Guardrails.md

## 架构设计红线（必须遵守）

1. Domain 层不得依赖 WPF、UI、硬件、MES
2. 所有业务规则必须可单元测试
3. 不允许在 UI 层直接写业务逻辑
4. 外部系统（ADB / MES）必须通过 Service 抽象
5. 不允许为了“方便”跳过文档约束
6. **禁止以实施阶段命名命名空间或代码文件夹**：不得使用 Phase25、Phase2 等阶段名作为 C# 命名空间或项目内文件夹名；应使用领域/功能名（如 Validation、Session、Rules）。

---