# 技术计划：端到端自动化原理图生成流程补全

本计划对应 `automation_flow_spec.md` 的差距清单，按“框架 → 流程 → 公式 → 模型”给出落地技术方案。
**在用户批准本计划前，不写实现代码。**

---

## 1. 框架层落地

### 1.1 表达式引擎统一（F-1, V-1, V-2）
- **决策**：二选一，需用户拍板：
  - 方案 A（推荐，改动小）：保留 Jint，但**修正宪法与 spec**，把所有文档示例改为 JS 语法，并补齐 `Ceiling/Floor/Round(x,n)`、`Concat` 变参、`Lookup_Breaker(power, startType)` 的签名文档。
  - 方案 B（贴合宪法）：替换为 NCalc，重写 `EvaluateExpression`/`PreprocessNCalcSyntax`/`FormulaEditorDialog` 的语法高亮与补全。
- **统一校验与求值**：让 `ValidateExpression` 复用 `CreateEngine` 与同一套类型化 mock（按变量 `Type` 生成 mock 值，`Boolean→false`、`Integer/Double→0`、`String→""`、枚举→首个选项），`Lookup_*` 返回占位字符串而非 "Mock"。

### 1.2 MVVM 化（F-2，分期）
- 先抽 `ConfiguratorWindow` 的**状态与命令**为 `ConfiguratorViewModel`（Database、SelectedTab、SelectedItem、Dirty、Save/Generate/Import/Export 命令），code-behind 只保留纯视图交互（拖拽、布局、树展开）。
- 后续再把 `ProjectBuilder` 表单、`DetailsAndModals` 迁移到独立 VM。

### 1.3 解耦规则引擎（F-3）
- 将 `EvaluateExpression`、`PreprocessNCalcSyntax`、`QueryGenericCatalog`、`QueryBreakerCatalog` 移到 `RuleEngine`（纯 C#，无 EPLAN using）。
- 通过 `IRuleEngineContext` 注入 `ProjectName/User/Date` 与目录解析（`ResolvePath`），EPLAN 侧实现该接口。

### 1.4 数据源抽象（F-4）
- 引入 `ICatalogSource { string[] Load(string name); }`，现有 CSV 实现为 `CsvCatalogSource`，预留 `RestCatalogSource`。

### 1.5 脚本与子段（F-5, F-6）
- 脚本：新增 `ScriptTypical` 编辑 UI（名称/触发点/排序/内容），复用 `FormulaEditorDialog`。
- 子段：明确 `UseSubSegmentLogic` 语义（分页时按子段结构编号），实现或移除该设置项，避免虚假能力。

---

## 2. 生成流程层落地

### 2.1 变体与规则（P-1）
- 模型新增 `VariantDefinition { Name, Description, Condition }`，挂到 `MacroTypical` 或 `TypicalGroup`。
- 生成时先求值各变体的 `Condition`，选出唯一命中的变体；多命中/无命中进入预检报告。

### 2.2 模块接口与连接（P-2）
- 新增 `PortDefinition { Name, Direction, SignalType }` 与 `ConnectionRule`。
- 一期实现“接口→占位符”的映射（把接口名映射到占位符变量），二期接入 EPLAN 连接 API（`Connection`）做自动连接。

### 2.3 生成前预检（P-3）
- 新增 `PreflightValidator`，生成前扫描并报告：
  - 公式语法/未知变量/未知函数；
  - `TargetName` 引用缺失、`TypicalGroup` 环（已有）、`MacroElement.MacroPath` 文件缺失（用 `ResolveMacroPath` 判定）；
  - 可见必填变量缺失；
  - 结构标识符冲突（重复键、空 PageName）；
  - 变体多命中/无命中。
- 输出 `PreflightReport`（错误/警告列表），UI 与静默动作共用。

### 2.4 增量生成（P-4）
- 一期：由“全量删除”改为“按模块标识生成/更新”，保留未被本次规则覆盖的手工页面。
- 引入 `GenerationScope`（全量/增量）参数，增量模式下不删除已存在且非本次生成的目标页面。

### 2.5 后处理（P-5, P-7）
- 一期实现可插拔 `IPostProcessor`，提供 `WireNumberingPostProcessor` 或 `BomReportPostProcessor`（用 EPLAN `ActionCallingContext` 调 `XEsAction...` 动作），并接入 `ExecuteScriptSteps` 的 `AfterGenerate` 触发点。
- 部件选择：把 `Lookup_Breaker` 返回值扩展为“部件号 + 值集名”，新增 `ArticleCatalog`。

### 2.6 事务与锁（P-6）
- 在 `GenerateTypical`/`GenerateConfigurator`/静默动作中，包 `Transaction` 外再套 `LockingStep`，并确保 `Progress` 正确 `EndPart`。

### 2.7 拓扑排序（P-8）
- 在 `GenerateTypicalGroup`/`GenerateTypicalInternal` 求值前，对变量按公式引用做依赖排序（Kahn 算法），缺失/环依赖进入预检报告。

### 2.8 多语言占位符（P-9）
- 用 `MultiLangString.GetString(language)`/`SetString(language, ...)` 按 `Settings.SelectedLanguages` 逐语言写入，默认语言兜底。

### 2.9 错误粒度（P-10）
- `GenerateTypicalInternal` 单元素插入失败时记录到 `PreflightReport`/日志并可选择“跳过继续”，由配置开关 `AbortOnFirstError` 控制；默认仍整体回滚以保证一致性。

### 2.10 生成历史（P-11）
- 新增 `GenerationRecord { Id, Timestamp, Configurator, Parameters, Result, LogPath }`，写入 AppData 下的结构化 JSON 列表，供后续审计/回放。

---

## 3. 公式编辑层落地

- **语法高亮/补全**：补全 `Ceiling/Floor/Round(x,n)`、`Concat` 变参模板；变量补全项按 `Type`/`PredefinedValues` 显示标签。
- **错误定位**：解析 Jint/NCalc 异常的 `line/position`，在 AvalonEdit 对应位置加波浪线标记与工具提示。
- **实时校验**：复用统一后的类型化 mock，避免校验/运行结果不一致。

---

## 4. 数据模型层落地

- `StructureMappings`：改为 `Dictionary<string,string>`（或保留 List 但生成/读取时按 Key 去重、取非空值），修复重复键静默丢值。
- 页面结构：新增 `PageStructure` 模型（Functional/Plant/Location/DocType/UserDefined/Counter），`FormatPageName` 改为其格式化方法。
- 新增：`VariantDefinition`、`PortDefinition`、`ArticleInfo`、`GenerationRecord`。

---

## 5. 验证计划

### 自动化
- 为 `RuleEngine`（抽离后）、`PreflightValidator`、`ConvertToVariableType`、拓扑排序、`StructureMappings` 去重、`FormatPageName` 新增/更新单元测试。
- `dotnet build` 0 错误；`dotnet test` 全部通过。

### 手动（EPLAN 内）
- 用含命名变体的配置走完整流程，验证变体选择与预检报告。
- 验证增量生成不删除手工页面。
- 验证后处理动作（线号/BOM）与 `LockingStep` 生效。
- 验证多语言占位符按语言写入。

---

## 6. 依赖与顺序（关键路径）

1. F-1 表达式引擎决策（阻塞 V-1/V-2 与文档修正）。
2. F-3 抽离 `RuleEngine`（阻塞 2.7 拓扑排序与单测）。
3. 4 模型改造（阻塞 2.1/2.2）。
4. 2.3 预检（依赖 2.1/2.7/模型）。
5. 2.4 增量 / 2.5 后处理 / 2.6 锁（可并行）。
