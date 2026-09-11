# ADR-0006：以命名结果和同名重载执行 Composite

- 状态：Superseded
- 日期：2026-09-11
- 决策所有者：library maintainers
- 批准来源：用户于 2026-09-11 在当前 Codex 任务中接受 `Unit`、单一 Pipeline 执行入口、同名 Composite 执行重载、命名 Result 类型，并明确要求删除 `GeneratedCompositeStepAttribute` 后执行。
- 决策范围：Pipeline 的结果模型、根执行 API、Composite 生成形态和跨程序集编译协议。
- 适用产品意图：[Product intent](../product/README.md)
- 适用原则：[P1、P2、P3、P4、P6、P7、P8](../principles/README.md)
- 替代：[ADR-0005](ADR-0005-named-composite-functions.md)
- 被替代：[ADR-0007](ADR-0007-natural-pipeline-return-shapes.md)

## 决策摘要

每个 Pipeline 只提供一个返回结果的 `Execute` 或 `ExecuteAsync`。Composite 生成与声明函数同名的唯一静态执行重载，返回 `<函数名>Result`，跨程序集 consumer 通过严格方法结构识别它；不再生成保留名称执行函数，也不再使用 `GeneratedCompositeStepAttribute`。底层返回 `void` 或 `Task` 的 Leaf 继续合法，其根 Pipeline 返回 `Unit`。

## 背景与决策问题

ADR-0005 已去掉 Composite state，却仍生成收集结果 core、丢弃结果 core 和 `__TedToolkitExecuteCompositeStep` 转发协议，并用公开 compiler-services Attribute 标记跨程序集入口。固定 `Results` 名称也不能表达结果属于哪个 Composite。公开执行面和生成代码因此重复，协议标记承载的信息又能由完整方法签名静态恢复。

本决策回答如何让函数式 Step、Composite 与 Pipeline 使用一个结果导向执行模型，同时保留同步/异步、DI、Logger、重试、取消、命名节点结果和跨程序集静态组合。

## 决策驱动与约束

| 类型 | 驱动或约束 | 证据或来源 | 优先级 |
| --- | --- | --- | --- |
| 硬约束 | Pipeline class 只公开一个结果导向执行入口 | 用户决定 | 必须 |
| 硬约束 | `void` 与非泛型 `Task` Leaf 继续合法，其根 Pipeline 返回稳定公共 `Unit` | 用户决定；现有副作用 Step 用例 | 必须 |
| 硬约束 | Composite 结果类型与函数名关联，且生成代码没有通用 `Results`、state、prepare 或保留名称执行函数 | 用户决定 | 必须 |
| 硬约束 | 删除 `GeneratedCompositeStepAttribute`，跨程序集协议仍须编译期唯一、完整、可拒绝 | 用户决定；P6 | 必须 |
| 硬约束 | 根 Pipeline 仍是 class；公共 DSL marker、图和执行语义保持 | 用户此前决定；ADR-0005 | 必须 |
| 硬约束 | 无反射直接调用；服务/Logger 在节点重试外解析一次；caller 继续拥有 provider、scope 和 Token | Product intent；P1、P4、P7 | 必须 |

## 选项与证据

| 选项 | 证据与置信度 | 满足驱动 | 决定性取舍 | 结果 |
| --- | --- | --- | --- | --- |
| 保留 Attribute 与保留名称协议 | 当前生成实现，置信度高 | 不满足精简目标 | 显式版本握手，但继续暴露固定 compiler API 与转发层 | 拒绝 |
| 删除 Attribute，仅按模糊方法形状猜测 | C# metadata 可见性，置信度高 | 表面精简 | 用户重载可能被误识别，违反 P6 | 拒绝 |
| 使用同名、唯一、完整结构校验的执行重载 | Roslyn 符号可提供名称、参数、返回类型、Attribute 与可见性，置信度高 | 满足全部硬约束 | 协议升级时依靠严格结构失败并要求 producer/consumer 重编译，而非显式版本号 | 选择 |
| 禁止 `void`/`Task` Leaf | 现有测试包含副作用节点，置信度高 | 强制业务返回值 | 迫使纯副作用操作制造无意义业务类型 | 拒绝 |

## 决策

Composite 声明继续是首参数为 `StepGraph` 的任意命名静态 `void` 函数，一个所属类型只允许一个声明。生成器在同一所属类型中生成一个与声明函数同名的静态执行重载；该重载直接包含完整图执行，不再生成结果丢弃 core、结果收集 core 或 `__TedToolkitExecuteCompositeStep`。其可见性与 Composite 的跨程序集可组合性一致，并通过 `EditorBrowsable(Never)` 隐藏于普通调用体验。

生成结果容器是 nested readonly struct，名称严格为 `<Composite函数名>Result`。同步执行重载返回该类型，异步重载返回 `Task<<Composite函数名>Result>`。结果属性仍按已命名的、有业务结果的节点生成；无业务结果节点保持控制依赖语义，不制造可传输数据属性。结果类型名或生成执行重载与用户成员冲突时必须诊断并停止生成。

结构协议按以下信息唯一配对并完整验证：同一 owner 中恰有一个首参数为 `StepGraph` 的声明函数，以及一个同名结构候选；结构候选按需以 `IServiceProvider` 开始，随后是 display path、声明中 `StepGraph` 之后且 Token 之前的业务与 `[FromServices]` 参数，最后是默认值为 `default` 的 `CancellationToken`；返回类型必须是对应命名结果或其 `Task`。consumer 由首个 provider 参数推导传递需求，由返回类型推导同步性，不使用协议 Attribute 或显式版本号。

协议成员身份完全由上述结构决定：引用程序集中的手写成员只要精确符合结构就被接受；与声明同名但不属于结构候选的普通重载被忽略；两个或更多精确结构候选、只有畸形候选或缺失候选均报告 `TTP019`。在当前源码中，任何用户声明的 `<函数名>Result` 或除 StepGraph 声明外的同名方法都会占用生成成员名称，报告静态图诊断且不生成回退成员。这一区分让 metadata 协议不依赖不可观察的生成来源，同时保证本地生成所有权确定。

根 Pipeline 仍生成 nested sealed class，但只公开 `Execute` 或 `ExecuteAsync`。Composite Pipeline 返回其命名结果容器；有业务返回值的 Leaf Pipeline 返回原业务类型；底层返回 `void` 或 `Task` 的 Leaf Pipeline 分别返回 `global::TedToolkit.Orchestration.Pipeline.Unit` 或 `Task<global::TedToolkit.Orchestration.Pipeline.Unit>`。`Unit` 是 `TedToolkit.Orchestration.Pipeline` namespace 中无 payload 的 `public readonly struct`；`default(Unit)` 只表达成功完成。它不替代非泛型 `StepBuilder`，不生成 `StepBuilder<Unit>`，也不成为图数据边。删除全部 `ExecuteWithoutResults` 与 `ExecuteWithoutResultsAsync`。

## 为什么现在作出此决策

函数式 Composite 的第一次候选实现表明：state 已消失后，两个 core、一个保留协议转发和一个公开标记 Attribute 不再分别承担不可替代的职责。合并为一个结构化执行重载符合 P1/P2/P4；完整符号验证保持 P6；`Unit` 让副作用 Step 保持自然签名，同时使 Pipeline 始终返回结果。

## 证据与链接

- [Pipeline product intent](../product/README.md)
- [Pipeline design principles](../principles/README.md)
- [Pipeline architecture](../architecture/pipeline-system.md)
- [ADR-0005](ADR-0005-named-composite-functions.md)

## 后果与接受的取舍

- `GeneratedCompositeStepAttribute` 及其显式协议版本被删除；协议演进依赖严格结构不匹配和整包重编译。
- `Results` 改名为 `<函数名>Result`，`ExecuteWithoutResults*` 被删除，均是有意的源码兼容变化。
- `void`/`Task` Leaf 无需改变声明；只有其根 Pipeline 的返回类型变为 `Unit`/`Task<Unit>`。
- 根 Pipeline class 仍有薄包装，以持有 caller provider 并隐藏 display path；它与 Composite 的静态执行重载职责不同。
- Composite 工厂仍以所属类型命名，公共 DSL marker 和节点结果传输规则不变。

## 下游交付约束

- 同名执行重载必须是 Composite 图唯一的执行实现；不得通过另一个生成 core 或保留名称方法转发。
- 结构协议验证必须覆盖本地和 metadata producer，接受精确匹配的 metadata 成员，忽略无关重载，并拒绝冲突、重复、缺失或不完整的结构候选。
- DI、Logger、重试、超时、取消、并行清理、同步/异步与显示路径语义不得因统一结果入口而改变。
- 当前 README、架构、迁移和诊断文档必须只描述新执行面和命名结果。

## 退出要求

任何替代方向仍须保留编译期可判定的唯一协议、强类型直接调用、结果导向 Pipeline class、自然的副作用 Leaf 声明和 caller-owned 资源边界。

## 后续与审查触发器

| 项目 | 所有者 | 到期或客观触发条件 | 状态 |
| --- | --- | --- | --- |
| 重新评估结构协议版本握手 | library maintainers | producer/consumer 需要在不重编译的情况下协商多个协议版本 | Open |
| 重新评估 `Unit` 暴露 | library maintainers | consumer 需要把完成结果作为数据边传输，而不仅是根 Pipeline 返回 | Open |
