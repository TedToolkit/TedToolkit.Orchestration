# ADR-0007：Pipeline 保留自然返回形状

- 状态：Accepted
- 日期：2026-09-11
- 决策所有者：library maintainers
- 批准来源：用户于 2026-09-11 在当前 Codex 任务中明确拒绝无业务意义的 `Unit`，确认无值 Pipeline 直接返回 `void` 或 `Task`，并要求完成后继续交付。
- 决策范围：Pipeline 返回模型、Composite 生成形态和跨程序集结构协议。
- 适用产品意图：[Product intent](../product/README.md)
- 适用原则：[P1、P2、P3、P4、P6、P7、P8](../principles/README.md)
- 替代：[ADR-0006](ADR-0006-structural-composite-execution.md)
- 被替代：无
- Composite 身份与同 owner 多声明由：[ADR-0008](ADR-0008-method-identified-composite-functions.md) 扩展

## 决策摘要

每个 Pipeline 仍只提供一个 `Execute` 或 `ExecuteAsync`，但返回形状忠实于根函数：有值 Leaf 返回业务值，无值 Leaf 返回 `void` 或 `Task`；不引入 `Unit`。Composite 继续生成并返回 `<函数名>Result`。Composite 的同名执行重载和无 Attribute 的严格跨程序集结构协议保持不变。

## 背景与决策问题

ADR-0006 为了让所有 Pipeline 都“返回结果”，给 `void` 和非泛型 `Task` Leaf 的 facade 引入了空 `Unit` 值。Pipeline facade 并不实现要求统一返回类型的公共泛型接口，调用者也不会把该值传入图中，因此 `Unit` 只增加一个公共类型和一次无意义的 `return default`，没有承载业务或编排语义。

本决策回答如何在保留单一 Pipeline 入口、Composite 命名结果和严格静态协议的同时，让无值 Step 保持自然的 C# 返回形状。

## 决策驱动与约束

| 类型 | 驱动或约束 | 证据或来源 | 优先级 |
| --- | --- | --- | --- |
| 硬约束 | 无业务结果时不得制造占位结果类型 | 用户决定；P2、P3 | 必须 |
| 硬约束 | 每个 Pipeline class 只公开一个 `Execute` 或 `ExecuteAsync` | 用户此前决定 | 必须 |
| 硬约束 | `void`/`Task` Leaf 保持合法，facade 分别返回 `void`/`Task` | 用户决定 | 必须 |
| 硬约束 | Composite 始终返回与声明名关联的命名 Result | 用户此前决定 | 必须 |
| 硬约束 | 不恢复 `ExecuteWithoutResults*`、保留名称 core 或协议 Attribute | 用户此前决定；P1、P4、P6 | 必须 |
| 硬约束 | 跨程序集协议必须在生成调用前完整、唯一地验证 | P6；既有候选审查证据 | 必须 |
| 硬约束 | DI、Logger、retry、timeout、cancellation、并行清理和 caller-owned scope 语义保持 | Product intent；P7 | 必须 |

## 选项与证据

| 选项 | 证据与置信度 | 决定性取舍 | 结果 |
| --- | --- | --- | --- |
| 保留公共 `Unit` | 当前未完成候选，置信度高 | 统一了表面返回形状，但增加无语义公共 API | 拒绝 |
| 无值 facade 恢复第二个丢弃结果入口 | ADR-0004/0005 历史实现，置信度高 | 自然返回，但重新制造重复入口 | 拒绝 |
| 单一入口忠实返回 `void`/`Task` | C# 原生函数模型，置信度高 | 有值与无值 facade 返回形状不同，但不引入占位抽象 | 选择 |
| 禁止无值 Leaf | 现有副作用 Step 场景，置信度高 | 迫使业务制造无意义返回值 | 拒绝 |

## 决策

根 Pipeline 继续生成 nested sealed class，且只公开一个执行入口。同步有值 Leaf 的 `Execute` 返回 `TResult`，异步有值 Leaf 的 `ExecuteAsync` 返回 `Task<TResult>`；同步无值 Leaf 的 `Execute` 返回 `void`，异步无值 Leaf 的 `ExecuteAsync` 返回 `Task`。不得生成或公开 `Unit`，也不得恢复 `ExecuteWithoutResults` 或 `ExecuteWithoutResultsAsync`。

Composite 声明继续是首参数为 `StepGraph` 的任意命名静态 `void` 函数。生成器为每个声明函数在同一 owner 中生成 `<函数名>Result` 和唯一的同名静态执行重载；同步返回该 Result，异步返回 `Task<Result>`。Pipeline facade 只是该执行重载的 class 包装，不新增第二条执行路径。同 owner 多 Composite 和函数身份规则由 ADR-0008 定义。

跨程序集 consumer 继续通过声明和同名执行重载的完整结构进行静态识别，不使用 `GeneratedCompositeStepAttribute`、反射或运行时回退。结构候选若缺失、重复或畸形必须报告 `TTP019` 并停止调用生成。若声明包含任何 `[FromServices]` 参数，执行重载必须包含前导 `IServiceProvider`；该一致性必须在候选被接受前验证。结构候选也可因图内部传递的服务需求包含 provider。

无值 Leaf 继续使用非泛型 `StepBuilder`，只参与执行和控制依赖，不形成数据边。Composite Result 只包含有业务结果且被命名的节点属性。

## 为什么现在作出此决策

当前交付尚未完成，`Unit` 只存在于未发布候选中。此时删除它可以直接恢复自然函数语义，不需要兼容适配或迁移占位值。单一入口、命名 Composite Result 和严格结构协议均不依赖统一返回类型，因此无需为形式一致性支付公共 API 成本。

## 证据与链接

- [Pipeline product intent](../product/README.md)
- [Pipeline design principles](../principles/README.md)
- [Pipeline architecture](../architecture/pipeline-system.md)
- [ADR-0006](ADR-0006-structural-composite-execution.md)

## 后果与接受的取舍

- 调用无值 Pipeline 时不再出现无意义的返回值；同步调用是普通语句，异步调用仍可直接 `await`。
- facade 的返回类型不再被一个统一“总有结果”规则覆盖；它明确跟随根函数是否有业务值。
- Composite 仍总有命名 Result，因此其跨程序集协议和嵌套组合形状不变。
- `Unit` 未进入已发布基线，无需提供兼容类型或弃用周期。
- 结构协议必须额外校验声明服务参数与 provider 形状的一致性，避免接受随后无法调用的 metadata 成员。

## 下游交付约束

- 删除 `Unit` 类型及所有生成、测试、文档引用。
- 保持每个 facade 只有一个自然返回形状的执行入口。
- 保持 Composite 命名 Result、同名唯一执行实现和无 Attribute 协议。
- 为缺少必需 provider 的 service-bearing metadata 协议增加拒绝证明。
- 当前 README、架构、原则和迁移文档只描述本决策。

## 退出要求

任何替代方向仍须保持无占位业务值、单一 facade 入口、Composite 命名结果、编译期严格协议和 caller-owned 资源边界。

## 后续与审查触发器

| 项目 | 所有者 | 到期或客观触发条件 | 状态 |
| --- | --- | --- | --- |
| 重新评估统一返回抽象 | library maintainers | Pipeline 引入确实要求统一结果类型的公共泛型接口 | Open |
| 重新评估结构协议版本握手 | library maintainers | producer/consumer 需要在不重编译情况下协商多协议版本 | Open |
