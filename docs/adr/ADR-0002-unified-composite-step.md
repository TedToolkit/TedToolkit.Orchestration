# ADR-0002：以 ref-struct Composite Step 统一 Pipeline 组合

- 状态：Superseded
- 日期：2026-09-08
- 决策所有者：library maintainers
- 批准来源：用户于 2026-09-08 在原始 Codex 任务中明确确认薄 Pipeline DI 门面设计，并要求继续实现及验证 benchmark。
- 决策范围：Pipeline 静态图的根执行、嵌套组合与叶子操作如何统一为 Step 家族，直至静态编排产品边界被重新审查。
- 适用产品意图：[Product intent](../product/README.md)
- 适用原则：[P1、P2、P3、P4、P6、P7](../principles/README.md)
- 相关决策：[ADR-0001 generated Step context](ADR-0001-generated-step-context.md)
- 替代：无
- 被替代：由 [ADR-0004](ADR-0004-function-declared-composite-and-pipeline.md) 完整替代

## 决策摘要

移除由用户维护的 Pipeline 编排 class；叶子操作与组合图都声明为 readonly ref partial struct Step。每个 Composite Step 生成一个普通引用类型的薄 `Pipeline` DI 门面用于根执行，而嵌套执行直接复用同一静态核心，不产生第二套编排模型。

## 背景与决策问题

当前模型把叶子 Step 定义为短生命周期 ref struct，把静态图定义为继承 Pipeline 的 partial class。两者在编译期都由同一个生成器读取，运行时也都表示一次有输入、依赖、策略、取消和结果的执行，但只有叶子可以直接嵌入图；复用一个图需要引入单独的 Pipeline-as-node 规则。

目标是让一个静态编排定义本身就是 Composite Step：根执行和嵌套执行不再是两套概念，所有编织都通过 Step 注册完成。同时必须保留 caller-owned DI scope、ref-struct 生命周期、异步安全、静态拓扑、直接调用和无运行时图等现有边界。

## 决策驱动与约束

| 类型 | 驱动或约束 | 证据或来源 | 优先级 |
| --- | --- | --- | --- |
| 硬约束 | Leaf 与 Composite 都必须能成为同一静态图中的强类型节点，使用相同依赖和节点策略 | 用户批准方向与 [Pipeline architecture](../architecture/pipeline-system.md) | 必须 |
| 硬约束 | Composite Step 必须是 ref struct，且自身不得跨越 await 或进入异步状态机 | 用户决定与 P3/P7 | 必须 |
| 硬约束 | IServiceProvider 与服务作用域由调用方拥有；Composite 仅在一次执行期间使用 provider 解析内部依赖 | P7 | 必须 |
| 硬约束 | DI 容器不得激活、缓存或装箱 ref-struct Step；根执行必须有可由常规 .NET DI 激活的入口 | ref-struct 语言约束与用户场景 | 必须 |
| 硬约束 | Configuration 只在编译期读取；不得引入反射、运行时图、接口装箱或动态分派 | P1/P4/P6 | 必须 |
| 硬约束 | Composite 身份必须显式，不能仅凭一个普通 `Configuration` 方法名猜测 | P6 | 必须 |
| 决策驱动 | 根执行与嵌套执行使用同一个定义，内部结果和拓扑保持封装 | 用户目标与产品意图 | 高 |
| 决策驱动 | DI、DisplayName、Logger、Retry、Timeout 和 DependsOn 的成本只出现在启用它们的节点 | ADR-0001 与 P2 | 高 |

## 选项与证据

| 选项 | 证据与置信度 | 满足驱动 | 决定性取舍 | 结果 |
| --- | --- | --- | --- | --- |
| 保持 Pipeline class 与 leaf ref-struct Step 二分 | 当前实现与架构记录，置信度高 | 满足现有行为 | 根与组合是不同概念，图不能天然作为节点复用 | 拒绝 |
| 保持由用户声明的 Pipeline class，但生成 Pipeline 节点适配器 | 能复用当前执行器，置信度高 | 满足嵌套和 DI | Pipeline 定义仍不是 Step，继续维护两套编排公共模型 | 拒绝 |
| static partial Composite 定义加生成 Invocation | 可让 Invocation 成为 ref struct，置信度高 | 满足运行边界 | 用户声明本身不是 Step，定义与实例仍分离 | 拒绝 |
| 普通 struct Composite 直接运行异步图 | struct 可进入异步状态机，置信度高 | 部分满足 | 允许隐式复制与 default 值，且不满足 ref-struct 决定 | 拒绝 |
| readonly ref partial struct Composite，根调用直接传 IServiceProvider | .NET 10 编译探针确认生成 partial 可读取 primary-constructor 输入，并可在不 await 的实例方法中返回静态核心 Task | 满足运行边界 | 每个应用入口都要手工传 provider，常规 DI 消费体验差 | 拒绝 |
| 为 Pipeline 构造器展开全部叶子服务 | 静态图可枚举依赖，置信度高 | 表面上是纯构造注入 | 提前解析未就绪节点服务，并改变 transient、keyed service、失败和多节点独立解析语义 | 拒绝 |
| readonly ref partial struct Composite + 生成的薄 Pipeline DI 门面 | ref struct 探针与当前 generated executor 的 provider 语义，置信度高 | 满足统一 Step、DI、延迟解析和调用方作用域所有权 | 每个根 Composite 增加一个可选引用类型门面及每个 DI 生命周期至多一个小对象 | 选择 |

## 决策

Step 家族有两种显式声明形式：

1. Leaf Step 是 readonly ref partial struct，用户实现现有同步或异步叶子执行契约。DI 参数由生成器在构造前解析；异步方法返回拥有后续状态的 Task，不让 Step 自身跨越 await。
2. Composite Step 是标记 `[CompositeStep]` 的 readonly ref partial struct。用户提供参数类型为无状态 `StepGraph` 的 `Configuration`，不手写执行方法。生成器根据内部图生成 `Results`、同步或异步 Composite 执行契约以及顶层运行入口。`StepGraph` 只负责声明节点；`StepBuilder`/`StepBuilder<TResult>` 只代表已注册节点的强类型句柄。
3. 每个 Composite Step 生成一个嵌套的 `public sealed class Pipeline`。它是可选的根执行与 DI 门面：公开 Execute/ExecuteAsync 方法接收 Composite 的强类型输入和 CancellationToken。只有传递子图包含服务参数或 Step Logger 时，公开构造器才接收并保存 `IServiceProvider`；纯计算图生成无参门面。全部执行状态仍是单次调用的局部状态。

Composite 的生成执行契约始终接收 `CancellationToken`，并且只在传递子图需要服务或 Logger 时接收 caller-owned `IServiceProvider`；异步契约返回 Task。生成的 ref-struct 实例方法只读取 primary-constructor 输入和 ADR-0001 定义的实例上下文，将这些值连同所需的 provider/token 复制给生成的静态执行核心，然后立即返回。真正跨越 await 的只有静态核心的参数与 Task 状态机。

一个 Composite 的 primary-constructor 参数是其强类型输入。它作为父图节点时，生成工厂接受这些输入，并只在子图需要服务时由父执行器传入相同 IServiceProvider；作为根时，生成的 `Composite.Pipeline` 将 Execute/ExecuteAsync 输入交给相同核心，并创建具有确定 DisplayName/可选 Logger 的 Composite 实例。Configuration 中每次 leaf 或 composite 注册都通过 `StepGraph` 返回同一 StepBuilder 家族，因此 DependsOn、Retry、Timeout 和 DisplayName 语义一致。现有 `Pipeline.Builder` 随用户声明的 Pipeline class 一并移除，不作为兼容别名保留。

`Composite.Pipeline` 不创建或释放 IServiceScope，不让容器解析任何 Step，也不把 provider 注入用户 Step。需要 DI 时，内部服务继续在对应节点依赖满足、即将执行时从门面持有的 caller-owned provider 解析；同一服务类型在不同节点仍是独立解析操作，keyed service 语义不变。门面可按应用需要注册为 scoped 或 transient；把持有 provider 的门面注册为 singleton 等同于调用方显式选择 root provider，库不隐式修正该生命周期。

Composite 的全部公开节点结果封装为一个生成的 `Results` 值，作为父图中的单一强类型结果；父图不展开子图。父节点 Retry 重启整个 Composite，子图各节点策略仍独立。取消向内传递，失败向外传播。生成器在编译期拒绝 Composite 引用循环、非 partial/ref/readonly 声明、手写执行冲突、动态图和不支持的 ref-like 输入。

不选择 Pipeline class、普通 struct、运行时图、仅靠方法名发现 Composite、隐式服务定位或将 ref struct 保存进异步状态机。

## 为什么现在做此决策

在首个带节点上下文和 Fluent 策略的发布候选冻结前统一组合模型，可避免先扩展 Pipeline class、再为它增加一套 Step 适配层。所选模型把编排抽象成本留在生成期，保持 Leaf/Composite 的直接调用和值语义；通过显式 Composite 契约承认内部 DI 解析需求，而不是把 provider 隐藏在 ref struct 的无效 default 状态中。

该方向符合 P1、P2、P3、P4、P6 与 P7，没有引入原则例外。若编译器无法保持所需的 partial ref-struct 访问、消费端需要运行时组合，或 Composite 必须持有跨调用可变状态，则需要重新评估。

## 证据与链接

- [Pipeline product intent](../product/README.md)
- [Pipeline design principles](../principles/README.md)
- [Pipeline compile-time and execution architecture](../architecture/pipeline-system.md)
- [Declaration-only pipelines](../architecture/named-executors.md)
- [ADR-0001 generated Step context](ADR-0001-generated-step-context.md)

## 后果与接受的取舍

- 根编排和嵌套编排成为同一种 Composite Step，Pipeline 不再是独立公共概念。
- 用户必须把现有 Pipeline partial class 迁移为 `[CompositeStep] readonly ref partial struct`，并把执行输入放入 primary constructor；这是源码与二进制破坏性变更。
- 应用通过生成的 `Composite.Pipeline` 获得普通构造注入体验；Leaf Step 仍只获得它声明的具体服务，而不访问 provider。
- 薄 Pipeline 门面不是 Step、图或作用域所有者；它只把 caller-owned provider 和一次调用输入送入 Composite 的生成执行核心。
- Composite Step 不能编写捕获自身的 async 实例执行；生成方法只转发到静态异步核心。
- 一个 Composite 的外层重试可能重复已完成的内部副作用；此语义必须在节点策略文档中明确。
- ref struct 不能装箱或存入普通集合；生成器必须始终以静态类型直接调用。
- ADR-0001 的 DisplayName、Logger 和精确 CS0282 抑制继续适用于 Leaf 与 Composite Step。
- 当前 Pipeline class 架构和相关文档在交付后被新的 Composite Step 架构替代，不保留运行时兼容层。

## 下游交付约束

- Leaf 和 Composite 注册必须进入同一个静态节点模型与 StepBuilder API。
- Composite 必须由 Attribute 与精确 `Configuration(StepGraph)` 符号共同识别；声明不完整时不生成部分执行器。
- 每个有效 Composite 必须生成无状态执行语义的嵌套 `Pipeline` 门面；仅当传递子图需要服务或 Logger 时，其构造器接收 IServiceProvider，节点服务仍按就绪时机逐次解析。
- 生成的 ref-struct 实例执行方法不得包含 await、捕获 this 或把任何 ref-like 值传入异步状态机。
- Composite 的 primary-constructor 输入必须在进入静态异步核心前按声明顺序复制；不支持的 ref-like 输入在编译期拒绝。
- IServiceProvider 只在传递子图需要服务或 Logger 时作为 Composite 执行参数向内传播，Pipeline 不创建或释放服务作用域；纯计算图不创建空 provider。
- 根与嵌套执行必须共享结果、取消、失败、策略和日志语义，并在编译期检测组合循环。
- 打包消费者验证必须覆盖 Leaf、根 Composite、嵌套 Composite、同步/异步、DI、warnings-as-errors 与 trimming。

## 退出要求

替换此方向必须继续提供根/嵌套统一、强类型输入与结果、静态循环检查、caller-owned DI、无运行时图，并对分配、ref-struct 生命周期、异步状态和迁移给出等价或更强证据。

## 后续与审查触发器

| 项目 | 所有者 | 到期或客观触发条件 | 状态 |
| --- | --- | --- | --- |
| 重新评估 Composite 执行上下文 | library maintainers | Composite 需要 provider 之外的运行时能力或直接执行体验无法保持清晰 | Open |
| 重新评估 ref-struct Composite | library maintainers | C# 生命周期规则改变，或真实组合图需要跨调用持有状态 | Open |
| 重新评估统一 Step 模型 | library maintainers | 产品需要动态/持久化图、外部调度或预编译插件式节点 | Open |
