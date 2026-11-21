using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Params;
using Xunit;

namespace LifeAstroApi.Tests
{
    /// <summary>Wire formatting: JavaScript number text, URLSearchParams escaping, culture invariance, time zones.</summary>
    public class QueryStringTests
    {
        /// <summary>Doubles and the text JavaScript's <c>String(n)</c> gives them (recorded with Node 22).</summary>
        public static IEnumerable<object[]> Numbers() => new[]
        {
            new object[] { 19.076, "19.076" },
            new object[] { 72.8777, "72.8777" },
            new object[] { 0.1 + 0.2, "0.30000000000000004" },
            new object[] { 1e21, "1e+21" },
            new object[] { 1e-7, "1e-7" },
            new object[] { 123456789012345680000.0, "123456789012345680000" },
            new object[] { 5e-324, "5e-324" },
            new object[] { 1.7976931348623157e308, "1.7976931348623157e+308" },
            new object[] { 0.000001, "0.000001" },
            new object[] { -0.5, "-0.5" },
            new object[] { 100.0, "100" },
            new object[] { 1.5e-6, "0.0000015" },
            new object[] { 9007199254740992.0, "9007199254740992" },
            new object[] { 0.1, "0.1" },
            new object[] { 1.0 / 3, "0.3333333333333333" },
            new object[] { 2.0 / 3, "0.6666666666666666" },
            new object[] { 123.456, "123.456" },
            new object[] { -72.8777, "-72.8777" },
            new object[] { 1e20, "100000000000000000000" },
            new object[] { 12345678.9, "12345678.9" },
            new object[] { 4.35, "4.35" },
            new object[] { 0.07, "0.07" },
            new object[] { 9.999999999999999e22, "1e+23" },
            new object[] { 1.2e-7, "1.2e-7" },
            new object[] { 314.15926535897932, "314.1592653589793" },
            new object[] { 0.0, "0" },
            new object[] { -0.0, "0" },
            new object[] { double.NaN, "NaN" },
            new object[] { double.PositiveInfinity, "Infinity" },
        };

        [Theory]
        [MemberData(nameof(Numbers))]
        public void NumbersAsJavaScriptWritesThem(double value, string expected)
        {
            Assert.Equal(expected, QueryString.FormatNumber(value));
            // The netstandard2.0 build (.NET Framework) looks for the fewest digits that round-trip.
            Assert.Equal(expected, QueryString.FormatNumber(value, roundTripFormatIsShortest: false));
        }

        [Fact]
        public void EncodesSortedSkippingEmptyValues()
        {
            var q = new[]
            {
                new KeyValuePair<string, string>("b", "2"),
                new KeyValuePair<string, string>("a", "x y"),
                new KeyValuePair<string, string>("B", "upper first"),
                new KeyValuePair<string, string>("e", ""),
                new KeyValuePair<string, string>("c", "ä~*'()!"),
            };
            Assert.Equal("?B=upper+first&a=x+y&b=2&c=%C3%A4%7E*%27%28%29%21", QueryString.Encode(q));
            Assert.Equal(string.Empty, QueryString.Encode(new KeyValuePair<string, string>[0]));
            Assert.Equal("a%20b%2Fc!~*'()%C3%A4", QueryString.EncodeUriComponent("a b/c!~*'()ä"));
        }

        public static IEnumerable<object[]> Cultures() => new[]
        {
            new object[] { "hi-IN" },
            new object[] { "de-DE" },
            new object[] { "ar-SA" },
            new object[] { "th-TH" },
            new object[] { "fa-IR" },
        };

        /// <summary>
        /// A decimal comma (de-DE), the Hijri or Buddhist calendar (ar-SA, th-TH) or Persian digits never
        /// reach the URL: every value is formatted with the invariant culture.
        /// </summary>
        [Theory]
        [MemberData(nameof(Cultures))]
        public async Task TheCurrentCultureNeverChangesTheWire(string culture)
        {
            var stub = new StubHandler();
            using LifeAstroClient client = Fixtures.Client(stub);
            CultureInfo before = CultureInfo.CurrentCulture;
            CultureInfo beforeUi = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                CultureInfo.CurrentUICulture = new CultureInfo(culture);
                await client.Chart.PlanetsAsync(new ChartPlanetsParams(
                    new BirthInput(new DateTime(1990, 1, 15), new TimeSpan(10, 30, 0), "Asia/Kolkata", 19.076, 72.8777)));
                Assert.Equal("/v1/chart/planets?date=1990-01-15&lat=19.076&lon=72.8777&time=10%3A30&tz=Asia%2FKolkata", stub.Last.PathAndQuery);

                await client.Western.Returns.SolarAsync(new WesternReturnsSolarParams(Fixtures.Arjun, 2027) { RelocateLat = 51.5, RelocateLon = -0.125 });
                Assert.Contains("relocate_lat=51.5&relocate_lon=-0.125&return_year=2027", stub.Last.PathAndQuery);

                await client.Panchang.TithiAsync(new PanchangTithiParams(Fixtures.Mumbai) { Date = new DateTime(2027, 3, 20), Time = new DateTime(2027, 3, 20, 6, 5, 9) });
                Assert.Contains("date=2027-03-20", stub.Last.PathAndQuery);
                Assert.Contains("time=06%3A05%3A09", stub.Last.PathAndQuery);

                await client.Panchang.TithiAsync(new PanchangTithiParams(Fixtures.Mumbai));
                string today = stub.Last.Query["date"];
                Assert.Matches("^20[0-9]{2}-[0-9]{2}-[0-9]{2}$", today);

                await client.RequestAsync("/v1/x", new Dictionary<string, object?> { ["n"] = 1234.5, ["m"] = 1234.5m, ["f"] = 0.25f, ["d"] = new DateTime(2027, 1, 15) });
                Assert.Equal("/v1/x?d=2027-01-15&f=0.25&m=1234.5&n=1234.5", stub.Last.PathAndQuery);

                foreach (char ch in stub.Last.PathAndQuery)
                {
                    Assert.True(ch < 128, "non-ASCII character in the URL: " + ch);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
                CultureInfo.CurrentUICulture = beforeUi;
            }
        }

        [Fact]
        public void TodayInResolvesTheDateInTheZoneAndFallsBackToUtc()
        {
            var now = new DateTimeOffset(2027, 1, 15, 20, 0, 0, TimeSpan.Zero); // 01:30 on the 16th in India
            Assert.Equal("2027-01-16", Wire.TodayIn("Asia/Kolkata", now).Value);
            Assert.Equal("2027-01-16", Wire.TodayIn("+05:30", now).Value);
            Assert.Equal("2027-01-16", Wire.TodayIn("UTC+0530", now).Value);
            Assert.Equal("2027-01-15", Wire.TodayIn("America/New_York", now).Value);
            Assert.Equal("2027-01-15", Wire.TodayIn("-04:00", now).Value);
            Assert.Equal("2027-01-15", Wire.TodayIn("UTC", now).Value);
            Assert.Equal("2027-01-15", Wire.TodayIn("Not/AZone", now).Value);
            Assert.Equal("2027-01-15", Wire.TodayIn(null, now).Value);
            Assert.Equal("2027-01-15", Wire.TodayIn("+25:00", now).Value);
        }

        [Fact]
        public void IanaZonesAreFoundAndTheWindowsTableIsConsistent()
        {
            Assert.NotNull(TimeZones.Find("Asia/Kolkata"));
            Assert.NotNull(TimeZones.Find("Asia/Calcutta"));
            Assert.Equal(TimeSpan.FromHours(5.5), TimeZones.OffsetAt("Asia/Kolkata", DateTimeOffset.UtcNow));
            Assert.Equal(TimeSpan.FromHours(5.75), TimeZones.OffsetAt("Asia/Kathmandu", DateTimeOffset.UtcNow));
            Assert.Null(TimeZones.OffsetAt("", DateTimeOffset.UtcNow));
        }

        [Fact]
        public void TheTestsRunAgainstTheIntendedBuild()
        {
            string tfm = typeof(LifeAstroClient).Assembly.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false)
                .Cast<System.Runtime.Versioning.TargetFrameworkAttribute>().Single().FrameworkName;
#if LIFEASTRO_NETSTANDARD
            Assert.Equal(".NETStandard,Version=v2.0", tfm);
            Assert.False(QueryString.ShortestDigitsIsReliable);
#else
            Assert.Equal(".NETCoreApp,Version=v8.0", tfm);
            Assert.True(QueryString.ShortestDigitsIsReliable);
#endif
        }

        [Fact]
        public void TheDefaultJsonOptionsAreSnakeCaseAndReadOnly()
        {
            Assert.True(LifeAstroJson.DefaultOptions.IsReadOnly);
            Assert.Equal(System.Text.Json.JsonNamingPolicy.SnakeCaseLower, LifeAstroJson.DefaultOptions.PropertyNamingPolicy);
            Assert.False(LifeAstroJson.CreateOptions().IsReadOnly);
        }
    }
}
