# Changelog

## 1.1.0 (2026-11-28)

New methods in `LifeAstroApi`; `LifeAstroApi.Extensions.DependencyInjection` 1.1.0 has no change of its own. Nothing
changes for code written against 1.0.0, and there is no new dependency.

- `Dasha.AshtottariAsync` (`DashaAshtottariParams`): the full Ashtottari dasha (108-year cycle of 8 lords) with the
  balance at birth and whether the chart meets the classical conditions. Options `Depth` (1-3), `Scheme` (`28`
  default or `27`) and `AntarStart` (`maha_lord` default or `next_lord`). 10 credits.
- `Dasha.AshtottariCurrentAsync` (`DashaAshtottariCurrentParams`): the Ashtottari mahadasha and antardasha running
  now, or at `At`. Same options. 10 credits.
- `Chart.ArudhaPadasAsync` (`ChartArudhaPadasParams`): the twelve arudha padas (A1 to A12, with AL and UL) and how
  each was counted. Options `Exception` (`parashari` default or `none`), `ScorpioLord` (`mars` or `ketu`) and
  `AquariusLord` (`saturn` or `rahu`). 10 credits.
- `Chart.UpapadaAsync` (`ChartUpapadaParams`): the Upapada Lagna with its lord, the planets in it and the planets that
  aspect it. Same options as `ArudhaPadasAsync`. 10 credits.
- `Chart.ArgalaAsync` (`ChartArgalaParams`): argala and its obstruction on every house, or on one `House`. Options
  `IncludeEighth`, `NodeReversal` (`ketu` default, `rahu_ketu` or `none`) and `VipareetaMinMalefics` (default 3).
  10 credits.
- `Muhurta.VahanAsync`: score a moment for buying a vehicle or its first use. 20 credits.
- `Muhurta.BhoomiPujanAsync`: score a moment for ground-breaking or laying a foundation. 20 credits.
- `Muhurta.UpanayanAsync`: score a moment for the sacred thread ceremony. 20 credits.
- `Muhurta.AnnaprashanAsync`: score a moment for a child's first solid food. Optional `BirthDate` and `Sex` add a
  note on the child's age. 20 credits.
- `Muhurta.VidyarambhAsync`: score a moment for a child's first letters. Optional `BirthDate` adds an age note.
  20 credits.
- `Muhurta.KarnavedhaAsync`: score a moment for ear piercing. Optional `BirthDate` adds an age note. 20 credits.

Changes to existing methods (all optional, nothing to change in your code):

- `Muhurta.BestTimeAsync` takes the purposes `vyapar`, `yatra`, `vahan` and `bhoomi_pujan`, next to `general`,
  `vivah`, `graha_pravesh` and `naamkaran`, and a new `Direction` property (N, NE, E, SE, S, SW, W, NW) on
  `MuhurtaBestTimeParams` for `yatra`.
- `MuhurtaYatraParams` has a `Direction` property. When you leave it out, the API no longer assumes travel to the
  East: there is then no direction check.
- Every muhurta score now has `prohibited_by` in its `result`: the classical prohibitions that apply, as English ids
  such as `Bhadra`, `GuruShukraAsta` or `AdhikaMasa`. When the list is not empty, `grade` is `inauspicious` and
  `is_acceptable` is false. `BestTimeAsync` never returns a window under a prohibition, so it can return fewer
  windows than `TopN`.
- `UpanayanAsync`, `AnnaprashanAsync`, `VidyarambhAsync` and `KarnavedhaAsync` also return `result.notes`:
  information such as the child's age by tradition or a festival on that day. Notes never change the score.

## 1.0.0 (2026-11-27)

This is the first stable release, of both packages: `LifeAstroApi` and `LifeAstroApi.Extensions.DependencyInjection`.
From 1.0.0 on, a change that needs a code change on your side only comes with a new major version. There is no new
dependency.

New:

- Clear error for a reply that is not JSON. Such a reply did not come from the API: a proxy, firewall or CDN in
  between answered, usually with an HTML page. It now throws the exception of its HTTP status with code
  `non_json_response`; the message gives the status and the start of the body.
- README: the `non_json_response` error.

Changes that may need a code change:

- A 200 reply with a body that is not JSON now throws instead of returning the text.
  Before: the call returned the body as a JSON string (`JsonElement`).
  Now: `catch (LifeAstroException e) when (e.Code == "non_json_response") { /* ... */ }`
- `Numerology.FullAsync` requires the date of birth. The API always required it. `Dob` is now the constructor
  parameter of `NumerologyFullParams`, and `FullAsync` no longer accepts a call without parameters.
  Before: `await client.Numerology.FullAsync(new NumerologyFullParams { Name = "Arjun Mehta", Dob = "1990-01-15" });`
  Now: `await client.Numerology.FullAsync(new NumerologyFullParams("1990-01-15") { Name = "Arjun Mehta" });`

## 0.2.0 (2026-11-22)

New: the SDK makes branded PDF reports, on your own machine. Upgrading from 0.1.0 needs no code change: nothing
was removed or renamed, and there is no new dependency. Add `using LifeAstroApi.Pdf;` and pass a report to it:

```csharp
byte[] pdf = await PdfReports.KundliLitePdfAsync(report, new PdfBranding { CompanyName = "Acme Astro" });
```

What you get:

- A PDF for each of the twelve report endpoints, in all ten languages (Punjabi included), with your company
  name, logo, colours, footer text and watermark. Pass what `client.Reports` returned, or a report you saved as
  a JSON string.
- Your colours reach through the Brihad kundli: headings, banners, tables, charts, contents page numbers, running
  header and footer. Without a brand colour it is saffron. A colour too pale to read prints in a darker shade.
- A watermark prints faintly on every page (in the Brihad kundli behind the content), sized to fit, and adds
  no page. It stays whole when you print without backgrounds.
- `PdfOptions` win over a report's own print CSS: margins side by side, paper format, landscape, printing
  without backgrounds, and a header or footer of your own in place of the report's. `Timeout` and
  `PrintTimeout` give a long Brihad more time.
- The Brihad kundli prints at full size, and a section of a few lines shares its page with the next one.
- The Brihad kundli has a running header and footer with your branding, a table of contents with the real page
  numbers followed by Part 1 on a new page, and bookmarks that read correctly in every script. The PDF keeps
  its title.
- North Indian charts keep every planet inside its cell, also when many share one, in every script.
- A field that should be a list and is not never stops a PDF: a lone value prints, or that block is left out.
- A null inside a list never stops a PDF either: that item prints empty or is left out, and your JSON is not edited.
- The output does not depend on the server's culture or time zone: a timestamp without a zone is read as UTC.
- Numerology prints the person's name, with each label above its value. Varshaphal prints the solar return in
  the local time of the birth when the report carries it, otherwise in UTC.
- The HTML alone, if you prefer: `PdfReports.Render*Html(...)` needs no browser and no network, so it also works
  in desktop and mobile apps that print with a web view (`PdfReports.A4PrintOptions` has the page size). It is
  the same HTML as the Node.js SDK 0.14.0 makes.

What you need to print:

- Chrome, Chromium or Microsoft Edge on the machine (Windows 10 and 11 come with Edge). The SDK finds it, or
  set `LIFEASTRO_CHROME`. Keep one `ChromePdfEngine` for many PDFs: it is safe to share between requests,
  takes a `CancellationToken`, has timeouts, and cleans up the browser and its temporary profile.
- Or a Gotenberg service you run (`GotenbergPdfEngine`), or any converter of your own (`IPdfEngine`).
- Fonts for the Indian scripts on that machine (`fonts-noto-core` on Debian and Ubuntu).

Nothing is sent to LifeAstroAPI to make a PDF, and making one costs no credits. A failed print throws
`LifeAstroPdfException`, which is a `LifeAstroException`. The README has an ASP.NET Core download endpoint,
WebView2, .NET Framework and Docker; `examples/PdfReport` is a runnable example.

## 0.1.0 (2026-11-21)

First release of the official .NET SDK.

- `LifeAstroApi`: the `LifeAstroClient` for .NET 8, 9 and 10 (`net8.0` build) and .NET Framework 4.6.2+ /
  older runtimes (`netstandard2.0` build). Created from `LifeAstroClientOptions` (`ApiKey`, `BaseUrl`,
  `Timeout`, `MaxRetries`, `OnResponse`, `DefaultHeaders`, `JsonSerializerOptions`), or with your own
  `HttpClient`, which it never disposes. The API key falls back to the `LIFEASTRO_API_KEY` environment
  variable. Immutable and thread-safe; `IDisposable`. `User-Agent: lifeastroapi-dotnet/0.1.0`.
- 323 async methods (322 endpoints) in 23 resource groups (`client.Panchang.BasicAsync(...)`,
  `client.Chart.PlanetsAsync(...)`, `client.Western.Natal.ChartAsync(...)`, ...), generated from the same
  manifest as the Node.js SDK 0.13.0, with the Node method names in PascalCase plus `Async`. No method needed
  another name in C# (Node's `dasha.char` is `Dasha.CharAsync`).
- Each method returns `Task<JsonElement>`, and has a generic overload `Task<T?> ...Async<T>(...)` that maps
  the response to your type with System.Text.Json (default options: snake_case names, case-insensitive,
  numbers from strings, unknown properties ignored). Every method takes `RequestOptions` and a
  `CancellationToken`; methods whose parameters are all optional can be called without them.
- A parameter class per method (`PanchangBasicParams`, ...) in `LifeAstroApi.Params`: required values as
  constructor parameters (checked at once), optional values as properties, constructor overloads taking a
  `BirthInput` or a `Place`, and an `int` overload for numeric path values. XML docs from the API reference,
  with each method's credit cost. `ToQuery()` / `ToString()` show what a call sends.
- `BirthInput` and `Place` value types and the Node SDK's wire names: unprefixed `date` / `time` / `tz` /
  `lat` / `lon` for a chart, `birth.*` for the natal chart of transit-style methods, `boy.*` / `girl.*` for
  kundli matching, `personA.*` / `personB.*` for Western pairs. Moment methods send today's date in the
  given `tz` when `Date` is left out (IANA ids and UTC offsets; on .NET Framework the common IANA ids are
  mapped to Windows zones). Old and current names of a zone both work (`Asia/Calcutta` and `Asia/Kolkata`,
  `Asia/Katmandu` and `Asia/Kathmandu`, ...), also on Linux systems that ship only one of them, such as
  Ubuntu 24.04 and slim container images.
- `LifeAstroDate` / `LifeAstroTime`: every date and time parameter takes a string, `DateTime`,
  `DateTimeOffset`, `TimeSpan` and, on .NET 8+, `DateOnly` / `TimeOnly`. All values are formatted with the
  invariant culture (numbers as JavaScript writes them, dates in the Gregorian calendar), whatever the
  current culture.
- `Lang` on every Vedic method whose response carries names or text, sent as `lang` and `locale`;
  `enum Lang { En, Hi, Mr, Gu, Bn, Te, Ta, Kn, Ml, Pa }`, all ten languages complete. `Ayanamsa` and
  `HouseSystem` enums (`HouseSystem.WholeSign` is sent as `whole_sign`).
- Exceptions with `Status`, `Code`, `RequestId` and `Body`: `BadRequestException`,
  `AuthenticationException`, `PaymentRequiredException`, `PermissionException`, `NotFoundException`,
  `RateLimitException` (`RetryAfter`), `ServerException`, `LifeAstroConnectionException` and
  `LifeAstroTimeoutException`, all extending `LifeAstroException`. A cancelled token throws
  `OperationCanceledException` and is never retried.
- Automatic retries with exponential backoff on 408, 425, 429, 500, 502, 503, 504, network errors and
  timeouts, honouring `Retry-After` (seconds or HTTP date). The timeout bounds the whole call, including the
  body download.
- `OnResponse` hook (client-level and per call) with a `ResponseInfo`: status, path, request id, credits
  remaining, rate limit (`RateLimit` with `ResetAt` as a `DateTimeOffset`), cache and language. Same
  behaviour as the Node.js SDK.
- `client.RequestAsync(path, query, ...)` to call any `/v1/*` endpoint directly.
- `LifeAstroApi.Extensions.DependencyInjection`: `services.AddLifeAstro(configuration.GetSection("LifeAstroApi"))`
  and `services.AddLifeAstro(o => ...)` bind the options, register a named `HttpClient` through
  `IHttpClientFactory` (pooled connections recycled every 5 minutes) and one `LifeAstroClient` singleton,
  with `TryAdd` semantics. Uses `Microsoft.Extensions.*` 8.0.0.
- SourceLink and a symbol package (`.snupkg`) for both packages.
- README: console, ASP.NET Core (minimal API and controller), Entity Framework Core and .NET Framework usage.
