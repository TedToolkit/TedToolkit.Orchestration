# ADR-0004：以函数声明 Composite Step，并显式选择 Pipeline 入口

- 状态：Superseded
- 日期：2026-09-10
- 决策所有者：library maintainers
- 批准来源：用户于 2026-09-10 在当前 Codex 任务中明确确认函数式 Configuration、Pipeline 执行参数与生成命名规则，并要求继续开发。
- 决策范围：Pipeline 的 Composite 声明、根执行入口，以及函数式 Leaf/Composite 的统一生成边界。
- 适用产品意图：[Product intent](../product/README.md)
- 适用原则：[P1、P2、P3、P4、P6、P7、P8](../principles/README.md)
- 替代：[ADR-0001](ADR-0001-generated-step-context.md)、[ADR-0002](ADR-0002-unified-composite-step.md)
- 修订：[ADR-0003](ADR-0003-function-declared-leaf-steps.md) 中“Composite 保持对象模型”“Composite 自动生成 Pipeline”和 Logger category 只含当前节点名的条款；ADR-0003 的函数式 Leaf、参数分类、返回形状、Logger 创建时机与方法身份条款继续有效
- 被替代：[ADR-0005](ADR-0005-named-composite-functions.md)

## 决策摘要

Leaf 是标记 `[Step]` 的静态函数；首参数为 `StepGraph` 的静态 `void Configuration` 函数声明 Composite，并由生成器编译成同一种 Step；`[Pipeline]` 可标记任一入口函数以额外生成只持有 DI、在执行方法接收业务输入的 Pipeline 类型。

## 背景与决策问题

ADR-0003 已将 Leaf 从 ref-struct 对象迁移为静态函数，但 Composite 仍是带 primary constructor、生成实例上下文、执行接口和嵌套 Pipeline 的 ref struct。该对象只承载编译期图描述和生成器元数据，没有独立运行时身份；根 Pipeline 又被每个 Composite 无条件生成，使“可组合 Step”和“可实例化入口”继续耦合。

需要把 Composite 与根入口也收敛到函数模型，同时保留静态拓扑、强类型参数、直接调用、DI 所有权、跨程序集组合和现有执行结果。

## 决策驱动与约束

| 类型 | 驱动或约束 | 证据或来源 | 优先级 |
| --- | --- | --- | --- |
| 硬约束 | Leaf 与 Composite 都是编译期可识别并直接调用的强类型 Step，不使用反射或运行时图 | [Product intent](../product/README.md)、P1、P4、P6 | 必须 |
| 硬约束 | `Configuration` 的业务参数采用与 `[Step]` 相同的数据、DI、默认值和 CancellationToken 分类 | 用户于 2026-09-10 的明确决定 | 必须 |
| 硬约束 | Pipeline 构造函数只接收所需 DI；每次执行的业务输入进入 `Execute`/`ExecuteAsync` | 用户于 2026-09-10 的明确决定、P7 | 必须 |
| 硬约束 | `[Pipeline]` 可标在 Leaf 或 Configuration；未标记时不生成根门面 | 用户于 2026-09-10 的明确决定 | 必须 |
| 硬约束 | 默认类型名为方法名加 `Pipeline`；显式 `Name` 是名称主体，后缀由生成器添加；冲突必须诊断 | 用户于 2026-09-10 的明确决定、P6 | 必须 |
| 硬约束 | `WithDisplayName` 只命名当前节点；非泛型 ILogger category 打印从根到当前 Step 的多级路径 | 用户于 2026-09-10 的明确决定 | 必须 |
| 决策驱动 | Composite 只描述 Step 关系，不保留实例上下文或执行接口 | 用户目标、P2、P3 | 高 |
| 决策驱动 | 公开 Composite 仍可被另一消费程序集静态发现和嵌套 | 现有包边界与回归测试 | 高 |

## 选项与证据

| 选项 | 证据与置信度 | 满足驱动 | 决定性取舍 | 结果 |
| --- | --- | --- | --- | --- |
| 保持 ref-struct Composite 与自动 Pipeline | 当前实现和测试，置信度高 | 保留现状 | 继续保留无运行时身份的对象、Logger 上下文和执行接口 | 拒绝 |
| 只移除 Composite Logger/接口 | 可局部减少 API，置信度高 | 部分满足 | Composite 仍是对象，Pipeline 仍与组合能力绑定 | 拒绝 |
| 函数式 Configuration，所有 Composite 自动生成 Pipeline | 可复用现有发射器，置信度高 | 部分满足 | 无法表达“只组合、不提供根入口” | 拒绝 |
| 函数式 Configuration 编译成 Step，`[Pipeline]` 显式生成根入口 | 与现有静态图和函数 Leaf 模型兼容，置信度高 | 满足全部硬约束 | 是破坏性 API 迁移，并需要版本化跨程序集编译协议 | 选择 |

## 规范性 API

### Configuration 声明

Composite 候选仅由可静态判定的形状识别：方法名为 `Configuration`，且第一个参数类型恰为 `StepGraph`。`[Pipeline]` 不会把其他方法变成 Composite；它标在非 `[Step]`、非上述候选的方法时只产生“无效 Pipeline 目标”诊断。合法声明必须满足：

- 所属类型是顶层、非泛型、`internal` 或 `public static partial class`；
- 方法是 `internal` 或 `public static`、非泛型、非 async、返回 `void` 且有方法体；
- 所属类型只有一个 `Configuration`，方法没有 `[Step]`，并且只有第一个参数是 `StepGraph`；
- `StepGraph` 之后的参数均按值传递、是闭合且非 ref-like/指针/函数指针类型；
- 一个未标记 `[FromServices]` 的 `CancellationToken` 可以省略；若声明则必须唯一且位于末尾；
- 其余未标记参数按声明顺序成为数据输入，`[FromServices]` 参数成为执行服务输入。数据参数的名称、类型、nullability 和显式默认值原样投影。

因此 `static void Configuration(StepGraph pipeline)` 是零数据输入 Composite；它不需要为了获得执行取消而声明 Token。Configuration 只由生成器读取，不在运行时调用；用户代码直接调用任何合法 Configuration 都是 analyzer error，不提供运行时含义。生成到 `StepGraph` 上的 Composite 工厂名是所属类型名，避免所有 Composite 都暴露为 `Configuration(...)`。

Configuration 上的普通 `[FromServices]`/keyed 服务在 Composite 节点开始执行后解析一次，并在内部图中作为同一执行输入复用；服务默认值不替代 DI。未带 key 的非泛型 `[FromServices] ILogger` 使用 ADR-0003 的特殊规则，在同一时机由 `ILoggerFactory` 创建一次；类别为 `<Configuration 完整方法名>[<从根到当前 Composite 的 DisplayPath>]`。带 key 的非泛型 ILogger 是诊断错误。

`WithDisplayName("X")` 只把当前节点的路径段设为 `X`，不替换父级路径。未覆盖时，根 Leaf 使用方法名、根 Composite 使用所属类型名；有变量的嵌套节点使用变量名，无变量的节点使用现有 `<工厂名>#<序号>` 默认段。执行器用 `/` 原样连接各段，例如 `Root/Import/Save`；路径只用于日志显示，不作解析键，因此段内已有 `/` 不进行转义。Leaf 和 Configuration 的非泛型 Logger category 均为 `<完整方法身份>[<DisplayPath>]`。泛型 `ILogger<T>` 仍由 DI 决定类别。

### Pipeline Attribute 与门面

运行时提供仅面向方法的 `PipelineAttribute`，其可选 `Name` 是不含 `Pipeline` 后缀的合法 C# 标识符。它可标在合法 `[Step]` 方法或合法 Configuration 上；其他目标产生诊断。

生成类型位于方法所属的 partial static class 内，是 `public sealed`（仅当所属类型和入口方法均为 public）或 `internal sealed`。默认名称始终把 `Pipeline` 追加到完整方法名；例如 `Run` 生成 `RunPipeline`、`Configuration` 生成 `ConfigurationPipeline`、`RunPipeline` 生成 `RunPipelinePipeline`。`[Pipeline(Name = "Import")]` 生成 `ImportPipeline`；显式 Name 已以 `Pipeline` 结尾是诊断错误。

最终类型名不得与同一所属类型中的任何用户成员或其他生成 Pipeline 类型同名。普通 Leaf 不做跨程序集发现；本地和引用程序集中的 Composite 工厂若产生同名同参数签名，未限定调用必须诊断，调用方可通过生成的 namespace/assembly-qualified extension carrier 精确选择。两个引用程序集包含相同 metadata-qualified Composite 类型时，C# 本身要求引用具有不同 `extern alias`；生成器保留准确的 assembly symbol 并在生成代码中使用这些 alias，缺少或重复 alias 时诊断，不生成歧义调用。工厂方法本身不编号、不加哈希且不任选一个。

Pipeline 类型有以下稳定形状：

- 入口直接或传递需要服务时，公开构造函数只接收 `IServiceProvider services`；否则公开无参构造；构造函数不接收或保存业务输入，也不创建/拥有 scope；
- 同步入口生成 `Execute(...)` 和 `ExecuteWithoutResults(...)`；异步入口生成 `ExecuteAsync(...)` 和 `ExecuteWithoutResultsAsync(...)`；
- `Execute*` 按声明顺序接收所有数据输入并复制名称、类型、nullability 和显式默认值，最后接收可选执行 Token；若声明方法没有 Token，则生成 `CancellationToken cancellationToken = default`，但当该名称已被数据参数占用时追加下划线直至唯一；若声明了 Token，则保留其名称并令其默认值为 `default`；
- Leaf 的 `Execute*` 返回原始结果形状；结果丢弃方法返回 `void`/`Task`。Composite 的 `Execute*` 返回其强类型 `Results`/`Task<Results>`，结果丢弃方法返回 `void`/`Task`；即使内部没有结果节点，也保留空 `Results` 和两个方法；
- `[FromServices]` 和 Configuration 的首个 `StepGraph` 不出现在执行方法中。调用方继续拥有 provider、scope 和 CancellationToken。

图工厂中的数据参数仍使用 `StepArgument<T>`。省略带默认值的数据参数表示采用声明默认值；无默认值的数据参数必须显式绑定。该规则对 Leaf、Composite、根 Pipeline 和嵌套 Composite 一致。

### 跨程序集 Composite 编译协议

公开 Composite 不使用用户接口或反射。runtime 提供以下版本化、面向编译器的公共标记，类型本身通过 `EditorBrowsable(Never)` 隐藏：

```csharp
namespace TedToolkit.Orchestration.Pipeline.CompilerServices;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedCompositeStepAttribute : Attribute
{
    public GeneratedCompositeStepAttribute(int version, bool requiresServices)
    {
        Version = version;
        RequiresServices = requiresServices;
    }
    public int Version { get; }
    public bool RequiresServices { get; }
}
```

协议版本 1 为每个合法 Configuration 在所属 partial static class 中生成一个 prepare-once / execute-per-attempt 协议：

- `__TedToolkitPrepareCompositeStep` 在 Composite 节点的数据表达式求值之后、外层重试/超时循环之前调用一次。它依次接收当 `requiresServices` 为 true 时的 `IServiceProvider` 和从根累计到当前 Composite 的 DisplayPath，并返回隐藏的嵌套只读值类型 `__TedToolkitCompositeStepState`。该方法解析 Configuration 自己声明的普通/keyed 服务、按完整路径创建其非泛型 ILogger，并在需要时保存 caller provider 与路径；传递子节点所需的服务仍由子节点按既有时机解析。
- `__TedToolkitExecuteCompositeStep` 标记 `[GeneratedCompositeStep(1, requiresServices)]` 与 `EditorBrowsable(Never)`。它先接收准备好的 state，再按声明顺序接收所有数据参数（保留名称、类型、nullability 和默认值），最后接收可选的 `CancellationToken = default`。同步协议返回嵌套 `Results`，异步协议返回 `Task<Results>`；每个外层 attempt 只调用此方法，不再次准备服务或 Logger。

两个方法、state 和标记的有效可见性均与 Configuration 一致；只有 public owner 中的 public Configuration 能被引用程序集发现。用户声明任一保留名成员是诊断错误。prepare 的 provider/路径分别以 `__services`、`__displayPath` 为名称起点；execute 的 state/Token 分别以 `__state`、用户声明 Token 名或 `cancellationToken` 为名称起点；若与该方法中的用户数据参数重名，生成器追加下划线直至唯一。下游 analyzer 依据 Attribute 的 `RequiresServices`、固定的两个方法/状态名称以及参数位置和类型识别协议，而不依赖具体参数名。

`StepGraph`、Configuration 的服务参数和用户声明的 Token 不直接进入 execute 协议签名。根 Pipeline 在每次 `Execute*` 调用中以所属类型名作为起始路径 prepare 一次；嵌套调用把当前节点段追加到父路径后 prepare 一次。DisplayPath 和 state 只属于生成器协议，不进入用户 Configuration、StepGraph 工厂或 Pipeline `Execute*` API，也不能被业务函数直接读取。

下游 analyzer 只接受其支持的完整协议版本，并从 attributed execute 方法验证对应 prepare/state、建立本地 `StepGraph` 工厂和两阶段直接静态调用。版本不受支持、任一协议成员/签名不匹配或同名协议多于一个时必须诊断，不得回退到反射、接口激活或运行时图。嵌套 `Results` 的有效可见性与 Configuration 一致，属性继续按图中有结果节点生成。runtime 与 analyzer 作为匹配版本发布；修改协议标记、版本、保留方法/state、参数顺序、返回形状或 Results 可见性，需要新的 ADR 和协议版本。

### 继续有效的 Leaf 与执行规则

ADR-0003 的 Leaf 契约继续有效：`[Step]` 方法支持 `void`、非 ref-like `TResult`、`Task`、`Task<TResult>`；必须有唯一、未标记且末尾的 `CancellationToken`；数据、普通/keyed 服务与非泛型 ILogger 的分类和解析时机不变。非泛型 Logger 的节点名扩展为本 ADR 定义的完整 DisplayPath。Pipeline 不引入 `StepContext`、`StepExecutionInfo`、DisplayName 或 DisplayPath 参数。

服务解析、固定参数求值和 Logger 创建保持在业务重试之外；依赖就绪、并行启动、重试、超时、取消、失败排空、清理和结果收集语义不变。

## 后果与接受的取舍

- 现有 Composite ref-struct 声明、执行接口和自动嵌套 `Pipeline` 是有意的源码与二进制破坏。
- 需要根执行的消费者必须显式添加 `[Pipeline]`，并迁移到方法名派生的嵌套类型。
- Configuration 参数成为 Composite 的唯一显式输入契约，不再使用 primary constructor 或生成实例属性。
- `CompositeStepAttribute`、`StepLoggerAttribute`、Composite 的生成 `DisplayName`/Logger 上下文、`ICompositeStep<TResult>`、`IAsyncCompositeStep<TResult>` 和相关 CS0282 抑制从公共 Pipeline 契约移除。DisplayName 只保留在节点编译模型及非泛型 ILogger 类别中。
- 包仍要求 runtime 与 analyzer 匹配并由消费者重新编译。

## 证据与链接

- [Pipeline product intent](../product/README.md)
- [Pipeline design principles](../principles/README.md)
- [Pipeline compile-time and execution architecture](../architecture/pipeline-system.md)
- [Composite Step declarations](../architecture/named-executors.md)

## 下游交付约束

- 未标 `[Pipeline]` 的 Step 不得生成根门面；合法标记必须生成唯一且符合上述 synopsis 的类型。
- 公开 Composite 的 producer/consumer 跨程序集嵌套必须只依赖版本 1 静态协议。
- Pipeline 构造函数不得捕获业务输入或拥有 provider/scope 生命周期。
- 除非泛型 Logger category 从单级名称扩展为多级 DisplayPath 外，现有依赖、并行、重试、超时、取消、清理、失败与结果语义不得改变。

## 退出要求

替换此方向必须继续提供编译期可判定的图与参数边界、直接强类型调用、显式根入口、caller-owned DI/取消和跨程序集静态组合；任何运行时图、反射发现或隐式 scope 所有权需要新的已接受 ADR。

## 后续与审查触发器

| 项目 | 所有者 | 到期或客观触发条件 | 状态 |
| --- | --- | --- | --- |
| 重新评估多 Configuration 支持 | library maintainers | 一个声明类型需要多个独立 Composite 图 | Open |
| 重新评估 Pipeline 类型位置 | library maintainers | 消费者无法把 Step 容器声明为 partial 或需要顶层 DI 注册类型 | Open |
| 重新评估跨程序集 Leaf 发现 | library maintainers | 消费者需要直接注册另一个程序集中的非生成 Leaf | Open |
