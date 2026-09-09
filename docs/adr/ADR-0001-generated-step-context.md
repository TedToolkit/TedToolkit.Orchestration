# ADR-0001：生成 Step 实例上下文

- 状态：Accepted
- 日期：2026-09-08
- 决策所有者：library maintainers
- 批准来源：用户于 2026-09-08 在原始 Codex 任务中明确批准，并要求 CS0282 不出现在消费项目中。
- 决策范围：Pipeline 的 consumer-defined ref-struct Step 如何获得节点显示名与可选日志能力，直至该 Step 模型被重新审查。
- 适用产品意图：[Product intent](../product/README.md)
- 适用原则：[P1、P2、P3、P4、P6、P7](../principles/README.md)
- 替代：无
- 被替代：无

## 决策摘要

Pipeline 为每个受支持的 partial ref-struct Step 生成 required init 实例属性，并只为显式标记的 Step 生成按“完整类型名 + DisplayName”分类的 ILogger；匹配的分析器精确抑制由这些生成字段引起的 CS0282。

## 背景与决策问题

Pipeline 的 Step 是栈上 ref struct，并由生成执行器在每次尝试时直接构造。节点 DisplayName 属于一次注册而不是业务构造参数；可选日志同样需要同时知道 Step 类型与节点显示身份。现有构造函数 `StepMetadata` 能安全传值，但把框架上下文混入业务构造函数。用户要求业务 Step 直接读取生成属性，并以 Attribute 显式选择 ILogger。

C# 对跨 partial struct 声明的实例字段发出 CS0282，因为字段顺序未定义。常见 Step 的 primary constructor 会在用户声明中捕获字段，而 generated required auto-property 会在生成声明中引入 backing field。因此必须决定能否在不放弃 ref-struct 模型、不产生消费项目警告、也不引入运行时环境上下文的前提下提供生成属性。

## 决策驱动与约束

| 类型 | 驱动或约束 | 证据或来源 | 优先级 |
| --- | --- | --- | --- |
| 硬约束 | Step 保持 ref struct，执行器直接构造且每次重试创建新实例 | [Pipeline architecture](../architecture/pipeline-system.md) 与 P1/P3/P7 | 必须 |
| 硬约束 | 普通属性访问必须返回当前节点的实例值，不能使用反射、AsyncLocal、静态表或运行时图 | P1/P3/P6 | 必须 |
| 硬约束 | 生成上下文不能让正常消费项目产生 CS0282，也不能吞掉无关 partial struct 的诊断 | [Microsoft compiler guidance for partial declarations](https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/partial-declarations) 与 P6 | 必须 |
| 决策驱动 | DisplayName 不占用业务构造函数参数；ILogger 仅在 Attribute opt-in 时存在与创建 | 用户批准方向与 P2/P4 | 高 |
| 决策驱动 | 调用方继续拥有 IServiceProvider、ILoggerFactory 和 logger；Pipeline 不创建服务作用域 | P7 | 高 |
| 决策驱动 | 生成 API、运行时依赖和分析器作为匹配版本一起发布 | 产品意图与 Pipeline architecture | 高 |

## 选项与证据

| 选项 | 证据与置信度 | 满足驱动 | 决定性取舍 | 结果 |
| --- | --- | --- | --- | --- |
| 保留构造函数 `StepMetadata` | 已在当前候选中实现并通过测试，置信度高 | 满足布局与性能约束 | 污染每个需要显示名的业务构造函数，不满足所选使用体验 | 拒绝 |
| 用户手写 required 属性，生成器只验证并初始化 | C# 字段位于用户声明，不触发跨 partial 字段问题，置信度高 | 技术上满足 | 每个 Step 重复框架样板，Attribute 不能独立表达“需要 ILogger” | 拒绝 |
| 改用 partial class Step | 普通 class 可安全生成字段，置信度高 | 满足属性体验 | 每次尝试引入对象分配并放弃既有 ref-struct 生命周期边界 | 拒绝 |
| 使用 AsyncLocal 或静态关联表提供无字段属性 | 能避免生成实例字段，置信度高 | 不满足 | 隐藏运行时状态、并发身份与生命周期成本，违背 P1/P3/P7 | 拒绝 |
| 生成 required init 属性并精确抑制 CS0282 | 普通字段访问和对象初始化保持直接；CS0282 的根因与适用范围可由匹配分析器识别，置信度中高 | 满足 | 接受字段物理顺序未定义，并承担一个窄范围 DiagnosticSuppressor 的维护成本 | 选择 |

## 决策

受支持的 Step 必须是 partial ref struct。生成部分始终添加 `public required string DisplayName { get; init; }`；标记 `[StepLogger]` 时再添加 `public required Microsoft.Extensions.Logging.ILogger Logger { get; init; }`。生成执行器在每次构造 Step 时通过对象初始化器赋值。

分析器只在诊断目标是有效 Pipeline Step、该类型确实接收生成实例上下文、且未使用显式布局时抑制 CS0282。无关 partial struct、无效 Step 和用户自行拆分字段的诊断不得被抑制。Pipeline 不承诺或依赖 Step 实例字段的物理顺序。

ILoggerFactory 从调用方的 IServiceProvider 在节点就绪后、重试循环外解析一次，并为一次节点调用创建一个 Logger。Category 使用 `<Step fully-qualified type name>[<DisplayName>]`，所以类型与节点身份无需 Pipeline 拥有日志 Scope。缺失 factory 或创建 Logger 的失败是尝试开始前的终止失败，不参与重试。未标记 Step 不解析日志服务、不创建 Logger，也不增加日志相关执行代码。

不选择 framework-owned logging scope、NullLogger 回退、运行时上下文容器或 class Step。

## 为什么现在做此决策

节点级显示身份和日志是同一份实例上下文，必须在第一次发布前形成一致的构造模型。生成 required 属性符合 P1、P2 与 P4，让固定信息进入直接可读的生成代码；精确诊断抑制维持 P6，而不是要求所有消费项目全局关闭 CS0282。Logger 的动态 Category 避免新的 Scope 生命周期与异常优先级，同时维持 P7 的调用方服务所有权。

如果未来 C# 能让生成器在原 struct 声明中安全增加存储、Step 不再是值类型，或产品要求通用结构化日志作用域，应重新评估。

## 证据与链接

- [Pipeline product intent](../product/README.md)
- [Pipeline design principles](../principles/README.md)
- [Pipeline compile-time and execution architecture](../architecture/pipeline-system.md)
- [Declaration-only pipelines](../architecture/named-executors.md)
- [Microsoft C# partial declaration diagnostics](https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/partial-declarations)

## 后果与接受的取舍

- Step 作者获得直接的 `DisplayName`，并可通过 `[StepLogger]` 选择直接的 `Logger` 属性，不再传递 `StepMetadata`。
- 所有 Step 源声明必须迁移为 partial；直接构造必须初始化生成的 required 属性。这是明确的源码和二进制破坏性变更，需要匹配分析器重新编译。
- `Microsoft.Extensions.Logging.Abstractions` 成为 Pipeline runtime 的公共包依赖，但只有标记 Step 才产生日志运行时工作。
- Step 的物理字段顺序未定义且不受支持。显式布局、依赖字段地址/顺序的 unsafe 使用与此模型不兼容。
- 分析器必须维护一个窄范围 CS0282 suppressor，并以反例证明它不会抑制无关诊断。
- 动态 Category 的基数由节点显示名决定；DisplayName 是编译期常量，因此类别集合在编译时有界。

## 下游交付约束

- DisplayName 与可选 Logger 必须是 public required init 属性，并在每个新 Step 尝试上初始化。
- 默认 DisplayName 必须由静态注册源确定；配置覆盖值必须是非空编译期常量。
- Logger 的类型名与 DisplayName 必须体现在 Category；Pipeline 不建立日志 Scope。
- Logger 解析/创建失败不得进入业务重试；未标记 Step 不得触发日志服务解析。
- CS0282 抑制必须按 Pipeline Step 符号与生成上下文精确限定，并拒绝显式布局。
- 包必须携带匹配分析器及 Logging.Abstractions 依赖，且通过打包消费者边界验证。

## 退出要求

替换此方向必须继续提供确定的每节点显示身份、显式的日志 opt-in、无隐藏运行时上下文，并给出 ref-struct 布局、分配、生命周期和消费兼容性的等价或更强证据。

## 后续与审查触发器

| 项目 | 所有者 | 到期或客观触发条件 | 状态 |
| --- | --- | --- | --- |
| 重新评估 partial struct 存储 | library maintainers | C# 编译器消除相关布局警告或支持安全的生成存储注入 | Open |
| 重新评估日志身份模型 | library maintainers | 消费者需要结构化 Scope、固定类别基数或可配置 logger category | Open |
| 重新评估 Step 类型边界 | library maintainers | 产品需要 class、nested/generic、显式布局或预编译外部 Step | Open |
