# Examples

| Folder | What it shows |
|--------|---------------|
| [QuickStart](QuickStart/) | Console app: panchang in Gujarati, a birth chart, kundli matching mapped to your own type, credits and request id from `OnResponse`, error handling. |
| [AspNetCore](AspNetCore/) | Minimal API: `AddLifeAstro` from `appsettings.json`, today's panchang cached per city with `IMemoryCache`, a birth chart and kundli matching from JSON input, SDK errors as problem responses. |

Both reference the SDK projects in this repository. In your own project use the packages instead:

```bash
dotnet add package LifeAstroApi                                   # console, worker, .NET Framework
dotnet add package LifeAstroApi.Extensions.DependencyInjection   # ASP.NET Core (brings LifeAstroApi)
```

They target .NET 10; change `TargetFramework` to `net8.0` or `net9.0` to run them on those runtimes.

```bash
export LIFEASTRO_API_KEY=dv_live_...
dotnet run --project examples/QuickStart

dotnet user-secrets init --project examples/AspNetCore
dotnet user-secrets set "LifeAstroApi:ApiKey" "dv_live_..." --project examples/AspNetCore
dotnet run --project examples/AspNetCore
curl "http://localhost:5000/panchang/today?lat=19.076&lon=72.8777&tz=Asia/Kolkata&lang=gu"
```
