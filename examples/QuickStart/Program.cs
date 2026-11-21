// The README's quick start as a runnable console app:
//
//   export LIFEASTRO_API_KEY=dv_live_...        # your key; never commit it
//   dotnet run --project examples/QuickStart
//
// LIFEASTRO_BASE_URL points it at another server (for example a local one).
using System.Text.Json;
using LifeAstroApi;
using LifeAstroApi.Params;

using var client = new LifeAstroClient(new LifeAstroClientOptions
{
    // ApiKey = "dv_live_...",   // or leave it out and set LIFEASTRO_API_KEY
    BaseUrl = Environment.GetEnvironmentVariable("LIFEASTRO_BASE_URL") ?? LifeAstroClient.DefaultBaseUrl,
    OnResponse = info => Console.WriteLine(
        $"    [{info.Status} {info.Path}] credits left: {info.CreditsRemaining?.ToString() ?? "n/a"}, request {info.RequestId ?? "n/a"}"),
});

try
{
    // Today's panchang in Mumbai, in Gujarati
    var mumbai = new Place(19.076, 72.8777, "Asia/Kolkata");
    JsonElement day = await client.Panchang.BasicAsync(new PanchangBasicParams(mumbai) { Lang = Lang.Gu });
    Console.WriteLine("Tithi:     " + day.GetProperty("tithi").GetProperty("name").GetString());
    Console.WriteLine("Nakshatra: " + day.GetProperty("nakshatra").GetProperty("name").GetString());
    Console.WriteLine("Sunrise:   " + day.GetProperty("sunrise_local").GetString());

    // A Vedic birth chart
    var arjun = new BirthInput("1990-01-15", "10:30", "Asia/Kolkata", 19.076, 72.8777);
    JsonElement chart = await client.Chart.PlanetsAsync(new ChartPlanetsParams(arjun));
    foreach (JsonElement p in chart.GetProperty("planets").EnumerateArray())
    {
        Console.WriteLine($"{p.GetProperty("name").GetString(),-8} house {p.GetProperty("house").GetInt32(),2}, {p.GetProperty("dms_in_sign").GetString()}");
    }

    // Kundli matching (Ashtakoota, 36 points), mapped to your own type
    var girl = new BirthInput("1992-06-20", "14:15", "Asia/Kolkata", 28.6139, 77.209);
    Match? match = await client.Milan.AshtakootaAsync<Match>(new MilanAshtakootaParams(arjun, girl) { Lang = Lang.Hi });
    Console.WriteLine($"Ashtakoota: {match?.Total} / {match?.Max}");
}
catch (PaymentRequiredException)
{
    Console.Error.WriteLine("Out of credits: top up at https://lifeastroapi.com/dashboard");
}
catch (LifeAstroException e)
{
    Console.Error.WriteLine($"{e.Status} {e.Code}: {e.Message} (request {e.RequestId})");
}

/// <summary>The part of the Ashtakoota answer this app reads; snake_case JSON maps to PascalCase.</summary>
internal sealed class Match
{
    public double Total { get; set; }

    public double Max { get; set; }
}
