# ADR-0008：以函数标识 Composite Step

- 状态：Accepted
- 日期：2026-09-11
- 决策所有者：library maintainers
- 批准来源：用户于 2026-09-11 在当前 Codex 任务中确认同一 class 支持多个 Pipeline/CompositeStep，并批准以函数名作为 Composite 身份后继续实现、审查和提交。
- 决策范围：Pipeline 的 Composite 工厂身份、同 owner 多声明、生成命名和跨程序集结构协议。
- 适用产品意图：[Product intent](../product/README.md)
- 适用原则：[P1、P2、P3、P4、P6、P7、P8](../principles/README.md)
- 相关：[ADR-0005](ADR-0005-named-composite-functions.md)、[ADR-0007](ADR-0007-natural-pipeline-return-shapes.md)

## 决策摘要

Composite Step 的身份是声明函数，而不是所属 class。同一 top-level static partial class 可声明多个不同名称的 Composite 函数；每个函数独立生成工厂、命名 Result、同名执行重载和可选 Pipeline facade。

## 背景与决策问题

现有方向已把 Step 与 Composite 改为函数，但 Composite 工厂仍以所属类型命名，并限制一个 owner 只能有一个 `StepGraph` 声明。这使 class 继续承担无必要的单实例身份，也阻止相关 Composite 函数自然归组。

本决策回答多个 Composite 函数如何在同一 owner 中被声明、组合、生成和跨程序集识别，同时保持编译期确定性。

## 决策驱动与约束

| 类型 | 驱动或约束 | 证据或来源 | 优先级 |
| --- | --- | --- | --- |
| 硬约束 | 同一 class 支持多个 Pipeline 与 CompositeStep | 用户决定 | 必须 |
| 硬约束 | Composite 是函数，工厂和生成类型按函数名区分 | 用户决定；P2、P6 | 必须 |
| 硬约束 | 每个 Composite 保留独立命名 Result 和唯一同名执行重载 | ADR-0007 | 必须 |
| 硬约束 | 跨程序集识别保持完整、唯一、无 Attribute、无反射 | ADR-0007；P1、P4 | 必须 |
| 硬约束 | 不恢复 Step struct、state、core、Unit 或重复执行入口 | ADR-0007 | 必须 |

## 选项与证据

| 选项 | 证据与置信度 | 决定性取舍 | 结果 |
| --- | --- | --- | --- |
| 保持一个 owner 一个 Composite | 当前生成器，置信度高 | 协议简单，但 class 被迫充当 Composite 身份 | 拒绝 |
| 继续以 owner 名生成工厂并增加二级选择 API | C# API 设计，置信度高 | 可区分函数，但引入与普通函数调用不同的额外层级 | 拒绝 |
| 以 Composite 函数名生成图工厂 | 既有 Leaf 函数模型与生成命名规则，置信度高 | 不同 owner 的同名函数可能需要限定扩展 carrier | 选择 |

## 决策

每个满足 Composite 声明形状的不同名称函数都是独立 Composite Step。所属 class 只是代码组织和生成成员容器，不限制 Composite 数量。

在 `StepGraph` 中，Composite 工厂名等于声明函数名，例如 `Import(StepGraph, ...)` 由 `steps.Import(...)` 组合。每个函数生成 `<函数名>Result`、唯一的同名静态执行重载；标记 `[Pipeline]` 时再生成默认 `<函数名>Pipeline`，显式 Pipeline Name 规则保持不变。同一 owner 的多个 Leaf 或 Composite Pipeline 只要最终 facade 名不同即可并存。

同一 owner 不允许 Composite 声明函数重载。重载会争用同一个图工厂、Result 和执行协议名称，生成器必须诊断而不能猜测或自动改名。用户成员占用任一函数的 Result、执行或 Pipeline 名时继续按现有规则诊断。

跨程序集 consumer 以“精确 owner 类型 + Composite 声明函数名 + 对应 Result + 同名执行重载”识别每个协议。一个 owner 可暴露多个协议；每个被请求函数独立验证，某个协议畸形不影响其他合法函数。不同 owner 暴露同名工厂时沿用限定生成扩展 carrier 的消歧规则；相同 metadata 类型和函数名来自不同程序集时继续要求 distinct extern aliases。

## 为什么现在作出此决策

实际示例需要把多个可复用 Composite 函数归组在一个 class 中。函数已经承担输入、命名和执行身份，继续让 owner 独占 Composite 与函数模型冲突。以函数名统一 Leaf 与 Composite 的图调用方式，减少一项特殊规则，并保持静态可判定。

## 后果与接受的取舍

- 现有 `steps.<owner type>(...)` Composite 调用改为 `steps.<declaration function>(...)`，是当前未发布候选内的源码兼容变化。
- 同 owner 可生成多个 Result、执行重载和 Pipeline class；它们均按函数名确定区分。
- 同名 Composite 重载明确不支持；需要不同业务名称。
- metadata 搜索和冲突检测从类型粒度变为类型与函数联合粒度。

## 下游交付约束

- 本地生成、图读取、扩展 carrier、hint name、重绑定和 metadata 协议收集必须使用同一函数身份。
- 测试必须覆盖同 owner 多 Composite、多 Pipeline、跨程序集多协议、独立畸形拒绝和命名冲突。
- README、架构和迁移文档必须展示函数名工厂语法，不再把 owner 名描述为 Composite 工厂。

## 退出要求

替代方向仍须支持同 owner 多 Composite、命名结果、唯一静态执行路径、确定性跨程序集调用，以及无 Step 对象和无反射执行。

## 后续与审查触发器

| 项目 | 所有者 | 到期或客观触发条件 | 状态 |
| --- | --- | --- | --- |
| 重新评估 Composite 重载 | library maintainers | 出现无法通过业务命名表达、且确需同名重载的真实 API | Open |
| 评估大量同 owner 声明的生成规模 | library maintainers | 真实项目出现显著编译时间或生成文件规模回归 | Open |
