# ADR-0005：以任意命名函数声明 Composite Step

- 状态：Superseded
- 日期：2026-09-11
- 决策所有者：library maintainers
- 批准来源：用户于 2026-09-11 在当前 Codex 任务中确认根 Pipeline 保持生成 class，Composite Step 使用任意命名函数，不再生成 state/Step struct，`Results` 与公共 DSL 类型保持。
- 决策范围：Pipeline 的 Composite 声明识别、根门面命名和跨程序集 Composite 编译协议。
- 适用产品意图：[Product intent](../product/README.md)
- 适用原则：[P1、P2、P3、P4、P6、P7、P8](../principles/README.md)
- 替代：[ADR-0004](ADR-0004-function-declared-composite-and-pipeline.md)
- 被替代：[ADR-0006](ADR-0006-structural-composite-execution.md)
- 身份与多声明规则后续由：[ADR-0008](ADR-0008-method-identified-composite-functions.md) 替代

## 决策摘要

Composite Step 由首参数为 `StepGraph` 的任意命名静态 `void` 函数声明；函数名派生可选根 Pipeline class，而生成协议不再创建 Step 或 prepared-state struct。

## 背景与决策问题

ADR-0004 把 Composite 从用户声明的 ref struct 改成函数，却固定要求函数名为 `Configuration`，Composite 工厂仍以所属类型命名，并为跨程序集执行生成 prepared-state struct。固定名称不能表达函数自身的业务含义，隐藏 state 也让函数模型继续带有不必要的对象形态。

本决策回答如何让 Composite 保持纯函数声明，同时保留强类型结果、根 Pipeline、DI/Logger 时机、重试边界和跨程序集静态组合。

## 决策驱动与约束

| 类型 | 驱动或约束 | 证据或来源 | 优先级 |
| --- | --- | --- | --- |
| 硬约束 | Composite 声明函数可使用任意合法方法名；一个所属类型仍只声明一个 Composite | 用户决定；现有单 Composite owner 契约 | 必须 |
| 硬约束 | `[Pipeline]` 的默认 class 名由声明函数名加 `Pipeline` 派生 | 用户决定；既有 Pipeline 命名规则 | 必须 |
| 硬约束 | Composite Step 不生成 struct 或要求用户 `new`；根 Pipeline 仍生成 class | 用户决定 | 必须 |
| 硬约束 | 每个 Composite 的强类型 `Results` 保留；`StepGraph`、`StepBuilder<T>`、`StepArgument<T>` 不变 | 用户决定 | 必须 |
| 硬约束 | 服务和 Logger 在节点重试循环外解析一次，调用方继续拥有 provider、scope 和 CancellationToken | ADR-0004；现有回归测试 | 必须 |
| 硬约束 | 公开 Composite 继续支持无反射的跨程序集静态组合 | Product intent、P1、P4、P6 | 必须 |

## 选项与证据

| 选项 | 证据与置信度 | 满足驱动 | 决定性取舍 | 结果 |
| --- | --- | --- | --- | --- |
| 保持固定 `Configuration` 与 state struct | 当前实现，置信度高 | 不满足命名和无 Step struct 目标 | 协议简单但保留无业务意义名称与载体类型 | 拒绝 |
| 把 state struct 改成 class | C# 类型系统，置信度高 | 只满足字面上的无 struct | 仍保留无独立职责的对象与分配 | 拒绝 |
| 取消 state，调用方把 provider、路径和预解析服务直接传给生成执行函数 | 当前生成器已有强类型服务解析路径，置信度高 | 满足全部硬约束 | 协议升级，runtime 与 analyzer 必须匹配发布 | 选择 |

## 决策

任何满足现有 Composite 方法形状、且首参数恰为 `StepGraph` 的方法都是 Composite 候选，不再检查名称 `Configuration`。本 ADR 当时选择一个 owner 只允许一个 Composite、图工厂以 owner 类型命名；该身份与数量规则现已由 ADR-0008 替代。根 Pipeline 默认名称仍由实际声明函数名加 `Pipeline` 派生，显式 `[Pipeline(Name = ...)]` 规则不变。

跨程序集协议升级为版本 2。生成代码保留 `Results`，但删除 `__TedToolkitCompositeStepState` 和 prepare 方法。每个 Composite 生成且只生成一个名为 `__TedToolkitExecuteCompositeStep` 的协议方法，并标记 `[GeneratedCompositeStep(2, requiresServices)]`。方法的可见性与 Composite 声明一致，必须为 static、非泛型、非 async，且参数严格按以下顺序生成：

1. 当 `requiresServices` 为 true 时，首参数为 `IServiceProvider`，供子节点继续解析服务；否则省略。
2. 一个 `string` display-path 参数。
3. Composite 声明中 `StepGraph` 之后、CancellationToken 之前的全部业务与服务参数，保持原声明顺序、类型、nullability、默认值和 `[FromServices]` 标记。
4. 一个默认值为 `default` 的末尾 `CancellationToken`。

同步协议返回所属类型的 `Results`，异步协议返回 `Task<Results>`。consumer 由 marker 的 `requiresServices`、固定位置和保留的 `[FromServices]` 标记恢复各参数角色并验证完整签名。调用方在进入节点重试循环前完成直接服务与 Logger 解析，把解析值传入 execute；每次 attempt 只调用 execute。版本、保留名称、可见性、参数次序/角色、token 默认值或返回形状不匹配均诊断，不回退到反射或运行时图。

不改变公共 DSL marker 类型，也不把根 Pipeline 改为静态函数。

## 为什么现在作出此决策

最近完成的函数式迁移暴露出固定 `Configuration` 和 generated state 仍在表达旧对象模型。删除无职责载体符合 P2/P3；任意函数名和确定性派生 Pipeline 名符合 P6；保留静态强类型协议、调用方资源所有权和编译期图符合 P1/P4/P7。

## 证据与链接

- [Pipeline product intent](../product/README.md)
- [Pipeline design principles](../principles/README.md)
- [Pipeline architecture](../architecture/pipeline-system.md)
- [ADR-0004](ADR-0004-function-declared-composite-and-pipeline.md)

## 后果与接受的取舍

- `Configuration` 仍是合法名称，但不再特殊；已有源码可继续声明它。
- 协议版本 1 的 producer 不作为版本 2 Composite 被发现；包继续要求 runtime 与 analyzer 匹配并重新编译消费者。
- `Results` 保持强类型值结果容器；它不是 Step 本身。
- 取消 state 避免一个生成类型和一次 state 构造，但协议参数数量会随直接服务数量增长。

## 下游交付约束

- Composite 的方法形状、参数分类、图语义、结果属性、Pipeline class 构造与 Execute API 除名称来源外保持。
- 服务与非泛型 Logger 必须在重试外解析一次；嵌套子 Step 所需 provider 继续直接传递。
- 版本 2 协议必须验证方法、参数、结果、可见性和唯一性，不能回退到反射或运行时图。
- 生成代码不得声明 Composite Step 或 prepared-state struct。

## 退出要求

替换此方向仍须保留编译期可判定图、强类型直接调用、显式根入口、caller-owned DI/取消和跨程序集静态组合。

## 后续与审查触发器

| 项目 | 所有者 | 到期或客观触发条件 | 状态 |
| --- | --- | --- | --- |
| 重新评估单 Composite owner 限制 | library maintainers | 一个容器需要声明多个独立 Composite | Resolved by ADR-0008 |
| 重新评估协议参数规模 | library maintainers | 真实 Composite 的直接服务参数导致不可接受的生成签名或编译成本 | Open |
