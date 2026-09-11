; Unreleased analyzer rules.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
TTP001 | Pipeline | Error | Step result type mismatch
TTP009 | Pipeline | Error | Pipeline graph must be statically known
TTP013 | Pipeline | Error | Step methods require the supported static function contract
TTP014 | Pipeline | Warning | Step factories outside a Composite declaration do not run work
TTP015 | Pipeline | Error | Composite declarations are declaration-only and cannot be called directly
TTP017 | Pipeline | Warning | Node modifier used outside a Composite declaration
TTP018 | Pipeline | Error | Pipeline attributes require a valid, uniquely named function entry
TTP019 | Pipeline | Error | Referenced Composite same-name execution structure is missing, malformed, or ambiguous
