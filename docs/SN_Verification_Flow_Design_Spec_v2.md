SN_Verification_Flow_Design_Spec v2

SN 校验流程设计规范（Phase 3）

Version: v2.0
Status: Frozen for Phase 3 Development

1. 文档目的

本文档定义 SN 产线验证流程的业务规则与决策逻辑，用于指导：

ProcessCoordinator（Phase3 扩展入口：`ProcessScanAsync`）流程实现

单元测试（TDD）

产线失败原因记录

未来规则扩展

本文档 只定义业务规则与校验顺序。

系统模块架构请参见：

docs/SN_Verify_System_Architecture.md
2. 术语定义
术语	说明
StickerSN	扫码枪扫描得到的 SN（外部标签）
DeviceSN	设备内部读取的 SN（ADB读取）
ChipId	设备芯片ID（ADB读取）
WifiMac	设备WiFi MAC地址（ADB读取）
AndroidVersion	设备 Android 系统版本
BoardVersion	芯片板版本
ChargeBoardVersion	充电小板版本
PASS	本条测试成功
FAIL	本条测试失败
3. 产线测试触发

产线流程：

扫码枪扫描 SN
      ↓
ScanInputService 捕获
      ↓
ProcessCoordinator.ProcessScanAsync(sn, productCode)
      ↓
执行 SN 验证流程

触发条件：

扫码输入完成
4. ADB 设备信息读取

设备信息通过 ADB 读取。

读取内容：

字段	用途
DeviceSN	SN匹配校验
ChipId	唯一性校验
WifiMac	记录
AndroidVersion	版本校验
BoardVersion	版本校验
ChargeBoardVersion	版本校验

建议命令：

adb shell echo "SN=$(getprop xxx) CHIP=$(cat xxx) WIFI=$(cat xxx) ..."

理想情况：

一次ADB读取返回所有信息

如果设备限制：

允许多次ADB读取

实现必须：

- ProcessCoordinator / RulePipelineExecutor 不依赖读取次数（聚合命令或分字段读取均可）
- ADB 读取失败需按策略重试（见第 14 节）
5. 校验规则总览（Decision Table）
Step	校验项	规则	失败结果
1	参数检查（Parameter）	parameter != null	PARAMETER_NOT_CONFIGURED
2	读取产品Profile（ProductRegistry）	ProductProfile 存在	PRODUCT_PROFILE_NOT_FOUND
3	ADB读取	ADB读取设备信息成功	ADB_READ_FAIL
4	SN匹配	StickerSN == DeviceSN	SN_NOT_MATCH
5	SN历史PASS（Order维度）	SN在本订单已有PASS	SN_DUPLICATE
6	ChipID格式	ChipID 以 F50 开头	CHIPID_INVALID
7	ChipID唯一（Order维度）	本订单未出现该ChipID	CHIPID_DUPLICATE
8	三版本强校验	Android/Board/ChargeBoard 版本匹配	ANDROID_VERSION_MISMATCH / BOARD_VERSION_MISMATCH / CHARGE_BOARD_VERSION_MISMATCH
9	写入记录	记录测试数据（由 Coordinator 落库）	PASS

执行规则：

按顺序执行
失败立即停止
6. SN 唯一性规则

SN 唯一范围：

同一订单（OrderId：业务订单名 OrderName）

规则：

StickerSN 在同一订单 PASS 后
禁止再次测

SQL示例：

说明：

- 本文档中的 `OrderId` 指 **业务订单号（即 UI 输入框中的订单号字符串）**，在数据库中对应 `Order.OrderName`。
- 若未来流程层改为传递数据库内部主键 `Order.Id`（int），则可直接以 `TestSession.OrderId = @orderIdInt` 过滤，无需再 JOIN `Order` 表。

SELECT COUNT(1)
FROM TestRecord r
JOIN TestSession s ON r.SessionId = s.Id
JOIN "Order" o ON s.OrderId = o.Id
WHERE o.OrderName = @OrderId
AND r.StickerSN = @StickerSN
AND r.Result = 'PASS'
7. ChipID 校验规则

ChipID 必须满足：

F50xxxxxx

校验方式：

ChipID.StartsWith("F50")

失败结果：

CHIPID_INVALID
8. ChipID 唯一规则

唯一范围：

同一订单

SQL示例：

说明：

- 本文档中的 `OrderId` 指 **业务订单号（OrderName）**，而非数据库内部 `Order.Id`。
- 当前实现使用 `OrderName` 的原因：上层流程天然持有订单号字符串；若改为传内部 Id，需要额外做一次 `OrderName → Order.Id` 映射查询或调整接口参数。

SELECT COUNT(1)
FROM TestRecord r
JOIN TestSession s ON r.SessionId = s.Id
JOIN "Order" o ON s.OrderId = o.Id
WHERE o.OrderName = @OrderId
AND r.ChipId = @ChipId
AND r.Result = 'PASS'
9. 版本校验规则

版本校验采用：

严格字符串匹配

规则：

ActualVersion == ExpectedVersion

涉及版本：

版本	来源
AndroidVersion	ADB读取
BoardVersion	ADB读取
ChargeBoardVersion	ADB读取

目标版本来源：

Project 配置

版本目标值录入规则：

AndroidVersion / BoardVersion / ChargeBoardVersion
均采用 Expected / Actual 校验模型。

在开始验证流程前，必须完成 Parameter 配置读取（`parameter != null`）。

说明（Phase3 实现）：

- Parameter 对象必须存在，否则流程 FailFast：`PARAMETER_NOT_CONFIGURED`。
- 对于 Expected 字段：若某一项为空，则该项版本校验跳过；非空项执行严格字符串匹配并返回对应 FailReason。

10. WiFi MAC 规则

WifiMac 处理方式：

只记录
不参与校验

记录目的：

设备追溯
11. 失败处理规则

任何校验失败：

立即停止流程

并记录：

TestRecord(Result=FAIL)
FailureReason

失败原因枚举：

Code	说明
PARAMETER_NOT_CONFIGURED	项目参数未配置（Expected* 缺失）
PRODUCT_PROFILE_NOT_FOUND	未找到产品 Profile（ProductRegistry 无对应 ProductCode）
SN_DUPLICATE	订单维度内已存在该 StickerSN 的 PASS 记录
ADB_READ_FAIL	ADB读取失败
SN_NOT_MATCH	设备SN与标签不一致
CHIPID_INVALID	ChipID格式错误
CHIPID_DUPLICATE	ChipID重复
ANDROID_VERSION_MISMATCH	Android版本错误
BOARD_VERSION_MISMATCH	板卡版本错误
CHARGE_BOARD_VERSION_MISMATCH	充电板版本错误
12. PASS 记录规则

所有校验成功：

写入 PASS 记录

记录字段：

字段	来源
StickerSN	扫码
DeviceSN	ADB
ChipID	ADB
WifiMac	ADB
AndroidVersion	ADB
BoardVersion	ADB
ChargeBoardVersion	ADB
Result	流程结果（PASS）
FailReason	为空（PASS 时）
13. ProcessCoordinator 流程图
扫码SN
   │
   ▼
Parameter存在?
   │
   ├─NO → FAIL(PARAMETER_NOT_CONFIGURED)
   │
   ▼
读取ProductProfile（ProductRegistry）
   │
   ├─NOT FOUND → FAIL(PRODUCT_PROFILE_NOT_FOUND)
   │
   ▼
ADB读取设备信息（RulePipelineExecutor 内执行）
   │
   ├─FAIL → FAIL(ADB_READ_FAIL)
   │
   ▼
SN匹配?
   │
   ├─NO → FAIL(SN_NOT_MATCH)
   │
   ▼
SN已PASS?(Order维度)
   │
   ├─YES → FAIL(SN_DUPLICATE)
   │
   ▼
ChipID合法?
   │
   ├─NO → FAIL(CHIPID_INVALID)
   │
   ▼
ChipID重复?
   │
   ├─YES → FAIL(CHIPID_DUPLICATE)
   │
   ▼
Android版本匹配?
   │
   ├─NO → FAIL(ANDROID_VERSION_MISMATCH)
   │
   ▼
Board版本匹配?
   │
   ├─NO → FAIL(BOARD_VERSION_MISMATCH)
   │
   ▼
ChargeBoard版本匹配?
   │
   ├─NO → FAIL(CHARGE_BOARD_VERSION_MISMATCH)
   │
   ▼
PASS
14. ADB 读取失败策略

ADB读取失败时：

自动重试

默认：

Retry = 3

如果仍失败：

FAIL
提示操作员重新扫码
15. 记录字段（TestRecord）

新增字段：

字段	说明
ChipId	芯片ID
WifiMac	WiFi MAC
AndroidVersion	Android版本
BoardVersion	芯片板版本
ChargeBoardVersion	充电板版本
16. Phase 3 扩展点（预留）

未来可能扩展：

MES Gate
多项目规则
规则配置化
规则引擎

当前 Phase3 规则：

代码内固定实现
17. TDD 测试矩阵

必须覆盖测试：

测试	说明
Parameter 未配置	必须失败（PARAMETER_NOT_CONFIGURED）
ProductProfile 不存在	必须失败（PRODUCT_PROFILE_NOT_FOUND）
SN 已PASS（Order维度）	必须失败（SN_DUPLICATE）
ADB读取失败	必须失败
SN不匹配	必须失败
ChipID非法	必须失败
ChipID重复	必须失败
Android版本错误	必须失败
Board版本错误	必须失败
ChargeBoard版本错误	必须失败
全部正确	PASS
18. 文档版本
版本	说明
v1	初始SN验证流程
v2	新增 ChipID / WifiMac / 多版本校验