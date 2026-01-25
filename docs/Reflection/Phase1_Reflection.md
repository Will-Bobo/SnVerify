Reflection: SnVerify Phase 1 人类与 AI 协作复盘
【A. 已验证有效的协作模式】

模块化 Prompt 驱动开发

每个核心模块（StorageService、ADB、ScanInputService、ProcessCoordinator、VerificationFlowService）均有单独、完整、可执行的 Cursor Prompt。

Prompt 明确职责、接口、测试优先要求，避免 Cursor Agent 生成越界或不符合架构的代码。

提升效率：模块可并行生成，易于审核。

TDD 流程全程引导

单元测试先行，先验证逻辑和接口，再生成实现。

Cursor Agent 按测试驱动生成代码，减少返工。

确保逻辑闭环可控，原子流程、异常路径可验证。

架构规则冻结 + 文档化

MVVM 分层约束、Service 层职责、状态 vs 事件规则、UI 线程封送规则。

所有模块遵循同一规范，Cursor 可以长期读取并遵守。

提升协作稳定性和一致性。

最小闭环阶段交付

Phase 1 明确目标：逻辑闭环 + 接口 + 测试覆盖。

模拟 Mock 测试代替真实设备，保证开发节奏。

提升早期迭代效率，降低硬件依赖风险。

文档包整理

Prompt、任务清单、架构规则、需求假设、测试状态统一整理。

为团队交接和 Phase 2 准备提供清晰参考。

【B. 人类的稳定偏好】

分阶段开发与交付

小模块、可测、可交付 → 再扩展到下一阶段。

优先完成最核心、风险最高的模块（Storage、ADB），降低后续问题传播。

明确责任边界

Cursor 生成代码只做 Service 层逻辑。

人审核者只关注关键设计、流程与测试。

避免职责重叠，协作清晰。

Prompt 与文档结构化

长期可用、易复用的 Markdown / Canvas 格式。

每个模块都包含接口、测试要求、架构约束、输出要求。

渐进式引入复杂性

Phase 1 不引入 Prism 等复杂框架，只理解思想。

Phase 2 才考虑 UI、产线、日志落地等复杂场景。

【C. 可改进或需避免的协作方式】

过早设计复杂框架

如果在 Phase 1 就引入 Prism、插件化或复杂日志，会增加 Cursor Agent 生成复杂度，降低可控性。

混合逻辑和 UI

初期若在 Service 中处理 UI 或事件，将破坏原子性和 TDD 流程。

缺乏文档同步

Prompt、架构规则、任务清单未及时整理，容易造成 Cursor 行为不一致或返工。

【长期记忆候选】
值得长期保留

模块化、Prompt 驱动协作：每个模块独立 Prompt + TDD 测试，Cursor 可复用。

架构规则冻结与文档化：MVVM、Service 层、状态 vs 事件、线程约束。

阶段性交付最小闭环：先保证逻辑正确性，再扩展复杂场景。

职责边界清晰：Cursor 仅生成 Service，人工审核关键流程。

仅适用于当前项目或阶段

Mock 测试代替真实硬件验证（Phase 1 特定）

Phase 1 不引入 Prism 或完整日志框架（小型上位机项目特定）

扫码枪、ADB 单设备场景（当前业务特定）