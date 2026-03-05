SN Verify 系统架构与生产流程

版本：v1.0
阶段：Phase 2.6（进入正式开发）

一、SN Verify 生产流程（Production Decision Flow）

这是 真实产线执行的逻辑顺序。


二、ADB 设备信息获取

ADB 命令：

adb shell echo -n "SN:$(getprop sys.skyroam.osi.sn)|Version:$(getprop ro.build.display.id)|ChipID:$(getprop sys.skyroam.chipid)"

返回示例：

SN:ABC123456|Version:V1.2.0|ChipID:F500987654

解析结构：

public class AdbDeviceInfoResult
{
    public string Sn { get; set; }

    public string Version { get; set; }

    public string ChipId { get; set; }
}
三、ChipID 校验规则

新增生产规则：

ChipID 必须以 F50 开头

代码逻辑：

if (!deviceInfo.ChipId.StartsWith("F50"))
{
    return Fail("ChipID非法");
}

失败提示：

ChipID非法
四、SN Verify 系统架构图

这是 代码结构层级图。

五、核心模块职责
ScanInputService

负责：

接收扫码枪输入

聚合字符流

触发 SN Capture 事件

输出：

SnCapturedEventArgs
ProcessCoordinator

系统核心流程引擎。

负责：

SN 校验流程编排

调用 ADB

调用 Storage

调用 MES Gate

AdbAccessService

负责：

调用 adb

获取设备 SN

获取 Version

获取 ChipID

StorageService

负责：

SQLite 数据库访问

TestRecord 写入

PASS 历史查询

MES Gate

用于：

生产前 MES 校验

接口：

IMesPreCheck
MES Result Reporter

用于：

测试结果回写 MES

接口：

IMesResultReporter
六、PASS 记录规则

系统规则：

SN PASS 后不可再次测试

逻辑：

StorageService.HasPassRecord(sn)

返回 true：

拒绝测试
七、典型测试失败类型
错误类型	说明
SN 已 PASS	历史存在 PASS
条形码不在号段	SN 不属于订单
设备通信失败	ADB 失败
SN 不匹配	设备SN与扫码SN不同
版本错误	设备版本错误
ChipID 非法	ChipID 不以 F50 开头
八、SN Verify 执行顺序总结

最终执行顺序：

SN 扫码
↓
MES Gate
↓
SN PASS检查
↓
订单号段检查
↓
ADB读取设备
↓
SN匹配
↓
Version校验
↓
ChipID校验
↓
写入PASS
↓
MES回传
九、建议补充一个文档（强烈推荐）

你现在项目其实已经到了一个很好的阶段，我建议再加一个文档：

docs/Architecture/SN_Verify_Decision_Table.md

这个是 产线工程师最喜欢看的东西：

校验步骤	条件	结果
SN已PASS	true	拒绝测试
SN不在订单	true	条形码不在号段
ADB失败	true	设备通信失败
SN不匹配	true	SN不匹配
Version错误	true	版本错误
ChipID非法	true	ChipID非法

它的价值是：

调试非常快

产线异常定位快

MES对接简单