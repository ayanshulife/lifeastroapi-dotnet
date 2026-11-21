// Live API sweep: calls EVERY SDK method once against a running API and lists each one the API
// rejects. This checks that the paths and parameter names match what the server accepts; the stub
// tests cannot see that. A port of the Node SDK's scripts/live-sweep.mjs (and the Java SDK's
// LiveSweepTest). Uses the sample inputs of tests/Shared/Samples.g.cs.
//
//   LIFEASTRO_API_KEY=dv_test_... dotnet run -c Release --project tools/LiveSweep -- --base-url http://127.0.0.1:18080
//
// --base-url is required: there is no default, so the sweep never reaches production by accident.
// Spends credits on a billed server: one request per endpoint. Options:
//   --base-url URL         the API base URL (required)
//   --skip a,b             path prefixes to leave out; default /v1/reports/ (reports cost 500-8000 credits)
//   --skip-none            include everything
//   --only a,b             substrings; only matching paths or keys run
//   --lang CODE            language sent to every method that takes Lang (en hi mr gu bn te ta kn ml pa)
//   --dump DIR             write each response to DIR/<key>.json
//   --pause-ms N           pause between calls, default 450 (per-minute rate limit)
//   --timeout-s N          per-call timeout, default 90
//   --allow-production     allow api.lifeastroapi.com
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeAstroApi;
using LifeAstroApi.Samples;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? baseUrl = null;
        var skip = new List<string> { "/v1/reports/" };
        var only = new List<string>();
        Lang? lang = null;
        string? dump = null;
        int pauseMs = 450;
        int timeoutS = 90;
        bool allowProduction = false;
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(args[i] + " needs a value");
            switch (args[i])
            {
                case "--base-url": baseUrl = Next(); break;
                case "--skip": skip = Split(Next()); break;
                case "--skip-none": skip.Clear(); break;
                case "--only": only = Split(Next()); break;
                case "--lang": lang = LangExtensions.FromCode(Next()); break;
                case "--dump": dump = Next(); break;
                case "--pause-ms": pauseMs = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--timeout-s": timeoutS = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--allow-production": allowProduction = true; break;
                case "-h":
                case "--help":
                    Console.WriteLine("Usage: LiveSweep --base-url URL [--skip a,b | --skip-none] [--only a,b] [--lang gu] [--dump DIR] [--pause-ms 450] [--timeout-s 90] [--allow-production]");
                    return 0;
                default:
                    Console.Error.WriteLine("Unknown option " + args[i] + "; see --help.");
                    return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Console.Error.WriteLine("--base-url is required (there is no default), e.g. --base-url http://127.0.0.1:18080");
            return 2;
        }

        string host = new Uri(baseUrl).Host;
        if (host.EndsWith("lifeastroapi.com", StringComparison.OrdinalIgnoreCase) && !allowProduction)
        {
            Console.Error.WriteLine("Refusing to sweep production (" + baseUrl + "); pass --allow-production to do so. It spends credits.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LifeAstroClient.ApiKeyEnvironmentVariable)))
        {
            Console.Error.WriteLine(LifeAstroClient.ApiKeyEnvironmentVariable + " is required.");
            return 2;
        }

        if (dump != null)
        {
            Directory.CreateDirectory(dump);
        }

        ResponseInfo? last = null;
        var failures = new List<(string Key, string Path, string Status, string Code, string Message)>();
        int ran = 0;
        var watch = Stopwatch.StartNew();
        using var client = new LifeAstroClient(new LifeAstroClientOptions
        {
            BaseUrl = baseUrl,
            Timeout = TimeSpan.FromSeconds(timeoutS),
            MaxRetries = 0, // a retried failure is still a failure worth seeing
            OnResponse = info => last = info,
        });
        foreach (Sample s in Samples.All())
        {
            if (skip.Any(p => s.Path.StartsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }

            if (only.Count > 0 && !only.Any(o => s.Path.Contains(o, StringComparison.Ordinal) || s.Key.Contains(o, StringComparison.Ordinal)))
            {
                continue;
            }

            ran++;
            try
            {
                JsonElement data = await s.Call(client, s.HasLang ? lang : null);
                if (dump != null)
                {
                    await File.WriteAllTextAsync(Path.Combine(dump, s.Key + ".json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                }
            }
            catch (LifeAstroException e)
            {
                failures.Add((s.Key, s.Path, e.Status.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Code, e.Message));
                Console.WriteLine("FAIL " + s.Key + "  (" + s.Path + ")");
                Console.WriteLine("     " + e.Status + " " + e.Code + " " + e.Message);
            }
            catch (Exception e)
            {
                failures.Add((s.Key, s.Path, "-", e.GetType().Name, e.Message));
                Console.WriteLine("FAIL " + s.Key + "  (" + s.Path + ")");
                Console.WriteLine("     " + e);
            }

            if (pauseMs > 0)
            {
                await Task.Delay(pauseMs);
            }
        }

        Console.WriteLine();
        if (failures.Count > 0)
        {
            Console.WriteLine("Failures by status and code:");
            foreach (var g in failures.GroupBy(f => f.Status + " " + f.Code + ": " + f.Message).OrderByDescending(g => g.Count()))
            {
                Console.WriteLine("  " + g.Count().ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(4) + "  " + g.Key);
            }

            Console.WriteLine();
        }

        Console.WriteLine(
            "live sweep: " + ran + " methods called, " + (ran - failures.Count) + " OK, " + failures.Count + " failed"
            + (lang == null ? string.Empty : " (lang=" + lang.Value.ToCode() + ")")
            + " in " + watch.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s"
            + (last == null ? string.Empty : "; last response: credits remaining " + (last.CreditsRemaining?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a")
                + ", rate limit " + (last.RateLimit?.ToString() ?? "n/a") + ", request " + (last.RequestId ?? "n/a")));
        return ran == 0 ? 2 : failures.Count == 0 ? 0 : 1;
    }

    private static List<string> Split(string value) =>
        value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
}
