# 任务清单：端到端自动化原理图生成流程补全

> 每项可追溯到 `automation_flow_spec.md`（差距 ID）与 `automation_flow_plan.md`（小节）。

## 阶段 0：决策与基线（阻塞项）

- [x] 决定表达式引擎：方案 B（切换到 NCalc-Edge 对齐宪法）（F-1）
- [x] 同步文档：宪法/spec/AGENTS 均为 NCalc 语法，无需改写（F-1, V-2）

## 阶段 1：框架分层与引擎

- [x] 抽离 `RuleEngine`（`EvaluateExpression`/查表），移除 EPLAN 依赖（F-3）
- [x] 引入 `IRuleEngineContext` 注入 `ProjectName/User/Date` 与目录解析（F-3）
- [x] 统一 `ValidateExpression` 与 `EvaluateExpression` 使用同一 NCalc 引擎（V-1 核心；类型化 mock 细化待做）
- [x] 补齐 `Ceiling`/`Floor`/`Round(x,n)`/三元 `?:`（NCalc-Edge 原生支持并加测试）（V-2, F-1）
- [x] 引入 `ICatalogSource`（含 `RestCatalogSource` 占位），规则引擎经接口读目录（F-4）
- [x] 移除死配置 `UseSubSegmentLogic`（模型 + 设置对话框 + 测试）（F-6）
- [~] 抽 `ConfiguratorViewModel`（Database + Dirty 已入 VM；选中态/命令待做）（F-2 一期）
- [x] 新增 `ScriptTypical` 编辑 UI（`ScriptTypicalEditorDialog` + 列表 + 增删改）（F-5）

## 阶段 2：数据模型改造

- [x] `StructureMappings` 去重：`ConfiguratorData.Normalize()` 修复重复键丢值，加载/导入统一调用（M-2）
- [x] 新增 `PageStructure` 模型并重构 `GetOrCreatePage`/页宏段 + `FormatPageName(PageStructure)` 重载（M-3）
- [x] 新增 `VariantDefinition`、`PortDefinition`、`ArticleInfo`、`GenerationRecord`（M-1）

## 阶段 3：生成流程核心

- [x] 命名变体模型 + 条件选择 + 多/无命中报告（P-1）
- [x] 接口/端口模型 + 接口→占位符映射（`Ports`/`PortDefinition.PlaceholderName` 模型，自动连接逻辑待阶段后续）（P-2 一期）
- [x] 变量依赖拓扑排序（P-8）
- [x] `PreflightValidator` + `PreflightReport`：已接入 UI 生成前与静默动作 dry-run（P-3）
- [x] 生成套 `LockingStep`（P-6）
- [x] 增量生成 `GenerationScope`/`IncrementalGeneration`，避免破坏式删除（P-4）
- [x] 多语言占位符逐语言写入（P-9）
- [x] 单元素失败可跳过并报告的开关 `AbortOnFirstError`（P-10）

## 阶段 4：后处理与可追溯

- [ ] `IPostProcessor` 抽象 + 线号/BOM 处理器（P-5）
- [ ] 部件选择 `ArticleCatalog`，`Lookup_Breaker` 返回部件号（P-7）
- [ ] `GenerationRecord` 结构化历史与回放（P-11）

## 阶段 5：公式编辑器

- [ ] 变量补全按类型/枚举/描述显示（V-3）
- [ ] 错误行列定位与波浪线高亮（V-4）
- [ ] 实时校验与求值共用同一套 mock（V-1）

## 阶段 6：验证

- [ ] 为 `RuleEngine`/`PreflightValidator`/拓扑排序/去重/`PageStructure` 新增单测
- [ ] `dotnet build` 0 错误、`dotnet test` 全绿
- [ ] EPLAN 手动走查：变体选择、预检报告、增量生成、后处理、多语言、`LockingStep`
