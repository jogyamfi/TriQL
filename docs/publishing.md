# Publishing TriQL to NuGet.org

Release checklist for the four shipping packages: `TriQL.Client`, `TriQL.Client.Auth`,
`TriQL.Client.Compression`, and `TriQL.Data.ADO`.

## Prerequisite: credential hygiene

Before the first public release, confirm no credentials are committed or embedded in local git
configuration:

```powershell
git remote -v   # must NOT contain a token in the URL
```

If the remote URL contains an embedded personal access token, revoke it at
<https://github.com/settings/tokens>, then reset the remote and authenticate via the Git
Credential Manager or `gh auth login`:

```powershell
git remote set-url origin https://github.com/jogyamfi/TriQL.git
```

## Packaging setup (already in place)

| Concern | Where it lives |
| --- | --- |
| Shared package metadata, Source Link, deterministic build, `.snupkg` symbols | `Directory.Build.props` |
| Per-package `PackageId`, `Title`, `Description`, `PackageTags` | `src/*/*.csproj` |
| License expression `Apache-2.0` + license text | `Directory.Build.props`, `LICENSE` |
| Package README (repo `README.md`, packed to the package root) | `Directory.Build.props` |
| Tag-triggered pack/push/GitHub Release | `.github/workflows/release.yml` |
| Public API surface lock | `src/*/PublicAPI.Shipped.txt` |

`VersionPrefix` in `Directory.Build.props` is a fallback for local and CI builds only. Released
builds are stamped from the git tag by the release workflow (`-p:Version=<tag without leading v>`),
which overrides it.

## One-time setup on nuget.org

### 1. Reserve the `TriQL.` package ID prefix

Sign in to nuget.org, go to **Manage Packages** and request prefix reservation for `TriQL.*`.
Without a reservation anyone can publish a package named `TriQL.Anything`. Do this before the
first push.

### 2. Configure trusted publishing (OIDC)

Trusted publishing avoids storing a long-lived API key in the repository. On nuget.org, open the
username menu → **Trusted Publishing** → add a policy:

- **Repository Owner:** `jogyamfi`
- **Repository:** `TriQL`
- **Workflow File:** `release.yml` (file name only — no `.github/workflows/` prefix)
- **Environment:** leave empty (the release workflow does not use a GitHub environment)

Then add a GitHub repository secret:

- `NUGET_USER` — your nuget.org **profile name** (not your email address)

The workflow uses `NuGet/login@v1` to exchange the GitHub Actions OIDC token for a nuget.org API
key valid for one hour, then pushes with it.

> A newly created policy is only **temporarily active for 7 days**. It becomes permanent after the
> first successful publish (which supplies the GitHub repository and owner IDs that lock the policy
> to this repo). Do not create the policy and then leave it unused for weeks.

## Versioning policy

- The release workflow derives the package version from the tag: `v1.2.3` produces `1.2.3`.
- **Versions on nuget.org are permanently immutable.** A published version can be unlisted but
  never deleted or overwritten. Publish a preview first.
- Ship all four packages in **lockstep** on a single version. They inter-depend via
  `ProjectReference`, so packing `TriQL.Data.ADO 1.0.0` pins `TriQL.Client` to exactly `1.0.0`.
- From `1.0.0` onward, follow [Semantic Versioning](https://semver.org):
  - **major** — breaking public API change
  - **minor** — additive API, backwards compatible
  - **patch** — bug fixes only
- The public API surface is locked by `PublicAPI.Shipped.txt` in each shipping project and enforced
  at build time by `Microsoft.CodeAnalysis.PublicApiAnalyzers`. When a release adds API, move the
  entries from `PublicAPI.Unshipped.txt` into `PublicAPI.Shipped.txt` as part of the release commit.

### Suggested sequence

| Tag | Purpose |
| --- | --- |
| `v1.0.0-preview.1` | First public package. Validates the whole pipeline against immutable, low-stakes versions. |
| `v1.0.0-preview.N` | Iterate on feedback. |
| `v1.0.0` | Stable. API is now locked under SemVer. |

## Release procedure

### 1. Prepare the release commit

- Update the README status section if the stability level changed.
- Move any `PublicAPI.Unshipped.txt` entries to `PublicAPI.Shipped.txt`.
- Ensure `packages.lock.json` files are current and committed — the release workflow restores with
  `--locked-mode` and will fail if they are stale.

### 2. Pre-flight locally

```powershell
dotnet restore TriQL.slnx --locked-mode
dotnet build TriQL.slnx -c Release
dotnet test TriQL.slnx -c Release
dotnet pack TriQL.slnx -c Release -p:Version=1.0.0-preview.1 -o ./artifacts
```

Every command must succeed with zero warnings (`TreatWarningsAsErrors` is enabled).

### 3. Inspect a package before shipping

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead("$PWD\artifacts\TriQL.Client.1.0.0-preview.1.nupkg")
$zip.Entries.FullName
$zip.Dispose()
```

Confirm the package contains `README.md`, `lib/net8.0/`, `lib/net10.0/`, and the XML docs, and that
the `.nuspec` carries the license expression, project URL, and a `<repository>` element with a
commit SHA (Source Link).

### 4. Tag and push

```powershell
git tag v1.0.0-preview.1
git push origin v1.0.0-preview.1
```

The `Release` workflow then packs, pushes to nuget.org via trusted publishing, uploads the
packages as build artifacts, and creates a GitHub Release with generated notes.

### 5. Verify

- Packages appear on nuget.org (indexing takes a few minutes).
- The listing renders the README correctly — **nuget.org does not resolve relative Markdown links**,
  so every link in `README.md` must be an absolute URL.
- `dotnet add package TriQL.Client --prerelease` resolves in a scratch project.
- Symbols resolve when stepping into the library from a consuming project.

## Manual publishing (fallback)

The tag-triggered workflow is the supported path. Publish by hand only when CI is unavailable, when
re-pushing a package that failed mid-workflow, or when pushing from a machine that cannot use OIDC.
A manual push loses the guarantees the workflow provides: a clean checkout, a verified locked
restore, and build provenance.

### 1. Create a scoped API key

On nuget.org: username menu → **API Keys** → **Create**.

- **Key name:** something identifying the machine, e.g. `manual-release-laptop`
- **Expiration:** the shortest workable period (365 days maximum; prefer far less)
- **Scopes:** `Push` only. Use **Push new packages and package versions** for the very first
  publish of a package ID, then rotate to **Push only new versions of existing packages**.
- **Glob pattern:** `TriQL.*` — never `*`

The key is displayed once. Treat it as a password.

### 2. Store the key out of source control

Put it in an environment variable for the session rather than pasting it into a command that lands
in your shell history or into any file in the repository:

```powershell
$env:NUGET_API_KEY = Read-Host -AsSecureString | ConvertFrom-SecureString -AsPlainText
```

Never run `dotnet nuget setapikey`, never add the key to `NuGet.Config`, and never paste it into a
committed script. If a key is ever exposed, revoke it immediately on nuget.org.

### 3. Build and pack from a clean tree

```powershell
git status --short          # must be empty
git checkout v1.0.0-preview.1

dotnet clean TriQL.slnx -c Release
dotnet restore TriQL.slnx --locked-mode
dotnet build TriQL.slnx -c Release --no-restore -p:Version=1.0.0-preview.1
dotnet test TriQL.slnx -c Release --no-build

dotnet pack TriQL.slnx -c Release --no-build -p:Version=1.0.0-preview.1 -o ./artifacts
```

Set `$env:CI = "true"` before building to enable `ContinuousIntegrationBuild`, so the manual output
is deterministic and has the same normalised Source Link paths the workflow produces.

The version passed to `pack` must exactly match the tag you checked out. There is no automatic check
for this on the manual path — a mismatch publishes a version that corresponds to no tag.

### 4. Verify the packages before pushing

Confirm all four packages and their symbol packages were produced, then inspect one:

```powershell
Get-ChildItem ./artifacts

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead("$PWD\artifacts\TriQL.Client.1.0.0-preview.1.nupkg")
$zip.Entries.FullName
$zip.Dispose()
```

Optionally install the package into a scratch console app from the local folder as a source
(`dotnet add package TriQL.Client -s ./artifacts --prerelease`) and run a query against a real
cluster. This is the last point at which a mistake is still reversible.

### 5. Push

Push `TriQL.Client` first — the other three depend on it, and a consumer who restores
`TriQL.Data.ADO` before `TriQL.Client` has indexed will get a resolution failure.

```powershell
dotnet nuget push ./artifacts/TriQL.Client.1.0.0-preview.1.nupkg `
  --api-key $env:NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json `
  --skip-duplicate
```

Wait for it to appear as listed, then push the rest:

```powershell
foreach ($id in "TriQL.Client.Auth", "TriQL.Client.Compression", "TriQL.Data.ADO") {
    dotnet nuget push "./artifacts/$id.1.0.0-preview.1.nupkg" `
      --api-key $env:NUGET_API_KEY `
      --source https://api.nuget.org/v3/index.json `
      --skip-duplicate
}
```

Notes:

- `dotnet nuget push` uploads the matching `.snupkg` alongside each `.nupkg` automatically. Do not
  push `.snupkg` files explicitly — a wildcard such as `./artifacts/*.nupkg` already excludes them.
- `--skip-duplicate` turns an "already exists" conflict into a warning instead of a failure, which
  makes a partially completed push safe to retry.
- Indexing typically takes a few minutes; validation can take longer.

### 6. Finish the release manually

The workflow also performs steps that a manual push does not. Do them by hand:

```powershell
git push origin v1.0.0-preview.1
gh release create v1.0.0-preview.1 ./artifacts/*.nupkg --generate-notes
```

Then clear the key from the session (`Remove-Item Env:\NUGET_API_KEY`) and delete `./artifacts`.

### If you published something wrong

A published version cannot be deleted or replaced. **Unlist** it (nuget.org → Manage Packages →
the version → Unlist), which hides it from search and from unversioned restores while keeping
existing pinned consumers working, then publish a corrected higher version. Never attempt to reuse
the version number.

## Open items

- **Package icon** — `PackageIcon` is not set, so nuget.org shows a generic placeholder. Add a real
  `icon.png` (128×128, under 1 MB) at the repository root, set `<PackageIcon>icon.png</PackageIcon>`
  and pack it with a `None` item using `PackagePath="\"`.
- **Package signing** — the signing step in `release.yml` is commented out. Author signing is
  optional; trusted publishing already provides provenance.
- **Dependency floor on `net8.0`** — `Microsoft.Extensions.Logging.Abstractions` is pinned to
  `10.0.0` for both target frameworks, which forces .NET 8 consumers onto the 10.x assemblies.
  Consider a conditional `PackageVersion` so the `net8.0` target references an 8.0.x version.
- **Package validation** — from 1.0.0 onward, consider enabling
  `<EnablePackageValidation>true</EnablePackageValidation>` with `PackageValidationBaselineVersion`
  set to the previous release to catch accidental binary-breaking changes at pack time.
