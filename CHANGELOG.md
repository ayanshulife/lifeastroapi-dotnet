# Changelog

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
  Not in this version: the PDF rendering of the Node.js SDK (the JSON report endpoints are included).
