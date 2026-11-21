// A minimal ASP.NET Core API on top of LifeAstroAPI.
//
//   dotnet user-secrets init --project examples/AspNetCore            # once
//   dotnet user-secrets set "LifeAstroApi:ApiKey" "dv_live_..." --project examples/AspNetCore
//   dotnet run --project examples/AspNetCore
//
//   curl "http://localhost:5000/panchang/today?lat=19.076&lon=72.8777&tz=Asia/Kolkata&lang=gu"
//   curl -X POST http://localhost:5000/kundli -H "Content-Type: application/json" \
//        -d '{"date":"1990-01-15","time":"10:30","tz":"Asia/Kolkata","lat":19.076,"lon":72.8777}'
//
// The key can also come from the environment: LifeAstroApi__ApiKey or LIFEASTRO_API_KEY.
using System.Text.Json;
using LifeAstroApi;
using LifeAstroApi.Params;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLifeAstro(builder.Configuration.GetSection("LifeAstroApi"));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PanchangService>();

var app = builder.Build();

// SDK errors become problem responses: a bad input stays 400, the rest is the upstream's fault (502).
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (ArgumentException e)
    {
        await Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
    }
    catch (BadRequestException e)
    {
        await Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = e.Code }).ExecuteAsync(context);
    }
    catch (LifeAstroException e)
    {
        app.Logger.LogError(e, "LifeAstroAPI call failed: {Status} {Code}, request {RequestId}", e.Status, e.Code, e.RequestId);
        await Results.Problem("The astrology service is unavailable.", statusCode: StatusCodes.Status502BadGateway).ExecuteAsync(context);
    }
});

// GET /panchang/today?lat=19.076&lon=72.8777&tz=Asia/Kolkata&lang=gu  (cached per city, language and day)
app.MapGet("/panchang/today", async (double lat, double lon, string? tz, string? lang, PanchangService panchang, CancellationToken ct) =>
    Results.Ok(await panchang.TodayAsync(new Place(lat, lon, tz ?? "Asia/Kolkata"), Languages.Parse(lang), ct)));

// POST /kundli?lang=hi  {"date":"1990-01-15","time":"10:30","tz":"Asia/Kolkata","lat":19.076,"lon":72.8777}
app.MapPost("/kundli", async (BirthRequest birth, string? lang, LifeAstroClient astro, CancellationToken ct) =>
    Results.Ok(await astro.Chart.PlanetsAsync(
        new ChartPlanetsParams(birth.ToBirthInput()) { Lang = Languages.Parse(lang) },
        cancellationToken: ct)));

// POST /match  {"boy":{...},"girl":{...},"lang":"hi"}
app.MapPost("/match", async (MatchRequest match, LifeAstroClient astro, CancellationToken ct) =>
    Results.Ok(await astro.Milan.AshtakootaAsync(
        new MilanAshtakootaParams(match.Boy.ToBirthInput(), match.Girl.ToBirthInput()) { Lang = Languages.Parse(match.Lang) },
        cancellationToken: ct)));

app.Run();

/// <summary>Birth details as your API receives them.</summary>
internal sealed record BirthRequest(string Date, string Time, string Tz, double Lat, double Lon)
{
    public BirthInput ToBirthInput() => new BirthInput(Date, Time, Tz, Lat, Lon);
}

/// <summary>A kundli matching request.</summary>
internal sealed record MatchRequest(BirthRequest Boy, BirthRequest Girl, string? Lang);

/// <summary>Today's panchang per place and language, cached: it is the same for every user of a city.</summary>
internal sealed class PanchangService(LifeAstroClient astro, IMemoryCache cache)
{
    public async Task<JsonElement> TodayAsync(Place place, Lang? lang, CancellationToken ct)
    {
        var p = new PanchangBasicParams(place) { Lang = lang };

        // ToString() is the request path and query, including today's date in the place's time zone,
        // so the key changes at local midnight.
        return await cache.GetOrCreateAsync(p.ToString(), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            return astro.Panchang.BasicAsync(p, cancellationToken: ct);
        });
    }
}

internal static class Languages
{
    /// <summary>"gu" to <see cref="Lang.Gu"/>; null when not given.</summary>
    public static Lang? Parse(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null
        : LangExtensions.TryFromCode(code, out Lang lang) ? lang
        : throw new ArgumentException($"Unknown language \"{code}\"; use en, hi, mr, gu, bn, te, ta, kn, ml or pa.");
}
