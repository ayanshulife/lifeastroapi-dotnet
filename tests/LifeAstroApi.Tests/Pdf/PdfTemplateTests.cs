using System;
using System.Linq;
using System.Text.Json;
using LifeAstroApi.Pdf;
using LifeAstroApi.Pdf.Templates;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>What the templates promise, in words (the parity tests pin the bytes).</summary>
    public class PdfTemplateTests
    {
        private static readonly string[] Languages = { "en", "hi", "mr", "gu", "bn", "te", "ta", "kn", "ml", "pa" };

        [Fact]
        public void TheCustomersHtmlCarriesNoCssComments()
        {
            foreach (string renderer in PdfFixtures.Renderers.Keys)
            {
                foreach (string language in Languages)
                {
                    Assert.DoesNotContain("/*", PdfFixtures.Renderers[renderer](PdfFixtures.Example(renderer, language), null), StringComparison.Ordinal);
                }
            }

            string watermark = PdfReports.RenderKundliLiteHtml("{}", new PdfBranding { WatermarkText = "A/*B*/C" });
            Assert.Contains("content: \"A/*B*/C\"", watermark, StringComparison.Ordinal);
        }

        [Fact]
        public void ALogoUrlStaysAsWrittenOnlyEscaped()
        {
            string html = PdfReports.RenderKundliLiteHtml("{}", new PdfBranding { LogoUrl = "https://cdn.example/a,,b->c—d.png?x=1&y=2" });
            Assert.Contains("src=\"https://cdn.example/a,,b-&gt;c—d.png?x=1&amp;y=2\"", html, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("rgb(1 2 3 / 50%)", "rgb(1 2 3 / 50%)")]
        [InlineData("#9333ea", "#9333ea")]
        [InlineData("red}body{display:none", "#666666")]
        public void TheFooterTakesTheColoursTheOtherColoursTake(string given, string printed)
        {
            string html = PdfReports.RenderKundliLiteHtml("{}", new PdfBranding { SecondaryColor = given });
            Assert.Contains("--brand-secondary: " + printed + ";", html, StringComparison.Ordinal);
            Assert.Contains("    color: " + printed + ";", html, StringComparison.Ordinal);
        }

        [Fact]
        public void LetterSpacingIsOffInEveryIndianLanguage()
        {
            string rule = PdfReports.RenderKundliLiteHtml("{}").Split('\n').Single(line => line.Contains("letter-spacing: normal"));
            Assert.All(Languages.Skip(1), language => Assert.Contains("html:lang(" + language + ")", rule, StringComparison.Ordinal));
        }

        [Fact]
        public void BrihadValuesAreEscaped()
        {
            string html = PdfReports.RenderKundliBrihadHtml(
                "{\"rajyoga_power\":{\"percent\":5,\"planets\":[\"<img src=x>\"]},"
                + "\"kp\":{\"cusps\":[{\"house\":1}],\"planet_significators\":[{\"key\":\"Su\",\"values\":[\"<b>1</b>\"]}]}}");
            Assert.DoesNotContain("<img src=x>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<b>1</b>", html, StringComparison.Ordinal);
        }

        [Fact]
        public void NumerologyPrintsTheNameTheApiSends()
        {
            string html = PdfReports.RenderNumerologyHtml("{\"name\":\"Arjun Mehta\",\"driver\":6}");
            Assert.Contains("<div class=\"subject\">Arjun Mehta</div>", html, StringComparison.Ordinal);
            Assert.Contains("grid-auto-flow: column", html, StringComparison.Ordinal);
        }

        [Fact]
        public void VarshaphalPrintsAReadableSolarReturn()
        {
            string local = PdfReports.RenderVarshaphalHtml(
                "{\"SolarReturnISO\":\"2027-01-15T16:39:17Z\",\"SolarReturnLocal\":\"2027-01-15 22:09 IST\"}");
            Assert.Contains("2027-01-15 22:09 IST", local, StringComparison.Ordinal);
            Assert.DoesNotContain("16:39:17Z", local, StringComparison.Ordinal);
            Assert.Contains("2027-01-15 16:39 UTC", PdfReports.RenderVarshaphalHtml("{\"SolarReturnISO\":\"2027-01-15T16:39:17Z\"}"), StringComparison.Ordinal);
        }

        [Fact]
        public void ALocaleEveryObjectHasIsEnglish()
        {
            string[] names = PdfFixtures.Read("runtime.json").GetProperty("prototypeKeys")
                .EnumerateArray().Select(k => k.GetString()!).ToArray();
            foreach (string renderer in PdfFixtures.Renderers.Keys)
            {
                string key = renderer == "Horoscope" ? "language" : "Locale";
                JsonElement report = PdfFixtures.Example(renderer, "en");
                string english = PdfFixtures.Renderers[renderer](report, null);
                Assert.Contains("<html lang=\"en\">", english, StringComparison.Ordinal);
                foreach (string name in names)
                {
                    Assert.Equal(english, PdfFixtures.Renderers[renderer](PdfFixtures.With(report, key, name), null));
                }
            }

            string heading = PdfReports.TocHeading(Js.Table("Locale", "en"));
            Assert.All(names, name => Assert.Equal(heading, PdfReports.TocHeading(Js.Table("Locale", name))));
        }

        [Theory]
        [InlineData("KundliLite")]
        [InlineData("KundliDetailed")]
        [InlineData("KundliBrihad")]
        [InlineData("MatchMaking")]
        [InlineData("DashaAnalysis")]
        [InlineData("Numerology")]
        [InlineData("SadeSati")]
        [InlineData("MangalDosha")]
        [InlineData("Varshaphal")]
        public void ATimestampWithoutAZoneIsUtc(string renderer)
        {
            string[] times =
            {
                "2026-11-22T10:05", "2026-11-22T10:05:30", "2026-11-22T10:05:30.123456789",
                "2026-11-22 10:05", "2026-11-22t10:05:30.5",
            };
            foreach (string time in times)
            {
                string zoned = time.Substring(0, 10) + "T" + time.Substring(11) + "Z";
                using JsonDocument bare = JsonDocument.Parse("{\"GeneratedAt\":\"" + time + "\"}");
                using JsonDocument utc = JsonDocument.Parse("{\"GeneratedAt\":\"" + zoned + "\"}");
                string html = PdfFixtures.Renderers[renderer](bare.RootElement, null);
                Assert.Contains("2026-11-22 10:05 UTC", html, StringComparison.Ordinal);
                Assert.Equal(PdfFixtures.Renderers[renderer](utc.RootElement, null), html);
            }
        }

        [Fact]
        public void TheRunningHeaderAndFooterUseTheirOwnFontAndTheChartKeepsIts()
        {
            string margin = "font-family:" + Locales.PRINT_MARGIN_FONT_STACK_ATTR.Str() + ";";
            Assert.Contains("Helvetica Neue, Helvetica, Arial, Liberation Sans, sans-serif", margin, StringComparison.Ordinal);
            Assert.Contains(margin, KundliBrihadSections.brihadHeaderTemplate(Js.Undefined, Js.Undefined).Str(), StringComparison.Ordinal);
            Assert.Contains(margin, KundliBrihadSections.brihadFooterTemplate(Js.Undefined, Js.Undefined).Str(), StringComparison.Ordinal);

            string html = PdfReports.RenderKundliBrihadHtml(PdfFixtures.Example("KundliBrihad", "en"));
            string svg = html.Substring(html.IndexOf("<svg viewBox=\"0 0 100 100\"", StringComparison.Ordinal));
            svg = svg.Substring(0, svg.IndexOf('>', StringComparison.Ordinal));
            Assert.Contains("font-family:" + Locales.INDIC_FONT_STACK_ATTR.Str() + ";", svg, StringComparison.Ordinal);
            Assert.DoesNotContain("Helvetica", svg, StringComparison.Ordinal);
        }

        [Fact]
        public void EveryCellPrintsEveryPlanetOnceWithOneDecimalAtMost()
        {
            for (int house = 1; house <= 12; house++)
            {
                for (int count = 1; count <= 13; count++)
                {
                    Js planets = Js.Arr(Enumerable.Range(1, count)
                        .Select(i => Js.Obj(Js.Prop("label", Js.S("p" + i)), Js.Prop("house", Js.N(house))))
                        .ToArray());
                    string svg = NorthIndianChart.northIndianChartSvg(
                        Js.Obj(Js.Prop("lagnaSignIndex", Js.N(0)), Js.Prop("planets", planets))).Str();
                    string[] printed = svg.Split("<tspan fill=").Skip(1)
                        .Select(t => t.Substring(t.IndexOf('>', StringComparison.Ordinal) + 1))
                        .Select(t => t.Substring(0, t.IndexOf('<', StringComparison.Ordinal)))
                        .ToArray();
                    Assert.Equal(Enumerable.Range(1, count).Select(i => "p" + i), printed);
                    Assert.All(
                        svg.Split("<text x=\"").Skip(1).Select(t => t.Substring(0, t.IndexOf('"', StringComparison.Ordinal))),
                        x => Assert.True(!x.Contains('.', StringComparison.Ordinal) || x.Length - x.IndexOf('.', StringComparison.Ordinal) == 2, x));
                }
            }
        }

        [Fact]
        public void MarathiDrawsTheSamePlanetsInTheSameCellsAsEnglish()
        {
            string[] english = { "Sun", "Moon", "Mars", "Mercury", "Jupiter", "Venus", "Saturn", "Rahu", "Ketu" };
            string[] marathi = { "सूर्य", "चंद्र", "मंगळ", "बुध", "बृहस्पती", "शुक्र", "शनी", "राहू", "केतू" };

            static string Render(string language, string sign, string[] names) => PdfReports.RenderKundliBrihadHtml(
                JsonSerializer.SerializeToElement(new
                {
                    Locale = language,
                    Ascendant = new { sign },
                    Planets = names.Select((name, i) => new { name, house_num = (i % 4) + 1 }),
                    DivisionalCharts = new[]
                    {
                        new { varga = "D9", lagna_sign = sign, planets = names.Select((name, i) => new { name, house = 12 - i }) },
                        new { varga = "D2", lagna_sign = sign, planets = names.Select((name, i) => new { name, house = (i % 2) + 1 }) },
                    },
                }));

            string inEnglish = Render("en", "Leo", english);
            string inMarathi = Render("mr", "सिंह", marathi);

            // Lagna, navamsa, and the two divisional charts.
            Assert.Equal(4 * 9, ChartReader.Planets(inEnglish).Count);
            Assert.Equal(ChartReader.Planets(inEnglish), ChartReader.Planets(inMarathi));
            Assert.Equal(9, ChartReader.Glyphs(inMarathi));
        }

        [Theory]
        [InlineData("hi", "36 गुण")]
        [InlineData("mr", "36 गुण")]
        [InlineData("gu", "36 ગુણ")]
        [InlineData("bn", "36 গুণ")]
        [InlineData("pa", "36 ਗੁਣ")]
        public void AshtakootaSaysThirtySixGuna(string language, string words)
        {
            JsonElement report = PdfFixtures.Example("MatchMaking", language);
            // Ordinal: to a culture, a word inside a longer one ends mid-cluster.
            Assert.Contains(words, PdfReports.RenderMatchMakingHtml(report), StringComparison.Ordinal);
        }
    }
}
