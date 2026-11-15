# TriQL repository clean-up plan

This is a work plan for an AI coding agent (written with Claude Opus 5.5 in mind) picking up where
an earlier clean-up session stopped. It assumes you have the repository checked out and no memory
of that session. Delete this file when every task is done or dropped.

## Background

TriQL is a .NET 10 client for Trino, published to NuGet as five packages (`TriQL.Client`,
`TriQL.Data.ADO`, `TriQL.EntityFrameworkCore`, `TriQL.Client.Auth`, `TriQL.Client.Compression`).
The code was written against an internal requirements specification and a phased implementation
plan, and it still carries their vocabulary: requirement IDs (`FR-9.1.11`, `NFR-PERF-3`, `SEC-7`,
`REL-4`, `TEST-9`, `REQ-ARCH-4`), task IDs (`P6-T10`, `EF7-T6`), and phrases such as "Phase 5",
"Lane B", "risk register", "Appendix A.2", "requirements.md §23". None of that means anything to
someone using or contributing to the library. The goal is a repository that reads as if written
for its users and contributors, with no change in behaviour.

## Already done — do not redo

- `README.md` rewritten for package users.
- XML doc comments (`///`) under `src/` no longer contain requirement or task IDs.
- Exception messages and other string literals under `src/` no longer contain IDs.
- IDs removed from `docs/api-reference.md`, `docs/troubleshooting.md`, `BannedSymbols.txt`,
  `Directory.Build.props`, `Directory.Packages.props`, `.github/workflows/*.yml`,
  `src/TriQL.Client.Auth/TriQL.Client.Auth.csproj` and the two sample `Program.cs` files.
- `requirements.md`, `implementation-plan.md` and `efcore-plan.md` moved from `docs/` to
  `docs/internal/`. They are historical; leave their content alone.

At the end of that session the solution built with zero warnings and the four unit-test projects
passed (392 + 103 + 16 + 230 tests). The integration and EF functional tests were not run.

## Ground rules

- **No behaviour changes.** Tasks 1–3 touch comments and documentation only. If you believe a
  code change is needed, stop and report it instead.
- **Do not change public API.** `PublicAPI.Shipped.txt` in each `src/` project is enforced at
  build time; none of these tasks should require editing it.
- **Warnings are errors.** A malformed XML doc comment fails the build, so build after each task:
  `dotnet build TriQL.slnx`.
- **Run the unit tests after each task**, one project per command (`dotnet test` rejects several
  project paths at once):
  `dotnet test tests/TriQL.Client.Tests`, `tests/TriQL.Data.ADO.Tests`,
  `tests/TriQL.Client.Auth.Tests`, `tests/TriQL.EntityFrameworkCore.Tests`.
  `tests/TriQL.IntegrationTests` and `tests/TriQL.EntityFrameworkCore.FunctionalTests` need
  Docker; run them if it is available and say so plainly if it is not.
- **Preserve file encoding and line endings.** Some `.cs` files start with a UTF-8 BOM and the
  repository mixes LF and CRLF. Edit in place; do not normalise.
- **Rewrite, don't just delete, when the ID carries the meaning.** `(FR-9.1.11)` at the end of a
  sentence can simply go. "Defaults match FR-3.3.1" has to become a sentence that still says
  something true. When a comment describes a plan that has since been carried out ("placeholder
  until Phase 3"), check the code and describe what is there now.
- **Do not commit, push or publish** unless the repository owner asks. The working tree already
  holds uncommitted changes from the earlier session plus a `global.json` SDK bump the owner made;
  leave the latter as it is.
- Tasks marked **Ask first** change something the owner should decide. Present the choice and
  wait; carry on with the other tasks meanwhile.

This pattern finds what remains (run from the repository root):

```bash
git grep -nE '\b(P[0-9]+-T[0-9]+[a-z]?|EF[0-9]+-T[0-9]+|(FR|SEC|TEST|REL)-[0-9]|NFR-[A-Z]+-[0-9]|REQ-[A-Z]+-[0-9]|requirements\.md|implementation-plan|efcore-plan|Phase [0-9]|Appendix [A-Z]|Lane [A-Z]|risk register)' -- . ':!docs/internal'
```

It also matches bare risk and gate IDs only when they sit next to another ID, so additionally
skim for stray `R3`, `G2`, `G5`-style references and `§` section marks.

## Task 1 — Remaining ID references in `src/` comments

About 40 lines in 18 files, all ordinary `//` comments plus one `#pragma` comment in
`src/TriQL.Data.ADO/Internal/SyncBridge.cs` and one `Justification = "…"` string in
`src/TriQL.Data.ADO/TrinoDataReader.cs`. Remove or reword each one under the ground rules.

Done when the pattern above returns nothing under `src/`, the build is clean and the unit tests
pass.

## Task 2 — ID references in `tests/`

About 160 lines in 74 files: class-level doc comments ("Deterministic tests (P5-T13)…"), inline
comments, a few assertion messages, and one certificate subject name,
`"CN=TriQL Phase 7 Test CA"` in `tests/TriQL.IntegrationTests/Fixtures/TestCertificateAuthority.cs`.

- Comments and assertion messages: reword as in Task 1.
- The certificate subject is a runtime value. Rename it (for example `CN=TriQL Test CA`) only
  after checking nothing else matches on that string.
- Two comments describe disagreements between the specification and real Trino behaviour
  (`tests/TriQL.IntegrationTests/Fixtures/MinioFixture.cs`,
  `tests/TriQL.Client.Tests/Security/UriGuardTests.cs`). Keep the technical finding; drop the
  reference to the specification.
- Do not rename test methods, classes or `Category` traits; CI filters on `Category=Spooling`.

Done when the pattern returns nothing under `tests/` and the build and unit tests pass.

## Task 3 — Rewrite `docs/benchmarks.md` for readers

The file is currently an internal status report: a document-control table, requirement IDs in
headings ("Row Decoding — NFR-PERF-3"), a section listing benchmarks "not yet written (Phase 5,
P5-T10)", and notes about CI gates that do not exist.

Rewrite it as a page a user would want: what was measured, on what hardware, how to reproduce it
(`tests/TriQL.Benchmarks`), the results tables, and honest caveats. Keep every measured number
exactly as recorded — do not re-run benchmarks or invent figures. Replace the "outstanding" list
with one short "Not yet measured" paragraph (time to first row, throughput against the JDBC
driver, peak memory, the spooled path). The README no longer links this page; add it back to the
README's Documentation list once it reads well.

Done when the file contains no IDs or phase references and all numbers match the previous version
(`git diff` it to check).

## Task 4 — Community files

Create, in the repository root unless noted:

- `CONTRIBUTING.md` — prerequisites (the .NET SDK version in `global.json`; Docker for
  integration tests), build and test commands, which test projects need Docker, the coding
  conventions that fail the build (warnings as errors, nullable, the public-API baseline and how
  to update `PublicAPI.Unshipped.txt`, `BannedSymbols.txt`), and how releases are cut (read
  `.github/workflows/release.yml` and `publish-manual.yml` first; describe what they do, do not
  guess). Base the conventions on `.github/instructions/*.md`, condensed for people.
- `SECURITY.md` — supported versions and how to report a vulnerability privately. **Ask first**
  for the contact route (GitHub private vulnerability reporting, or an address); do not put the
  owner's personal email in the file on your own initiative.
- `.github/ISSUE_TEMPLATE/bug_report.yml` (TriQL version, Trino version, connector, .NET version,
  minimal reproduction) and `.github/pull_request_template.md` (short checklist: tests, changelog
  entry, public-API baseline).

Then link `CONTRIBUTING.md` from the README's "Getting help and contributing" section.

**Ask first:** whether to keep `.github/instructions/*.md`. They are instruction files for AI
coding assistants. Keeping them is harmless and useful if the owner continues to use such tools;
the alternative is deleting them once `CONTRIBUTING.md` covers the same ground.

## Task 5 — Repository hygiene

- `tests/TriQL.IntegrationTests/NUL` is an untracked stray file (a Windows redirect to `NUL` run
  from a POSIX shell). `.gitignore` has a line ignoring it. Delete the file and that line. On
  Windows the name is reserved, so an ordinary delete may fail; from Git Bash `rm` works, or use
  the `\\?\` path prefix.
- **Ask first:** `global.json` pins the SDK with `"rollForward": "disable"`, so every contributor
  and CI runner needs that exact patch. Recommend `"latestPatch"`, explain that the pin exists to
  keep `packages.lock.json` files stable, and let the owner choose.
- **Ask first:** `Authors` in `Directory.Build.props` is `TriQL Contributors`. Offer to add the
  owner's name or handle; do not pick one yourself.
- `CHANGELOG.md` has an "Unreleased" entry announcing that `TriQL.EntityFrameworkCore` is stable.
  Add entries there for anything user-visible from this clean-up: exception messages no longer
  end with internal reference codes, and the README was rewritten. Do not choose a version number.

## Task 6 — Samples

- `samples/TriQL.Samples.UserNamePassword` and `samples/TriQL.Samples.CertificateAuth` hard-code
  placeholders such as `"testhost"` and `"Your password"`. Read them from environment variables,
  following the pattern in `samples/TriQL.Samples.Console/Program.cs` (`TRINO_SERVER` with a
  local default), and fail with a clear message when a required one is missing. Never put a real
  host name or credential in a sample.
- Add `samples/README.md`: one line per sample, the environment variables each needs, and how to
  run one (`samples/Run-Sample.ps1` or `dotnet run --project …`).
- Samples build as part of `TriQL.slnx` under the repository's analyzer settings, so build to
  verify.

**Ask first** before adding a new sample (a Dapper one was suggested); it adds a package
dependency to `Directory.Packages.props` and lock files.

## Task 7 — Per-package NuGet READMEs  (**Ask first**)

Every package currently ships the root `README.md`, wired up in `Directory.Build.props`
(`PackageReadmeFile` plus a `None Include=…README.md` item for projects under `src/`). The page
for `TriQL.Client.Compression` on nuget.org therefore opens with EF Core quick starts.

Proposal to put to the owner: keep the root README for `TriQL.Client`, `TriQL.Data.ADO` and
`TriQL.EntityFrameworkCore`, and give `TriQL.Client.Auth` and `TriQL.Client.Compression` a short
`README.md` in their project folders (what the package adds, the two or three lines needed to use
it, a link back to the main README). Implement by letting a project-local `README.md` take
precedence over the root one in `Directory.Build.props`.

Verify with `dotnet pack` into a scratch directory and inspect each `.nupkg` (it is a zip): one
`README.md` at the root of each, with the expected content. NuGet renders only absolute links and
images from a short list of trusted hosts, so use absolute `https://github.com/jogyamfi/TriQL/…`
URLs.

## Task 8 — Hosted API reference  (**Ask first**)

`docs/api-reference.md` explains how to generate the reference locally with docfx
(`docs/docfx.json`); nothing is published. Proposal: a workflow that builds the docfx site and
deploys it to GitHub Pages on release, then a link from the README. This needs GitHub Pages
enabled in the repository settings, which only the owner can do — confirm before writing the
workflow. Look up the current docfx and `actions/deploy-pages` documentation rather than relying
on memory; the repository uses Dependabot to keep action versions current, so match the major
versions already in `.github/workflows/`.

Note that `docs/docfx.json` builds every `*.md` directly under `docs/`. Files in `docs/internal/`
are excluded by that glob; keep it that way.

## Suggested order

1, 2 and 3 first (same kind of edit, independent of each other). Then 5 and 4, then 6. Raise the
**Ask first** questions together at the start so the answers arrive while the other work
proceeds; 7 and 8 wait on those answers.

## Reporting back

When you finish, tell the owner: which tasks are complete, the build and test results with
counts, which test suites could not be run and why, any comment you left alone because you could
not tell what the code now does, and the open questions still waiting on them.
