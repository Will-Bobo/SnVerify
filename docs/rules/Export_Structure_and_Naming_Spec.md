# 导出结构与命名规范（Product / Order 维度）

## 一、目标

本规范用于统一 SnVerify 项目中 **测试结果导出（Excel / TXT / ZIP）** 的目录结构、文件命名及行为约束，确保：

* 导出结果具备**强业务可读性**
* 与当前数据模型（Product / Order / TestSession / TestRecord）严格一致
* 便于产线、测试、运维、交付人员直接使用
* 为 AI / Cursor Agent 协作提供**明确不可变的规则边界**

---

## 二、导出维度与结构定义

### 2.1 按【项目 / Product】导出

#### ZIP 包命名

```
{ProductName}.zip
```

#### ZIP 内部结构

```
{ProductName}/
└── {OrderName}/
    ├── {SessionName}.xlsx
    └── {SessionName}.txt
```

#### 说明

* ProductName 为业务上的产品型号名称
* OrderName 为订单名称（全局唯一）
* SessionName 为测试会话名称（通常包含时间戳）
* 目录结构严格映射：`Product → Order → Session`

---

### 2.2 按【订单 / Order】导出

#### ZIP 包命名

```
{OrderName}.zip
```

#### ZIP 内部结构

```
{OrderName}/
├── {SessionName}.xlsx
└── {SessionName}.txt
```

#### 说明

* Order 已天然绑定 Product，因此不再额外创建 Product 层级
* Session 作为最小业务单元直接落在 Order 下

---

## 三、统一导出行为约束

### 3.1 所有导出必须以 ZIP 为最终交付物

* 禁止散文件导出
* 禁止在用户选择目录下直接生成 xlsx/txt
* 所有中间文件应在临时目录或内存中生成

### 3.2 冲突检测规则

* 仅检测 ZIP 是否已存在：

```
{ExportRoot}/{ZipName}.zip
```

* 不检测 ZIP 内部单个 Session 文件
* 已存在 ZIP 时，提示用户并中止导出

### 3.3 文件系统安全命名

* ProductName / OrderName / SessionName 在用于文件系统前，必须经过统一的安全处理：

  * 替换非法字符（`\\ / : * ? \" < > |`）为 `_`
  * UI 显示名称与文件系统名称必须区分

---

## 四、与数据模型的关系（不可变）

* 数据事实来源：`TestRecord`
* Session 通过 `TestSession.Id` 查询 TestRecord
* **目录 / 文件命名只使用业务名称，不使用数据库 Id**