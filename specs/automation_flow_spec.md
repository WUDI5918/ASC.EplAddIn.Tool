# 规格：端到端自动化原理图生成流程补全

> 本文档基于对现有代码库（`ConfiguratorService` 各分部类、`ConfiguratorWindow`、`FormulaEditorDialog` 等）的审查，
> 对照 EPLAN eBUILD 的模块化自动生成范式，梳理当前工具**已完成**与**仍缺失**的流程环节，
> 并给出需要补齐的目标流程。本文是 Spec 阶段产物，不包含实现代码。

---

## 1. 目标与范围

**目标**：把当前“规则驱动的宏插入器”补全为接近 EPLAN eBUILD 的**端到端自动化原理图生成流程**，覆盖
配置定义 → 规则校验 → 变体选择 → 宏插入/占位符替换 → 连接/后处理 → 可重复生成 的完整闭环。

**范围**：框架分层、生成流程、公式引擎与公式编辑、数据模型、校验与可追溯性。

**范围外**：EPLAN 宏库本身的制作（在 EPLAN 内完成）、UI 主题美化、与现有 `contrast_spec` 重叠的纯视觉项。

---

## 2. 参考基线：EPLAN eBUILD 的自动生成流程

eBUILD 的核心范式（作为对照基准）：

| # | eBUILD 环节 | 说明 |
|---|-------------|------|
| E1 | 模块化（Building Blocks） | 将功能单元封装为带**接口/端口**的模块（电气/流体/PLC），而非孤立宏 |
| E2 | 配置器（Configurator） | 面向用户的选项/参数定义，隐藏宏细节 |
| E3 | 变体与规则（Variant & Rules） | 每个模块有多个**命名变体**，由规则选择适用变体 |
| E4 | 接口匹配（Interface Matching） | 模块之间通过定义好的接口自动连接，而非人工摆坐标 |
| E5 | 预检校验（Pre-flight Validation） | 生成前对整套配置做一致性/完整性校验 |
| E6 | 自动生成（Generation） | 多学科、批量、可增量更新地生成图纸/程序 |
| E7 | 后处理（Post-processing） | 线号/连接编号、报表/BOM、部件选择 |
| E8 | 版本与增量（Versioning & Incremental） | 模块版本化、支持增量再生成而非全量删除重建 |

---

## 3. 现状流程（As-Is，基于代码）

```
Configurator
  └─ TypicalGroupRefs → TypicalGroup
       └─ Instances → TypicalInstance (IsActiveFormula + VariableMappings)
            └─ TargetName → MacroTypical
                 └─ Elements → MacroElement (IsActiveFormula / StructureMappings / VariableMappings / 坐标)
                      └─ 插入 .emp 页宏 或 .ema 窗口宏
                           └─ 占位符 SetValue / ApplyRecord
```

现有能力已覆盖：三级模型（Configurator/TypicalGroup/MacroTypical）、条件激活（IsActiveFormula/VisibilityFormula）、
变量映射与默认值回退、CSV 查表（Lookup/Lookup_Breaker）、页宏/窗口宏插入、占位符值集应用、
事务回滚、静默模式与 dry-run、XLSX/JSON 库导入导出。

---

## 4. 差距分析（仍存在的流程问题）

### 4.1 框架层（Framework）

| ID | 问题 | 证据 | 影响 |
|----|------|------|------|
| F-1 | **表达式引擎与宪法/规格不符**：宪法与 spec §3.1 规定使用 **NCalc**，实际使用 **Jint(JS)**，靠 `PreprocessNCalcSyntax` 把 `and/or/not` 翻译成 `&&/||/!` | `.specify/memory/constitution.md:22`；`ConfiguratorService.Evaluation.cs:107-153`；csproj 只引用 `Jint 4.10.1` | 文档/示例（`[Power_kW]`、`if(...)`、`Ceiling`、`Round(x,1)`）与真实语法不一致，用户按文档写公式必然报错 |
| F-2 | **未实现 MVVM**：规格 §3 声明 MVVM，但 `ConfiguratorWindow` 由 7 个分部类共约 3000 行 code-behind 组成，无 ViewModel | `ConfiguratorWindow.*.cs` | 业务逻辑与 UI 强耦合，难测试、难维护，后续功能叠加更脆弱 |
| F-3 | **分层不彻底**：规则引擎层应不依赖 EPLAN API（宪法 II.2），但评估层通过 `PathMap.SubstitutePath`/`CachedProjectName` 与 EPLAN 耦合 | `ConfiguratorService.cs:471-492`、`Evaluation.cs:158-159` | 无法独立单测规则引擎，无法脱离 EPLAN 验证规则 |
| F-4 | **数据源抽象缺失**：宪法 III 要求支持 REST JSON API，当前只有本地 CSV/Excel | `QueryGenericCatalog` 仅 `File.ReadAllLines` | 无法接入云端选型库/物料库 |
| F-5 | **脚本 Typical 只有执行无 UI 创作** | `BtnAddScriptTypical_Click` 仅 `ShowToast("not supported")`；`ScriptTypical` 模型已存在 | 用户无法配置后处理/扩展逻辑 |
| F-6 | **`UseSubSegmentLogic` 是死配置**：设置项只在对话框读写，生成逻辑从不使用 | `LibrarySettings.UseSubSegmentLogic`；grep 无生成侧引用 | 子段（sub-segment）逻辑未实现，属于虚假能力 |

### 4.2 生成流程层（Flow，对照 eBUILD）

| ID | 问题 | 证据 | 对应 eBUILD |
|----|------|------|-------------|
| P-1 | **无变体/选项管理**：`VariantIndex` 只是宏层的整数，Configurator 层无“命名变体 + 选择规则”概念 | `MacroElement.VariantIndex` | E3 变体与规则 |
| P-2 | **无模块接口/自动连接**：宏之间孤立插入，靠 `StructureMappings` 字符串拼页面名，无接口/端口模型，无自动布线/连接 | `GetOrCreatePage`、`FormatPageName` | E1/E4 模块与接口匹配 |
| P-3 | **无生成前整套预检**：UI 仅做可见必填变量校验；dry-run 只在静默动作里，且不检查宏文件缺失/目标引用缺失/公式无效/结构冲突 | `ValidateVariables`；`ExecuteCogineerProjectBuilderAction` | E5 预检校验 |
| P-4 | **无增量更新，全量破坏式重建**：生成前删除同名页面/清页，无法“更新既有图纸” | `existingTarget.Remove()`、`ClearedPages` | E8 版本与增量 |
| P-5 | **无后处理**：规格 §4.2.5 要求线号/连接编号/BOM，代码中完全没有（grep 无 `WireNumber`/`GenerateReports`/`PartsManagement`） | 无 | E7 后处理 |
| P-6 | **缺 LockingStep**：宪法 IV 要求 Transaction + LockingStep，实际只有 Transaction | `TransactionManager().CreateTransaction()`，无 `LockingStep` | E6 |
| P-7 | **无部件/物料选择**：`Lookup_Breaker` 返回的是“值集名”字符串，不是部件号/物料 | `QueryBreakerCatalog` 返回 `ValueSet` 名 | E7 部件选择 |
| P-8 | **变量依赖无拓扑排序**：变量按列表顺序一次性求值，后向引用会取到 null | `GenerateTypicalGroup` 逐变量 `evalContext[variable.Name]=typedVal` | E5 |
| P-9 | **多语言占位符未实现**：`SelectedLanguages` 只存不生效，`MultiLangString.SetAsString` 把所有语言设成同一值 | `Generator.cs:814-822` | E6 |
| P-10 | **错误粒度粗**：单个宏失败即整体回滚，无“跳过失败项并报告”的能力 | `GenerateTypicalInternal` 抛异常即 `transaction.Abort()` | E6 |
| P-11 | **生成历史/审计缺失**：只有 `debug_log.txt`，无结构化生成记录/回放 | `WriteGenerationLogToFile` | E8 |

### 4.3 公式编辑与公式引擎层（Formula）

| ID | 问题 | 证据 | 影响 |
|----|------|------|------|
| V-1 | **校验与求值用不同 mock**：`ValidateExpression` 把变量全绑成 `0`（int）、`Contains/StartsWith` 恒 false、`Lookup_Breaker` 返回 "Mock"；`EvaluateExpression` 用真实类型值 | `ConfiguratorService.cs:449-484` | 类型敏感/查表公式“校验通过但运行报错”或反之 |
| V-2 | **文档函数缺失**：`Ceiling`/`Floor` 未注册，`Round` 只有 1 参（规格要求 `Round(x,1)`），`Concat([x],...)` 括号语法不可用 | `RegisterMathFunctions` 仅 9 个函数 | 规格 §3.1 示例无法执行 |
| V-3 | **自动补全无类型语义**：所有变量同类别显示，无类型/枚举选项/描述提示 | `FormulaEditorDialog` `VariableCompletionData` | 公式易写错 |
| V-4 | **无错误定位高亮**：AvalonEdit 只给状态文字，无波浪线/行列定位 | `ValidateCurrentExpression` → `SetStatus` | 排错效率低 |
| V-5 | **无单位/量纲支持**：公式对 kW/A/V 等量纲无感知 | 无 | 工程公式易出错 |

### 4.4 数据模型层（Model）

| ID | 问题 | 证据 |
|----|------|------|
| M-1 | 无接口/端口、连接、变体、部件等模型类型 | `ConfiguratorModel.cs` 仅 MappingEntry/ConfigurationVariable/TypicalInstance/MacroElement/MacroTypical/TypicalGroup/Configurator/ScriptTypical |
| M-2 | `StructureMappings` 用 `List<MappingEntry>` 且靠 `FirstOrDefault(Key==...)` 取首个，重复键会静默丢值（数据文件已出现重复键） | `configurator_data.json` 中 `==`/`=`/`+` 各出现两次 |
| M-3 | 页面结构用字符串拼接（`FormatPageName`），无 EPLAN 页面结构对象模型，易受分隔符/空值影响 | `FormatPageName` |
| M-4 | 无多语言字段模型（每个变量仅一个 `DefaultValue` 字符串） | `ConfigurationVariable` |

---

## 5. 目标流程（To-Be）

```
1. 定义
   模块(带接口/端口) → 变体(命名) → 规则(选择变体) → 配置器(选项)
        │
2. 预检（Pre-flight）
   公式/引用/文件/必填/结构/变体 一致性校验 → 生成"预检报告"
        │
3. 求值
   依赖拓扑排序 → 规则引擎(NCalc 或统一后的引擎) → 变体选择 → 参数解析
        │
4. 生成（事务 + LockingStep + Progress）
   页面结构(对象模型) → 页宏/窗口宏插入 → 占位符(按语言) → 接口自动连接
        │
5. 后处理
   部件选择/线号/连接编号/BOM报表 → 结构化生成记录
        │
6. 增量与版本
   模块版本化 → 增量再生成(保留手工修改) → 审计/回放
```

---

## 6. 验收标准

- **框架**：表达式引擎与宪法/文档一致（NCalc 或显式修改宪法）；规则引擎层可脱离 EPLAN 单测；引入 ViewModel 至少覆盖核心表单。
- **流程**：支持命名变体与选择规则；提供生成前整套预检报告；后处理至少支持线号/连接编号或 BOM 之一；生成使用 `LockingStep`。
- **公式**：校验与求值共用同一套类型化 mock；补全 `Ceiling/Floor/Round(x,n)` 等文档函数；编辑器提供行列错误定位。
- **模型**：新增接口/端口、变体、部件模型；`StructureMappings` 改为无重复键结构（字典或去重）。
- **质量门**：`dotnet build` 0 错误；`dotnet test` 全部通过；每条任务可追溯到本 spec 与 plan。

---

## 7. 范围外（本阶段不做）

- EPLAN 宏库内部制作与占位符值集设计（在 EPLAN 侧完成）。
- 多学科（流体/PLC）生成（本期聚焦电气原理图）。
- 云端/REST 物料库的完整接入（仅预留抽象接口）。
