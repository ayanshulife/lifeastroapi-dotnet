# NOTES (work log for helpers)

Brief: ../LifeAstroAPI/marketing_drafts/handoff-dotnet-sdk.md. Never commit/push/publish. Never call production.
dotnet: `DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 PATH="$HOME/.dotnet:$PATH" dotnet ...` (SDK 10.0.404, user folder).

## Done
- Repo created (git init, no commits); tool/samples.json + tool/costs.json copied from the Java SDK, LICENSE from Node.
- Directory.Build.props (root: shared) + src/Directory.Build.props (packable libs: netstandard2.0;net8.0, docs, symbols).
- Core library src/LifeAstroApi: LifeAstroClient(+Options), RequestOptions, ResponseInfo/RateLimit, errors (Errors.cs),
  BirthInput, Place, LifeAstroDate/LifeAstroTime (implicit from string/DateTime/DateOnly...), Lang/Ayanamsa/HouseSystem,
  LifeAstroParams base + QueryWriter, Internal/HttpCore (retries, timeout, hooks), Internal/QueryString (JS number
  format, URLSearchParams escaping), Internal/Wire (+TimeZones fallback table for .NET Framework).
- tool/generate.mjs (port of Java generator): 323 methods, 317 params classes, 0 renames. Library builds 0 warnings.

- DI package src/LifeAstroApi.Extensions.DependencyInjection (AddLifeAstro x3, named HttpClient "LifeAstroApi", TryAddSingleton). Builds 0 warnings.
- tool/node-goldens.mjs -> tests/LifeAstroApi.Tests/Resources/node-requests.json (same as Java's goldens).
- tests/LifeAstroApi.Tests: 515 passing (parity 323, transport, params, language, errors, retry, timeout incl. body,
  cancellation, culture hi-IN/de-DE/ar-SA/th-TH/fa-IR, JS number format incl. netstandard path).

- DI tests (7 passing), tools/LiveSweep, examples/QuickStart + examples/AspNetCore, LifeAstroApi.sln: whole solution
  builds 0 warnings in Release.
- Live run 2026-11-21 against local dev API (127.0.0.1:18080, no DB, key dv_test_devkey): sweep of all 323 methods in
  en, gu and pa: 265 OK, 58 expected failures each (narrative corpus / remedy registry / horoscope corpus / 2 asteroid
  ephemeris files missing on a dev server); no 400/404. QuickStart + AspNetCore example ran OK against it.
  `house_system=whole-sign` -> 400 on the API, `whole_sign` -> 200 (confirms the HouseSystem finding).

## Decisions
- HouseSystem.WholeSign sends `whole_sign` (API accepts whole_sign/whole/w); Node/Java send `whole-sign` (rejected) -> report.
- Resource classes are `XxxResource` in LifeAstroApi.Resources; params in LifeAstroApi.Params; errors in root namespace.
- Optional props are `{ get; set; }` (no init: C# 7.3 / .NET Framework callers); required ones ctor params, get-only.

## Status (2026-11-21): 0.1.0 complete, ready for the owner to commit and publish
- README.md, CHANGELOG.md, RELEASING.md written. tests/LifeAstroApi.NetStandard.Tests runs the core suite against the
  netstandard2.0 build (SetTargetFramework). Totals: 516 + 516 + 7 = 1039 tests green.
- `dotnet pack -c Release -o artifacts` checked (lib/net8.0 + lib/netstandard2.0 + XML docs + README; snupkg with pdbs),
  then artifacts/ deleted so nothing un-SourceLinked gets pushed. Dev API server stopped.

## Next (owner / later helper)
- Owner: commit + push (GitHub Desktop), then RELEASING.md steps 5-9 (pack with CI flag, nuget push, validation, tag).
- Later: 0.2.0 PDF reports (as PHP did); optional package icon; tell other SDKs about HouseSystem `whole-sign` (API
  wants `whole_sign`).
