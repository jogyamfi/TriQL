# API Reference

TriQL's API reference is generated from the XML documentation comments on every public type and
member (`NFR-MAINT-3` requires them on all four packages; `NFR-MAINT-1`/`TreatWarningsAsErrors`
makes a missing doc comment a build error, so the reference is never stale relative to the code).

## Generating the reference locally

```bash
dotnet tool install -g docfx   # once
cd docs
docfx docfx.json --serve       # builds to docs/_site and serves it at http://localhost:8080
```

`docfx.json` points at the four packaged projects' `.csproj` files (`TriQL.Client`,
`TriQL.Client.Auth`, `TriQL.Client.Compression`, `TriQL.Data.ADO`) and extracts their public API
surface plus doc comments directly from source — nothing is hand-maintained.

## Other references

The generated reference documents *what* every member does. For *how* to use the library, see:

- [Connection-string reference](connection-string-reference.md)
- [Type-mapping reference](type-mapping-reference.md)
- [Troubleshooting guide](troubleshooting.md)
- [Migration guide](migration.md)
- [README quick starts](../README.md)
