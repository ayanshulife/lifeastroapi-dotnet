using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeAstroApi.Params;
using Xunit;
using static LifeAstroApi.Tests.Fixtures;

namespace LifeAstroApi.Tests
{
    /// <summary>The "localization" cases of the Node SDK's client.test.ts.</summary>
    public class LanguageTests : IDisposable
    {
        private const string D = "2027-01-15";
        private readonly StubHandler _stub = new StubHandler();
        private readonly LifeAstroClient _client;

        public LanguageTests()
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

        [Fact]
        public async Task SendsBothLangAndLocaleForNarrativeEndpoints()
        {
            Dictionary<string, string> q = (await Call(c => c.Narrative.LagnaAsync(new NarrativeLagnaParams(Arjun) { Lang = Lang.Hi }))).Query;
            Assert.Equal("hi", q["lang"]);
            Assert.Equal("hi", q["locale"]);
        }

        /// <summary>One method per Vedic group; <c>both</c> is false where the shape's own builder sends <c>lang</c> only.</summary>
        public static IEnumerable<object[]> VedicGroups()
        {
            Lang gu = Lang.Gu;
            yield return Case("panchang.basic", "/v1/panchang/basic", c => c.Panchang.BasicAsync(new PanchangBasicParams(Mumbai) { Date = D, Lang = gu }), true);
            yield return Case("panchang.panchaka", "/v1/panchang/panchaka", c => c.Panchang.PanchakaAsync(new PanchangPanchakaParams(Mumbai) { Date = D, Lang = gu }), true);
            yield return Case("calendar.ritu", "/v1/calendar/ritu", c => c.Calendar.RituAsync(new CalendarRituParams(D, "Asia/Kolkata") { Lang = gu }), true);
            yield return Case("festivals.month", "/v1/festivals/month", c => c.Festivals.MonthAsync(new FestivalsMonthParams(2027, 1) { Lang = gu }), true);
            yield return Case("eclipses.all", "/v1/eclipses/all", c => c.Eclipses.AllAsync(new EclipsesAllParams("2027-01-01", "2027-12-31") { Lang = gu }), true);
            yield return Case("planetMoments.ingress", "/v1/planet-moments/ingress", c => c.PlanetMoments.IngressAsync(new PlanetMomentsIngressParams("Jupiter", D, "Asia/Kolkata") { Lang = gu }), true);
            yield return Case("transit.positions", "/v1/transit/positions", c => c.Transit.PositionsAsync(new TransitPositionsParams { Lang = gu }), true);
            yield return Case("transit.tarabala", "/v1/transit/tarabala", c => c.Transit.TarabalaAsync(new TransitTarabalaParams(Arjun, D, "06:00", "Asia/Kolkata") { Lang = gu }), true);
            yield return Case("milan.ashtakoota", "/v1/milan/ashtakoota", c => c.Milan.AshtakootaAsync(new MilanAshtakootaParams(Arjun, Girl) { Lang = gu }), true);
            yield return Case("muhurta.vivah", "/v1/muhurta/vivah", c => c.Muhurta.VivahAsync(new MuhurtaVivahParams(Mumbai) { Date = D, Lang = gu }), true);
            yield return Case("muhurta.bestTime", "/v1/muhurta/best-time", c => c.Muhurta.BestTimeAsync(new MuhurtaBestTimeParams(Mumbai, D, "2027-01-17") { Lang = gu }), true);
            yield return Case("chart.planets", "/v1/chart/planets", c => c.Chart.PlanetsAsync(new ChartPlanetsParams(Arjun) { Lang = gu }), true);
            yield return Case("chart.kundliFacts", "/v1/chart/kundli-facts", c => c.Chart.KundliFactsAsync(new ChartKundliFactsParams(Arjun) { Lang = gu }), true);
            yield return Case("prashna.chart", "/v1/prashna/chart", c => c.Prashna.ChartAsync(new PrashnaChartParams(Mumbai) { Date = D, Time = "12:00", Lang = gu }), true);
            yield return Case("numerology.driver", "/v1/numerology/driver", c => c.Numerology.DriverAsync(new NumerologyDriverParams("1990-01-15") { Lang = gu }), true);
            yield return Case("numerology.full", "/v1/numerology/full", c => c.Numerology.FullAsync(new NumerologyFullParams { Name = "Arjun Mehta", Dob = "1990-01-15", Lang = gu }), false);
            yield return Case("dasha.vimshottari", "/v1/dasha/vimshottari", c => c.Dasha.VimshottariAsync(new DashaVimshottariParams(Arjun) { Lang = gu }), true);
            yield return Case("varshaphal.chart", "/v1/varshaphal/chart", c => c.Varshaphal.ChartAsync(new VarshaphalChartParams(Arjun, 2026) { Lang = gu }), true);
            yield return Case("ashtakavarga.bhinna", "/v1/ashtakavarga/bhinna/Jupiter", c => c.Ashtakavarga.BhinnaAsync(new AshtakavargaBhinnaParams("Jupiter", Arjun) { Lang = gu }), true);
            yield return Case("lalkitab.chart", "/v1/lalkitab/chart", c => c.Lalkitab.ChartAsync(new LalkitabChartParams(Arjun) { Lang = gu }), true);
            yield return Case("horoscope.daily", "/v1/horoscope/daily", c => c.Horoscope.DailyAsync(new HoroscopeDailyParams("aries") { Lang = gu }), false);
            yield return Case("reports.kundliLite", "/v1/reports/kundli/lite", c => c.Reports.KundliLiteAsync(new ReportsKundliLiteParams(Arjun) { Lang = gu }), true);
            yield return Case("narrative.yearlyBhavishyafal", "/v1/vedic/narrative/yearly-bhavishyafal", c => c.Narrative.YearlyBhavishyafalAsync(new NarrativeYearlyBhavishyafalParams(Arjun, 2027) { Lang = gu }), true);
            yield return Case("remedies.forGraha", "/v1/vedic/remedies/for-graha", c => c.Remedies.ForGrahaAsync(new RemediesForGrahaParams { Graha = "saturn", Lang = gu }), true);

            static object[] Case(string name, string path, Func<LifeAstroClient, Task> call, bool both) => new object[] { name, path, call, both };
        }

        [Theory]
        [MemberData(nameof(VedicGroups))]
        public async Task EveryVedicGroupSendsTheLanguage(string name, string path, Func<LifeAstroClient, Task> call, bool both)
        {
            Recorded r = await Call(call);
            Assert.Equal(path, r.Path);
            Assert.Equal("gu", r.Query["lang"]);
            if (both)
            {
                Assert.True(r.Query.TryGetValue("locale", out string? locale), name);
                Assert.Equal("gu", locale);
            }
        }

        [Fact]
        public async Task AcceptsEveryLangValueIncludingPa()
        {
            var sent = new List<string>();
            foreach (Lang lang in Enum.GetValues(typeof(Lang)).Cast<Lang>())
            {
                sent.Add((await Call(c => c.Panchang.TithiAsync(new PanchangTithiParams(Mumbai) { Date = D, Lang = lang }))).Query["locale"]);
            }

            Assert.Equal(new[] { "en", "hi", "mr", "gu", "bn", "te", "ta", "kn", "ml", "pa" }, sent);
        }

        [Fact]
        public async Task SendsNoLanguageParamWhenLangIsOmitted()
        {
            Dictionary<string, string> q = (await Call(c => c.Panchang.BasicAsync(new PanchangBasicParams(Mumbai) { Date = D }))).Query;
            Assert.False(q.ContainsKey("lang"));
            Assert.False(q.ContainsKey("locale"));
        }

        [Fact]
        public async Task KeepsTheLocaleInputWorkingAndLangWinsWhenBothAreGiven()
        {
            Dictionary<string, string> a = (await Call(c => c.Festivals.MonthAsync(new FestivalsMonthParams(2027, 1) { Locale = Lang.Hi }))).Query;
            Assert.Equal("hi", a["locale"]);
            Assert.False(a.ContainsKey("lang"));
            Dictionary<string, string> b = (await Call(c => c.Panchang.MonthlyAsync(new PanchangMonthlyParams(19.076, 72.8777, 2027, 1) { Locale = Lang.Hi, Lang = Lang.Gu }))).Query;
            Assert.Equal("gu", b["locale"]);
            Assert.Equal("gu", b["lang"]);
        }

        public static IEnumerable<object[]> NoLanguage()
        {
            yield return Case("western.natal.planets", typeof(WesternNatalPlanetsParams));
            yield return Case("western.synastry.aspects", typeof(WesternSynastryAspectsParams));
            yield return Case("western.transits.positions", typeof(WesternTransitsPositionsParams));
            yield return Case("tarot.daily", typeof(TarotDailyParams));
            yield return Case("geo.search", typeof(GeoSearchParams));
            yield return Case("panchang.rahuKaal", typeof(PanchangRahuKaalParams));
            yield return Case("chart.yogas", typeof(ChartYogasParams));
            yield return Case("transit.windows", typeof(TransitWindowsParams));

            static object[] Case(string name, Type type) => new object[] { name, type };
        }

        /// <summary>English-only products and Vedic endpoints with nothing to localize have no <c>Lang</c> property (Node: a type error).</summary>
        [Theory]
        [MemberData(nameof(NoLanguage))]
        public void MethodsWithNothingToTranslateHaveNoLang(string name, Type type)
        {
            Assert.Null(type.GetProperty("Lang"));
            Assert.Null(type.GetProperty("Locale"));
            Assert.NotNull(name);
        }

        [Fact]
        public async Task WesternNarrativeKeepsItsOwnLang()
        {
            Assert.Equal("hi", (await Call(c => c.Western.Narrative.SabianAsync(new WesternNarrativeSabianParams(100) { Lang = Lang.Hi }))).Query["lang"]);
        }

        [Fact]
        public void LangCodesRoundTrip()
        {
            foreach (Lang lang in Enum.GetValues(typeof(Lang)).Cast<Lang>())
            {
                Assert.Equal(lang, LangExtensions.FromCode(lang.ToCode()));
                Assert.Equal(lang, LangExtensions.FromCode(" " + lang.ToCode().ToUpperInvariant() + " "));
                Assert.True(lang.IsComplete());
            }

            Assert.Equal("Punjabi", Lang.Pa.EnglishName());
            Assert.Equal("gu", Lang.Gu.ToCode());
            Assert.Throws<ArgumentException>(() => LangExtensions.FromCode("xx"));
            Assert.False(LangExtensions.TryFromCode(null, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => ((Lang)42).ToCode());
            Assert.Equal(Ayanamsa.TrueChitrapaksha, AyanamsaExtensions.FromWireValue("TRUE_CHITRAPAKSHA"));
            Assert.Equal(HouseSystem.WholeSign, HouseSystemExtensions.FromWireValue("whole-sign"));
            Assert.Equal("whole_sign", HouseSystem.WholeSign.ToWireValue());
        }
    }
}
