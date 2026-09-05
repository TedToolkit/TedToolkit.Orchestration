; Unreleased analyzer rules.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
TTP001 | Pipeline | Error | Step result type mismatch
TTP004 | Pipeline | Error | Step cannot be constructed
TTP008 | Pipeline | Error | Unsupported constructor parameter
TTP009 | Pipeline | Error | Pipeline graph must be statically known
TTP012 | Pipeline | Error | Step types must be internal
TTP013 | Pipeline | Error | Steps require a supported ref struct contract and valid compile-time policies
TTP014 | Pipeline | Warning | Step factories outside configuration do not run work
TTP015 | Pipeline | Warning | Direct configuration calls do not execute the generated pipeline
TTP016 | Pipeline | Info | Direct execution bypasses StepPolicy
