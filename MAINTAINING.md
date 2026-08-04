# Maintaining Requisite

## Toolchain and checks

`global.json` starts at SDK 10.0.100 and uses `latestFeature`. This permits
servicing and later .NET 10 feature bands while preventing an implicit major
upgrade. Keep `LangVersion`, the pinned analysis level, and CI in sync when
changing the SDK line.

SourceLink and source-control queries are enabled for CI builds, where a real
commit is available. Ordinary local builds keep those queries off rather than
emitting empty or misleading provenance for an uncommitted working tree.

Run before proposing a release:

```sh
dotnet restore Requisite.slnx --locked-mode
dotnet format Requisite.slnx --verify-no-changes --no-restore
dotnet build Requisite.slnx -c Release --no-restore
dotnet test tests/Requisite.Tests/Requisite.Tests.csproj -c Release -f net8.0 --no-build
dotnet test tests/Requisite.Tests/Requisite.Tests.csproj -c Release -f net10.0 --no-build
dotnet run --project examples/PaymentFlow/PaymentFlow.csproj -c Release -f net8.0 --no-build
dotnet run --project examples/PaymentFlow/PaymentFlow.csproj -c Release -f net10.0 --no-build
dotnet pack src/Requisite/Requisite.csproj -c Release --no-build --no-restore
```

Inspect both `.nupkg` and `.snupkg`. The package must contain assemblies and XML
documentation for both target frameworks, the analyzer under
`analyzers/dotnet/cs`, the README, and both licenses. The runtime dependency
groups must remain empty.

## Versioning and compatibility

Update `VersionPrefix`; package, assembly, file, and informational versions are
derived from it. Informational versions gain source revision metadata in a
repository build. Package validation always compares the shipped TFMs. The
release workflow also uses the latest stable NuGet version as the API
compatibility baseline when one exists; the first release has no external
baseline.

Review analyzer release tracking files whenever adding or changing diagnostics.
Regenerate and commit package lock files after intentional dependency updates.

## Release

1. Update `VersionPrefix` and `CHANGELOG.md`.
2. Run all checks above and inspect the package.
3. Push a tag exactly matching `v<VersionPrefix>`, for example `v0.1.0`.

The tag workflow repeats formatting, both TFM tests, examples, package
validation, and package inspection. It creates or updates a GitHub release. If
the repository has a `NUGET_API_KEY` secret, it also pushes to NuGet; otherwise
it reports that publication was skipped and still retains release artifacts.
No publishing credential is assumed.

For a validated release whose tag workflow ran without credentials, configure
the protected `nuget` environment with `NUGET_API_KEY`, then run **Publish
NuGet package** with the released version. It downloads the exact `.nupkg` and
`.snupkg` already attached to the GitHub release rather than rebuilding them.
