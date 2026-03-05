SN_Verification_Flow_Design_Spec v2

SN 校验流程设计规范（Phase 3）

Version: v2.0
Status: Frozen for Phase 3 Development

1. 文档目的

本文档定义 SN 产线验证流程的业务规则与决策逻辑，用于指导：

ProcessCoordinator 流程实现

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
ProcessCoordinator.StartVerification(sn)
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

ProcessCoordinator 不依赖读取次数
5. 校验规则总览（Decision Table）
Step	校验项	规则	失败结果
1	SN历史PASS	SN在本订单已有PASS	SN_ALREADY_PASS
2	ADB读取	ADB读取设备信息成功	ADB_READ_FAIL
3	SN匹配	StickerSN == DeviceSN	SN_NOT_MATCH
4	ChipID格式	ChipID 以 F50 开头	CHIPID_INVALID
5	ChipID唯一	本订单未出现该ChipID	CHIPID_DUPLICATE
6	Android版本	设备版本 == 目标版本	ANDROID_VERSION_MISMATCH
7	Board版本	设备版本 == 目标版本	BOARD_VERSION_MISMATCH
8	ChargeBoard版本	设备版本 == 目标版本	CHARGE_BOARD_VERSION_MISMATCH
9	写入记录	记录测试数据	PASS

执行规则：

按顺序执行
失败立即停止
6. SN 唯一性规则

SN 唯一范围：

同一订单 (OrderId)

规则：

StickerSN 在同一订单 PASS 后
禁止再次测试

SQL示例：

SELECT COUNT(1)
FROM TestRecord r
JOIN TestSession s ON r.SessionId = s.Id
WHERE s.OrderId = @OrderId
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

SELECT COUNT(1)
FROM TestRecord r
JOIN TestSession s ON r.SessionId = s.Id
WHERE s.OrderId = @OrderId
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

在开始验证流程前，必须完成目标版本录入。

若任一目标版本为空：

系统必须提示：

请先录入目标版本参数。

禁止启动校验流程。

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
SN_ALREADY_PASS	SN 已测试成功
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
13. ProcessCoordinator 流程图
扫码SN
   │
   ▼
SN已PASS?
   │
   ├─YES → FAIL(SN_ALREADY_PASS)
   │
   ▼
ADB读取设备信息
   │
   ├─FAIL → FAIL(ADB_READ_FAIL)
   │
   ▼
SN匹配?
   │
   ├─NO → FAIL(SN_NOT_MATCH)
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
   ├─NO → FAIL
   │
   ▼
Board版本匹配?
   │
   ├─NO → FAIL
   │
   ▼
ChargeBoard版本匹配?
   │
   ├─NO → FAIL
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
SN 已PASS	必须失败
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