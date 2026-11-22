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
- Optional package icon. (HouseSystem: the live API takes `whole-sign` and `whole_sign` since 2026-11-22; this
  SDK keeps sending `whole_sign`, as published.)

## 0.2.0 PDF reports (2026-11-22)
- src/LifeAstroApi/Pdf: public API + PdfOutline; Engine/ (CDP over ClientWebSocket, locator, process lifetime);
  Runtime/ (JavaScript semantics); Generated/ (templates, never edit: `node tool/generate-pdf.mjs`).
- Node's src/pdf is the truth. After it changes: generate-pdf, pdf-goldens, then test. A construct outside the
  translator's subset stops the generator with file and line: add it to tool/generate-pdf.mjs and Runtime/.
- Tests: tests/LifeAstroApi.Tests/Pdf, also run on the netstandard2.0 build. Fixtures Resources/Pdf are byte-exact
  (.gitattributes -text). Deep parity: ../LifeAstroAPI/pdf_work (432 cases).
- Traps: never RegexOptions.IgnoreCase/ECMAScript in templates; never Unicode-normalise; .NET 10 raises no
  ProcessExit on SIGTERM (Engine/UnixLauncher keeps browsers from being orphaned); on netstandard2.0 do not use
  IAsyncDisposable (Microsoft.Bcl.AsyncInterfaces is not deployed on .NET Core 3.1 to 7).
- Synced to Node 0.14.0's templates (2026-11-22, SYNC5). Hand-ported from Node, so check them on every sync (HTML
  parity does not cover them): src/pdf/index.ts (Brihad wrapper: wrapper.json), render.ts printOptionsCss +
  mergeMargin (PdfPrintCss.cs, PdfOptions.MergeMargin: branding.json) and src/pdf/outline.ts (outline.json).
- Print CSS is added in PdfReports (so custom engines get it) and in both engines (for direct callers); Apply adds
  it once. All-default options must leave the HTML untouched.
- Colour maths: Math.Pow differs from V8 in the last bit, but no RGB colour is nearer than 1.4e-7 to a contrast
  threshold (3, 4.5, 5.2), so every decision is the same. Re-check that if Node changes a threshold.
- Timestamps: no zone (T, t or space) is UTC, as Node's parseTimestamp reads them; pdf-goldens needs no TZ.
- Templates look keys up in plain tables (no JavaScript prototype); Node's own() is translated with Js.HasOwn.
- Engine contract: every call settles as a result, LifeAstroPdfException (engine_closed when disposed) or the caller's
  own cancellation; nothing unobserved, Dispose never throws. The browser's working directory is never the app's
  (its helpers outlive it and would pin that folder).
- In tests pass StringComparison.Ordinal to Assert.Contains: the culture comparison ignores control characters.
