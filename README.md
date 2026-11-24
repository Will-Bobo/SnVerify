# SnVerify

SnVerify 是一套 Windows 桌面上位机，用于产线工位核对设备身份和规定的检测项。操作员选择产品类型，填写项目名和订单号并开始测试，然后用扫码枪输入设备条码。软件按该产品的流程读取设备、给出 PASS 或 FAIL，把记录写入本地数据库，并可以导出。

主窗口标题为「Smartke SN校验程序」。程序是单个 WPF 应用，当前注册了 SOLTAG25、KM001、KM008 和 YME009。四种产品共用会话、扫码和本地存储，检测流程按产品模式分开，并不共用同一套判定。

## 产品与功能

产品类型在 `SnVerify/Infrastructure/Product/ProductRegistry.cs` 中注册。项目名是操作员输入的批次所属项目，与产品类型分开保存。

| 产品 | 模式 | 主要用途 | 主要能力 |
|---|---|---|---|
| SOLTAG25 | Legacy | 核对外壳条码与设备序列号，并比对 Android 版本 | 通过 ADB 读取设备 SN 和 Android 版本。按全库历史 PASS 绑定关系判定贴纸 SN 与设备 SN。SN 关系通过后，再比对会话中的目标 Android 版本 |
| KM001 | Phase3 | 在项目和订单范围内核对 SN、芯片和版本 | 通过 ADB 聚合命令读取设备信息。比对扫码 SN 与设备 SN，并在「项目名 + 订单号」的 PASS 记录中检查 SN 是否重复。芯片 ID 须以 `F50` 开头，并在同一范围内查重。按已填写的目标比对 Android、主板和充电板版本。读取并记录 WiFi MAC，MAC 不参与判定 |
| KM008 | Phase3 | 在项目和订单范围内核对 SN 和 Android 版本 | 使用与 KM001 不同的聚合解析，读取 Android 版本、设备 SN 和 WiFi MAC。执行 SN 匹配、SN 的 PASS 查重和 Android 版本比对。展示并导出 WiFi MAC。不校验芯片 ID、主板版本和充电板版本 |
| YME009 | Independent | 包装前核验设备身份、软件版本，以及设备上已有的产测和老化结果 | 在 30 秒的 CDC 窗口内确认目标设备，经串口 AT 打开 ADB，就绪后读取属性。五项均通过才为 PASS：设备 SN、软件版本、产测、重启老化、恢复出厂老化。同时读取 IMEI1、IMEI2 和 WiFi MAC，这三项不参与综合判定。同一项目和订单的目标软件版本必须一致。每次完成检测新增一条记录 |

SOLTAG25 由 `ProcessCoordinator.StartVerificationAsync` 执行。KM001 和 KM008 由 `ProcessCoordinator.ProcessScanAsync` 进入 `RulePipelineExecutor`。YME009 由 `Yme009RoundCoordinator` 执行，不进入上述两条流程。

YME009 不执行产测或老化，只读取设备上已经存在的结果。设备接入没有形成可检测上下文时，不写入检测记录。

## 核心业务流程

1. 选择产品类型，填写项目名和订单号。SOLTAG25、KM001、KM008、YME009 还要填写该产品要求的目标版本。
2. 开始测试。程序创建订单和 `TestSession`，并把目标版本保存为该会话的参数快照。
3. 扫码枪以键盘方式输入条码，回车表示一次输入结束。会话未开始时不接受扫码；检测进行中不接受下一台。
4. 按产品模式进入对应检测流程，读取设备并判定。
5. 将 `TestRecord` 写入本地 SQLite。界面显示本次 PASS 或 FAIL 及失败原因。
6. 结束测试。无检测记录时，结束操作被忽略。
7. 按项目或按订单导出。每个会话生成 Excel，并打入 ZIP。导出列随产品变化。

主界面另有设备读取自检，用来确认当前产品能否读到设备信息。YME009 轮次进行期间不能自检。

当前启动代码把 MES 模式设为关闭，检验流程不访问 MES。

## 技术栈与运行环境

- 语言：C#
- 界面：WPF
- 目标框架：.NET Framework 4.7.2（应用工程 `LangVersion` 为 8.0）
- 数据：SQLite（`System.Data.SQLite` 1.0.119）。数据库文件名为 `SnVerify.db`，路径相对于进程工作目录
- 日志：程序目录下的 `logs`
- 导出：EPPlus 7.0.0，生成 `.xlsx` 并打包 ZIP
- 设备通信：
  - SOLTAG25、KM001、KM008 使用随程序提供的 `tools/adb/adb.exe`
  - YME009 先用 SetupAPI 发现 CDC 设备，再用 `System.IO.Ports` 做 AT 交换，随后使用同一套 ADB 读取属性
- 组合根：`Infrastructure/ServiceFactory.cs`。Domain、Services、Infrastructure 是同一 WPF 工程中的目录，不是独立程序集
- 测试：NUnit、Moq。测试工程目标框架为 `net472`

运行检测需要 Windows、已安装的 .NET Framework 4.7.2、USB 连接的目标设备，以及作为键盘输入的扫码枪。构建需要带 WPF 目标的 Visual Studio 或 Build Tools。

## 工程结构

```text
SnVerify.slnx                 解决方案，包含应用和测试工程
SnVerify/                     WPF 应用
  App.xaml、MainWindow.xaml   应用入口和主界面
  ViewModels/                 MainViewModel
  Views/Dialogs/              导出维度、记录筛选等对话框
  Domain/                     实体、产品判定、导出和设备访问契约
  Services/                   会话、检验编排、规则、存储、日志、参数和 YME009 轮次
  Infrastructure/             ServiceFactory、产品注册、ADB 实现、YME009 的 CDC 与串口
  Ui/                         WPF 对话框服务
  tools/adb/                  构建时复制到输出目录的 adb
SnVerify.Tests/               NUnit 测试，引用 SnVerify 工程
build/                        build.cmd、package.cmd
docs/                         需求、设计和技术说明
packages/                     由 packages.config 还原的依赖
```

## 构建与运行

在仓库根目录执行。`build\build.cmd` 通过 `vswhere` 调用 Visual Studio MSBuild。应用工程是旧式 csproj，不要只用 `dotnet build` 编译它。脚本在 PATH 中存在 `nuget` 时，会按 `packages.config` 还原依赖。

```bat
build\build.cmd
```

上面的命令默认以 Debug 重新生成 `SnVerify\SnVerify.csproj`。指定配置时：

```bat
build\build.cmd SnVerify\SnVerify.csproj Release Rebuild
```

启动：

```bat
SnVerify\bin\Debug\SnVerify.exe
```

Release 构建的程序在 `SnVerify\bin\Release\SnVerify.exe`。运行目录需要包含 `tools\adb\adb.exe`；正常构建会从 `SnVerify\tools\adb` 复制过去。

打包 Release 输出到 `dist\`：

```bat
build\package.cmd
```

自动化测试在 `SnVerify.Tests`。测试工程使用 SDK 风格和 `PackageReference`，应用工程必须由上面的 MSBuild 编译；直接用 `dotnet test` 编译应用工程时，WPF 标记代码不会生成。

```bat
dotnet restore SnVerify.Tests\SnVerify.Tests.csproj
build\build.cmd SnVerify.Tests\SnVerify.Tests.csproj Debug Build
dotnet test SnVerify.Tests\SnVerify.Tests.csproj -c Debug --no-build
```

## 文档索引

`docs/` 中还有各阶段的执行记录、审查报告和提示词。下面只列仍适合作为入口的文档。文档与当前源码不一致时，以源码为准。

### 产品需求

- [YME009 包装工位需求](docs/YME009-Packaging-Station-PRD.md)：YME009 的业务范围、检测项和记录要求。文档头仍标为讨论稿。

### 技术设计

- [YME009 技术设计](docs/YME009-Technical-Design.md)：YME009 接入、判定、记录和生命周期设计。文档成稿时按设计稿编写；对应能力已在当前工程实现。
- [YME009 工位界面设计](docs/YME009-Phase5D-Workstation-UI-Design.md)：当前主窗口中 YME009 工位的界面基线。

### 产品专项说明

- [KM008 接入方案](docs/phase4/Phase4_KM008_ProjectCategory_Technical_Proposal.md)：KM008 相对 KM001、SOLTAG25 的范围。方案中的实施约束已经落地到注册表、解析器和规则链。
- [Phase3 SN 校验扩展](docs/Phase3_SN_Verify_Extension_Spec.md)：Phase3 增加芯片、版本和 WiFi MAC 记录的需求说明。文中的订单内唯一，在当前实现中是「项目名 + 订单号」下的 PASS 记录。
- [贴纸 SN 与设备 SN 关系规则](docs/rules/SN_Sticker_Device_Relation_Rules.md)：SOLTAG25 使用的历史 PASS 绑定规则。KM001、KM008、YME009 不使用这套全库绑定判定。

### 架构与开发规范

- [技术架构与开发指引](docs/07_Technical_Architecture_and_Dev_Guide.md)：分层、会话模型和早期检验流程。其中的阶段规划未覆盖 YME009 独立轮次。
- [架构技术规则](docs/06_Architecture_Technical_Rules.md)：分层、Snapshot 和 ViewModel 边界。
- [架构约束](docs/02_Architecture_Guardrails.md)：Phase3 流程保持固定顺序的约束。YME009 使用独立轮次，不改写该顺序。
- [导出结构与命名](docs/rules/Export_Structure_and_Naming_Spec.md)
- [导出语义](docs/Architecture/Export_Semantics.md)
- [TDD 与 AI 协作规则](docs/03_Dev_Rules_TDD_and_AI.md)
