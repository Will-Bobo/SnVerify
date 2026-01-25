# 单元测试失败问题分析与解决方案

## 问题概览

共发现 6 个测试失败，涉及以下方面：
1. CancellationToken 处理
2. 异常类型包装（AggregateException）
3. 异常类型匹配（DirectoryNotFoundException vs Exception）
4. 路径格式验证
5. 快照状态更新逻辑

---

## 问题 1: ReadDeviceSnAsync_ShouldRespectCancellationToken

### 错误信息
```
Expected: <System.OperationCanceledException>
But was: null
```

### 原因分析
查看 `AdbAccessService.ReadDeviceSnAsync` 实现（第 66-167 行）：
- 第 145-148 行：当捕获到 `OperationCanceledException` 且不是超时导致的，会重新抛出
- 第 162-165 行：当 `cancellationToken.IsCancellationRequested` 为 true 时，会重新抛出
- **问题**：测试中 mock 的 `RunAsync` 抛出 `OperationCanceledException`，但实际代码在第 141-144 行捕获了超时相关的取消异常，并返回了 `AdbSnReadResult.Failure`，而不是抛出异常

### 解决方案
**方案 A（推荐）**：修改测试，验证返回结果的 `IsTimeout` 或 `ErrorReason`，而不是期望抛出异常
- 因为 `ReadDeviceSnAsync` 的设计是返回结果对象，而不是抛出异常

**方案 B**：修改实现代码，当 `cancellationToken` 被取消时（非超时），应该抛出 `OperationCanceledException` 而不是返回失败结果

---

## 问题 2: CreateBatch_ShouldHandleStorageServiceException

### 错误信息
```
Expected: <System.Exception>
But was: <System.AggregateException: 发生一个或多个错误。 ---> System.Exception: Database error
```

### 原因分析
查看 `BatchManager.CreateBatch` 实现（第 95-112 行）：
- 第 97 行：`createTask.Wait()` 同步等待异步任务
- **问题**：当异步任务抛出异常时，`.Wait()` 会将异常包装成 `AggregateException`

### 解决方案
**方案 A（推荐）**：修改测试，期望捕获 `AggregateException`，并验证内部异常
```csharp
var caughtException = Assert.Throws<AggregateException>(() => 
    _batchManager.CreateBatch(TestBatchId));
Assert.That(caughtException.InnerException, Is.InstanceOf<Exception>());
Assert.That(caughtException.InnerException.Message, Is.EqualTo("Database error"));
```

**方案 B**：修改实现代码，使用 `GetAwaiter().GetResult()` 或 `ConfigureAwait(false).GetAwaiter().GetResult()`，这样会抛出原始异常而不是 `AggregateException`（但这不是最佳实践）

**方案 C（最佳）**：将 `CreateBatch` 改为异步方法 `CreateBatchAsync`，避免同步等待异步操作

---

## 问题 3: ExportBatchResultAsync_ShouldUpdateSnapshot_WhenError

### 错误信息
```
Expected: <System.Exception>
But was: <System.IO.DirectoryNotFoundException: 未能找到路径"Z:InvalidPath"的一部分。
```

### 原因分析
查看测试代码（第 266-277 行）：
- 第 272 行：期望抛出 `Exception` 类型
- **问题**：实际抛出的是 `DirectoryNotFoundException`（继承自 `Exception`），但 NUnit 的 `Assert.ThrowsAsync<T>` 要求精确类型匹配

### 解决方案
**方案 A（推荐）**：修改测试，使用 `Is.InstanceOf<Exception>()` 或直接期望 `DirectoryNotFoundException`
```csharp
var caughtException = Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
    await _storageService.ExportBatchResultAsync(TestBatchId, invalidDirectory));
```

**方案 B**：使用 `Assert.CatchAsync<Exception>` 代替 `Assert.ThrowsAsync<Exception>`（`CatchAsync` 允许派生类型）

---

## 问题 4: SaveVerifyResultAsync_ShouldUpdateSnapshot_WhenDatabaseError

### 错误信息
```
System.NotSupportedException : 不支持给定路径的格式。
```

### 原因分析
查看测试代码（第 159-178 行）：
- 第 162 行：使用 `"invalid:path"` 作为数据库路径
- **问题**：`"invalid:path"` 不是有效的文件系统路径格式，SQLite 在尝试打开连接时抛出 `NotSupportedException`，而不是预期的数据库操作异常

### 解决方案
**方案 A（推荐）**：使用一个格式正确但无法访问的路径（如不存在的驱动器或受保护的目录）
```csharp
var invalidService = new StorageService(@"Z:\NonExistentDrive\test.db");
// 或者使用一个无效的长路径
var invalidService = new StorageService(new string('A', 300) + ".db");
```

**方案 B**：修改测试，期望捕获 `NotSupportedException` 或 `SQLiteException`，而不是通用的 `Exception`

**方案 C**：在 `StorageService` 构造函数中验证路径格式，提前抛出 `ArgumentException`

---

## 问题 5: Reset_ShouldUpdateSnapshotToIdle

### 错误信息
```
Assert.That(snapshot.IsProcessing, Is.False)
Expected: False
But was: True
```

### 原因分析
查看测试代码（第 177-206 行）和 `VerificationFlowService.Reset` 实现：
- `VerificationFlowService.Reset`（第 53-57 行）只是委托给 `_processCoordinator.Reset()`
- `VerificationFlowService.Snapshot` 属性（第 23 行）直接返回 `_processCoordinator.Snapshot`
- 测试第 184-186 行：使用 `SetupSequence` 设置快照序列，第一个返回 `processingSnapshot`，第二个返回 `idleSnapshot`
- **问题**：`SetupSequence` 会在每次访问 `Snapshot` 属性时按顺序返回值。但测试中可能：
  1. 在 `SetUp` 中已经访问了一次（第 32 行返回 `Idle()`）
  2. 在 `Reset()` 调用前可能访问了 `Snapshot`
  3. 导致 `SetupSequence` 的顺序错乱

### 解决方案
**方案 A（推荐）**：修改测试，在 `Reset()` 调用前不访问 `Snapshot`，或者使用 `Setup` 代替 `SetupSequence`，在 `Reset()` 的回调中更新返回值：
```csharp
_processCoordinatorMock
    .Setup(x => x.Snapshot)
    .Returns(processingSnapshot); // 初始值

_processCoordinatorMock
    .Setup(x => x.Reset())
    .Callback(() =>
    {
        // 在回调中更新 Snapshot 返回值
        _processCoordinatorMock
            .Setup(x => x.Snapshot)
            .Returns(idleSnapshot);
        
        _processCoordinatorMock.Raise(
            x => x.SnapshotChanged += null,
            this,
            idleSnapshot);
    });
```

**方案 B**：确保 `SetupSequence` 的顺序正确，考虑 `SetUp` 中已经访问了一次：
```csharp
_processCoordinatorMock
    .SetupSequence(x => x.Snapshot)
    .Returns(VerificationSnapshot.Idle())  // SetUp 中的访问
    .Returns(processingSnapshot)            // Reset 前的访问（如果有）
    .Returns(idleSnapshot);                // Reset 后的访问
```

---

## 问题 6: StartVerificationAsync_ShouldUpdateSnapshot_WhenCoordinatorUpdates

### 错误信息
```
Assert.That(finalSnapshot.LastResult, Is.EqualTo("PASS"))
Expected: "PASS"
But was: null
```

### 原因分析
查看测试代码（第 81-116 行）：
- 第 88-91 行：使用 `SetupSequence` 设置快照序列，返回 3 个值：`Idle()`、`processingSnapshot`、`completedSnapshot`
- 第 94-107 行：mock `StartVerificationAsync` 方法，手动触发 `SnapshotChanged` 事件
- **问题**：`SetupSequence` 会在每次访问 `Snapshot` 时按顺序返回值。但：
  1. `SetUp` 中已经访问了一次（第 32 行），返回 `Idle()`
  2. 在 `StartVerificationAsync` 执行过程中可能访问了 `Snapshot`
  3. 最后断言时访问 `Snapshot`，但 `SetupSequence` 已经用完了所有返回值，返回了默认值（null 或最后一个值）

### 解决方案
**方案 A（推荐）**：修改测试，在 `StartVerificationAsync` 的回调中动态更新 `Snapshot` 返回值：
```csharp
_processCoordinatorMock
    .Setup(x => x.StartVerificationAsync(TestSn))
    .Returns(async () =>
    {
        // 更新 Snapshot 返回值为 processingSnapshot
        _processCoordinatorMock
            .Setup(x => x.Snapshot)
            .Returns(processingSnapshot);
        _processCoordinatorMock.Raise(
            x => x.SnapshotChanged += null,
            this,
            processingSnapshot);
        await Task.Delay(10);
        
        // 更新 Snapshot 返回值为 completedSnapshot
        _processCoordinatorMock
            .Setup(x => x.Snapshot)
            .Returns(completedSnapshot);
        _processCoordinatorMock.Raise(
            x => x.SnapshotChanged += null,
            this,
            completedSnapshot);
    });
```

**方案 B**：增加 `SetupSequence` 的返回值数量，考虑所有可能的访问：
```csharp
_processCoordinatorMock
    .SetupSequence(x => x.Snapshot)
    .Returns(VerificationSnapshot.Idle())  // SetUp 中的访问
    .Returns(VerificationSnapshot.Idle())   // StartVerificationAsync 开始前（如果有）
    .Returns(processingSnapshot)            // 处理中
    .Returns(completedSnapshot)             // 完成后
    .Returns(completedSnapshot);            // 断言时访问
```

---

## 总结与建议

### 优先级排序
1. **高优先级**：问题 2（AggregateException）、问题 5（Reset）、问题 6（StartVerificationAsync）- 这些涉及核心功能
2. **中优先级**：问题 1（CancellationToken）- 涉及取消机制的正确性
3. **低优先级**：问题 3、4（异常类型匹配）- 主要是测试断言的问题

### 修复策略
1. **问题 1**：建议修改测试，验证返回结果而不是异常
2. **问题 2**：建议修改测试，期望 `AggregateException` 并验证内部异常
3. **问题 3**：建议修改测试，使用 `Assert.CatchAsync<Exception>` 或期望具体异常类型
4. **问题 4**：建议修改测试，使用有效的路径格式但无法访问的路径
5. **问题 5、6**：需要检查 mock 设置，确保 `Snapshot` 属性在事件触发后返回正确的值

### 注意事项
- 所有修改都应该保持测试的意图不变
- 如果修改实现代码，需要确保不影响其他测试和实际功能
- 建议先修复测试代码，如果测试意图有问题，再考虑修改实现代码
