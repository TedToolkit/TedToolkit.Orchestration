# ADR-0003：以静态函数声明 Leaf Step

- 状态：Accepted（由 ADR-0004 修订 Composite/Pipeline 条款）
- 日期：2026-09-10
- 决策所有者：library maintainers
- 批准来源：用户于 2026-09-10 在当前 Codex 任务中明确接受 ADR-0003 并要求开始实施。
- 决策范围：Pipeline 的 Leaf Step 声明、参数绑定、返回形状和节点日志身份。
- 适用产品意图：[Product intent](../product/README.md)
- 适用原则：[P1、P2、P3、P4、P6、P7、P8](../principles/README.md)
- 替代：[ADR-0001](ADR-0001-generated-step-context.md) 与 [ADR-0002](ADR-0002-unified-composite-step.md) 中仅适用于 Leaf Step 的决定
- 被修订：[ADR-0004](ADR-0004-function-declared-composite-and-pipeline.md) 替代本文关于 Composite 保持对象模型、Composite Logger 上下文与自动 Pipeline 的条款；本文的函数式 Leaf、参数分类、返回形状和 Logger 规则继续有效

> 修订说明：下文保留了作出本决策时的 Composite 背景。当前 Composite 与 Pipeline 契约以 ADR-0004 为准；本文仅继续治理函数式 Leaf 契约。

## 决策摘要

Leaf Step 由标记 `[Step]` 的静态方法声明；Composite Step 继续使用现有 `[CompositeStep]`、`Configuration(StepGraph)` 和 `StepBuilder` API 描述关系。普通方法参数是图数据输入，标记 `[FromServices]` 的参数按现有时机从调用方 `IServiceProvider` 解析，`CancellationToken` 由执行器提供；不生成或公开 `StepContext`、`StepExecutionInfo`、`DisplayName` 或 Logger 实例属性。

## 背景与决策问题

当前 Leaf Step 要求一个 `internal readonly ref partial struct` 实现四个同步/异步接口之一。业务参数位于构造函数，执行逻辑位于 `Execute`/`ExecuteAsync`，生成器还为每个实例补充 `DisplayName` 和可选 Logger 属性。这使一个本质上无持久状态的操作需要专门类型、接口和生成实例上下文。

用户要求只改变声明 API，不改变 Composite 图、节点策略、执行顺序、服务解析、重试、超时、取消、结果或失败语义。决策问题是：如何让 Leaf Step 成为静态函数，同时保持编译期图和直接生成执行，并让函数按显式参数获得服务和日志。

## 决策驱动与约束

| 类型 | 驱动或约束 | 证据或来源 | 优先级 |
| --- | --- | --- | --- |
| 硬约束 | Composite 声明和 Fluent 关系 API 保持现状 | 用户于 2026-09-10 的明确决定 | 必须 |
| 硬约束 | 调度、参数求值、DI、策略、取消、失败和结果语义不变 | 用户于 2026-09-10 的明确决定；[Pipeline architecture](../architecture/pipeline-system.md) | 必须 |
| 硬约束 | 图和调用绑定仍在编译期完成，不使用反射或运行时图 | P1、P4、P6 | 必须 |
| 硬约束 | `DisplayName` 只用于非泛型 Logger 类别，不暴露给 Step 函数 | 用户于 2026-09-10 的明确决定 | 必须 |
| 决策驱动 | Leaf 作者只声明静态函数及其真实依赖 | 用户批准方向；P2、P3 | 高 |
| 决策驱动 | 调用方继续拥有 provider、scope 和 cancellation | P7 | 高 |

## 选项与证据

| 选项 | 证据与置信度 | 满足驱动 | 决定性取舍 | 结果 |
| --- | --- | --- | --- | --- |
| 保持 ref-struct Leaf 和生成实例上下文 | 当前实现与测试，置信度高 | 满足运行约束 | 保留专用类型、接口和实例属性，不满足函数式声明目标 | 拒绝 |
| 静态函数接收统一 `StepContext` | 可集中提供服务和节点信息，置信度高 | 可执行 | 隐藏真实依赖，并把不应公开的 `DisplayName` 暴露给业务逻辑 | 拒绝 |
| 静态函数参数按类型隐式判断数据或服务 | 技术上可在运行期查询容器，置信度高 | 部分满足 | 同一类型可能既是数据又是服务，图签名依赖运行时注册 | 拒绝 |
| `[Step]` 静态函数，`[FromServices]` 显式区分服务 | 现有参数标记和生成式直接调用可复用，置信度高 | 满足全部硬约束 | 这是源码和二进制破坏性迁移；方法形状需要新的编译期诊断 | 选择 |

## 决策

Leaf Step 是标记 `[Step]` 的静态方法。首个交付只发现当前消费编译中的方法；方法必须声明在顶层、非泛型的 `internal` 或 `public static class` 中，自身为 `internal` 或 `public static`、非泛型且在所属类型内名称唯一。跨程序集 Leaf 发现、私有方法、嵌套或泛型容器以及重载 `[Step]` 方法不受支持。

方法只支持与当前契约等价的四种返回形状：`void`、非 ref-like 的 `TResult`、`Task` 和 `Task<TResult>`。方法必须恰有一个未标记 `[FromServices]` 的 `CancellationToken`，并把它放在最后；该 Token 不成为数据端口。其余参数必须按值且不是 ref-like、指针或函数指针。任何不满足这些条件的声明均产生编译期诊断，不生成执行回退。

未标记参数按声明顺序成为 Composite 工厂的数据输入。`[FromServices]` 继续支持普通和 keyed DI，并从生成的工厂参数中省略。唯一的 `CancellationToken` 是执行器参数，不是数据输入或 DI 服务。零个数据输入由零个未标记参数自然表达，不增加 `Inputless` 概念。

Composite Step 继续使用现有 readonly ref partial struct、`[CompositeStep]`、primary-constructor 数据输入、`Configuration(StepGraph)`、生成的嵌套 `Pipeline`、`StepBuilder` 及全部节点修饰器。生成器为 `[Step]` 方法提供同名 `StepGraph` 工厂，因此 Composite 配置的调用形状保持不变。

标记 `[FromServices]` 的 `ILogger<T>` 和其他服务一样解析。未带 key 的非泛型 `[FromServices] ILogger` 是编译期识别的特殊服务参数：生成执行器从 `ILoggerFactory` 创建一次 Logger，时机仍在节点就绪后、其他数据表达式及普通/keyed 服务解析之后、重试循环之外；类别为 `<Step 方法完整名称>[<DisplayName>]`。默认与 `WithDisplayName` 覆盖逻辑沿用现状。LoggerFactory 解析或 Logger 创建失败仍发生在第一次业务尝试前且不参与重试。带 key 的非泛型 ILogger 声明无明确类别语义，编译期拒绝。

不为 Leaf 生成 `DisplayName`、Logger、`StepContext` 或 `StepExecutionInfo` 方法参数或成员。`DisplayName` 仅存在于编译期节点模型和生成的 Logger 类别中。Step 需要的每项业务服务必须通过自己的 `[FromServices]` 参数显式声明。

Composite 上现有 `[StepLogger]`、生成的 `DisplayName`/Logger 实例上下文和精确 CS0282 抑制继续保留；这些能力不迁移到函数式 Leaf。`ICompositeStep<TResult>` 与 `IAsyncCompositeStep<TResult>` 继续作为生成的 Composite 执行契约。

生成代码继续直接调用具体方法，不进行反射、委托装箱、运行时方法发现或运行时图解释。四个现有 Leaf 接口 `IStep`、`IStep<TResult>`、`IAsyncStep`、`IAsyncStep<TResult>` 及 ref-struct Leaf 声明不保留兼容执行路径；消费者随匹配分析器重新编译并迁移源码。`[StepLogger]` 仅继续服务 Composite，不再适用于 Leaf。

## 为什么现在做此决策

静态方法直接表达 Leaf 的执行本质，并移除专用实例类型、接口、required 属性和 CS0282 抑制成本，同时仍允许生成器静态读取图、生成强类型局部值并直接调用。显式 `[FromServices]` 保持数据边与服务依赖的边界稳定；非泛型 Logger 的特殊绑定保留节点日志身份，而不重新引入通用上下文。

该方向继续满足 P1、P2、P4、P6、P7 和 P8。它偏离 P3 中“Step 使用栈上值”的既有表述，但以“执行期不存在 Step 对象”提供更强的无隐藏分配边界；接受后应更新 P3 和架构记录，而不是保留过时的 ref-struct 要求。

## 证据与链接

- [Pipeline product intent](../product/README.md)
- [Pipeline design principles](../principles/README.md)
- [Pipeline compile-time and execution architecture](../architecture/pipeline-system.md)
- [Composite Step declarations](../architecture/named-executors.md)
- [ADR-0001 generated Step context](ADR-0001-generated-step-context.md)
- [ADR-0002 unified Composite Step](ADR-0002-unified-composite-step.md)

## 后果与接受的取舍

- Leaf 声明从类型、构造函数和接口迁移到 Attribute 静态方法；这是有意的源码和二进制破坏性变更。
- Composite 的图、工厂调用、策略和根/嵌套执行体验保持不变。
- `DisplayName` 不再能被业务逻辑读取；它只参与节点日志类别。
- Leaf 的 `[StepLogger]` 被 `[FromServices] ILogger` 取代；Composite 的 `[StepLogger]` 保持不变，泛型 Logger 不采用动态类别。
- Leaf 不再具有框架管理的实例或 `IDisposable` 生命周期；需要资源清理的函数在其同步方法体或返回的异步操作中显式拥有清理。迁移证明必须维持当前清理顺序和异常传播结果。
- 分析器必须以方法符号而不是类型名识别 Step，并按上述容器、可见性、唯一名称、CancellationToken、参数和返回形状规则给出确定诊断。
- 日志创建、服务解析和固定参数求值仍发生在重试循环外；方法本体按每次尝试调用。

## 下游交付约束

- Composite 的公开声明、`[StepLogger]`、生成实例上下文、生成的 `Pipeline`、`ICompositeStep<TResult>`、`IAsyncCompositeStep<TResult>` 和 `StepBuilder` API 不得因 Leaf 迁移而改变。
- `void`/结果和同步/Task 四类方法必须映射到现有执行路径，不增加 ValueTask 或运行时 dispatch。
- 数据、普通服务、keyed 服务、CancellationToken 和非泛型 ILogger 必须在编译期得到唯一分类；无歧义时才生成执行器。
- 非泛型 Logger 的默认/覆盖 DisplayName、创建次数、失败时机和重试边界必须与现有 `[StepLogger]` 行为相同。
- 现有依赖就绪、并行启动、参数求值、重试、超时、取消、失败排空、结果收集和嵌套 Composite 语义必须由迁移后的测试继续证明。
- 匹配 runtime/analyzer 包与消费端重新编译仍是发布边界；不提供旧 Leaf API 兼容层。

## 退出要求

替换此方向必须继续提供编译期可判定的数据/服务边界、直接强类型调用、现有 Composite API、caller-owned DI 与取消，以及不弱于当前执行语义的证明；任何反射或运行时图回退需要新的已接受 ADR。

## 后续与审查触发器

| 项目 | 所有者 | 到期或客观触发条件 | 状态 |
| --- | --- | --- | --- |
| 重新评估方法可见性和跨程序集 Leaf 复用 | library maintainers | 消费者需要在另一个程序集的 Composite 中注册公开 Leaf 方法 | Open |
| 重新评估支持的异步返回形状 | library maintainers | 真实消费者需要 ValueTask 或自定义 awaitable 且能保持直接调用语义 | Open |
| 重新评估节点上下文 | library maintainers | Logger 类别之外出现必须暴露给业务函数的节点级能力 | Open |
