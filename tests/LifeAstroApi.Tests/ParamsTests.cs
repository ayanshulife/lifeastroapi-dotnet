using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LifeAstroApi.Params;
using Xunit;
using static LifeAstroApi.Tests.Fixtures;

namespace LifeAstroApi.Tests
{
    /// <summary>
    /// The "param shapes", "endpoints corrected against the live API", "natal chart + transit moment",
    /// "data endpoints", "params the manifest lacked" and "optional calculation settings" cases of the
    /// Node SDK's client.test.ts (as ported to Java's ParamsTest).
    /// </summary>
    public class ParamsTests : IDisposable
    {
        private readonly StubHandler _stub = new StubHandler();
        private readonly LifeAstroClient _client;

        public ParamsTests()
        {
            _client = Client(_stub);
        }

        public void Dispose() => _client.Dispose();

        private async Task<Recorded> Call(Func<LifeAstroClient, Task> call)
        {
            _stub.Reset();
            await call(_client);
            return _stub.Last;
        }

        private async Task<Dictionary<string, string>> Query(Func<LifeAstroClient, Task> call) => (await Call(call)).Query;

        [Fact]
        public async Task FlatBirthParams()
        {
            Dictionary<string, string> q = await Query(c => c.Chart.PlanetsAsync(new ChartPlanetsParams(Arjun)));
            Assert.Equal("1990-01-15", q["date"]);
            Assert.Equal("10:30", q["time"]);
            Assert.Equal("Asia/Kolkata", q["tz"]);
            Assert.Equal("19.076", q["lat"]);
            Assert.Equal("72.8777", q["lon"]);
            Assert.False(q.ContainsKey("birth.date"));
        }

        [Fact]
        public void FlatValuesAndTheBirthConvenienceSendTheSame()
        {
            var a = new ChartPlanetsParams(Arjun);
            var b = new ChartPlanetsParams(19.076, 72.8777, new DateTime(1990, 1, 15), new TimeSpan(10, 30, 0), "Asia/Kolkata");
            Assert.Equal(a.ToQuery(), b.ToQuery());
            Assert.Equal(a.ToString(), b.ToString());
            Assert.Equal("/v1/chart/planets?date=1990-01-15&lat=19.076&lon=72.8777&time=10%3A30&tz=Asia%2FKolkata", a.ToString());
        }

        [Fact]
        public async Task BirthPrefixedParams()
        {
            Recorded r = await Call(c => c.Milan.MangalDoshaAsync(new MilanMangalDoshaParams(Arjun)));
            Assert.Equal("/v1/milan/mangal-dosha", r.Path);
            Assert.Equal("1990-01-15", r.Query["birth.date"]);
            Assert.Equal("19.076", r.Query["birth.lat"]);
            Assert.False(r.Query.ContainsKey("date"));
        }

        [Fact]
        public async Task BoyGirlMatchmakingParams()
        {
            Dictionary<string, string> q = await Query(c => c.Milan.AshtakootaTotalAsync(
                new MilanAshtakootaTotalParams(Arjun, new BirthInput("1992-03-20", "10:30", "Asia/Kolkata", 19.076, 72.8777))));
            Assert.Equal("1990-01-15", q["boy.date"]);
            Assert.Equal("1992-03-20", q["girl.date"]);
            Assert.Equal("72.8777", q["girl.lon"]);
        }

        [Fact]
        public async Task LowercasesRashi()
        {
            Assert.Equal("aries", (await Query(c => c.Horoscope.DailyAsync(new HoroscopeDailyParams(" Aries "))))["rashi"]);
        }

        [Fact]
        public async Task SubstitutesPathParams()
        {
            Recorded r = await Call(c => c.Chart.DivisionalAsync(new ChartDivisionalParams("D9", Arjun)));
            Assert.Equal("/v1/chart/divisional/D9", r.Path);
            Assert.False(r.Query.ContainsKey("varga"));
        }

        [Fact]
        public async Task NumericPathParamsTakeAnInt()
        {
            Recorded r = await Call(c => c.Western.Natal.HarmonicAsync(new WesternNatalHarmonicParams(5, Arjun)));
            Assert.Equal("/v1/western/natal/harmonic/5", r.Path);
            Assert.Equal(new WesternNatalAsteroidParams("433", Arjun).ToString(), new WesternNatalAsteroidParams(433, Arjun).ToString());
        }

        [Fact]
        public async Task CustomShapeFestivalsMonth()
        {
            Dictionary<string, string> q = await Query(c => c.Festivals.MonthAsync(
                new FestivalsMonthParams(2026, 11) { Tz = "Asia/Kolkata", Lat = 19.076, Lon = 72.8777, Locale = Lang.Mr, MonthSystem = "amanta" }));
            Assert.Equal("2026", q["year"]);
            Assert.Equal("19.076", q["lat"]);
            Assert.Equal("amanta", q["month_system"]);
            Assert.Equal("mr", q["locale"]);
        }

        [Fact]
        public void PlaceConvenience()
        {
            var p = new PanchangBasicParams(Mumbai) { Date = "2027-01-15" };
            Assert.Equal(19.076, p.Lat);
            Assert.Equal("Asia/Kolkata", p.Tz);
            var f = new FestivalsMonthParams(2027, 1, Mumbai);
            Assert.Equal(72.8777, f.Lon);
            Assert.Equal("Asia/Kolkata", f.Tz);
        }

        [Fact]
        public async Task NatalTransitShapeMapsNatalToBirthAndTransitFlat()
        {
            Dictionary<string, string> q = await Query(c => c.Western.Transits.ToNatalAsync(new WesternTransitsToNatalParams(Arjun, "2026-06-16", "12:00", "Asia/Kolkata")));
            Assert.Equal("1990-01-15", q["birth.date"]);
            Assert.Equal("2026-06-16", q["date"]);
        }

        [Fact]
        public void MissingRequiredValuesFailInTheConstructor()
        {
            ArgumentException e = Assert.Throws<ArgumentException>(() => new ChartDivisionalParams("", Arjun));
            Assert.Equal("varga", e.ParamName);
            Assert.Equal("tz", Assert.Throws<ArgumentNullException>(() => new PanchangBasicParams(1, 2, null!)).ParamName);
            Assert.Equal("tz", Assert.Throws<ArgumentException>(() => new PanchangBasicParams(1, 2, " ")).ParamName);
            Assert.Equal("place", Assert.Throws<ArgumentNullException>(() => new PanchangBasicParams((Place)null!)).ParamName);
            e = Assert.Throws<ArgumentException>(() => new MilanAshtakootaTotalParams(Arjun, BirthInput.WithoutPlace("1992-03-20", "08:15", "Asia/Kolkata")));
            Assert.Equal("girl", e.ParamName);
            Assert.Equal("birth", Assert.Throws<ArgumentException>(() => new ChartPlanetsParams(BirthInput.WithoutPlace("1990-01-15", "10:30", "Asia/Kolkata"))).ParamName);
            Assert.Equal("date", Assert.Throws<ArgumentException>(() => new CalendarRituParams("", "UTC")).ParamName);
            Assert.Throws<ArgumentException>(() => new BirthInput("", "10:30", "UTC", 1, 2));
            Assert.Throws<ArgumentException>(() => new BirthInput("1990-01-15", "10:30", "UTC", double.NaN, 2));
            Assert.Throws<ArgumentException>(() => new Place(1, double.PositiveInfinity, "UTC"));
        }

        [Fact]
        public async Task TheWindowMethodsTakeABirthWithoutPlace()
        {
            Dictionary<string, string> q = await Query(c => c.Transit.SadeSatiWindowsAsync(new TransitSadeSatiWindowsParams(BirthInput.WithoutPlace("1990-01-15", "10:30", "Asia/Kolkata"))
            {
                From = "2027-01-15",
                Tz = "UTC",
                Years = 30,
                IncludePast = true,
            }));
            Assert.Equal("1990-01-15", q["birth.date"]);
            Assert.Equal("10:30", q["birth.time"]);
            Assert.Equal("Asia/Kolkata", q["birth.tz"]);
            Assert.False(q.ContainsKey("birth.lat"));
            Assert.False(q.ContainsKey("date"));
            Assert.Equal("2027-01-15", q["from"]);
            Assert.Equal("UTC", q["tz"]);
            Assert.Equal("30", q["years"]);
            Assert.Equal("true", q["include_past"]);
        }

        [Fact]
        public async Task TransitWindowsPlanetRequiredBirthOptional()
        {
            Recorded r = await Call(c => c.Transit.WindowsAsync(new TransitWindowsParams("jupiter") { From = "2027-01-15", Years = 12 }));
            Assert.Equal("/v1/transit/windows", r.Path);
            Assert.Equal("jupiter", r.Query["planet"]);
            Assert.Equal("2027-01-15", r.Query["from"]);
            Assert.Equal("12", r.Query["years"]);
            Assert.DoesNotContain(r.Query.Keys, k => k.StartsWith("birth.", StringComparison.Ordinal));
            Dictionary<string, string> q = await Query(c => c.Transit.WindowsAsync(new TransitWindowsParams("saturn") { Birth = Arjun }));
            Assert.Equal("1990-01-15", q["birth.date"]);
            Assert.Equal("19.076", q["birth.lat"]);
            Assert.Equal("72.8777", q["birth.lon"]);
            Assert.False(q.ContainsKey("lat"));
        }

        [Fact]
        public async Task PersonalisedHoroscopeSendsTheBirthChartUnprefixedAndTheDayAsTransitDate()
        {
            Dictionary<string, string> q = await Query(c => c.Narrative.HoroscopeDailyByLagnaAsync(new NarrativeHoroscopeDailyByLagnaParams(Arjun) { OnDate = "2026-10-10" }));
            Assert.Equal("1990-01-15", q["date"]);
            Assert.Equal("2026-10-10", q["transit_date"]);
            Assert.False(q.ContainsKey("birth.date"));
        }

        [Fact]
        public async Task ReportsAcceptTheNameAndPlaceForTheCoverPage()
        {
            Dictionary<string, string> one = await Query(c => c.Reports.KundliLiteAsync(new ReportsKundliLiteParams(Arjun) { Name = "Arjun Mehta", Place = "Mumbai" }));
            Assert.Equal("Arjun Mehta", one["name"]);
            Assert.Equal("Mumbai", one["place"]);
            Dictionary<string, string> q = await Query(c => c.Reports.MatchMakingAsync(new ReportsMatchMakingParams(Arjun, Arjun) { BoyName = "A", GirlName = "B", BoyPlace = "Pune" }));
            Assert.Equal("A", q["boy.name"]);
            Assert.Equal("B", q["girl.name"]);
            Assert.Equal("Pune", q["boy.place"]);
        }

        [Fact]
        public async Task MonthlyHoroscopeReportTakesMonthAsYearMonth()
        {
            Dictionary<string, string> q = await Query(c => c.Reports.HoroscopeMonthlyAsync(new ReportsHoroscopeMonthlyParams("Aries", "2026-11")));
            Assert.Equal("2026-11", q["month"]);
            Assert.False(q.ContainsKey("year"));
        }

        [Fact]
        public async Task ExposesTheMetaCatalogsAndNestedWesternGroups()
        {
            _stub.Reset();
            await _client.Meta.EndpointsAsync();
            await _client.Meta.VedicYogasAsync();
            Assert.Equal(new[] { "/v1/meta/endpoints", "/v1/meta/vedic/yogas" }, _stub.Calls.Select(x => x.Path).ToArray());
            Assert.NotNull(_client.Western.Natal);
            Assert.NotNull(_client.Western.Synastry);
            Assert.Equal("/v1/western/firdaria", (await Call(c => c.Western.FirdariaAsync(new WesternFirdariaParams("1990-01-15"))) ).Path);
        }

        public static IEnumerable<object[]> NatalTransitCases()
        {
            const string D = "2027-01-15";
            const string T = "06:00";
            const string Tz = "Asia/Kolkata";
            yield return Case("/v1/vedic/narrative/transit-phal", c => c.Narrative.TransitPhalAsync(new NarrativeTransitPhalParams(Arjun, D, T, Tz)), true);
            yield return Case("/v1/vedic/narrative/dasha-phal", c => c.Narrative.DashaPhalAsync(new NarrativeDashaPhalParams(Arjun, D, T, Tz)), true);
            yield return Case("/v1/vedic/narrative/transit-double", c => c.Narrative.TransitDoubleAsync(new NarrativeTransitDoubleParams(Arjun, D, T, Tz)), false);
            yield return Case("/v1/vedic/narrative/sade-sati-status", c => c.Narrative.SadeSatiStatusAsync(new NarrativeSadeSatiStatusParams(Arjun, D, T, Tz)), false);
            yield return Case("/v1/vedic/narrative/sade-sati-phase", c => c.Narrative.SadeSatiPhaseAsync(new NarrativeSadeSatiPhaseParams(Arjun, D, T, Tz)), false);
            yield return Case("/v1/vedic/narrative/sade-sati-history", c => c.Narrative.SadeSatiHistoryAsync(new NarrativeSadeSatiHistoryParams(Arjun, D, T, Tz)), false);
            yield return Case("/v1/ashtakavarga/kaksha", c => c.Ashtakavarga.KakshaAsync(new AshtakavargaKakshaParams(Arjun, D, T, Tz)), false);
            yield return Case("/v1/ashtakavarga/transit-score", c => c.Ashtakavarga.TransitScoreAsync(new AshtakavargaTransitScoreParams(Arjun, D, T, Tz)), false);
            yield return Case("/v1/transit/small-panoti", c => c.Transit.SmallPanotiAsync(new TransitSmallPanotiParams(Arjun, D, T, Tz)), false);

            static object[] Case(string path, Func<LifeAstroClient, Task> call, bool place) => new object[] { path, call, place };
        }

        [Theory]
        [MemberData(nameof(NatalTransitCases))]
        public async Task NatalChartAndTransitMomentAreSeparate(string path, Func<LifeAstroClient, Task> call, bool place)
        {
            Recorded r = await Call(call);
            Assert.Equal(path, r.Path);
            Dictionary<string, string> q = r.Query;
            Assert.Equal("1990-01-15", q["birth.date"]);
            Assert.Equal("10:30", q["birth.time"]);
            Assert.Equal("19.076", q["birth.lat"]);
            Assert.Equal("2027-01-15", q["date"]);
            Assert.Equal("Asia/Kolkata", q["tz"]);
            // Endpoints that cast the natal lagna read the birth place unprefixed.
            Assert.Equal(place ? "19.076" : null, q.TryGetValue("lat", out string? lat) ? lat : null);
        }

        [Fact]
        public async Task NarrativePrashnaSendsTheQuestionMomentTopicAndHouse()
        {
            Recorded r = await Call(c => c.Narrative.PrashnaAsync(new NarrativePrashnaParams(19.076, 72.8777, "Asia/Kolkata")
            {
                Date = "2027-01-15",
                Time = "10:00",
                Topic = "career",
                QuestionHouse = 10,
                Lang = Lang.Hi,
            }));
            Assert.Equal("/v1/vedic/narrative/prashna", r.Path);
            Assert.Equal("2027-01-15", r.Query["date"]);
            Assert.Equal("10:00", r.Query["time"]);
            Assert.Equal("career", r.Query["topic"]);
            Assert.Equal("10", r.Query["question_house"]);
            Assert.Equal("hi", r.Query["locale"]);
        }

        [Fact]
        public async Task ChartDataEndpointsReadTheBirthChartUnprefixed()
        {
            var cases = new (string Path, Func<LifeAstroClient, Task> Call, Dictionary<string, string> Extra)[]
            {
                ("/v1/chart/kundli-facts", c => c.Chart.KundliFactsAsync(new ChartKundliFactsParams(Arjun) { Vargas = "D9,D10", Include = "all", Gender = "male" }),
                    new Dictionary<string, string> { ["vargas"] = "D9,D10", ["include"] = "all", ["gender"] = "male" }),
                ("/v1/chart/yogas", c => c.Chart.YogasAsync(new ChartYogasParams(Arjun) { All = true }), new Dictionary<string, string> { ["all"] = "true" }),
                ("/v1/chart/doshas", c => c.Chart.DoshasAsync(new ChartDoshasParams(Arjun) { Gender = "female" }), new Dictionary<string, string> { ["gender"] = "female" }),
                ("/v1/chart/sensitivity", c => c.Chart.SensitivityAsync(new ChartSensitivityParams(Arjun) { Window = 60, Vargas = "D9", TopocentricMoon = true }),
                    new Dictionary<string, string> { ["window"] = "60", ["vargas"] = "D9", ["topocentric_moon"] = "true" }),
            };
            foreach (var k in cases)
            {
                Recorded r = await Call(k.Call);
                Assert.Equal(k.Path, r.Path);
                Assert.Equal("1990-01-15", r.Query["date"]);
                Assert.Equal("10:30", r.Query["time"]);
                Assert.Equal("19.076", r.Query["lat"]);
                Assert.False(r.Query.ContainsKey("birth.date"));
                foreach (KeyValuePair<string, string> e in k.Extra)
                {
                    Assert.Equal(e.Value, r.Query[e.Key]);
                }
            }
        }

        [Fact]
        public async Task RemediesForGrahaMapsGrahaReasonIncludeCategoryAndLanguage()
        {
            Recorded r = await Call(c => c.Remedies.ForGrahaAsync(new RemediesForGrahaParams
            {
                Graha = "saturn",
                Reason = "dosha:sade_sati",
                Include = "pooja,yantra",
                Category = "mantra",
                Lang = Lang.Hi,
            }));
            Assert.Equal("/v1/vedic/remedies/for-graha", r.Path);
            Assert.Equal("saturn", r.Query["graha"]);
            Assert.Equal("dosha:sade_sati", r.Query["reason"]);
            Assert.Equal("pooja,yantra", r.Query["include"]);
            Assert.Equal("mantra", r.Query["category"]);
            Assert.Equal("hi", r.Query["locale"]);
            Assert.Equal("/v1/vedic/remedies/for-graha", (await Call(c => c.Remedies.ForGrahaAsync())).PathAndQuery);
        }

        [Fact]
        public async Task MilanOverlaySummaryAndDashaSyncSendBoyAndGirl()
        {
            Recorded r = await Call(c => c.Milan.OverlayAsync(new MilanOverlayParams(Arjun, Girl)));
            Assert.Equal("/v1/milan/overlay", r.Path);
            Assert.Equal("1990-01-15", r.Query["boy.date"]);
            Assert.Equal("1992-03-20", r.Query["girl.date"]);
            Assert.Equal("28.6139", r.Query["girl.lat"]);
            r = await Call(c => c.Milan.SummaryAsync(new MilanSummaryParams(Arjun, Girl) { From = "2027-01-15", Tz = "Asia/Kolkata", Include = "kundli_facts" }));
            Assert.Equal("/v1/milan/summary", r.Path);
            Assert.Equal("10:30", r.Query["boy.time"]);
            Assert.Equal("08:15", r.Query["girl.time"]);
            Assert.Equal("kundli_facts", r.Query["include"]);
            r = await Call(c => c.Milan.DashaSyncAsync(new MilanDashaSyncParams(Arjun, Girl)
            {
                Timeline = true,
                Years = 5,
                From = "2027-01-15",
                MarriageWindows = true,
                Date = "2027-02-01",
                Tz = "Asia/Kolkata",
            }));
            Assert.Equal("/v1/milan/dasha-sync", r.Path);
            Assert.Equal("19.076", r.Query["boy.lat"]);
            Assert.Equal("77.209", r.Query["girl.lon"]);
            Assert.Equal("true", r.Query["timeline"]);
            Assert.Equal("5", r.Query["years"]);
            Assert.Equal("true", r.Query["marriage_windows"]);
            Assert.Equal("2027-02-01", r.Query["date"]);
        }

        [Fact]
        public async Task NarrativeMilanBoyAndGirlConveniences()
        {
            Dictionary<string, string> q = await Query(c => c.Narrative.MilanAsync(new NarrativeMilanParams(Arjun, Girl)));
            Assert.Equal("1990-01-15", q["boy.date"]);
            Assert.Equal("08:15", q["girl.time"]);
            Assert.False(q.ContainsKey("boy.lat"));
        }

        [Fact]
        public async Task ParamsTheManifestLackedAreSent()
        {
            Dictionary<string, string> q = await Query(c => c.Muhurta.BestTimeAsync(new MuhurtaBestTimeParams(Mumbai, "2027-01-15", "2027-01-31")
            {
                Purpose = "vivah",
                TopN = 3,
                BirthDate = "1990-01-15",
            }));
            Assert.Equal("vivah", q["purpose"]);
            Assert.Equal("3", q["top_n"]);
            Assert.Equal("1990-01-15", q["birth_date"]);
            Assert.Equal("2027-01-15", q["start_date"]);
            Recorded r = await Call(c => c.Dasha.CharCurrentAsync(new DashaCharCurrentParams(Arjun) { At = "2027-01-15T06:00:00Z" }));
            Assert.Equal("/v1/dasha/char/current", r.Path);
            Assert.Equal("2027-01-15T06:00:00Z", r.Query["at"]);
            r = await Call(c => c.Lalkitab.DashaCurrentAsync(new LalkitabDashaCurrentParams(Arjun) { At = "2027-01-15" }));
            Assert.Equal("/v1/lalkitab/dasha/current", r.Path);
            r = await Call(c => c.Reports.HoroscopeWeeklyAsync(new ReportsHoroscopeWeeklyParams("Aries") { Week = "2026-W47" }));
            Assert.Equal("2026-W47", r.Query["week"]);
            Assert.Equal("aries", r.Query["rashi"]);
        }

        [Fact]
        public async Task OptionalCalculationSettings()
        {
            Dictionary<string, string> q = await Query(c => c.Western.Natal.ChartAsync(new WesternNatalChartParams(Arjun) { OrbTrine = 6, OrbSextile = 4, Rulership = "traditional" }));
            Assert.Equal("6", q["orb_trine"]);
            Assert.Equal("4", q["orb_sextile"]);
            Assert.Equal("traditional", q["rulership"]);
            q = await Query(c => c.Western.Synastry.AspectsAsync(new WesternSynastryAspectsParams(Arjun, Girl) { HouseSystem = HouseSystem.Koch, OrbConjunction = 8 }));
            Assert.Equal("koch", q["house_system"]);
            Assert.Equal("8", q["orb_conjunction"]);
            Assert.Equal("1990-01-15", q["personA.date"]);
            q = await Query(c => c.Western.Transits.ToNatalAsync(new WesternTransitsToNatalParams(Arjun, "2027-01-15", "06:00", "UTC") { OrbSquare = 5 }));
            Assert.Equal("5", q["orb_square"]);
            q = await Query(c => c.Western.Composite.ChartAsync(new WesternCompositeChartParams(Arjun, Girl) { CompositeMethod = "midpoint_cusps" }));
            Assert.Equal("midpoint_cusps", q["composite_method"]);
            q = await Query(c => c.Western.Returns.SolarAsync(new WesternReturnsSolarParams(Arjun, 2027) { RelocateLat = 51.5, RelocateLon = -0.12 }));
            Assert.Equal("51.5", q["relocate_lat"]);
            Assert.Equal("-0.12", q["relocate_lon"]);
            q = await Query(c => c.Calendar.RituAsync(new CalendarRituParams("2027-01-15", "Asia/Kolkata") { Ayanamsa = Ayanamsa.Raman }));
            Assert.Equal("raman", q["ayanamsa"]);
            q = await Query(c => c.PlanetMoments.IngressAsync(new PlanetMomentsIngressParams("Jupiter", "2027-01-15", "Asia/Kolkata") { Ayanamsa = Ayanamsa.Krishnamurti }));
            Assert.Equal("krishnamurti", q["ayanamsa"]);
            q = await Query(c => c.Chart.PlanetsAsync(new ChartPlanetsParams(Arjun) { Ayanamsa = Ayanamsa.TrueChitrapaksha, HouseSystem = HouseSystem.Placidus }));
            Assert.Equal("true_chitrapaksha", q["ayanamsa"]);
            Assert.Equal("placidus", q["house_system"]);
            q = await Query(c => c.Chart.HousesAsync(new ChartHousesParams(Arjun) { HouseSystem = HouseSystem.WholeSign }));
            Assert.Equal("whole_sign", q["house_system"]);
        }

        [Fact]
        public async Task DateDefaultsToTodayForMomentEndpoints()
        {
            string d = (await Query(c => c.Panchang.TithiAsync(new PanchangTithiParams(Mumbai))))["date"];
            Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}$"), d);
            Assert.Equal(Wire.TodayIn("Asia/Kolkata").Value, d);
            Assert.Equal("2026-11-20", (await Query(c => c.Panchang.TithiAsync(new PanchangTithiParams(Mumbai) { Date = "2026-11-20" })))["date"]);
        }

        [Fact]
        public async Task TransitPositionsNeedsNoArguments()
        {
            Dictionary<string, string> q = await Query(c => c.Transit.PositionsAsync());
            Assert.Equal("UTC", q["tz"]);
            Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}$"), q["date"]);
            Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), q["date"]);
        }

        [Fact]
        public void DateAndTimeValuesFormatLikeTheApiExpects()
        {
            var p = new PanchangTithiParams(1, 2, "+05:30")
            {
                Date = new DateTime(2027, 1, 5, 23, 59, 0, DateTimeKind.Utc),
                Time = new TimeSpan(6, 5, 9),
            };
            Assert.Equal("2027-01-05", p.ToQuery()["date"]);
            Assert.Equal("06:05:09", p.ToQuery()["time"]);
            p.Date = new DateTimeOffset(2027, 1, 5, 0, 30, 0, TimeSpan.FromHours(5.5));
            p.Time = new DateTime(2027, 1, 5, 6, 5, 0);
            Assert.Equal("2027-01-05", p.ToQuery()["date"]);
            Assert.Equal("06:05", p.ToQuery()["time"]);
#if !LIFEASTRO_NETSTANDARD
            p.Date = new DateOnly(2027, 2, 3);
            p.Time = new TimeOnly(18, 45);
            Assert.Equal("2027-02-03", p.ToQuery()["date"]);
            Assert.Equal("18:45", p.ToQuery()["time"]);
            Assert.Equal("10:30", new BirthInput(new DateOnly(1990, 1, 15), new TimeOnly(10, 30), "Asia/Kolkata", 19.076, 72.8777).Time);
#endif
            p.Date = null;
            Assert.Equal(Wire.TodayIn("+05:30").Value, p.ToQuery()["date"]);
            Assert.Throws<ArgumentOutOfRangeException>(() => (LifeAstroTime)TimeSpan.FromHours(25));
        }

        [Fact]
        public void ParamsShowWhatTheySend()
        {
            var a = new PanchangBasicParams(Mumbai) { Date = "2027-01-15", Lang = Lang.Gu };
            Assert.Equal("/v1/panchang/basic", a.ToPath());
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["date"] = "2027-01-15",
                    ["lang"] = "gu",
                    ["lat"] = "19.076",
                    ["locale"] = "gu",
                    ["lon"] = "72.8777",
                    ["tz"] = "Asia/Kolkata",
                },
                a.ToQuery());
            Assert.Equal(new[] { "date", "lang", "lat", "locale", "lon", "tz" }, a.ToQuery().Keys.ToArray());
            Assert.Equal("/v1/panchang/basic?date=2027-01-15&lang=gu&lat=19.076&locale=gu&lon=72.8777&tz=Asia%2FKolkata", a.ToString());
        }

        [Fact]
        public void ValueTypesHaveValueEquality()
        {
            Assert.Equal(new BirthInput("1990-01-15", "10:30", "Asia/Kolkata", 19.076, 72.8777), Arjun);
            Assert.Equal(Arjun.GetHashCode(), new BirthInput("1990-01-15", "10:30", "Asia/Kolkata", 19.076, 72.8777).GetHashCode());
            Assert.NotEqual(Girl, Arjun);
            Assert.Equal(new Place(19.076, 72.8777, "Asia/Kolkata"), Mumbai);
            Assert.Equal(Mumbai, Arjun.Place);
            Assert.Equal(new BirthInput("1990-01-15", "10:30", Mumbai), Arjun);
            Assert.Null(BirthInput.WithoutPlace("1990-01-15", "10:30", "UTC").Place);
            Assert.Equal((LifeAstroDate)"2027-01-15", (LifeAstroDate)new DateTime(2027, 1, 15));
#if !LIFEASTRO_NETSTANDARD
            Assert.Equal("2027-01-15", ((LifeAstroDate)new DateOnly(2027, 1, 15)).ToString());
#endif
        }
    }
}
