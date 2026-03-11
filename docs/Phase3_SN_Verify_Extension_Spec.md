Phase3 · SN校验扩展技术需求文档（冻结版）

版本：v1.0
阶段：Phase 3
目标：扩展现有 SN 校验系统，支持 ChipId / WifiMac / 版本信息校验
开发方式：Cursor Agent 驱动开发

一、Phase3 开发目标

在现有 SN 校验流程基础上，增加新的检测规则：

新增检测内容：

WifiMac
ChipId
BoardVersion
ChargeBoardVersion
AndroidVersion

新增校验规则：

1 SN匹配
2 SN唯一（Order内）
3 ChipId格式校验
4 ChipId唯一（Order内）
5 Version校验

WifiMac：

只记录，不参与规则

ChipId规则：

1 不能为空
2 必须F50开头

二、核心概念定义（冻结）
1 Order（生产批次）
Order = 一个生产批次

示例：

Order: MIFI_20260305_001
数量: 2000

SN唯一范围：

Order
2 TestSession（测试轮次）
Session = Order下的一轮测试

示例：

Session1 初测
Session2 复测
Session3 补测

关系：

Order
 ├ Session1
 ├ Session2
 └ Session3
3 TestRecord（设备测试记录）
TestRecord = 一台设备一次测试

示例：

SN001 FAIL (Session1)
SN001 PASS (Session2)

唯一判断：

只看 PASS
三、系统架构

系统架构如下：

UI (WPF)
   │
   ▼
ProcessCoordinator
   │
   ▼
Verify Pipeline
   │
   ├ SN Match Verify
   ├ SN Unique Verify
   ├ ChipId Format Verify
   ├ ChipId Unique Verify
   ├ Version Verify
   └ Save Record
   │
   ▼
Service Layer
   ├ AdbService
   └ StorageService

说明：

ProcessCoordinator 负责流程调度
Verify Pipeline 执行规则
四、数据库结构（冻结）
TestRecord 表

最终结构：

TestRecord
---------------------------------
Id

OrderId
SessionId

StickerSN
DeviceSN

WifiMac
ChipId

BoardVersion
ChargeBoardVersion

ExpectedVersion
ActualVersion

Result
FailReason

VerifyTime

说明：

ExpectedVersion = AndroidVersion 目标
ActualVersion   = AndroidVersion 实际读取
五、数据库索引

新增索引：

idx_order_sn
(OrderId, StickerSN)
idx_order_chip
(OrderId, ChipId)

用于：

SN唯一查询
ChipId唯一查询
六、ADB读取设备信息

新增统一设备信息结构：

DeviceInfo

结构：

DeviceSn
WifiMac
ChipId
BoardVersion
ChargeBoardVersion
AndroidVersion

ADB接口：

ReadDeviceInfoAsync()

返回：

DeviceInfo

说明：

优先一次ADB读取全部信息
如果无法实现，可分多次读取

六点五 Project 驱动 ADB 读取
不同项目可能使用不同的ADB读取命令。

因此 ADB读取规则由 Project 决定。

系统在开始 Session 时确定 ProjectId，
并通过 ProjectProfile 获取对应ADB读取命令。

Phase3 实现方式：

ProjectProfileFactory 根据 ProjectId 返回对应配置。

示例：

Project A
  read sn command

Project B
  read chip command

说明：

Phase3 可先写死在代码中
未来可改为 JSON 配置

七、验证流程

最终验证流程：

扫码 StickerSN
      │
      ▼
ADB读取设备信息
(DeviceSN / WifiMac / ChipId / Version)
      │
      ▼
SN匹配
StickerSN == DeviceSN ?
      │
      ├ 否 → FAIL
      │
      ▼
SN唯一校验 (Order)
      │
      ├ 已存在 PASS → FAIL
      │
      ▼
ChipId格式校验
ChipId.StartsWith("F50")
      │
      ├ 否 → FAIL
      │
      ▼
ChipId唯一校验 (Order)
      │
      ├ 已存在 PASS → FAIL
      │
      ▼
Version校验
      │
      ├ 不一致 → FAIL
      │
      ▼
保存 TestRecord
      │
      ▼
PASS
八、重复测试规则

SN唯一判断必须在 Order 范围内。

SQL实现必须使用：

TestRecord
JOIN TestSession
JOIN Order

查询条件：

OrderId + StickerSN + Result='PASS'

如果 SN 在当前 Order 已 PASS：

系统：

阻止再次测试

提示：

SN 已在本批次 PASS
九、FailReason 规范

统一错误码：

SN_MISMATCH
SN_DUPLICATE
CHIPID_INVALID
CHIPID_DUPLICATE
VERSION_MISMATCH
ADB_READ_FAIL
CHIPID_EMPTY

作用：

方便产线统计
方便MES对接
十、ProcessCoordinator 执行流程

核心方法：

ProcessScanAsync(stickerSn, projectId)

入口链路：

ScanInput
 → MainViewModel.HandleScanInputAsync
 → IVerificationFlowService.StartPhase3VerificationAsync(sn, projectId)
 → ProcessCoordinator.ProcessScanAsync(sn, projectId)

流程（Frozen Pipeline）：

1 Parameter 非空检查
2 ADB 读取 DeviceInfo
3 SN 匹配（StickerSN == DeviceSN）
4 SN 唯一（订单维度，PASS 历史）
5 ChipId 格式校验（F50 开头）
6 ChipId 唯一（订单维度，PASS 历史）
7 Version 校验（三版本强校验服务）
8 保存记录（含 WifiMac / ChipId / 多版本字段）
十一、Cursor Agent 实现步骤

Cursor需要执行以下修改：

Step1 数据库迁移

修改：

TestRecord

新增字段：

OrderId
WifiMac
ChipId
BoardVersion
ChargeBoardVersion

新增索引：

idx_order_sn
idx_order_chip
Step2 新增 DeviceInfo 模型

新增文件：

Models/DeviceInfo.cs

字段：

DeviceSn
WifiMac
ChipId
BoardVersion
ChargeBoardVersion
AndroidVersion
Step3 修改 AdbService

新增方法：

ReadDeviceInfoAsync()

返回：

DeviceInfo
Step4 修改 StorageService

新增方法：

IsStickerSnPassInOrderAsync(orderId, sn)
IsChipIdPassInOrderAsync(orderId, chipId)

SQL 查询使用：

OrderId + Result='PASS'
Step5 修改 ProcessCoordinator

修改：

ProcessScanAsync

新增逻辑：

ChipId格式校验
ChipId唯一校验
Version校验
Step6 保存 TestRecord

保存字段：

StickerSN
DeviceSN
WifiMac
ChipId
BoardVersion
ChargeBoardVersion
ExpectedVersion
ActualVersion
十二、Phase3 不包含的功能

以下功能不在 Phase3 范围：

MES接口
项目规则引擎
动态规则配置

原因：

优先保证产线版本快速交付
十三、开发原则

Cursor 开发必须遵循：

1 不改变数据库核心结构
2 不修改验证流程顺序
3 不改变 SN 唯一规则
4 所有新增字段必须记录
十四、Phase3 验证规则（最终）
1 SN匹配
2 SN唯一 (Order)
3 ChipId格式校验 (F50)
4 ChipId唯一 (Order)
5 Version校验
6 保存记录

WifiMac：

只记录
十五、开发完成后的验证点

必须测试以下场景：

SN匹配成功
SN匹配失败
SN重复
ChipId格式错误
ChipId重复
Version错误
ADB读取失败
Phase3 文档状态
状态：Frozen
版本：v1.0

开发必须按此文档实现。