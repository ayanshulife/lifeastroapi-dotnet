using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LifeAstroApi.Params;
using Xunit;

namespace LifeAstroApi.Tests
{
    /// <summary>URL, headers, envelope and body handling: the "transport", "escape hatch", "non-enveloped success bodies" and "query encoding edge cases" of the Node SDK's client.test.ts.</summary>
    public class TransportTests
    {
        private readonly StubHandler _stub = new StubHandler();

        private static PanchangTithiParams TithiAt(double lat, double lon, string tz) => new PanchangTithiParams(lat, lon, tz) { Date = "2027-01-15" };

        private static string Raw(JsonElement e) => e.GetRawText();

        [Fact]
        public async Task BuildsAnAuthenticatedGetAndUnwrapsData()
        {
            _stub.Respond(Reply.Ok("{\"data\":{\"number\":3},\"meta\":{\"cache\":\"miss\"}}"));
            using LifeAstroClient client = Fixtures.Client(_stub);
            JsonElement res = await client.Panchang.TithiAsync(TithiAt(28.61, 77.2, "Asia/Kolkata"));

            Assert.Equal("{\"number\":3}", Raw(res));
            Recorded call = _stub.Last;
            Assert.Equal("GET", call.Method);
            Assert.Equal("sdk.test", call.Uri.Host);
            Assert.Equal("/v1/panchang/tithi", call.Path);
            Assert.Equal("28.61", call.Query["lat"]);
            Assert.Equal("Asia/Kolkata", call.Query["tz"]);
            Assert.Equal("Bearer dv_live_test", call.Header("Authorization"));
            Assert.Equal("application/json", call.Header("Accept"));
            Assert.Equal("lifeastroapi-dotnet/" + LifeAstroClient.Version, call.Header("User-Agent"));
        }

        [Fact]
        public async Task ReturnsTheFullEnvelopeWithRaw()
        {
            _stub.Respond(Reply.Ok("{\"data\":{\"x\":1},\"meta\":{\"tier\":\"standard\"}}"));
            using LifeAstroClient client = Fixtures.Client(_stub);
            JsonElement res = await client.RequestAsync(
                "/v1/panchang/tithi",
                new Dictionary<string, object?> { ["lat"] = 1, ["lon"] = 2, ["tz"] = "UTC" },
                new RequestOptions { Raw = true });
            Assert.Equal("{\"data\":{\"x\":1},\"meta\":{\"tier\":\"standard\"}}", Raw(res));
        }

        [Fact]
        public async Task OmitsEmptyParamsAndSortsTheQueryString()
        {
            using LifeAstroClient client = Fixtures.Client(_stub);
            await client.Panchang.TithiAsync(new PanchangTithiParams(28.61, 77.2, "Asia/Kolkata") { Date = "" });
            Assert.False(_stub.Last.Query.ContainsKey("date"));
            Assert.Equal("/v1/panchang/tithi?lat=28.61&lon=77.2&tz=Asia%2FKolkata", _stub.Last.PathAndQuery);
        }

        [Fact]
        public async Task KeepsALegitimateZero()
        {
            using LifeAstroClient client = Fixtures.Client(_stub);
            await client.Panchang.TithiAsync(TithiAt(0, 0, "UTC"));
            Assert.Equal("0", _stub.Last.Query["lat"]);
            Assert.Equal("0", _stub.Last.Query["lon"]);
        }

        [Fact]
        public async Task UrlEncodesAFixedOffsetTimeZone()
        {
            using LifeAstroClient client = Fixtures.Client(_stub);
            await client.Panchang.TithiAsync(TithiAt(1, 2, "+05:30"));
            Assert.Contains("tz=%2B05%3A30", _stub.Last.PathAndQuery);
            Assert.Equal("+05:30", _stub.Last.Query["tz"]);
        }

        [Fact]
        public async Task PercentEncodesPathParameters()
        {
            using LifeAstroClient client = Fixtures.Client(_stub);
            await client.Chart.PlanetAsync(new ChartPlanetParams("a b/c", Fixtures.Arjun));
            Assert.Equal("/v1/chart/planet/a%20b%2Fc", _stub.Last.Path);
        }

        [Fact]
        public async Task SendsDefaultHeadersAndTheyWinOverTheSdkHeaders()
        {
            using (LifeAstroClient client = Fixtures.Client(_stub, o => o.DefaultHeaders["X-Trace"] = "abc"))
            {
                await client.Panchang.TithiAsync(TithiAt(1, 2, "UTC"));
            }

            Assert.Equal("abc", _stub.Last.Header("X-Trace"));
            using (LifeAstroClient client = Fixtures.Client(_stub, o => o.DefaultHeaders["authorization"] = "Bearer dv_live_other"))
            {
                await client.Meta.VersionAsync();
            }

            Assert.Equal("Bearer dv_live_other", _stub.Last.Header("Authorization"));
        }

        [Fact]
        public async Task EscapeHatchHitsAnArbitraryPathAndUnwrapsData()
        {
            _stub.Respond(Reply.Ok("{\"data\":{\"v\":1}}"));
            using LifeAstroClient client = Fixtures.Client(_stub);
            Assert.Equal("{\"v\":1}", Raw(await client.RequestAsync("/v1/anything", new Dictionary<string, object?> { ["a"] = "b" })));
            Assert.Equal("{\"v\":1}", Raw(await client.RequestAsync("v1/no-slash")));
            Assert.Equal("/v1/anything", _stub.Calls[0].Path);
            Assert.Equal("b", _stub.Calls[0].Query["a"]);
            Assert.Equal("/v1/no-slash", _stub.Calls[1].Path);
        }

        [Fact]
        public async Task EscapeHatchFormatsValuesAsTheNodeSdk()
        {
            using LifeAstroClient client = Fixtures.Client(_stub);
            await client.RequestAsync("/v1/x", new Dictionary<string, object?>
            {
                ["b"] = true,
                ["a"] = 5.0,
                ["c"] = Lang.Gu,
                ["d"] = Ayanamsa.Krishnamurti,
                ["e"] = null,
                ["f"] = "",
                ["g"] = 1e-7,
                ["h"] = "a b+c",
                ["i"] = HouseSystem.WholeSign,
                ["j"] = new System.DateTime(2027, 1, 15, 18, 30, 0),
                ["k"] = new System.TimeSpan(6, 5, 0),
                ["l"] = 12.50m,
            });
            Assert.Equal("/v1/x?a=5&b=true&c=gu&d=krishnamurti&g=1e-7&h=a+b%2Bc&i=whole_sign&j=2027-01-15&k=06%3A05&l=12.5", _stub.Last.PathAndQuery);
        }

        [Fact]
        public async Task ReturnsABareArrayBodyAsIs()
        {
            _stub.Respond(Reply.Ok("[1,2,3]"));
            using LifeAstroClient client = Fixtures.Client(_stub);
            Assert.Equal("[1,2,3]", Raw(await client.RequestAsync("/v1/list")));
        }

        [Fact]
        public async Task ReturnsANoDataObjectBodyAsIs()
        {
            _stub.Respond(Reply.Ok("{\"foo\":1,\"bar\":2}"));
            using LifeAstroClient client = Fixtures.Client(_stub);
            Assert.Equal("{\"foo\":1,\"bar\":2}", Raw(await client.RequestAsync("/v1/raw")));
        }

        [Fact]
        public async Task ReturnsANonJsonSuccessBodyAsAJsonString()
        {
            _stub.Respond(new Reply(200, Encoding.UTF8.GetBytes("plain text"), new Dictionary<string, string> { ["Content-Type"] = "text/plain" }));
            using LifeAstroClient client = Fixtures.Client(_stub);
            JsonElement res = await client.RequestAsync("/v1/text");
            Assert.Equal(JsonValueKind.String, res.ValueKind);
            Assert.Equal("plain text", res.GetString());
            Assert.Equal("plain text", await client.RequestAsync<string>("/v1/text"));
        }

        [Fact]
        public async Task AnEmptyBodyIsAJsonNullOrDefault()
        {
            _stub.Respond(new Reply(200, new byte[0], new Dictionary<string, string>()));
            using LifeAstroClient client = Fixtures.Client(_stub);
            Assert.Equal(JsonValueKind.Null, (await client.RequestAsync("/v1/empty")).ValueKind);
            Assert.Null(await client.RequestAsync<Dictionary<string, object>>("/v1/empty"));
            Assert.Equal(0, await client.RequestAsync<int>("/v1/empty"));
        }

        [Fact]
        public async Task ReadsAGzipBodyTheHandlerDidNotDecode()
        {
            byte[] gz;
            using (var bytes = new MemoryStream())
            {
                using (var zip = new GZipStream(bytes, CompressionLevel.Optimal, leaveOpen: true))
                {
                    byte[] json = Encoding.UTF8.GetBytes("{\"tithi\":{\"name\":\"Saptami\"}}");
                    zip.Write(json, 0, json.Length);
                }

                gz = bytes.ToArray();
            }

            _stub.Respond(new Reply(200, gz, new Dictionary<string, string> { ["Content-Type"] = "application/json", ["Content-Encoding"] = "gzip" }));
            using LifeAstroClient client = Fixtures.Client(_stub);
            JsonElement res = await client.RequestAsync("/v1/x");
            Assert.Equal("Saptami", res.GetProperty("tithi").GetProperty("name").GetString());
        }

        [Fact]
        public async Task KeepsNonAsciiTextIntact()
        {
            _stub.Respond(Reply.Ok("{\"tithi\":{\"name\":\"સપ્તમી\"}}"));
            using LifeAstroClient client = Fixtures.Client(_stub);
            JsonElement res = await client.Panchang.TithiAsync(new PanchangTithiParams(Fixtures.Mumbai) { Date = "2027-01-15", Lang = Lang.Gu });
            Assert.Equal("સપ્તમી", res.GetProperty("tithi").GetProperty("name").GetString());
        }

        /// <summary>A user's own DTO: snake_case JSON names map to PascalCase properties.</summary>
        public sealed class Tithi
        {
            public int Number { get; set; }

            public string? Name { get; set; }

            public string? SunriseLocal { get; set; }
        }

        [Fact]
        public async Task MapsTheResponseToYourOwnType()
        {
            _stub.Respond(Reply.Ok("{\"number\":7,\"name\":\"Saptami\",\"paksha\":\"Shukla\",\"sunrise_local\":\"06:42\"}"));
            using LifeAstroClient client = Fixtures.Client(_stub);
            Tithi? t = await client.Panchang.TithiAsync<Tithi>(TithiAt(19.076, 72.8777, "Asia/Kolkata"));
            Assert.NotNull(t);
            Assert.Equal(7, t!.Number);
            Assert.Equal("Saptami", t.Name);
            Assert.Equal("06:42", t.SunriseLocal);

            Dictionary<string, JsonElement>? asMap = await client.Panchang.TithiAsync<Dictionary<string, JsonElement>>(TithiAt(19.076, 72.8777, "Asia/Kolkata"));
            Assert.Equal("Shukla", asMap!["paksha"].GetString());

            JsonNode? node = await client.Panchang.TithiAsync<JsonNode>(TithiAt(19.076, 72.8777, "Asia/Kolkata"));
            Assert.Equal(7, (int)node!["number"]!);
        }

        [Fact]
        public async Task YourOwnSerializerOptionsAreUsed()
        {
            _stub.Respond(Reply.Ok("{\"number\":7,\"Name\":\"Saptami\"}"));
            var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.JsonSerializerOptions = json);
            Tithi? t = await client.Panchang.TithiAsync<Tithi>(TithiAt(1, 2, "UTC"));
            Assert.Equal(0, t!.Number); // "number" does not match "Number" without case-insensitive matching
            Assert.Equal("Saptami", t.Name);
        }

        [Fact]
        public async Task AResponseThatDoesNotMapIsAnInvalidResponseError()
        {
            _stub.Respond(Reply.Ok("{\"number\":\"not a number\"}").With("X-Request-ID", "req_map"));
            using LifeAstroClient client = Fixtures.Client(_stub);
            LifeAstroException e = await Assert.ThrowsAsync<LifeAstroException>(() => client.Panchang.TithiAsync<Tithi>(TithiAt(1, 2, "UTC")));
            Assert.Equal("invalid_response", e.Code);
            Assert.Equal(200, e.Status);
            Assert.Equal("req_map", e.RequestId);
            Assert.IsType<JsonException>(e.InnerException);
        }

        [Fact]
        public async Task ASharedClientServesConcurrentCalls()
        {
            _stub.Respond(r => Reply.Ok("{\"lat\":\"" + r.Query["lat"] + "\"}"));
            using LifeAstroClient client = Fixtures.Client(_stub);
            var tasks = new List<Task<JsonElement>>();
            for (int i = 0; i < 50; i++)
            {
                tasks.Add(client.Panchang.TithiAsync(TithiAt(i, 2, "UTC")));
            }

            JsonElement[] all = await Task.WhenAll(tasks);
            for (int i = 0; i < 50; i++)
            {
                Assert.Equal(i.ToString(System.Globalization.CultureInfo.InvariantCulture), all[i].GetProperty("lat").GetString());
            }
        }
    }
}
