# 命名结果与结构化 Composite 执行

<!-- change-format: 3 -->
<!-- workflow-profile: controlled -->
<!-- change-kind: behavior-change -->
<!-- change-status: implemented -->
<!-- delivery-shape: single -->

- Priority: P1
<!-- approval-source: user approved the function-identified multiple Composite/Pipeline contract and requested implementation, independent review, and commit on 2026-09-11 -->
<!-- candidate-binding: workspace:a549164f4bcdb1f991846449c0cae0cb9b502549:sha256:129e83838eb67c103f0ec6cbc1a04f84674fb2febd93996e71eb2f8211b95d02 -->

<!-- section: goal-rationale -->
## 目标与理由

让 Step、Composite 与根 Pipeline 使用自然的函数模型：Composite 以声明函数而非 owner 类型作为身份，同一 class 可声明多个不同名称的 Composite/Pipeline；每个 Composite 生成独立命名结果和唯一执行入口，无值 Leaf 保持 `void`/`Task`，跨程序集组合继续由严格结构协议静态识别。

<!-- section: scope -->
## 范围与非目标

- 范围：Composite 声明/执行/结果生成、Pipeline facade、无值 Leaf 的自然返回、跨程序集发现与诊断、runtime compiler-services API、测试和当前文档。
- 非目标：禁止 `void`/非泛型 `Task` Leaf、为无值 Leaf 制造占位结果或数据边、支持同一 owner 的同名 Composite 重载、改变公共 `StepGraph`/`StepBuilder`/`StepArgument` marker、移除根 Pipeline class。
- 有意兼容变化：删除 `GeneratedCompositeStepAttribute`、`ExecuteWithoutResults*`、通用 `Results` 和 `__TedToolkitExecuteCompositeStep`；producer/consumer 必须使用匹配包并重新编译。
- 保持：任意 Composite 函数名、`Configuration` 作为普通合法名称、`<函数名>Pipeline`、显式 Pipeline Name、DI/Logger/retry/timeout/cancellation/parallel cleanup、结果属性和 caller-owned scope 语义。

<!-- section: behavior-contract -->
## 行为契约

<!-- behavior-change: OB-01 -->
| ID | 可观察边界 | 当前 | 预期 | 保持 |
| --- | --- | --- | --- | --- |
| OB-01 | Composite 生成 API | `Results`、两个 core、保留名称 execute 和协议 Attribute | `<函数名>Result` 与一个同名静态执行重载；无 state/prepare/core/保留名称 execute/协议 Attribute | 任意声明名、强类型节点结果、Pipeline class |
| OB-02 | 根 Pipeline 执行 | 同时生成收集与丢弃结果入口 | 只生成 `Execute` 或 `ExecuteAsync`，返回形状忠实于根函数 | 同步/异步由底层 Step 决定 |
| OB-03 | 无值 Leaf | `void`/`Task` 合法，Pipeline 可无返回值 | 声明及根 Pipeline 分别保持 `void`/`Task`，不生成 `Unit` 或其他占位结果 | 非泛型 StepBuilder 和控制依赖语义 |
| OB-04 | 跨程序集 Composite | Attribute 版本和保留名称识别 | 由同名声明/执行重载的完整结构唯一识别和拒绝错误协议 | 静态强类型、无反射、重编译兼容策略 |
| OB-05 | 同一 owner 的多个 Composite/Pipeline | owner 只能有一个 Composite，图工厂以 owner 类型命名 | 每个不同名称 Composite 独立生成函数名工厂、Result、同名执行重载和可选 Pipeline；合法 sibling 不被畸形协议连带禁用 | 现有生成成员冲突与 facade 最终名称冲突诊断 |

<!-- acceptance-case: AC-01 -->
### AC-01 — Composite 使用命名结果和唯一执行重载

```gherkin
Scenario: 生成业务命名的 Composite
  Given 一个名为 Import 的合法 Composite 声明
  When 生成器输出 Composite 代码
  Then 生成 ImportResult 和唯一的 Import 执行重载，且没有 Results、state、prepare、core、__TedToolkitExecuteCompositeStep 或 GeneratedCompositeStepAttribute
```

<!-- acceptance-case: AC-02 -->
### AC-02 — Pipeline 通过单一入口保留自然返回形状

```gherkin
Scenario: 执行有值和无值的根 Step
  Given 同步/异步有值 Leaf、同步 void Leaf、异步 Task Leaf 与同步/异步 Composite 分别标记 Pipeline
  When consumer 检查并执行生成的 facade
  Then 每个 facade 只有 Execute 或 ExecuteAsync
  And 六种返回形状分别是 TResult、Task<TResult>、void、Task、<函数名>Result 与 Task<<函数名>Result>
  And 不存在 ExecuteWithoutResults 系列
  And 不存在 Unit 占位类型，void 节点仍使用非泛型 StepBuilder 且不形成数据边
```

<!-- acceptance-case: AC-03 -->
### AC-03 — 结构协议支持跨程序集组合

```gherkin
Scenario: consumer 嵌套公开 Composite
  Given producer 只暴露 StepGraph 声明和符合 ADR-0007 的同名执行重载
  When consumer 从 metadata 组合并执行它
  Then consumer 无需 GeneratedCompositeStepAttribute 即可唯一验证和调用协议，获得命名结果，并保持普通/keyed 服务与 Logger 在重试外只解析一次
```

<!-- acceptance-case: AC-04 -->
### AC-04 — 合并执行实现保持运行语义

```gherkin
Scenario: 执行带策略和并行分支的嵌套 Composite
  Given 图包含层级显示名、timeout、retry、caller cancellation、并行分支与 caller-owned scoped services
  When 通过唯一 Composite 执行重载运行图
  Then display path 和 Logger category 正确，timeout/cancellation 保持，已启动并行工作被 drain/cleanup，且 provider/scope 不由 Pipeline 创建或释放
```

<!-- acceptance-case: AC-05 -->
### AC-05 — Metadata 结构协议确定地接受或拒绝

```gherkin
Scenario: 引用程序集暴露结构化 Composite 协议
  Given exact hand-authored match、无关同名重载、重复 exact match、缺少必需 provider 的 service-bearing match、其他畸形 match 与缺失 match
  When consumer 尝试组合该 Composite
  Then exact match 被接受且无关重载被忽略，重复/畸形/缺失 match 报告 TTP019，且不生成猜测、反射或运行时回退调用
```

<!-- acceptance-case: AC-06 -->
### AC-06 — 本地生成成员冲突被拒绝

```gherkin
Scenario: Composite owner 占用生成名称
  Given owner 已声明 <函数名>Result 或除 StepGraph 声明外的同名方法
  When 生成器处理该 Composite
  Then analyzer 报告静态图诊断，且不自动改名或生成部分执行协议
```

<!-- acceptance-case: AC-07 -->
### AC-07 — 同一 owner 的多个 Composite/Pipeline 独立生成与验证

```gherkin
Scenario: 一个 static partial class 声明多个 Composite
  Given 同一 owner 声明多个不同名称 Composite，且多个函数可标记 Pipeline
  When 本地和 metadata consumer 按函数名组合它们
  Then 每个函数独立生成同名图工厂、命名 Result、同名执行重载和可选 Pipeline class
  And 一个畸形 metadata Composite 不影响同 owner 的合法 sibling 被发现和生成
  And 同名 Composite 重载或相同最终 Pipeline facade 名被确定性诊断
```

<!-- acceptance-case: AC-08 -->
### AC-08 — 函数身份在 owner 与程序集冲突中保持确定性

```gherkin
Scenario: 多个 producer 暴露相同名称的 Composite 函数
  Given 不同 owner 暴露同名 Composite，或相同 metadata owner 与函数来自不同程序集
  When consumer 组合目标函数
  Then namespace/assembly-qualified extension carrier 可选择精确 owner
  And distinct extern aliases 可选择精确程序集
  And 缺少或重复 alias 时静态拒绝，不猜测目标
```

## 约束与风险

- 遵循 [ADR-0007](../../adr/ADR-0007-natural-pipeline-return-shapes.md) 与 [ADR-0008](../../adr/ADR-0008-method-identified-composite-functions.md)。
- Result、执行入口和 compiler-services 删除均是公共源码兼容变化；回退方式是恢复上一组匹配 runtime/analyzer 与调用源码。
- Metadata 中精确符合结构的手写执行成员被接受，无关同名重载被忽略；重复或畸形结构候选必须诊断。
- 本地同名执行重载及 `<函数名>Result` 与用户成员冲突时必须诊断，不得自动改名。
- 同一 owner 的 Composite 声明函数名必须唯一；若要支持同名重载，必须重新设计并取得批准。
- 若实现需要保留第二个 Composite core、重新引入协议 marker 或改变图中 void 节点的数据传输，必须重新设计并取得批准。

<!-- section: start-conditions -->
## 开始条件

<!-- change-prerequisite: none -->

无。

<!-- section: delivery-brief -->
## 交付概要

- 一个 bounded delivery 同时更新 runtime/analyzer、生成协议、既有调用测试和当前文档；这些 API 必须原子匹配，不能拆成可独立发布的中间状态。
- 可能触点（非绑定）：Composite/Results/Execution/Pipeline emitters、protocol discovery、Step return-shape、runtime compiler-services、Pipeline tests、README/architecture/migration docs。
- 私有实现选择：生成器内部模型与 helper 拆分、测试文件组织；自然返回形状和基础结构协议由 ADR-0007 固定，函数身份、多声明、重载与 owner/程序集消歧规则由 ADR-0008 固定。

<!-- section: proof-plan -->
## 证明

<!-- primary-proof: AC-01 purpose=acceptance shape=contract -->
<!-- primary-proof: AC-02 purpose=acceptance shape=contract -->
<!-- primary-proof: AC-03 purpose=boundary shape=integration -->
<!-- primary-proof: AC-04 purpose=regression shape=integration -->
<!-- primary-proof: AC-05 purpose=boundary shape=contract -->
<!-- primary-proof: AC-06 purpose=boundary shape=contract -->
<!-- primary-proof: AC-07 purpose=boundary shape=integration -->
<!-- primary-proof: AC-08 purpose=boundary shape=integration -->
| 契约 | 角色 | 可观察断言 | 命令或有界过程 |
| --- | --- | --- | --- |
| AC-01 | Primary | Roslyn 符号和生成文本对同步 Composite 生成 `ImportResult`、对异步 Composite 生成 `Task<ImportResult>`，且都只有同名执行重载，所有旧协议成员缺席 | `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests/TedToolkit.Orchestration.Pipeline.Tests.csproj --configuration Release -- --filter-uid "TedToolkit.Orchestration.Pipeline.Tests.ExecutorGeneratorTests.1.1.NamedCompositeGeneratesSingleExecutionOverload.1.1.0"` |
| AC-02 | Primary | 六种根 facade 分别返回 `TResult`、`Task<TResult>`、`void`、`Task`、`<函数名>Result`、`Task<<函数名>Result>` 并实际执行；`Unit` 不存在，void 节点仍为非泛型 builder | `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests/TedToolkit.Orchestration.Pipeline.Tests.csproj --configuration Release -- --filter-uid "TedToolkit.Orchestration.Pipeline.Tests.ExecutorGeneratorTests.1.1.PipelinesExposeSingleNaturalReturnShape.1.1.0"` |
| AC-03 | Primary | 独立 producer/consumer 编译并运行，命名结果、普通/keyed DI、Logger 与 retry 可观察值正确 | `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests/TedToolkit.Orchestration.Pipeline.Tests.csproj --configuration Release -- --filter-uid "TedToolkit.Orchestration.Pipeline.Tests.ExecutorGeneratorTests.1.1.StructuralCompositeProtocolExecutesAcrossAssemblies.1.1.0"` |
| AC-04 | Primary | display path、timeout/cancellation、parallel drain/cleanup 与 provider/scope ownership 保持 | `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests/TedToolkit.Orchestration.Pipeline.Tests.csproj --configuration Release -- --filter-uid "TedToolkit.Orchestration.Pipeline.Tests.ExecutorGeneratorTests.1.1.SingleCompositeExecutionPreservesRuntimeSemantics.1.1.0"` |
| AC-05 | Primary | exact/无关/重复/缺少必需 provider，以及 token 位置/数量/标记、重复 StepGraph、ref 输入等畸形 metadata case 分别得到接受或 TTP019，且错误 case 无调用生成 | `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests/TedToolkit.Orchestration.Pipeline.Tests.csproj --configuration Release -- --filter-uid "TedToolkit.Orchestration.Pipeline.Tests.ExecutorGeneratorTests.1.1.StructuralCompositeProtocolHasDeterministicMembership.1.1.0"` |
| AC-06 | Primary | 本地 Result 类型、字段、属性名称和同名方法冲突均诊断且无部分生成 | `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests/TedToolkit.Orchestration.Pipeline.Tests.csproj --configuration Release -- --filter-uid "TedToolkit.Orchestration.Pipeline.Tests.ExecutorGeneratorTests.1.1.NamedCompositeGeneratedMemberCollisionsAreRejected.1.1.0"` |
| AC-07 | Primary | 同 owner 多 Composite/Pipeline 的本地与跨程序集函数名工厂、独立结果/执行/facade、畸形 sibling 隔离及重载/名称冲突均被证明 | `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests/TedToolkit.Orchestration.Pipeline.Tests.csproj --configuration Release -- --filter-uid "TedToolkit.Orchestration.Pipeline.Tests.ExecutorGeneratorTests.1.1.MultipleCompositeFunctionsInOneOwnerGenerateIndependentFactoriesAndPipelines.1.1.0"` |
| AC-08 | Primary | 不同 owner 同名 Composite 的限定 carrier、相同 metadata owner+函数的 distinct extern alias 精确选择及缺失/重复 alias 拒绝均被证明 | `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests/TedToolkit.Orchestration.Pipeline.Tests.csproj --configuration Release -- --filter-uid "TedToolkit.Orchestration.Pipeline.Tests.ExecutorGeneratorTests.1.1.FunctionIdentifiedCompositeFactoriesRemainDeterministicAcrossOwnersAndAssemblies.1.1.0"` |
| 全部 | Conditional regression | 全解决方案、Pipeline playground 和 benchmark 均构建无错误，playground 实际执行独立 CompositeStep，Pipeline 全套测试通过且无临时产物 | `dotnet build TedToolkit.Orchestration.slnx --configuration Release`；`dotnet build playground/TedToolkit.Orchestration.Pipeline.Playground/TedToolkit.Orchestration.Pipeline.Playground.csproj --configuration Release`；`dotnet run --project playground/TedToolkit.Orchestration.Pipeline.Playground/TedToolkit.Orchestration.Pipeline.Playground.csproj --configuration Release --no-build`；`dotnet build benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks.csproj --configuration Release`；`dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests/TedToolkit.Orchestration.Pipeline.Tests.csproj --configuration Release` |

## 当前实现状态

- Composite 工厂、扩展 carrier、hint、图依赖和跨程序集协议均以声明函数为身份；同 owner 多 Composite 使用独立 Result、同名执行重载、私有执行 helper 和可选 Pipeline facade。
- Metadata 收集按 owner+函数逐项验证，畸形 sibling 不影响合法 sibling；同名重载、facade 冲突、不同 owner 同名和同 metadata owner+函数跨程序集 alias 规则均有确定性证明。
- 首个候选的独立实现审查发现私有 helper 拼接名称并非单射；修订后按长度编码函数与节点名称段，并以 `A_B/c` 对 `A/b_C` 的反例证明同 owner helper 不碰撞。
- 第二个候选的独立实现审查发现执行局部名可与 Composite 输入名冲突、执行 token 的 service marker 未被拒绝；修订后所有执行局部名统一通过 `GeneratedNameAllocator` 避让，结构协议要求执行 token 为未标记的默认 token，并加入顺序 `result0`、并行 `execution`/`task0` 及标记 token 反例。
- 第三个候选的独立实现审查发现任意参数表达式调用名会过早触发同名 metadata 协议验证；修订后预扫描只接受直接 `StepGraph` receiver 或首参为该 `StepGraph` 的明确生成 carrier，`int.Parse` 与畸形 `Broken.Parse` 同名不再误报，而真实 `steps.Parse` 仍稳定产生 TTP019 且无调用生成。
- 第四个候选的独立实现审查发现显式 carrier 请求仍被降格为函数名，导致未选择的同名畸形 owner 误报；修订后请求保留函数名与可选 carrier，显式 owner/assembly 只验证精确匹配的声明，并为所选 factory 保留该 carrier 名称。
- 第五个候选的独立实现审查发现多 alias 引用只取首项、混合未限定与显式 carrier 会丢失显式名称，且 AC-02 未直接证明无值节点的 builder/result 形态；修订后 alias 在冲突组内执行确定性 distinct assignment，同一 factory 可生成一个扩展 carrier 与多个非扩展静态 carrier，并直接断言 void Leaf 返回非泛型 `StepBuilder`、不会进入 Composite Result、只作为控制依赖。
- 最新修订在 AC-08 中覆盖共同首 alias 加唯一后续 alias、完全无 distinct alias、未限定加 basic/namespace/assembly 多 carrier 同时调用；AC-02/08 定向用例各 1/1、Pipeline 全套 204/204 通过，全 solution、Pipeline playground、Pipeline benchmark Release 构建均为 0 warning/0 error，playground 实际运行通过。
- 最终冻结候选经 fresh independent implementation review 判定 `Ready to merge`，AC-01 至 AC-08 全部 covered，无 Blocking、Important、Suggestion 或设计偏离。
- README、playground、benchmark、架构、迁移和 ADR-0005/0007/0008 已同步函数名工厂与多声明规则；`Unit`、生成 Attribute、旧执行入口和 Step struct 保持不存在。

<!-- section: completion-criteria -->
## 完成

AC-01 至 AC-08 在同一候选绑定上通过，独立实现审查 Ready；ADR-0007、ADR-0008、README、原则、架构和迁移文档完整承载当前语义；没有 `Unit`、生成 Attribute、旧执行入口或临时构建产物。终态 change 记录在合并并完成持久化引用后按仓库流程清理。
