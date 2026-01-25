# Phase2_Auto_Execution_Script_Template.md

> 目标：用于自动化执行 Phase 2 所有模块的 Cursor Agent 脚本模板，遵循折中方案
>
> * 核心流程模块顺序执行
> * 独立模块可并行执行
> * 阶段性记录单元测试状态，供人工 Review
> * 严格遵循 TDD 流程（先生成单元测试 → 验证通过 → 再生成实现代码）

---

## 1. 脚本总体结构

```text
Phase2_AutoExecution()
  1. 核心流程模块顺序执行
      a. ScanInputService
      b. AdbAccessService
      c. StorageService
      d. BatchManager
      e. ProcessCoordinator
      f. VerificationFlowService
      -> 每个模块完成单元测试后人工确认
  2. 独立模块并行生成
      a. LoggingService
      b. MESInterface
      c. UI / View
      d. AutoCheckButton
      -> 并行完成后人工快速确认接口、Snapshot、异常处理
  3. 集成测试阶段
      -> 核心流程 + UI + Logging + MES + AutoCheckButton
      -> 确认闭环可复现
```

---

## 2. 模块执行流程模板（以 ScanInputService 为例）

```text
ModuleExecution('ScanInputService'):
  1. 读取 Prompt 文件: docs/ai_prompts/Phase2/ScanInputService_Cursor_Prompt.md
  2. Cursor Agent 生成 TDD 单元测试
  3. 执行 Mock 单元测试
      - 如果通过 → 生成实现代码
      - 如果失败 → 停止并人工 Review
  4. Snapshot / 状态验证
  5. 记录模块完成状态
```

* 所有核心流程模块按此顺序执行
* 每个模块完成后人工确认 Snapshot 和单元测试结果

---

## 3. 并行模块执行模板（LoggingService / MESInterface / UI / AutoCheckButton）

```text
ParallelModules = ['LoggingService','MESInterface','UI','AutoCheckButton']
for Module in ParallelModules in parallel:
  ModuleExecution(Module)
  RecordModuleStatus(Module)
end parallel
```

* 并行模块完成后进行人工快速 Review，确认接口、异常处理、Snapshot 绑定正确

---

## 4. 阶段性记录与人工 Review

* 每个模块执行完毕后，生成一份执行状态记录:

```
ModuleName | Status | UnitTestPass | SnapshotValid | ReviewRequired
```

* 核心流程模块失败必须人工 Review，才能继续下一模块
* 并行模块失败人工 Review可决定是否重生成

---

## 5. 集成测试模板

```text
IntegratedTestPhase():
  1. 模拟完整流程：ScanInput → ADB → Storage → ProcessCoordinator → VerificationFlow → UI / Logging / MES / AutoCheckButton
  2. 测试异常路径：
       - 重复 SN
       - ADB/Storage 异常
       - 批次异常
       - 超时
  3. 确认 Snapshot、批次、日志、UI 显示一致
  4. 记录集成测试结果供人工 Review
```

---

## 6. 使用说明

1. 将该脚本作为 Cursor Agent 调用模板
2. 确保所有 Phase 2 模块完整 Prompt 已就位
3. 按顺序执行核心流程模块，阶段性人工 Review
4. 并行执行独立模块，完成后快速人工确认
5. 集成测试确保闭环可复现
6. 完成 Phase 2 自动化执行
