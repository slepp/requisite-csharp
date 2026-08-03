# Requisite for .NET

Requisite is a compact library for carrying data-handling requirements in C#
types:

- `Untrusted<T>` reaches `Trusted<T>` only through an explicit policy.
- `Probability`, `ConfidenceThresholds`, and `Confident<T>` validate confidence
  data before an exhaustive three-way gate.
- the highest confidence handler receives an opaque `Certain` capability with
  no public construction or inheritance path.
- `Fresh<T>` checks each value's TTL on every read and preserves stale metadata
  and the expired value for recovery.

The runtime library targets .NET 8 and .NET 10, uses C# 14 with nullable
annotations, and has no runtime dependencies.

## Install

```sh
dotnet add package Requisite --version 0.1.0
```

## Example

```csharp
using Requisite;

static bool TryId(string text, out int id) => int.TryParse(text, out id);
static void Approve(Certain _, Trusted<int> id) { /* sensitive action */ }

var raw = Untrusted.From(request.CustomerId);
if (!Trust.TrySanitize(raw, TryId, out Trusted<int>? customerId))
    return Results.BadRequest();

var quote = Fresh.Fetch(4.99m, TimeSpan.FromSeconds(30));
quote.Read().Switch(
    current => Charge(customerId, current),
    stale => Refresh(stale.Value, stale.Metadata));

Confident.Create(model.Approves, model.Confidence).Gate().Switch(
    (proof, approved) => { if (approved) Approve(proof, customerId); },
    _ => QueueReview(customerId),
    _ => RecordOnly(customerId));
```

`Gate<T>.Match` and `Gate<T>.Switch` require handlers for high, likely, and
unsure outcomes. Custom thresholds may raise the certain boundary but cannot
lower it below `0.95`.

Sanitization outputs are constrained to `notnull`; a policy that violates that
contract at runtime is rejected rather than producing `Trusted<T>` containing
`null`.

```csharp
var thresholds = ConfidenceThresholds.Create(likely: 0.75, certain: 0.99);
string action = forecast.Gate(thresholds).Match(
    (proof, value) => Execute(proof, value),
    value => Review(value),
    value => Record(value));
```

For deterministic freshness tests, pass a `TimeProvider`. Custom providers must
keep `GetTimestamp()` monotonic; wall-clock time is used only to establish the
initial age in `FetchedAt`. A stale read exposes `StaleValue<T>.Value` and
`Metadata`.

## Must-use diagnostics

The package includes a focused analyzer. `RQ0001` warns when `Gate()` or
`Read()` is used as a bare expression statement, because that silently skips
confidence or freshness handling.

```csharp
forecast.Gate(); // RQ0001
quote.Read();    // RQ0001
```

Handling the result removes the warning. Assigning to `_` is the explicit
opt-out when discarding is intentional. The analyzer does not attempt to make
all C# return values must-use, which would create noise.

## Scope

Requisite complements rather than replaces [Vogen](https://github.com/SteveDunn/Vogen)
and [LanguageExt](https://github.com/louthy/language-ext). Use Vogen for rich
domain primitives and LanguageExt for general result/effect composition;
Requisite focuses on transitions and capabilities required by receiving APIs.

The Rust library's `Live`/`with_live` API is intentionally omitted. C# cannot
enforce the same compile-time non-escape guarantee, so a similar-looking API
would overstate what the type system proves.

## Build

The repository uses .NET 10. `global.json` accepts newer .NET 10 feature bands
but does not roll into a later major version.

```sh
dotnet restore Requisite.slnx --locked-mode
dotnet format Requisite.slnx --verify-no-changes --no-restore
dotnet build Requisite.slnx --configuration Release --no-restore
dotnet test tests/Requisite.Tests/Requisite.Tests.csproj -c Release -f net8.0 --no-build
dotnet test tests/Requisite.Tests/Requisite.Tests.csproj -c Release -f net10.0 --no-build
dotnet pack src/Requisite/Requisite.csproj -c Release --no-build --no-restore
```

Tests include runtime coverage, analyzer coverage, Roslyn positive controls, and
compile-negative contracts for trusted sinks, capabilities, validated
construction, freshness metadata, closed gates, and confidence use.

See [MAINTAINING.md](MAINTAINING.md) for package and release checks.

## License

Licensed under either Apache-2.0 or MIT, at your option.
