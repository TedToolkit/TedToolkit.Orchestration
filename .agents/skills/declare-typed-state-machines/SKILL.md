---
name: declare-typed-state-machines
description: >-
  Use TedToolkit.Orchestration.StateMachine for compile-time enum-based transitions with guards, lifecycle callbacks, and trigger APIs.
---

# Declare Typed State Machines

Start with a transition table of current state, trigger, guard, destination, and side effects. Use it to distinguish unhandled triggers from expected guard rejection.

## Workflow

- Model states and triggers explicitly. Declare routes and guards in the machine definition and build to validate unsupported or ambiguous transitions.
- Select the generated strict, `Try...Async`, or `Can...Async` trigger API according to whether the caller should throw, attempt a transition, or check availability.
- Verify a successful route, a rejected guard, and relevant entry or exit ordering using the generated consumer API.

## Example

```csharp
var door = new DoorMachine();
door.TransitionCompleted += (_, transition) =>
    Console.WriteLine($"{transition.Source} -> {transition.Destination}");
await door.OpenAsync();
```

## Check the result

Test the initial state, a valid transition, and a guard rejection. Assert observable entry/exit effects and transition notification order. See [the complete door example](references/door-example.md) for generated declarations.
