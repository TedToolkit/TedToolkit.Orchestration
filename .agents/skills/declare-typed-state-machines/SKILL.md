---
name: declare-typed-state-machines
description: >-
  Use TedToolkit.Orchestration.StateMachine for compile-time enum-based transitions with guards, lifecycle callbacks, and trigger APIs.
---

# Declare Typed State Machines

- Model states and triggers explicitly. Declare routes and guards in the machine definition and build to validate unsupported or ambiguous transitions.
- Select the generated strict, `Try...Async`, or `Can...Async` trigger API according to whether the caller should throw, attempt a transition, or check availability.
- Verify a successful route, a rejected guard, and relevant entry or exit ordering using the generated consumer API.
