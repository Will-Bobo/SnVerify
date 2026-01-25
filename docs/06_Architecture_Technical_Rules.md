# 06_Architecture_Technical_Rules.md

## 一、总体架构规范

### 1. 分层约束

* Domain 层：纯业务逻辑，禁止引用 WPF、UI、网络、IO、Dispatcher。
* Service 层：负责设备通信、IO、线程调度、事件转发。
* ViewModel 层：仅消费 Service 提供的 Snapshot，触发 PropertyChanged，驱动 Command 刷新。
* View 层：仅包含 XAML 与 InitializeComponent()，不包含业务逻辑。

### 2. MVVM 规则

* 严格解耦，View 通过数据绑定消费 ViewModel 状态。
* Command 使用显式状态控制 CanExecute，状态变化必须调用 RaiseCanExecuteChanged()。
* ViewModel 不直接引用 WPF API (Application, Dispatcher) 或 Thread / Task.Run。

## 二、状态与事件规范

* 所有 UI 可感知状态使用属性 + INotifyPropertyChanged。
* 禁止为状态变化定义事件（如 Connected / Disconnected / SendSucceeded）。
* 事件仅用于不可重放的一次性事实：

  * 数据接收
  * 异常 / 错误
  * 用户操作（Click, KeyDown）
* Snapshot 对象必须：

  * 只读，无方法
  * 可缓存、可导出、可绑定
  * 不可变或只读属性

## 三、ViewModel 线程封送

* 所有 UI 更新必须通过统一封装方法（如 RunOnUI(Action)）执行。
* Service 层事件回调禁止直接修改 UI 绑定属性。
* ViewModel 的 Command Execute 默认在 UI 线程，不允许显式切换线程。

## 四、Service 与 ViewModel 边界

* ViewModel 只负责绑定和命令触发。
* Service 层维护设备状态和业务语义状态（如 IsConnected）。
* 所有通信和异步操作由 Service 完成。
* ViewModel 不直接处理协议、IO 或 MES 调用。

## 五、Command 刷新规则

1. Command 的 CanExecute 依赖显式状态（如 bool / string / enum / SelectedDevice / IsConnected）。
2. 依赖状态变化时必须在属性 setter 中调用 RaiseCanExecuteChanged()。
3. Execute 仅触发流程入口，不处理业务逻辑。
4. CanExecute 禁止包含耗时逻辑或 Service 调用。

## 六、长期约束总结

* MVVM 架构必须严格执行。
* View 不能包含业务逻辑。
* ViewModel 不直接访问 UI / Dispatcher / Thread。
* Service 层负责所有设备通信、线程管理与事件处理。
* 状态使用 PropertyChanged；事件仅用于一次性事实。
* Command 刷新与 Execute 行为必须遵守显式状态依赖规则。
* Snapshot 对象不可变、只读，安全缓存/导出/绑定。

> 本文档为 SnVerify 项目及后续上位机项目的技术宪法级规范，生成或重构任何 ViewModel / Service 代码时必须遵守。
