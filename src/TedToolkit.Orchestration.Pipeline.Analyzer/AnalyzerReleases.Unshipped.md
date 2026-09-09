; Unreleased analyzer rules.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
TTP001 | Pipeline | Error | Step result type mismatch
TTP009 | Pipeline | Error | Pipeline graph must be statically known
TTP012 | Pipeline | Error | Step types must be internal
TTP013 | Pipeline | Error | Steps require the supported readonly ref partial struct contract
TTP014 | Pipeline | Warning | Step factories outside configuration do not run work
TTP015 | Pipeline | Warning | Direct configuration calls do not execute the generated pipeline
TTP017 | Pipeline | Warning | Node modifier used outside configuration
