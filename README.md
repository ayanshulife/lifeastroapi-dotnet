# LifeAstroAPI .NET SDK

Official **C# / .NET SDK** for [LifeAstroAPI](https://lifeastroapi.com), a REST
[astrology API](https://lifeastroapi.com/apis/) for Vedic astrology and the Hindu calendar: panchang,
festivals, kundli, dasha, kundli matching, muhurat, transits, horoscopes, numerology, Lal Kitab, remedies,
readings, reports with branded PDFs, plus Western astrology and tarot. Location-aware, in 10 languages, pay per call.
Free tier: 1,500 credits a month, no card, [get a key](https://lifeastroapi.com/signup/).

- **All 322 endpoints** as 323 async methods in 23 resource groups (`Chart.NavamsaAsync` is a shortcut for
  the D9 chart).
- **.NET 8, 9 and 10, ASP.NET Core, and .NET Framework 4.6.2+** (the package targets `net8.0` and
  `netstandard2.0`). One dependency: `System.Text.Json`, which .NET 8 already has.
- **Dependency injection package** for ASP.NET Core: `services.AddLifeAstro(...)` from `appsettings.json`,
  requests through `IHttpClientFactory`.
- **Branded PDF reports, made on your side**: one call per report, in all 10 languages, with Chrome, Chromium,
  Microsoft Edge or Gotenberg, or just the HTML for a web view. See [PDF reports](#pdf-reports).
- A parameter class per method: required values in the constructor, optional ones as properties.
- Answers as `System.Text.Json.JsonElement`, or mapped to your own classes.
- Typed exceptions, automatic retries (408, 425, 429, 5xx, network) that honour `Retry-After`, a timeout
  that covers the whole call, `CancellationToken` on every method, and an `OnResponse` hook with your
  credit balance, rate limit and request id.
- The same methods, names and wire parameters as the [Node.js SDK](https://www.npmjs.com/package/lifeastroapi):
  both are generated from one endpoint manifest.

Docs: **https://lifeastroapi.com/docs/**. Get an API key at **https://lifeastroapi.com/dashboard/keys**.

## Install

```bash
dotnet add package LifeAstroApi
```

```xml
<PackageReference Include="LifeAstroApi" Version="0.2.0" />
```

In an ASP.NET Core application, add the dependency injection package instead (it brings `LifeAstroApi`
with it), see [ASP.NET Core](#aspnet-core):

```bash
dotnet add package LifeAstroApi.Extensions.DependencyInjection
```

```xml
<PackageReference Include="LifeAstroApi.Extensions.DependencyInjection" Version="0.2.0" />
```

## Quick start

```csharp
using System.Text.Json;
using LifeAstroApi;
using LifeAstroApi.Params;

using var client = new LifeAstroClient(new LifeAstroClientOptions
{
    ApiKey = "dv_live_...",          // or leave it out and set LIFEASTRO_API_KEY
});

// Today's panchang in Mumbai, in Gujarati
JsonElement day = await client.Panchang.BasicAsync(
    new PanchangBasicParams(19.076, 72.8777, "Asia/Kolkata") { Lang = Lang.Gu });
Console.WriteLine(day.GetProperty("tithi").GetProperty("name").GetString());   // e.g. સપ્તમી

// A Vedic birth chart
var arjun = new BirthInput("1990-01-15", "10:30", "Asia/Kolkata", 19.076, 72.8777);
JsonElement chart = await client.Chart.PlanetsAsync(new ChartPlanetsParams(arjun));

// Kundli matching (Ashtakoota, 36 points)
var girl = new BirthInput("1992-06-20", "14:15", "Asia/Kolkata", 28.6139, 77.209);
JsonElement match = await client.Milan.AshtakootaAsync(new MilanAshtakootaParams(arjun, girl) { Lang = Lang.Hi });
Console.WriteLine($"{match.GetProperty("total").GetDouble()} / 36");
```

The client is immutable and thread-safe. Create **one** and share it across your application (a static
field or a DI singleton); it keeps its HTTP connections open between calls. It is `IDisposable`: dispose
it when the application stops. Creating a client per request wastes sockets.

A runnable version is in [examples/QuickStart](https://github.com/ayanshulife/lifeastroapi-dotnet/tree/main/examples/QuickStart),
and an ASP.NET Core minimal API in [examples/AspNetCore](https://github.com/ayanshulife/lifeastroapi-dotnet/tree/main/examples/AspNetCore).

## Authentication

Your API key (`dv_live_…`) is sent as an `Authorization: Bearer …` header on every request. Give it to the
options, or set the `LIFEASTRO_API_KEY` environment variable and leave `ApiKey` out. Without a key, the
constructor throws an `InvalidOperationException` that says so.

```csharp
new LifeAstroClient(new LifeAstroClientOptions { ApiKey = "dv_live_..." }); // 1. explicit
new LifeAstroClient("dv_live_...");                                         // 2. shorthand
new LifeAstroClient();                                                      // 3. reads LIFEASTRO_API_KEY
```

> **Keep your key on your server.** A `dv_live_…` key carries your account's full billing access. Load it
> from an environment variable, user secrets or a secrets manager, never from source code, and never ship it
> inside a MAUI, WPF, WinForms, Unity or Blazor WebAssembly app, where anyone can read it out. Apps call your
> own backend, which calls LifeAstroAPI with this SDK. `client.ToString()` shows the key masked
> (`dv_live_****`).

## Configuration

```csharp
using var client = new LifeAstroClient(new LifeAstroClientOptions
{
    ApiKey = "dv_live_...",                       // or LIFEASTRO_API_KEY
    BaseUrl = "https://api.lifeastroapi.com",     // override for a local server
    Timeout = TimeSpan.FromSeconds(30),           // per request, to the last byte of the body (default 30 s)
    MaxRetries = 2,                               // retries on 408/425/429/5xx/network (default 2)
    OnResponse = info => { /* credits, rate limit, request id */ },
    DefaultHeaders = { ["X-Trace"] = "abc" },     // extra header on every request
    JsonSerializerOptions = myOptions,            // your System.Text.Json options, for your DTOs
});

// Or send the requests with your own HttpClient (proxy, custom TLS, IHttpClientFactory).
// The client never disposes an HttpClient you pass in.
using var viaProxy = new LifeAstroClient(myHttpClient, new LifeAstroClientOptions { ApiKey = "dv_live_..." });
```

The options are copied when the client is created. An `HttpClient` you pass in should have a longer
`Timeout` than the SDK's (or `Timeout.InfiniteTimeSpan`): the SDK times every call itself.

## Parameters

Every method takes a parameter object named after it: `client.Panchang.BasicAsync(...)` takes
`PanchangBasicParams`, `client.Western.Natal.ChartAsync(...)` takes `WesternNatalChartParams`. They are in
the `LifeAstroApi.Params` namespace. Required values are constructor parameters, checked at once
(`ArgumentException` when one is empty); optional values are properties you set with an object initializer.

```csharp
var p = new PanchangBasicParams(19.076, 72.8777, "Asia/Kolkata")   // or new PanchangBasicParams(new Place(...))
{
    Date = new DateTime(2027, 1, 15),     // or "2027-01-15", a DateTimeOffset or a DateOnly; defaults to today in tz
    Time = "06:30",                       // or a TimeSpan, DateTime or TimeOnly
    Lang = Lang.Gu,
    Ayanamsa = Ayanamsa.Krishnamurti,
};
```

Dates and times are `LifeAstroDate` / `LifeAstroTime` values: they convert implicitly from a string (sent as
given) and from `DateTime`, `DateTimeOffset`, `TimeSpan` and, on .NET 8+, `DateOnly` / `TimeOnly`, sent as
`YYYY-MM-DD` and `HH:mm` (`HH:mm:ss` with seconds). All formatting uses the invariant culture, so a German
decimal comma, a Hijri or Thai calendar or non-Latin digits never reach the URL, whatever
`CultureInfo.CurrentCulture` is.

The SDK sends each value under the wire name the API reads, exactly as the Node.js SDK does:

| Shape | Methods | Constructor | Sent as |
|-------|---------|-------------|---------|
| Moment | `Panchang`, `Muhurta` (one date), `Prashna` | `(lat, lon, tz)` or `(Place)` | `lat`, `lon`, `tz`, `date`, `time`; `date` defaults to today in `tz` |
| Birth chart | `Chart`, `Dasha`, `Lalkitab`, `Varshaphal`, narratives, reports | `(BirthInput)` or `(lat, lon, date, time, tz)` | unprefixed `date`, `time`, `tz`, `lat`, `lon` |
| Prefixed birth | `Milan.MangalDoshaAsync`, `Ashtakavarga.SarvaAsync`, ... | `(BirthInput)` | `birth.date`, `birth.time`, ... |
| Natal + transit | `Transit.TarabalaAsync`, `Narrative.TransitPhalAsync`, `Western.Transits.ToNatalAsync`, ... | `(BirthInput birth, date, time, tz)` | natal chart as `birth.*`, the moment unprefixed |
| Natal + window | `Transit.SadeSatiWindowsAsync`, `Transit.WindowsAsync` | `(BirthInput)` / `Birth` property; `From`, `Tz` | `birth.*` (lat/lon optional: `BirthInput.WithoutPlace(...)`), `from`, `tz` |
| Matching | `Milan`, `Reports.MatchMakingAsync` | `(BirthInput boy, BirthInput girl)` | `boy.*`, `girl.*` |
| Western pair | `Western.Synastry`, `Composite`, `Davison` | `(BirthInput personA, BirthInput personB)` | `personA.*`, `personB.*` |
| Rashi | `Horoscope` | `(string rashi)` | `rashi` lowercased |
| Date range | `Eclipses`, `Western.IngressesAsync` | `(startDate, endDate)` | `start_date`, `end_date` |

- `BirthInput` and `Place` are immutable with value equality. `new BirthInput(date, time, tz, lat, lon)` takes
  strings and numbers, or `DateTime` / `DateOnly` and `TimeSpan` / `TimeOnly` (for example the columns of
  your own entity); `new BirthInput(date, time, place)` takes a `Place`.
- Vedic methods take `Ayanamsa = Ayanamsa.Krishnamurti` and chart methods `HouseSystem = HouseSystem.Placidus`.
- Personalised horoscopes (`Narrative.HoroscopeDailyByLagnaAsync` and the like) take the birth chart;
  `OnDate = "2026-11-20"` reads another day than today.
- **Data endpoints** (`Chart.KundliFactsAsync`, `Chart.YogasAsync`, `Chart.DoshasAsync`,
  `Chart.SensitivityAsync`, `Transit.SadeSatiWindowsAsync`, `Transit.WindowsAsync`,
  `Remedies.ForGrahaAsync`, `Milan.OverlayAsync`, `Milan.SummaryAsync`) return ids and numbers, no prose:
  planet ids `sun` … `ketu`, `sign_index` 0-based (0 = Aries), `house` 1-based whole-sign from the lagna,
  nakshatra index 0-based, every instant as `*_local` plus `*_jd`.
- A method whose parameters are all optional can be called without them: `client.Transit.PositionsAsync()`
  (today, UTC), `client.Meta.VersionAsync()`.
- `p.ToQuery()` shows the exact query a parameter object sends and `p.ToString()` the path and query string
  (handy as a cache key). A parameter object is not thread-safe while you set it; build one per call.
- Method names are the Node.js SDK's names in PascalCase plus `Async` (Node's `dasha.char` is
  `Dasha.CharAsync`); no method needed another name in C#.

## Languages

Every Vedic method whose response carries names or text takes an optional `Lang`. The same fields come back
in that language; ids, dates, numbers and explicit `*_en` / `*_hi` fields do not change.

```csharp
JsonElement day = await client.Panchang.BasicAsync(new PanchangBasicParams(new Place(19.076, 72.8777, "Asia/Kolkata"))
{
    Date = "2027-01-15",
    Lang = Lang.Gu,       // tithi, nakshatra, yoga, karana and vara names in Gujarati
});
```

| `Lang` | Code | Language | Coverage |
|--------|------|----------|----------|
| `Lang.En` | `en` | English (default) | complete |
| `Lang.Hi` | `hi` | Hindi | complete |
| `Lang.Mr` | `mr` | Marathi | complete |
| `Lang.Gu` | `gu` | Gujarati | complete |
| `Lang.Bn` | `bn` | Bengali | complete |
| `Lang.Te` | `te` | Telugu | complete |
| `Lang.Ta` | `ta` | Tamil | complete |
| `Lang.Kn` | `kn` | Kannada | complete |
| `Lang.Ml` | `ml` | Malayalam | complete |
| `Lang.Pa` | `pa` | Punjabi | complete |

- **Where it works:** `Panchang`, `Calendar`, `Festivals`, `Eclipses`, `PlanetMoments`, `Transit`, `Milan`,
  `Muhurta`, `Chart`, `Prashna`, `Numerology`, `Dasha`, `Varshaphal`, `Ashtakavarga`, `Lalkitab`,
  `Horoscope`, `Reports`, `Narrative` and `Remedies`. The SDK sends it as `lang` and `locale` (the API reads
  either). `LangExtensions.FromCode("gu")` turns a code into the enum and `Lang.Gu.ToCode()` back.
- `Festivals.*`, `Panchang.MonthlyAsync` and some `Remedies.*` methods also keep the older `Locale`
  property; `Lang` wins when both are set.
- **No `Lang`** (the parameter class has no such property): Vedic methods that return only times, numbers or
  ids, such as `Panchang.RahuKaalAsync`, `Chart.YogasAsync` and `Transit.WindowsAsync`.
- **English only:** `Western`, `Tarot`, `Geo` and `Meta`. `Western.Narrative` takes `Lang` for `En`, `Hi`
  and `Mr`.
- **Stable keys:** muhurta `factors[]` items carry an English `id` (`Tithi`, `RahuKaal`, …) next to the
  localized `name`, so match on `id`.

## Resource groups

`client.<Group>.<Method>Async(parameters)`, one method per endpoint:

| Group | What it covers |
|-------|----------------|
| `Panchang` | tithi, nakshatra, yoga, karana, vara, sunrise/sunset, rahu kaal, choghadiya, hora, muhurtas, monthly calendar, `BasicAsync` / `AdvancedAsync` |
| `Chart` | planets, ascendant, houses, divisional (D1–D60), dignity, aspects, shadbala, ashtakvarga, avakhada, KP (sub lords, ruling planets, significators, 4-step chain), astro details; data bundles `KundliFactsAsync`, `YogasAsync`, `DoshasAsync`, `SensitivityAsync` |
| `Dasha` | Vimshottari (5 levels), Yogini and Chara (`CharAsync`) |
| `Milan` | Ashtakoota (total, full, per koota), mangal / nadi / shani dosha, navamsa and dasha compatibility, `OverlayAsync`, `SummaryAsync` |
| `Transit` | positions, sade sati, ashtakavarga, tarabala, vedha, double transit, small panoti, dated `SadeSatiWindowsAsync` and `WindowsAsync` |
| `Horoscope` | daily, weekly, monthly by rashi |
| `Numerology` | driver, conductor, soul, personality, destiny, full, advanced, challenges, personal periods |
| `Muhurta` | vivah, naamkaran, griha pravesh, vyapar, yatra, best time, chandra / tara bala |
| `Varshaphal` | annual chart, lord, muntha, mudda dasha, harsha bala, tajika yogas |
| `Prashna` | horary answer, chart, arudha, significators, KP horary (1–249) |
| `Eclipses` | solar, lunar, all (date range) |
| `Calendar` | Hindu month, ritu, samvatsara, adhik maas, solstices |
| `Festivals` | festivals and vrats by month, on a date, by vrat family |
| `PlanetMoments` | retrograde windows, ingress, combustion window, speed |
| `Ashtakavarga` | sarva, bhinna, kaksha, transit score |
| `Lalkitab` | Lal Kitab chart and dasha |
| `Remedies` | by rule id, `ForGrahaAsync`, mantra, pooja, vrat, daan, rudraksha, yantra, Lal Kitab |
| `Narrative` | authored readings: lagna, planets, yogas, doshas, dasha phal, transit phal, prashna, outlooks, personalised horoscopes |
| `Reports` | reports as JSON, for [branded PDFs](#pdf-reports) or your own pages: kundli (lite, detailed, brihad), match making, mangal dosha, sade sati, varshaphal, numerology, dasha analysis, horoscopes |
| `Geo` | place search, reverse geocoding, time zone |
| `Tarot` | cards, a card, daily card, spreads, yes/no |
| `Meta` | free catalogs: endpoints with their credit cost, ayanamsas, yogas, doshas, locales, version |
| `Western` | tropical astrology: `Natal`, `Horoscope`, `Synastry`, `Composite`, `Davison`, `Transits`, `Progressions`, `SolarArc`, `Returns`, `Dignities`, `Profections`, `ZodiacalReleasing`, `PrimaryDirections`, `Interpretation`, `Narrative`, `Heliocentric`, `Astrocartography`, `Eclipses`, `Lunar`, `Compatibility`, plus `FirdariaAsync`, `IngressesAsync`, `RetrogradeWindowAsync` |

The XML documentation of each method (IntelliSense) gives its path, its credit cost and the meaning of every
parameter.

## Responses

Each method returns the response JSON as a `JsonElement`:

```csharp
JsonElement day = await client.Panchang.BasicAsync(p);
string? tithi = day.GetProperty("tithi").GetProperty("name").GetString();
```

The generic overload maps it to your own type with System.Text.Json:

```csharp
public sealed class Tithi { public int Index { get; set; } public string? Name { get; set; } public string? Paksha { get; set; } }
public sealed class Basic { public Tithi? Tithi { get; set; } public string? SunriseLocal { get; set; } }

Basic? basic = await client.Panchang.BasicAsync<Basic>(p);
```

The default options (`LifeAstroJson.DefaultOptions`) read the API's snake_case names into PascalCase
properties (`sunrise_local` → `SunriseLocal`), match case-insensitively, read numbers from JSON strings too
and ignore properties your class does not have, so your classes only need the fields you use. Pass your own
`JsonSerializerOptions` in the client options to change that (`LifeAstroJson.CreateOptions()` is a writable
copy of the defaults). `JsonNode`, `JsonDocument` and `Dictionary<string, JsonElement>` work as `T` too. A
response that does not map throws a `LifeAstroException` with code `invalid_response`.

The API returns each payload as bare JSON. A `{ data, meta }` envelope is unwrapped if a response ever carries
one; `new RequestOptions { Raw = true }` returns the body exactly as received.

## Async, cancellation and synchronous code

Every method is async and takes a `CancellationToken`:

```csharp
JsonElement chart = await client.Chart.PlanetsAsync(new ChartPlanetsParams(arjun), cancellationToken: ct);
```

A cancelled token throws `OperationCanceledException` at once (it is never retried); in ASP.NET Core pass
`HttpContext.RequestAborted` (or the `CancellationToken` parameter of a minimal API handler) so an abandoned
request stops its API call. Retry waits are `Task.Delay`, so they hold no thread. The SDK uses
`ConfigureAwait(false)` throughout.

There are no synchronous methods. In code that cannot be async (an old WebForms page event, a console
`Main` before C# 7.1), block on the task:

```csharp
JsonElement day = client.Panchang.BasicAsync(p).GetAwaiter().GetResult();
```

Blocking ties up a thread for the whole call and, on a UI thread or in classic ASP.NET, can deadlock *your*
code if an `await` of your own between the call and the block captures the synchronization context. The SDK
itself never resumes on the captured context, but prefer async all the way: `async Task<ActionResult>` in
ASP.NET MVC 5 and Web API 2, and `RegisterAsyncTask(new PageAsyncTask(...))` with `Async="true"` in WebForms.

## Error handling

Every non-2xx response throws an exception carrying the API's error `Code`, the HTTP `Status` and the
`RequestId`:

```csharp
try
{
    await client.Chart.PlanetsAsync(p);
}
catch (PaymentRequiredException)
{
    // 402: out of credits, or the trial is used up
}
catch (RateLimitException e)
{
    TimeSpan? wait = e.RetryAfter;               // from Retry-After, may be null
}
catch (LifeAstroException e)
{
    logger.LogError("{Status} {Code} {Message} (request {RequestId})", e.Status, e.Code, e.Message, e.RequestId);
}
```

| Exception | When |
|-----------|------|
| `BadRequestException` | 400: a parameter is missing, malformed or out of range |
| `AuthenticationException` | 401: missing or invalid API key |
| `PaymentRequiredException` | 402: out of credits |
| `PermissionException` | 403: the key may not make this call (plan, IP allow-list) |
| `NotFoundException` | 404: unknown path or id |
| `RateLimitException` | 429: rate limit or daily quota; `RetryAfter` |
| `ServerException` | 5xx |
| `LifeAstroConnectionException` | no HTTP response: DNS, refused or reset connection, TLS (code `connection_error`, `Status` 0) |
| `LifeAstroPdfException` | a PDF could not be printed (namespace `LifeAstroApi.Pdf`), see [PDF reports](#options-and-errors) |
| `LifeAstroTimeoutException` | the call took longer than the timeout (code `timeout`); it extends `LifeAstroConnectionException`, so catch it first |

A response that is not JSON (an HTML page from a proxy or firewall in between, for example) throws the exception of
its status with code `non_json_response`; the message carries the HTTP status and the start of the body.

All extend `LifeAstroException` (in the `LifeAstroApi` namespace; `Body` holds the parsed error body). 408,
425, 429, 500, 502, 503, 504, network errors and timeouts are retried first (exponential backoff from 0.5 s
up to 8 s with jitter, or the `Retry-After` wait up to 30 s), up to `MaxRetries` times. Other 4xx are not
retried. Failed calls are free. (If you also import `System.Security.Authentication`, write
`LifeAstroApi.AuthenticationException`.)

## Credits, rate limit and request id

Every response carries your credit balance, your plan's rate limit and a request id in its headers.
`OnResponse` hands them to you, for every call (client options) or for one call (`RequestOptions`):

```csharp
using var client = new LifeAstroClient(new LifeAstroClientOptions
{
    OnResponse = info =>
    {
        if (info.CreditsRemaining < 5_000)
        {
            logger.LogWarning("LifeAstroAPI credits low: {Credits}", info.CreditsRemaining);
        }
    },
});

await client.Panchang.BasicAsync(p, new RequestOptions
{
    OnResponse = info => logger.LogDebug("{RequestId} {RateLimit}", info.RequestId, info.RateLimit),
});
```

| `ResponseInfo` | From header | Meaning |
|---|---|---|
| `Status` | | HTTP status |
| `Path` | | Endpoint path, e.g. `/v1/panchang/basic` |
| `RequestId` | `X-Request-ID` | Quote it to support |
| `CreditsRemaining` | `X-Credits-Remaining` | Balance after the call (`long?`); unchanged after a failed call (failed calls are free). `null` on the free tier, whose allowance is daily/monthly rather than a balance |
| `RateLimit` | `X-RateLimit-*` | `Limit`, `Remaining` and `ResetAt` (a `DateTimeOffset`) per minute for your plan |
| `Cache` | `X-Cache` | `HIT` when the API answered from its cache (billed the same) |
| `Language` | `Content-Language` | Language of the answer |

The hook runs once per call with the final response, success or error; not for retried attempts or network
failures. An exception thrown inside it is ignored, so logging can never break a call. The client-level hook
runs first, then the per-call one.

## Per-call options

```csharp
await client.Chart.PlanetsAsync(p, new RequestOptions
{
    Timeout = TimeSpan.FromSeconds(5),   // instead of the client's
    Raw = true,                          // the body exactly as received
    OnResponse = info => { },
});
```

## ASP.NET Core

Add `LifeAstroApi.Extensions.DependencyInjection`. It registers one `LifeAstroClient` (a singleton) from
your configuration, sending its requests through `IHttpClientFactory`:

```json
// appsettings.json: the key itself goes in user secrets or an environment variable (LifeAstroApi__ApiKey)
{
  "LifeAstroApi": {
    "BaseUrl": "https://api.lifeastroapi.com",
    "Timeout": "00:00:30",
    "MaxRetries": 2
  }
}
```

```csharp
using LifeAstroApi;
using LifeAstroApi.Params;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLifeAstro(builder.Configuration.GetSection("LifeAstroApi"));
// or in code: builder.Services.AddLifeAstro(o => o.ApiKey = builder.Configuration["LifeAstro:Key"]);

var app = builder.Build();

app.MapGet("/panchang/today", (double lat, double lon, string tz, LifeAstroClient astro, CancellationToken ct) =>
    astro.Panchang.BasicAsync(new PanchangBasicParams(lat, lon, tz) { Lang = Lang.Gu }, cancellationToken: ct));

app.Run();
```

or in a controller:

```csharp
[ApiController]
[Route("api/kundli")]
public sealed class KundliController : ControllerBase
{
    private readonly LifeAstroClient _astro;

    public KundliController(LifeAstroClient astro) => _astro = astro;

    [HttpPost]
    public async Task<IActionResult> Chart(BirthRequest r, [FromQuery] string lang = "en", CancellationToken ct = default)
    {
        var birth = new BirthInput(r.Date, r.Time, r.Tz, r.Lat, r.Lon);
        JsonElement chart = await _astro.Chart.PlanetsAsync(
            new ChartPlanetsParams(birth) { Lang = LangExtensions.FromCode(lang) }, cancellationToken: ct);
        return Ok(chart);
    }
}

public sealed record BirthRequest(string Date, string Time, string Tz, double Lat, double Lon);
```

- `ApiKey`, `BaseUrl`, `Timeout`, `MaxRetries` and `DefaultHeaders` bind from the section. Without an
  `ApiKey` the client reads `LIFEASTRO_API_KEY`; without either, resolving the client throws an
  `InvalidOperationException` that says so.
- `AddLifeAstro(section, o => o.OnResponse = ...)` adds settings configuration cannot hold, such as a hook.
- The client is a singleton (immutable, thread-safe). Its named `HttpClient` (`"LifeAstroApi"`) recycles
  pooled connections every 5 minutes, so DNS changes are picked up even though the singleton keeps it.
  `AddLifeAstro` returns the `IHttpClientBuilder`, for a proxy or a delegating handler. The SDK already
  retries; do not stack another retry policy on top.
- Registration uses `TryAdd`: a `LifeAstroClient` you register yourself wins.
- The package uses `Microsoft.Extensions.*` 8.0.0, the lowest versions with both `net8.0` and
  `netstandard2.0` builds, so it never forces newer versions on an application.

## Entity Framework Core

The SDK does not touch your database; it calls the API and returns JSON. An EF Core application keeps its
own data and turns it into SDK inputs. (Nothing here needs an EF dependency in the SDK.)

**1. Store a customer's birth details in your own entity**, and map it to `BirthInput`:

```csharp
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateOnly BirthDate { get; set; }          // date column (EF Core 8+); DateTime works too
    public TimeOnly BirthTime { get; set; }          // time column; TimeSpan works too
    public string BirthTimeZone { get; set; } = "Asia/Kolkata";   // IANA id
    public double BirthLat { get; set; }
    public double BirthLon { get; set; }

    public BirthInput ToBirthInput() => new BirthInput(BirthDate, BirthTime, BirthTimeZone, BirthLat, BirthLon);
}
```

```csharp
public async Task<JsonElement> BirthChartAsync(int customerId, Lang lang, CancellationToken ct)
{
    Customer c = await _db.Customers.AsNoTracking().SingleAsync(x => x.Id == customerId, ct);
    return await _astro.Chart.PlanetsAsync(new ChartPlanetsParams(c.ToBirthInput()) { Lang = lang }, cancellationToken: ct);
}
```

**2. Cache what is shared**, for example today's panchang per city: it is the same for every user. A
parameter object's `ToString()` is its path and query, including today's date in the place's time zone, so it
makes a key that changes at local midnight:

```csharp
public Task<JsonElement> TodayAsync(Place city, Lang lang, CancellationToken ct)
{
    var p = new PanchangBasicParams(city) { Lang = lang };
    return _cache.GetOrCreateAsync(p.ToString(), entry =>          // IMemoryCache
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
        return _astro.Panchang.BasicAsync(p, cancellationToken: ct);
    });
}

// .NET 9+ HybridCache (package Microsoft.Extensions.Caching.Hybrid), shared across servers:
JsonElement day = await _hybrid.GetOrCreateAsync(p.ToString(),
    async token => await _astro.Panchang.BasicAsync(p, cancellationToken: token), cancellationToken: ct);
```

**3. Save a report's JSON** if you need it again: showing it later costs no credits.

```csharp
public class SavedReport
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Kind { get; set; } = "";           // "kundli-lite"
    public string Lang { get; set; } = "";           // "gu"
    public string Json { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

// OnModelCreating: jsonb on PostgreSQL (Npgsql), nvarchar(max) on SQL Server
modelBuilder.Entity<SavedReport>().Property(r => r.Json).HasColumnType("jsonb");   // or "nvarchar(max)"
```

```csharp
public async Task<SavedReport> SaveKundliReportAsync(int customerId, Lang lang, CancellationToken ct)
{
    Customer c = await _db.Customers.SingleAsync(x => x.Id == customerId, ct);
    JsonElement report = await _astro.Reports.KundliLiteAsync(
        new ReportsKundliLiteParams(c.ToBirthInput()) { Name = c.Name, Lang = lang }, cancellationToken: ct);
    var saved = new SavedReport
    {
        CustomerId = c.Id, Kind = "kundli-lite", Lang = lang.ToCode(), Json = report.GetRawText(), CreatedAt = DateTimeOffset.UtcNow,
    };
    _db.SavedReports.Add(saved);
    await _db.SaveChangesAsync(ct);
    return saved;
}
```

Read it back with `JsonDocument.Parse(saved.Json)` or `JsonSerializer.Deserialize<MyReport>(saved.Json, LifeAstroJson.DefaultOptions)`,
or print it again with `PdfReports.KundliLitePdfAsync(saved.Json)`.

## .NET Framework

The package's `netstandard2.0` build runs on .NET Framework 4.6.2 and later (4.7.2+ recommended: it needs
no binding redirects for the netstandard facades), in ASP.NET MVC 5, Web API 2, WebForms, WCF, Windows
services and desktop apps. Every API works from C# 7.3, the default language version of .NET Framework
projects.

- **TLS 1.2.** `api.lifeastroapi.com` accepts TLS 1.2 and later. .NET Framework 4.7+ uses the operating
  system's defaults; on 4.6.2 turn TLS 1.2 on once at startup (in `Application_Start` or `Main`):

  ```csharp
  ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
  ```

- **One client for the application**, in a static field or your DI container, not one per request.
- **Synchronous code:** `client.Panchang.BasicAsync(p).GetAwaiter().GetResult()`, with the deadlock caveat
  in [Async, cancellation and synchronous code](#async-cancellation-and-synchronous-code); prefer
  `async Task<ActionResult>` actions.
- **"Today" in a time zone:** when you leave `Date` out of a moment method, the SDK sends today's date in
  the method's `tz`. .NET Framework knows only Windows time zone ids, so the SDK maps the common IANA ids
  (`Asia/Kolkata`, `Asia/Dubai`, `Europe/London`, `America/New_York`, ...) to their Windows zones itself; UTC
  offsets such as `+05:30` work everywhere. For a zone it cannot resolve it sends the UTC date, so set
  `Date` yourself in that case. (On .NET 6+ `TimeZoneInfo` reads IANA ids on every OS.)

## Escape hatch

Call any `/v1/*` endpoint directly, for example one newer than this SDK version:

```csharp
JsonElement t = await client.RequestAsync("/v1/panchang/tithi", new Dictionary<string, object?>
{
    ["lat"] = 28.61, ["lon"] = 77.2, ["tz"] = "Asia/Kolkata", ["date"] = "2027-01-15",
});
MyType? x = await client.RequestAsync<MyType>("/v1/...", query, options, ct);
```

Keys are the API's wire names; values are strings, numbers, booleans, the SDK's enums, dates
(`DateTime`, `DateOnly`: `YYYY-MM-DD`) or times (`TimeSpan`, `TimeOnly`: `HH:mm`). Null and empty values
are left out. Formatting is culture-invariant.

## PDF reports

Turn the JSON of any `client.Reports` method into a branded, print-ready PDF (kundli lite, detailed and
Brihad, match making, mangal dosha, sade sati, varshaphal, numerology, dasha analysis, horoscopes) in any
of the 10 languages. It is in the `LifeAstroApi` package, namespace `LifeAstroApi.Pdf`, and adds no
dependency. Pass what the client returned: there is nothing to convert.

**Where the PDF is made.** On your side, by this package. LifeAstroAPI returns the report as JSON (that
call costs the report's credits); turning it into HTML and PDF costs nothing and sends nothing to
LifeAstroAPI.

```csharp
using LifeAstroApi.Pdf;

JsonElement report = await client.Reports.KundliDetailedAsync(
    new ReportsKundliDetailedParams(arjun) { Name = "Arjun Mehta", Place = "Mumbai", Lang = Lang.Hi });

byte[] pdf = await PdfReports.KundliDetailedPdfAsync(report, new PdfBranding { CompanyName = "Acme Astro" });
File.WriteAllBytes("kundli.pdf", pdf);
```

### Two layers

| Layer | Methods | Needs |
|-------|---------|-------|
| **HTML** | `PdfReports.Render*Html(report, branding)` | Nothing: no browser, process, file or network. A complete, self-contained document, the same bytes as the Node.js SDK 0.14.0. |
| **PDF** | `PdfReports.*PdfAsync(report, branding, options, engine, cancellationToken)`, `PdfReports.HtmlToPdfAsync(html)` for your own HTML | A PDF engine: Chrome, Chromium or Microsoft Edge on the machine, or a Gotenberg service. |

`report` is the `JsonElement` from the client or the report as a JSON string (for example one you saved).

| Report (`client.Reports`) | HTML | PDF |
|--------|------|-----|
| `KundliLiteAsync` | `RenderKundliLiteHtml` | `KundliLitePdfAsync` |
| `KundliDetailedAsync` | `RenderKundliDetailedHtml` | `KundliDetailedPdfAsync` |
| `KundliBrihadAsync` | `RenderKundliBrihadHtml` | `KundliBrihadPdfAsync` (table of contents, running header, bookmarks) |
| `MatchMakingAsync` | `RenderMatchMakingHtml` | `MatchMakingPdfAsync` |
| `MangalDoshaAsync` | `RenderMangalDoshaHtml` | `MangalDoshaPdfAsync` |
| `SadeSatiAsync` | `RenderSadeSatiHtml` | `SadeSatiPdfAsync` |
| `VarshaphalAsync` | `RenderVarshaphalHtml` | `VarshaphalPdfAsync` |
| `NumerologyAsync` | `RenderNumerologyHtml` | `NumerologyPdfAsync` |
| `DashaAnalysisAsync` | `RenderDashaAnalysisHtml` | `DashaAnalysisPdfAsync` |
| `HoroscopeDailyAsync`, `HoroscopeWeeklyAsync`, `HoroscopeMonthlyAsync` | `RenderHoroscopeHtml` | `HoroscopePdfAsync` |

The report's language is the `Lang` you requested it in; the PDF needs nothing extra for it.

### Branding

Every field is optional; what you set wins over the branding inside the report JSON.

```csharp
var branding = new PdfBranding
{
    CompanyName = "Acme Astro",
    LogoUrl = "https://acme.example/logo.png",   // or a data:image/... URI
    PrimaryColor = "#6b21a8",                    // headings, table headers: hex, name, rgb() or hsl()
    SecondaryColor = "#9333ea",                  // secondary text, footer
    FooterText = "Acme Astro · www.acme.example",
    WatermarkText = "SAMPLE",
};
```

- A field that is null or only white space is not set. Company name and footer text print as you write them.
- A colour too pale to read on paper prints in a darker shade of it.
- The watermark is faint, on every page (in the Brihad kundli behind the content), and sized so that it is
  not cut at the edge.
- The Brihad kundli is saffron until it gets a brand colour. Then its headings, banners, tables, charts,
  contents page numbers and running header and footer follow the brand. The API's default colours in the
  report are no brand. The company name and footer text are in its running header and footer.

### Choose a PDF engine

**1. Chrome, Chromium or Microsoft Edge (default).** With no engine given, each `*PdfAsync` call starts a
headless browser and closes it afterwards. The browser is looked up in this order:

1. `new ChromePdfEngineOptions { ExecutablePath = ... }`;
2. the environment variables `LIFEASTRO_CHROME`, `CHROME_PATH`, `PUPPETEER_EXECUTABLE_PATH`;
3. the usual install locations: Chrome, Chromium, then Edge. Windows 10 and 11 come with Edge.

Starting a browser takes a moment, so for more than one PDF create **one** engine and pass it to every call.
It is thread-safe, prints up to `MaxConcurrency` (4) documents at a time, and starts the browser again if it
has gone away:

```csharp
using (var engine = new ChromePdfEngine())
{
    byte[] lite = await PdfReports.KundliLitePdfAsync(liteReport, branding, engine: engine);
    byte[] match = await PdfReports.MatchMakingPdfAsync(matchReport, branding, engine: engine);
}
```

Each engine has a temporary browser profile of its own, deleted when the engine is disposed. The browser is
started with `--no-sandbox` only when your process runs as root, or when you set
`ChromePdfEngineOptions.NoSandbox = true`. Do that only for HTML you trust.
`WebSocketDebuggerUrl` uses a browser that already runs, for example in a container of its own: the engine
prints in a browser context of its own there, whose tabs close with the connection.

**2. Gotenberg (no browser next to your app).** Where you cannot run a browser, run
[Gotenberg](https://gotenberg.dev) somewhere you control. The report's HTML, with your customer's birth
details, goes to that service.

```bash
docker run --rm -p 3000:3000 gotenberg/gotenberg:8
```

```csharp
using var engine = new GotenbergPdfEngine("http://localhost:3000");   // optional: headers, your HttpClient
byte[] pdf = await PdfReports.MatchMakingPdfAsync(matchReport, engine: engine);
```

Any other converter works through the one-method `IPdfEngine` interface.

### ASP.NET Core: an endpoint that returns a PDF

```csharp
using LifeAstroApi;
using LifeAstroApi.Params;
using LifeAstroApi.Pdf;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLifeAstro(builder.Configuration.GetSection("LifeAstroApi"));
builder.Services.AddSingleton<IPdfEngine>(_ => new ChromePdfEngine());   // disposed with the application

var app = builder.Build();

app.MapPost("/kundli/pdf", async (BirthRequest r, LifeAstroClient astro, IPdfEngine engine, CancellationToken ct) =>
{
    var birth = new BirthInput(r.Date, r.Time, r.Tz, r.Lat, r.Lon);
    var report = await astro.Reports.KundliLiteAsync(
        new ReportsKundliLiteParams(birth) { Lang = Lang.Hi }, cancellationToken: ct);
    byte[] pdf = await PdfReports.KundliLitePdfAsync(
        report, new PdfBranding { CompanyName = "Acme Astro" }, engine: engine, cancellationToken: ct);
    return Results.File(pdf, "application/pdf", "kundli.pdf");
});

app.Run();

public sealed record BirthRequest(string Date, string Time, string Tz, double Lat, double Lon);
```

A Brihad kundli runs to a few hundred pages and is printed twice (the second time with the page of every
section in its table of contents): make it in a background job (`BackgroundService`, Hangfire, a queue
worker), not while a request waits.

### Desktop and mobile apps

Your backend calls the API, so the key stays on it. It can send the app the finished PDF, the HTML, or the
report JSON: `Render*Html` is pure .NET and runs inside the app too. The app prints the HTML with the
platform's web view. For a print API that ignores the CSS `@page` rule, `PdfReports.A4PrintOptions` has the
page size and margins, in points and in inches.

WPF and WinForms with WebView2 (package `Microsoft.Web.WebView2` in your app):

```csharp
// webView.CoreWebView2, after: await webView.EnsureCoreWebView2Async();
static async Task<bool> SavePdfAsync(CoreWebView2 web, string reportJson, string pdfPath)
{
    string html = PdfReports.RenderKundliLiteHtml(reportJson, new PdfBranding { CompanyName = "Acme Astro" });

    // A file, because NavigateToString takes 2 MB at most and a Brihad kundli can be larger.
    string page = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".html");
    File.WriteAllText(page, html);
    try
    {
        var loaded = new TaskCompletionSource<bool>();
        EventHandler<CoreWebView2NavigationCompletedEventArgs> done = (sender, e) => loaded.TrySetResult(e.IsSuccess);
        web.NavigationCompleted += done;
        try
        {
            web.Navigate(new Uri(page).AbsoluteUri);
            if (!await loaded.Task) return false;
        }
        finally
        {
            web.NavigationCompleted -= done;
        }

        PdfPrintGeometry a4 = PdfReports.A4PrintOptions;
        CoreWebView2PrintSettings settings = web.Environment.CreatePrintSettings();
        settings.PageWidth = a4.WidthInches;
        settings.PageHeight = a4.HeightInches;
        settings.MarginTop = a4.MarginTopInches;
        settings.MarginRight = a4.MarginRightInches;
        settings.MarginBottom = a4.MarginBottomInches;
        settings.MarginLeft = a4.MarginLeftInches;
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        return await web.PrintToPdfAsync(pdfPath, settings);
    }
    finally
    {
        File.Delete(page);
    }
}
```

The running header, table of contents and bookmarks of the Brihad kundli come from `KundliBrihadPdfAsync`,
so make that PDF on the backend.

### .NET Framework

The same API, from C# 7.3, for example in ASP.NET MVC 5. The Chrome engine uses `ClientWebSocket`, which on
.NET Framework needs Windows 8 / Server 2012 or later.

```csharp
public class KundliController : Controller
{
    private static readonly LifeAstroClient Astro = new LifeAstroClient();   // LIFEASTRO_API_KEY
    internal static readonly ChromePdfEngine Engine = new ChromePdfEngine(); // dispose it in Application_End

    public async Task<ActionResult> Pdf(CancellationToken cancellationToken)
    {
        var birth = new BirthInput("1990-01-15", "10:30", "Asia/Kolkata", 19.076, 72.8777);
        JsonElement report = await Astro.Reports.KundliLiteAsync(
            new ReportsKundliLiteParams(birth) { Lang = Lang.Hi }, cancellationToken: cancellationToken);
        byte[] pdf = await PdfReports.KundliLitePdfAsync(report, engine: Engine, cancellationToken: cancellationToken);
        return File(pdf, "application/pdf", "kundli.pdf");
    }
}
```

If the application pool's account is not allowed to start a browser, use `GotenbergPdfEngine`.

### Fonts

The text is shaped with the fonts of the machine that prints. Missing fonts show as empty boxes.

| Where it prints | Fonts for the Indian scripts |
|-----------------|------------------------------|
| Windows | the system's Nirmala UI, or Noto Sans when installed |
| macOS | the system fonts, or Noto Sans when installed |
| Debian, Ubuntu | `fonts-noto-core` |
| Other Linux | Noto Sans for Devanagari, Bengali, Gujarati, Gurmukhi, Tamil, Telugu, Kannada and Malayalam |
| Gotenberg | the fonts inside its image |

### Docker

The .NET 8 images are Debian, which packages Chromium:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends chromium fonts-noto-core
USER app
```

The .NET 10 images are Ubuntu, whose `chromium` package is a snap, so install Google Chrome (amd64):

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl fonts-noto-core \
 && curl -fsSLo /tmp/chrome.deb https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb \
 && apt-get install -y /tmp/chrome.deb && rm /tmp/chrome.deb
USER app
```

Docker's default security profile can stop Chrome from building its sandbox for a non-root user. Then either
allow it (`--security-opt seccomp=...` with a profile for Chrome), run the browser as a Gotenberg service, or,
when you print only your own report HTML, set `new ChromePdfEngineOptions { NoSandbox = true }`.

### Options and errors

```csharp
byte[] pdf = await PdfReports.NumerologyPdfAsync(report, options: new PdfOptions
{
    Format = PdfPaperFormat.Letter,              // A4 (default), A3, A5, Letter, Legal, Tabloid
    Landscape = false,
    Margin = new PdfMargin { Top = "25mm" },     // a side you set wins; the others keep the report's
    PrintBackground = true,                      // false saves ink (and keeps the watermark)
    Timeout = TimeSpan.FromSeconds(60),          // to load the document, its images and its fonts (30 s)
    PrintTimeout = TimeSpan.FromMinutes(5),      // to print it (default: four times Timeout)
}, cancellationToken: ct);
```

These options win over the report's own print CSS: where that needs CSS, the SDK adds it to the HTML it
hands to Chrome, to Gotenberg or to an engine of your own.

- A `Format` other than A4, or `Landscape`, sets the paper. `PreferCssPageSize = true` keeps the document's
  own page size instead, and `false` scales the document to the paper.
- `DisplayHeaderFooter = true` with a `FooterTemplate` prints your footer in place of the report's footer line.
- The Brihad kundli has its running header and footer unless you set `DisplayHeaderFooter = false`. Your
  `HeaderTemplate` or `FooterTemplate` replaces the report's, and your margins replace its own side by side
  (26mm top, 16mm bottom, 0 at the sides).
- A long Brihad on a busy server may need a longer `PrintTimeout`, which is for each of its two prints.

A failed print throws `LifeAstroPdfException` (a `LifeAstroException`) with `Code` `browser_not_found` (the
message says what to install), `browser_error`, `timeout`, `connection_error`, `render_failed` or
`invalid_pdf`, or `engine_closed` when the engine was disposed before the PDF was done. JSON that cannot be
printed as that report throws `ArgumentException`, and a cancelled print `OperationCanceledException`. A
field that should be a list and is not does not throw: that block is left out, or the lone value is printed.
A null inside a list does not throw either: that item prints empty or is left out.

**Logo URL.** The browser that prints fetches it. If your customers type it in, check it, or store the image
and pass a `data:image/...` URI. Only `http(s)`, `data:image` and relative URLs are used (`//host/logo.png`
becomes https); others are dropped.

## Development

The resource and parameter classes are generated from the Node.js SDK's endpoint manifest, so the SDKs have
the same endpoints, method names and wire parameters. With the Node SDK checked out next to this repository
(`../lifeastroapi-node`, built with `npm run build`) and Node.js 18+:

```bash
node tool/generate.mjs          # src/LifeAstroApi/Generated, tests/Shared/Samples.g.cs, test manifest
node tool/node-goldens.mjs      # records the Node SDK's request for every method (parity fixture)
node tool/generate-pdf.mjs      # src/LifeAstroApi/Pdf/Generated, from the Node SDK's src/pdf templates
node tool/pdf-goldens.mjs       # records Node's HTML for the PDF tests
dotnet build -c Release         # both targets, warnings are errors
dotnet test -c Release
dotnet pack -c Release -o artifacts
```

`NodeParityTests` checks that every method sends exactly the path and query string the Node.js SDK sends for
the same input, with and without `Lang`. To call every method against a running API (one request each; there
is no default URL, so it never reaches production by accident):

```bash
LIFEASTRO_API_KEY=dv_test_... dotnet run -c Release --project tools/LiveSweep -- --base-url http://127.0.0.1:18080
```

The PDF templates are the Node.js SDK's, translated by `tool/generate-pdf.mjs`, and the tests check that
every renderer gives byte-identical HTML. When `../LifeAstroAPI/pdf_work` exists (or `LIFEASTRO_PDF_WORK`
points to it) they also compare 432 full reports. The browser tests need Chrome, Chromium or Edge and are
skipped without one; they opt in to `--no-sandbox` on Linux CI, or with `LIFEASTRO_TEST_NO_SANDBOX=1`.

The .NET 10 SDK builds everything (`global.json`); the libraries target `netstandard2.0` and `net8.0`.

## License

MIT, see [LICENSE](https://github.com/ayanshulife/lifeastroapi-dotnet/blob/main/LICENSE).
