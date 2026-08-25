---
name: ".NET Project and Build Standards"
description: "Use when editing csproj files, Directory.Build.props, Directory.Packages.props, or NuGet package metadata. Covers central package management, target frameworks, analyzer and warning settings, trimming and AOT flags, deterministic builds, Source Link, and package metadata requirements."
applyTo: ["**/*.csproj", "**/Directory.Build.props", "**/Directory.Packages.props", "**/*.props", "**/*.targets"]
---

# .NET Project and Build Standards

## Target frameworks

- Multi-target `net8.0;net10.0`. Do not add `netstandard2.0` or .NET Framework targets.
- Set `LangVersion` to `latest` only in `Directory.Build.props`, never per project.

## Central package management

- All package versions live in `Directory.Packages.props` via `<PackageVersion>`.
- Project files use `<PackageReference Include="..." />` with **no** `Version` attribute.
- `ManagePackageVersionsCentrally` and `RestorePackagesWithLockFile` are both `true`.
- Commit `packages.lock.json`. CI restores with `--locked-mode`.

## Required properties in `Directory.Build.props`

```xml
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<AnalysisLevel>latest-recommended</AnalysisLevel>
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>  <!-- set in CI only -->
<Deterministic>true</Deterministic>
<PublishRepositoryUrl>true</PublishRepositoryUrl>
<EmbedUntrackedSources>true</EmbedUntrackedSources>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

For every shipping library:

```xml
<IsTrimmable>true</IsTrimmable>
<IsAotCompatible>true</IsAotCompatible>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
```

## Dependencies

- Every added dependency needs a justification. Prefer the BCL.
- A dependency that pulls a native asset or a large transitive graph belongs in a separate opt-in
  package, not the core library.
- Never let an optional integration's dependencies leak into the core package — isolate behind an
  abstraction defined in the core.
- Do not add a package to work around something a dozen lines of BCL code would solve.

## Package metadata

Every packable project sets: `PackageId`, `Title`, `Description`, `Authors`,
`PackageLicenseExpression`, `RepositoryUrl`, `PackageTags`, `PackageReadmeFile`, `PackageIcon`,
and `PackageReleaseNotes`.

- Tags must include the terms users will actually search for, especially when the package id does
  not contain them.
- Do not imply official status or endorsement by an upstream project in package metadata.

## Public API tracking

- Ship `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` with the public API analyzer enabled,
  so an unintended breaking change fails the build rather than surfacing after release.
- Follow SemVer 2.0.0. A breaking public API change is a major bump with a documented migration
  note. A behavioural default change is at minimum a minor bump with prominent release notes.

## Analyzer settings

Configure severities in `.editorconfig`, not in project files. At minimum, treat as errors:

| Rule | Enforces |
|---|---|
| CA1305 | Invariant culture on format/parse |
| CA2007 | `ConfigureAwait` on awaits in library code |
| CA1063 / CA1816 | Correct `IDisposable` implementation |
| CA2016 | Forward `CancellationToken` to callees |
| CA1849 | No sync calls in async methods |
| CA5350 / CA5351 | No weak cryptography |

Ban symbols that must never reach shipping code via `BannedSymbols.txt` — for example
`System.Console.Write`, `System.Console.WriteLine`, and `DateTime.Now`.
