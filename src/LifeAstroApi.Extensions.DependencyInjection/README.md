# LifeAstroApi.Extensions.DependencyInjection

Registers the official [LifeAstroAPI](https://lifeastroapi.com) .NET client (package
[`LifeAstroApi`](https://www.nuget.org/packages/LifeAstroApi)) in ASP.NET Core and any generic host:
one shared, thread-safe `LifeAstroClient`, its options bound from `appsettings.json`, its requests sent
through `IHttpClientFactory`.

```bash
dotnet add package LifeAstroApi.Extensions.DependencyInjection
```

```json
// appsettings.json (keep the key itself in user secrets or an environment variable)
{
  "LifeAstroApi": {
    "ApiKey": "dv_live_...",
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
// or: builder.Services.AddLifeAstro(o => o.ApiKey = Environment.GetEnvironmentVariable("LIFEASTRO_API_KEY"));

var app = builder.Build();

app.MapGet("/panchang/today", (LifeAstroClient astro, CancellationToken ct) =>
    astro.Panchang.BasicAsync(
        new PanchangBasicParams(19.076, 72.8777, "Asia/Kolkata") { Lang = Lang.Gu },
        cancellationToken: ct));

app.Run();
```

- `ApiKey`, `BaseUrl`, `Timeout`, `MaxRetries` and `DefaultHeaders` bind from the section. Without an
  `ApiKey` the client reads the `LIFEASTRO_API_KEY` environment variable; without either, resolving the
  client throws an `InvalidOperationException` that says so.
- The client is a singleton (it is immutable and thread-safe). Its named `HttpClient`
  (`"LifeAstroApi"`) recycles pooled connections every 5 minutes, so DNS changes are picked up.
- `AddLifeAstro` returns the `IHttpClientBuilder`, for a proxy or a delegating handler.
- Registration uses `TryAdd`: a `LifeAstroClient` you register yourself wins.

Full documentation: https://github.com/ayanshulife/lifeastroapi-dotnet and https://lifeastroapi.com/docs/.
