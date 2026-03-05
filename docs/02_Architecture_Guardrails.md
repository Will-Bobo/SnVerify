一、系统核心目标

SnVerify 系统属于：

产线设备验证控制软件

设计原则：

原则	说明
稳定优先	避免复杂化架构
流程冻结	Phase3流程不允许动态变更
AI辅助开发	Cursor Agent 必须遵守规范
最小复杂度	禁止过度工程化
二、架构红线（最重要）
⭐ 红线1：禁止引入规则引擎

Phase3 不允许：

DSL规则语言

动态脚本执行

Runtime流程拼接

原因：

产线控制软件必须可预测执行
⭐ 红线2：Coordinator流程必须冻结

ProcessCoordinator 必须遵守：

Scan
↓
SN PASS检查
↓
ADB读取
↓
Device匹配
↓
Chip验证
↓
Version验证
↓
记录结果

禁止：

运行时插入流程节点

⭐ 红线3：ViewModel禁止业务判断

ViewModel 只能：

触发 Command

绑定 Snapshot

禁止：

校验逻辑

数据判断

协议解析

⭐ 红线4：Service层必须是唯一业务执行入口

业务逻辑必须：

Coordinator → Service → Domain

禁止：

UI → Domain
三、参数管理红线（非常关键）
⭐ 运行参数必须持久化

新增：

VerificationParameter Table

存储：

Project目标版本

⭐ 参数读取策略

必须：

Lazy Load
Memory Cache

禁止：

每次流程查询数据库
四、ADB访问红线
⭐ ADB访问必须封装

必须使用：

AdbAccessService

禁止：

Coordinator直接调用adb命令

⭐ ProjectProfile控制ADB命令

不同项目：

不同ADB读取规则
五、Snapshot体系红线

系统必须使用：

VerificationSnapshot（只读）

特点：

不可修改

可缓存

可绑定UI

DecisionTreeExecutionLock

控制流程执行状态。

六、SQLite数据层红线

StorageService 只能：

行为	允许
CRUD	✅
查询	✅
业务判断	❌

禁止：

StorageService内部写流程规则
七、Cursor Agent 开发红线（非常重要）

Cursor 修改代码必须遵守：

⭐ 允许修改

✅ Domain Model
✅ Storage SQL
✅ Service Implementation
✅ ADB封装

⭐ 禁止修改

❌ Coordinator流程顺序
❌ ViewModel业务逻辑
❌ Snapshot结构

八、未来扩展红线

Phase3 不允许：

功能	状态
规则引擎	禁止
MES深度耦合	延后
动态流程	禁止
九、系统复杂度控制目标（非常高级）

SnVerify Phase3 应保持：

控制流程 ≤ 8 步
核心 Service ≤ 6 个
Snapshot类型 ≤ 10 个
十、版本治理策略

以后所有设计调整：

必须遵循：

先写 Design Patch
再修改代码


架构设计红线（终极治理版）
一、分层架构约束

系统必须遵守：

层级	规则
Domain层	不得依赖UI / MES / 硬件
Service层	负责外部通信封装
ViewModel层	只负责状态绑定
二、业务规则实现原则

所有业务规则必须：

可单元测试

不可写入 UI 层

三、外部系统访问原则

外部系统：

系统	要求
ADB	Service封装
MES	Plugin Gate抽象
四、命名空间规范（非常重要）

禁止：

Phase2
Phase25
Stage1

必须：

Verification
Session
Storage
Validation
Rules