SnVerify Phase3 终极稳定架构设计（工业级）

本文档目标：
构建 SnVerify 上位机产线控制系统的 防崩溃运行架构。

核心原则：

不引入复杂规则引擎

不引入动态脚本系统

不允许业务流程被运行时污染

保证 Cursor Agent 可安全开发

一、系统稳定性核心目标（最重要）

系统必须保证：

⭐ 流程不可逆

SN 校验流程：

Scan → Verify → Record → Report

禁止：

中途流程分叉

动态规则注入

流程顺序修改

⭐ 业务规则冻结

Phase3 只允许：

操作	允许
新增字段	✅
新增校验规则	✅
修改流程顺序	❌
引入规则引擎	❌
二、三层运行架构（核心）

系统采用：

Presentation Layer
↓
Service Coordination Layer
↓
Domain Logic Layer
↓
Storage Layer
⭐ View 层

职责：

UI展示

用户输入

Command触发

禁止：

业务判断逻辑

⭐ Service Layer（最关键）

包含：

ProcessCoordinator

AdbAccessService

StorageService

ParameterService

⭐ Domain Layer

包含：

Verification Models

Snapshot Objects

Result Objects

三、Runtime Parameter 防污染设计（核心创新点 ⭐⭐⭐⭐⭐）

这是最重要部分。

⭐ Parameter必须持久化

新增：

VerificationParameter Table

结构：

字段	说明
ProjectId	项目ID
ExpectedAndroidVersion	目标版本
ExpectedBoardVersion	目标板版本
ExpectedChargeBoardVersion	目标充电板版本
⭐ Parameter读取策略

必须：

Lazy Load
缓存到 Memory

禁止：

每次流程查询数据库

Parameter生命周期：

UI输入
↓
ParameterService保存
↓
Coordinator读取缓存
四、ProcessCoordinator 防崩溃设计（最核心）

Coordinator 必须满足：

⭐ 不允许直接访问 UI

必须：

Coordinator → Service → Domain
⭐ 不允许线程控制代码

禁止：

Dispatcher
Task.Run
Thread.Sleep

线程由 Service 层管理。

五、ADB读取隔离策略（非常重要）

ADB Service 必须：

⭐ 封装命令执行

新增：

ProjectProfile

包含：

ADB读取命令
字段映射规则

示例：

ProjectA:
read_sn_cmd = xxx

ProjectB:
read_sn_cmd = yyy

Coordinator 不知道 ADB 命令细节。

六、SQLite 防污染设计（非常高级 ⭐⭐⭐⭐）
StorageService 只能：

CRUD

查询

写入结果

禁止：

业务流程判断

SQL必须集中管理。

七、Snapshot体系（工业软件黄金设计）

新增只读对象：

VerificationSnapshot

包含：

当前流程状态

读取设备信息

校验结果

规则：

Snapshot 必须不可变
八、TDD开发强制要求

必须先写测试：

测试矩阵：

测试场景	要求
SN重复	FAIL
ChipID非法	FAIL
ADB失败	FAIL
版本错误	FAIL
Parameter未配置	禁止启动
九、Cursor Agent 安全执行边界（超级重要）

Cursor 代码修改必须遵守：

⭐ 不允许修改

❌ ViewModel流程控制
❌ UI线程逻辑
❌ Coordinator流程顺序

⭐ 允许修改

✅ Domain模型
✅ Service实现
✅ Storage SQL
✅ ADB封装

十、未来扩展安全路径（终极设计）

未来可以扩展：

MES Gate

设计为：

Plugin Interface

而不是硬编码。

规则引擎

禁止 Phase3 引入。