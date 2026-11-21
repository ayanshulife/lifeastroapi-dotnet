# LifeAstroAPI .NET SDK

Official **C# / .NET SDK** for [LifeAstroAPI](https://lifeastroapi.com), a REST
[astrology API](https://lifeastroapi.com/apis/) for Vedic astrology and the Hindu calendar: panchang,
festivals, kundli, dasha, kundli matching, muhurat, transits, horoscopes, numerology, Lal Kitab, remedies,
readings, JSON reports, plus Western astrology and tarot. Location-aware, in 10 languages, pay per call.
Free tier: 1,500 credits a month, no card, [get a key](https://lifeastroapi.com/signup/).

- **All 322 endpoints** as 323 async methods in 23 resource groups (`Chart.NavamsaAsync` is a shortcut for
  the D9 chart).
- **.NET 8, 9 and 10, ASP.NET Core, and .NET Framework 4.6.2+** (the package targets `net8.0` and
  `netstandard2.0`). One dependency: `System.Text.Json`, which .NET 8 already has.
- **Dependency injection package** for ASP.NET Core: `services.AddLifeAstro(...)` from `appsettings.json`,
  requests through `IHttpClientFactory`.
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
<PackageReference Include="LifeAstroApi" Version="0.1.0" />
```

In an ASP.NET Core application, add the dependency injection package instead (it brings `LifeAstroApi`
with it), see [ASP.NET Core](#aspnet-core):

```bash
dotnet add package LifeAstroApi.Extensions.DependencyInjection
```

```xml
<PackageReference Include="LifeAstroApi.Extensions.DependencyInjection" Version="0.1.0" />
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
| `Reports` | JSON reports: kundli (lite, detailed, brihad), match making, mangal dosha, sade sati, varshaphal, numerology, dasha analysis, horoscopes |
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
| `LifeAstroTimeoutException` | the call took longer than the timeout (code `timeout`); it extends `LifeAstroConnectionException`, so catch it first |

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

Read it back with `JsonDocument.Parse(saved.Json)` or `JsonSerializer.Deserialize<MyReport>(saved.Json, LifeAstroJson.DefaultOptions)`.

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

The JSON report endpoints (`client.Reports`) are in this version. Branded PDF rendering of those reports,
as the Node.js SDK does it (`lifeastroapi/pdf`), is planned for a later version.

## Development

The resource and parameter classes are generated from the Node.js SDK's endpoint manifest, so the SDKs have
the same endpoints, method names and wire parameters. With the Node SDK checked out next to this repository
(`../lifeastroapi-node`, built with `npm run build`) and Node.js 18+:

```bash
node tool/generate.mjs          # src/LifeAstroApi/Generated, tests/Shared/Samples.g.cs, test manifest
node tool/node-goldens.mjs      # records the Node SDK's request for every method (parity fixture)
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

The .NET 10 SDK builds everything (`global.json`); the libraries target `netstandard2.0` and `net8.0`.

## License

MIT, see [LICENSE](https://github.com/ayanshulife/lifeastroapi-dotnet/blob/main/LICENSE).
