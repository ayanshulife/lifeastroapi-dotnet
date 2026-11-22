using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using LifeAstroApi.Pdf;
using LifeAstroApi.Pdf.Templates;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>Every branding field reaches the paper readable; values named like object properties are plain values.</summary>
    public class PdfBrandingTests
    {
        private static readonly string[] Languages = { "en", "hi", "mr", "te", "ta", "kn", "ml", "gu", "bn", "pa" };
        private static readonly string[] PropertyNames = { "constructor", "toString", "__proto__", "valueOf", "hasOwnProperty", "isPrototypeOf" };

        private static readonly string NoBreakSpace = ((char)0xA0).ToString();
        private static readonly string ByteOrderMark = ((char)0xFEFF).ToString();

        private static readonly Lazy<JsonElement> Vectors = new Lazy<JsonElement>(() => PdfFixtures.Read("branding.json"));

        private static readonly object ApiDefaults = new
        {
            CompanyName = string.Empty,
            LogoURL = string.Empty,
            PrimaryColor = "#1a1a1a",
            SecondaryColor = "#666666",
            FooterText = "Generated using LifeAstroAPI",
            WatermarkText = string.Empty,
        };

        private static JsonElement Report(object value) => JsonSerializer.SerializeToElement(value);

        private static string Brihad(object report, PdfBranding? branding = null) =>
            PdfReports.RenderKundliBrihadHtml(Report(report), branding);

        private static Js Rgb(int r, int g, int b) => Js.Arr(Js.N(r), Js.N(g), Js.N(b));

        private static double Number(Js value) => double.Parse(value.Str(), CultureInfo.InvariantCulture);

        private static double Contrast(Js a, Js b)
        {
            double x = Number(Colors.contrastOnWhite(a));
            double y = Number(Colors.contrastOnWhite(b));
            return Math.Max(x, y) / Math.Min(x, y);
        }

        private static string? Hex(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetString();

        private static string? Hex(Js rgb) => rgb ? Colors.toHex(rgb).Str() : null;

        private static string Between(string text, string before, string after)
        {
            int from = text.IndexOf(before, StringComparison.Ordinal) + before.Length;
            return text.Substring(from, text.IndexOf(after, from, StringComparison.Ordinal) - from);
        }

        // ---- colours ----

        [Theory]
        [InlineData("")]
        [InlineData("tr-TR")]
        [InlineData("fa-IR")]
        public void ColoursAreNodes(string culture)
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                JsonElement rows = Vectors.Value.GetProperty("colours");
                Assert.True(rows.GetArrayLength() > 1000);
                foreach (JsonElement row in rows.EnumerateArray())
                {
                    string value = row.GetProperty("value").GetString()!;
                    JsonElement rgb = row.GetProperty("rgb");
                    string wanted = rgb.ValueKind == JsonValueKind.Null
                        ? "undefined"
                        : string.Join(",", rgb.EnumerateArray().Select(c => c.GetInt32().ToString(CultureInfo.InvariantCulture)));
                    Assert.Equal(value + " -> " + wanted, value + " -> " + Colors.parseColor(Js.S(value)).Str());
                    Assert.Equal(
                        row.GetProperty("printed").GetString(),
                        Colors.printableColor(Js.S(value), Js.S("#1a1a1a")).Str());
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        }

        [Fact]
        public void ColourMathsIsNodesOverTheWholeCube()
        {
            var all = new StringBuilder();
            for (int r = 0; r < 256; r += 15)
            {
                for (int g = 0; g < 256; g += 15)
                {
                    for (int b = 0; b < 256; b += 15)
                    {
                        Js rgb = Rgb(r, g, b);
                        all.Append(Colors.toHex(Colors.darkenToContrast(rgb, Js.N(4.5))).Str())
                            .Append(Colors.toHex(Colors.darkenToContrast(rgb, Js.N(5.2))).Str())
                            .Append(Colors.toHex(Colors.tintOnWhite(rgb, Js.N(3))).Str())
                            .Append(Colors.toHex(Colors.tintOnWhite(rgb, Js.N(5))).Str())
                            .Append(Colors.toHex(Colors.tintOnWhite(rgb, Js.N(25))).Str())
                            .Append(Colors.seeThrough(rgb).Str())
                            .Append(Colors.printableColor(Colors.toHex(rgb), Js.S("none")).Str())
                            .Append('\n');
                    }
                }
            }

            Assert.Equal(Vectors.Value.GetProperty("grid").GetString(), PdfFixtures.Sha256(all.ToString()));
        }

        [Theory]
        [InlineData("#0B5CAD", "11,92,173")]
        [InlineData("#05a", "0,85,170")]
        [InlineData("#05af", "0,85,170")]
        [InlineData("#0b5cadff", "11,92,173")]
        [InlineData("RebeccaPurple", "102,51,153")]
        [InlineData("rgb(11, 92, 173)", "11,92,173")]
        [InlineData("rgb(11 92 173)", "11,92,173")]
        [InlineData("rgba(0,0,0,0.5)", "128,128,128")] // 127.5 goes up, not to even
        [InlineData("hsl(210, 100%, 50%)", "0,128,255")]
        [InlineData("rgb(0.5, 2.5, 4.5)", "1,3,5")]
        [InlineData("rgb(100%, 0%, 0%)", "255,0,0")]
        public void ReadsEveryAcceptedCssForm(string value, string rgb) => Assert.Equal(rgb, Colors.parseColor(Js.S(value)).Str());

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("constructor")]
        [InlineData("toString")]
        [InlineData("inherit")]
        [InlineData("transparent")]
        [InlineData("#0000")]
        [InlineData("rgb(1,2)")]
        [InlineData("#12345")]
        [InlineData("rgb(1.2.3, 4, 5)")]
        [InlineData("hsl(1,2,3)")]
        public void RefusesWhatCannotBePrinted(string value) => Assert.Equal("undefined", Colors.parseColor(Js.S(value)).Str());

        [Fact]
        public void PrintsReadableColoursAsGivenAndDarkensOnlyPaleOnes()
        {
            Js ink = Js.S("#1a1a1a");
            Assert.Equal("RebeccaPurple", Colors.printableColor(Js.S(" RebeccaPurple "), ink).Str());
            Assert.Equal("#1a1a1a", Colors.printableColor(Js.S("notacolour"), ink).Str());
            Assert.True(Number(Colors.contrastOnWhite(Colors.parseColor(Colors.printableColor(Js.S("#ffe14d"), ink)))) >= 4.5);
            Assert.DoesNotContain(
                "--brand-primary: #ffe14d",
                PdfReports.RenderKundliLiteHtml("{}", new PdfBranding { PrimaryColor = "#ffe14d" }),
                StringComparison.Ordinal);
            Assert.Equal("0,0,0", Colors.darkenToContrast(Rgb(255, 255, 255), Js.N(22)).Str());
        }

        [Fact]
        public void AColourLongerThanAnyRealOneIsRefusedAtOnce()
        {
            string longest = "rgb(" + new string('1', 55) + ",2,3)";
            Assert.Equal(64, longest.Length);
            Assert.NotEqual("undefined", Colors.parseColor(Js.S(longest)).Str());
            Assert.Equal("undefined", Colors.parseColor(Js.S("rgb(" + new string('1', 56) + ",2,3)")).Str());

            var watch = System.Diagnostics.Stopwatch.StartNew();
            Assert.Equal("undefined", Colors.parseColor(Js.S("rgb(" + new string(' ', 500000) + "x")).Str());
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "took " + watch.Elapsed);
        }

        // ---- watermark ----

        [Fact]
        public void WatermarkSizesAreNodes()
        {
            foreach (JsonElement row in Vectors.Value.GetProperty("watermarks").EnumerateArray())
            {
                string text = row.GetProperty("text").GetString()!;
                string expected = row.GetProperty("size").GetInt32().ToString(CultureInfo.InvariantCulture);
                Assert.Equal(text + " -> " + expected, text + " -> " + Size(text));
            }
        }

        [Fact]
        public void TheWatermarkShrinksForALongWordOrLine()
        {
            Assert.Equal("80", Size("SAMPLE"));
            Assert.Equal("51", Size("CONFIDENTIAL"));
            Assert.Equal("61", Size("DRAFT FOR REVIEW ONLY"));
            Assert.Equal("10", Size(new string('x', 500)));
            // Nine code points are 68pt; eighteen UTF-16 units would be 34pt.
            Assert.Equal("68", Size(string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F600), 9))));
        }

        private static string Size(string watermark)
        {
            string html = PdfReports.RenderKundliLiteHtml("{}", new PdfBranding { WatermarkText = watermark });
            return Between(Between(html, "body::before {", "}"), "font-size: ", "pt");
        }

        [Fact]
        public void TheWatermarkPrintsBehindTheContentInBrihad()
        {
            var watermark = new PdfBranding { WatermarkText = "SAMPLE" };
            string html = Brihad(new { }, watermark);
            Assert.Contains("content: \"SAMPLE\"", html, StringComparison.Ordinal);
            Assert.Contains("body::before { z-index: -1; }", html, StringComparison.Ordinal);
            Assert.DoesNotContain("body::before { display: none", html, StringComparison.Ordinal);
            const string SeeThrough = "tr:nth-child(even) td, .graha-card { background: transparent;";
            Assert.Contains(SeeThrough, html, StringComparison.Ordinal);
            Assert.DoesNotContain(SeeThrough, Brihad(new { }), StringComparison.Ordinal);
            Assert.Equal("transparent", Colors.seeThrough(Rgb(255, 255, 255)).Str());
        }

        [Fact]
        public void EveryRendererAndLanguageHasTheWatermark()
        {
            foreach (string renderer in PdfFixtures.Renderers.Keys)
            {
                foreach (string language in Languages)
                {
                    string html = PdfFixtures.Renderers[renderer](
                        Report(new Dictionary<string, string> { ["Locale"] = language, ["language"] = language }),
                        new PdfBranding { WatermarkText = "SAMPLE" });
                    Assert.Contains("content: \"SAMPLE\"", html, StringComparison.Ordinal);
                }
            }
        }

        // ---- Brihad palette ----

        [Fact]
        public void PalettesHeadersAndFootersAreNodes()
        {
            JsonElement rows = Vectors.Value.GetProperty("palettes");
            Assert.True(rows.GetArrayLength() >= 90);
            foreach (JsonElement row in rows.EnumerateArray())
            {
                // Absent and null are two values: resolveBranding gets what the report has.
                Js report = row.TryGetProperty("report", out JsonElement given) ? JsJson.Convert(given) : Js.Undefined;
                JsonElement callers = row.GetProperty("caller");
                Js caller = callers.ValueKind == JsonValueKind.Null ? Js.Undefined : JsJson.Convert(callers);

                Js palette = KundliBrihadSections.brihadPalette(report, caller);
                JsonElement expected = row.GetProperty("palette");
                if (expected.ValueKind == JsonValueKind.Null)
                {
                    Assert.Equal("undefined", palette.Str());
                }
                else
                {
                    Assert.Equal(Hex(expected.GetProperty("primary")), Hex(palette.Get("primary")));
                    Assert.Equal(Hex(expected.GetProperty("accent")), Hex(palette.Get("accent")));
                    Assert.Equal(Hex(expected.GetProperty("muted")), Hex(palette.Get("muted")));
                }

                Assert.Equal(row.GetProperty("header").GetString(), PdfFixtures.Sha256(KundliBrihadSections.brihadHeaderTemplate(report, caller).Str()));
                Assert.Equal(row.GetProperty("footer").GetString(), PdfFixtures.Sha256(KundliBrihadSections.brihadFooterTemplate(report, caller).Str()));
            }
        }

        [Fact]
        public void BrihadStaysSaffronForTheApisDefaultColours()
        {
            string plain = Brihad(new { });
            Assert.Equal(plain, Brihad(new { Branding = ApiDefaults }));

            // Compared as colours. (The :root variables still print the report's own spelling.)
            string spelt = Brihad(new { Branding = new { PrimaryColor = "#1A1A1A", SecondaryColor = " #666666 " } });
            Assert.DoesNotContain("--as-teal:#1a1a1a", spelt, StringComparison.Ordinal);
            Assert.Equal(plain, spelt.Replace("#1A1A1A", "#1a1a1a", StringComparison.Ordinal));

            // The exact default pair passed on by the caller is no brand either.
            var pair = new PdfBranding { PrimaryColor = "#1a1a1a", SecondaryColor = "#666666" };
            Assert.Equal(plain, Brihad(new { Branding = ApiDefaults }, pair));
        }

        [Fact]
        public void BrihadFollowsTheBrandsColours()
        {
            string branded = Brihad(new { Branding = ApiDefaults }, new PdfBranding { PrimaryColor = "#0b5cad", SecondaryColor = "#0b6b36" });
            Assert.Contains("--as-teal:#0b5cad", branded, StringComparison.Ordinal);
            Assert.Contains("--as-amber:#0b6b36", branded, StringComparison.Ordinal);
            Assert.Contains(".toc-row .toc-page { color: #0b5cad !important; }", branded, StringComparison.Ordinal);

            Assert.Contains("--as-teal:#0b5cad", Brihad(new { Branding = new { PrimaryColor = "#0b5cad" } }), StringComparison.Ordinal);

            // Black is a brand when the caller asks for it.
            Assert.Contains("--as-teal:#1a1a1a", Brihad(new { Branding = ApiDefaults }, new PdfBranding { PrimaryColor = "#1a1a1a" }), StringComparison.Ordinal);

            string onlySecondary = Brihad(new { }, new PdfBranding { SecondaryColor = "#0b6b36" });
            Assert.Contains("--as-amber:#0b6b36", onlySecondary, StringComparison.Ordinal);
            Assert.DoesNotContain("--as-teal:#0b6b36", onlySecondary, StringComparison.Ordinal);
        }

        [Fact]
        public void BrihadStaysReadableForAnyBrandColour()
        {
            Js white = Rgb(255, 255, 255);
            Js[] fills = new[] { "#fafafa", "#f0fdf4", "#fef2f2", "#fefce8" }.Select(h => Colors.parseColor(Js.S(h))).ToArray();
            double text = 21;
            double banner = 21;
            for (int r = 0; r < 256; r += 51)
            {
                for (int g = 0; g < 256; g += 15)
                {
                    for (int b = 0; b < 256; b += 51)
                    {
                        Js hex = Colors.toHex(Rgb(r, g, b));
                        Js palette = KundliBrihadSections.brihadPalette(
                            Js.Undefined, Js.Obj(Js.Prop("primaryColor", hex), Js.Prop("secondaryColor", hex)));
                        Js primary = palette.Get("primary");
                        Js[] behind = fills
                            .Append(Colors.tintOnWhite(primary, Js.N(5)))
                            .Append(Colors.tintOnWhite(primary, Js.N(3)))
                            .ToArray();
                        foreach (Js ink in new[] { primary, palette.Get("accent"), palette.Get("muted") })
                        {
                            banner = Math.Min(banner, Contrast(ink, white));
                            foreach (Js fill in behind)
                            {
                                text = Math.Min(text, Contrast(ink, fill));
                            }
                        }
                    }
                }
            }

            Assert.True(banner >= 5.2, "banner " + banner);
            Assert.True(text >= 4.5, "text " + text);
        }

        // ---- branding text ----

        [Fact]
        public void OnlyWhiteSpaceIsNotSet()
        {
            var blank = new PdfBranding
            {
                CompanyName = " " + NoBreakSpace,
                LogoUrl = ByteOrderMark,
                PrimaryColor = NoBreakSpace + ByteOrderMark,
                SecondaryColor = "\n",
                FooterText = ByteOrderMark,
                WatermarkText = NoBreakSpace,
            };
            JsonElement branded = Report(new
            {
                Branding = new
                {
                    CompanyName = "Json Co",
                    LogoURL = "https://j/l.png",
                    PrimaryColor = "#0b5cad",
                    SecondaryColor = "#0b6b36",
                    FooterText = "Json footer",
                    WatermarkText = "JSON",
                },
            });
            foreach (string renderer in PdfFixtures.Renderers.Keys)
            {
                Assert.Equal(PdfFixtures.Renderers[renderer](branded, null), PdfFixtures.Renderers[renderer](branded, blank));
                Assert.Equal(PdfFixtures.Renderers[renderer](Report(new { }), null), PdfFixtures.Renderers[renderer](Report(new { }), blank));
            }
        }

        [Fact]
        public void BrandingTextPrintsAsTheCallerWroteIt()
        {
            var branding = new PdfBranding { CompanyName = "Acme — Astro -> Pune", FooterText = "© Acme — <private>" };
            string html = PdfReports.RenderKundliLiteHtml("{}", branding);
            Assert.Contains("<div class=\"company\">Acme — Astro -&gt; Pune</div>", html, StringComparison.Ordinal);
            Assert.Contains("<footer>© Acme — &lt;private&gt;</footer>", html, StringComparison.Ordinal);

            Js caller = Js.Table("companyName", branding.CompanyName, "footerText", branding.FooterText);
            Assert.Contains("</span> Acme — Astro -&gt; Pune", KundliBrihadSections.brihadHeaderTemplate(Js.Undefined, caller).Str(), StringComparison.Ordinal);
            Assert.Contains("© Acme — &lt;private&gt;", KundliBrihadSections.brihadFooterTemplate(Js.Undefined, caller).Str(), StringComparison.Ordinal);
        }

        [Fact]
        public void AProtocolRelativeLogoIsHttps()
        {
            string html = PdfReports.RenderKundliLiteHtml("{}", new PdfBranding { LogoUrl = " //cdn.example/l.png" });
            Assert.Contains("<img src=\"https://cdn.example/l.png\"", html, StringComparison.Ordinal);
        }

        // ---- values and labels ----

        [Fact]
        public void ValuesNamedLikeObjectPropertiesAreUnknownWords()
        {
            foreach (string name in PropertyNames)
            {
                Assert.Equal(string.Empty, PlanetGlyphs.planetGlyph(Js.S(name)).Str());
                Assert.Equal(string.Empty, Locales.southPlanetToEn(Js.S(name)).Str());
                Assert.Equal(name, Locales.localizeLord(Js.S("te"), Js.S(name)).Str());
                Assert.Equal("Sun", Locales.localizeLord(Js.S(name), Js.S("Sun")).Str());
                Assert.Equal("Sunday", Locales.localizeWeekday(Js.S(name), Js.S("Sunday")).Str());
                Assert.Equal("undefined", Locales.own(Js.Table("a", "b"), Js.S(name)).Str());
            }

            // A report's own key of that name is a key like any other.
            Assert.Equal("x", Locales.own(Js.Table("constructor", "x"), Js.S("constructor")).Str());
        }

        [Fact]
        public void MangalWithoutADoshaHasNoDashOnlyHouseRow()
        {
            foreach (string language in Languages)
            {
                foreach (int[]? houses in new[] { null, Array.Empty<int>() })
                {
                    string none = PdfReports.RenderMangalDoshaHtml(
                        Report(new { Locale = language, HasDosha = false, Status = "none", HousesOccupied = houses }));
                    Assert.DoesNotContain("<dd>—</dd>", none, StringComparison.Ordinal);
                    Assert.Equal(3, none.Split("<dt>").Length - 1);
                }

                string dosha = PdfReports.RenderMangalDoshaHtml(Report(new { Locale = language, HasDosha = true, HousesOccupied = new[] { -4 } }));
                Assert.Equal(4, dosha.Split("<dt>").Length - 1);
            }
        }

        [Fact]
        public void TheGemNoteSendsNobodyToAnAstrologer()
        {
            foreach (string language in Languages)
            {
                string note = Between(
                    Brihad(new { Locale = language, gems = new { stones = new[] { new { stone = "x" } } } }),
                    "<p class=\"gems-note\">",
                    "</p>");
                Assert.NotEmpty(note);
                Assert.DoesNotContain("astrologer", note, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("consult", note, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ज्योतिष", note, StringComparison.Ordinal);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("tr-TR")]
        public void ApiCodesPrintAsWords(string culture)
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                string html = Brihad(new
                {
                    maitri = new
                    {
                        order = new[] { "Sun", "Moon" },
                        tatkalik = new[] { new { planet = "Sun", relations = new { Sun = "TempFriend", Moon = "TempEnemy" } } },
                    },
                    Remedies = new[]
                    {
                        new { Title = "A", Category = "graha_shanti", Description = "D" },
                        new { Title = "B", Category = "charity", Description = "D" },
                        new { Title = "C", Category = "ishta_puja", Description = "D" }, // i, not a Turkish dotted capital
                        new { Title = "D", Category = "Mantra_jaap", Description = "D" }, // not a lower-case code
                    },
                });
                foreach (string words in new[] { "Temporary friend", "Temporary enemy", "Graha shanti", "Charity", "Ishta puja", "Mantra_jaap" })
                {
                    Assert.Contains(">" + words + "<", html, StringComparison.Ordinal);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        }

        [Fact]
        public void TheTenColumnFriendshipTablesFitThePaper()
        {
            string html = Brihad(new
            {
                maitri = new { order = new[] { "Sun", "Moon" }, tatkalik = new[] { new { planet = "Sun", relations = new { Moon = "Neutral" } } } },
            });
            Assert.Contains("<section class=\"section maitri-section\">", html, StringComparison.Ordinal);
            Assert.Contains(".section.maitri-section table tbody td { padding-left: 1.2mm; padding-right: 1.2mm; }", html, StringComparison.Ordinal);
        }

        [Fact]
        public void OnlyASectionOfAFewLinesSharesItsPage()
        {
            string Rows(int count) => Brihad(new
            {
                rajyoga_power = new { percent = 50, swarnim_kaal = Enumerable.Repeat(new { lord = "Moon" }, count) },
            });
            const string Short = "<section class=\"section short-section\">";
            Assert.Contains(Short, Rows(1), StringComparison.Ordinal);
            Assert.DoesNotContain(Short, Rows(3), StringComparison.Ordinal);
            Assert.Contains(".short-section + .section { page-break-before: auto;", Rows(1), StringComparison.Ordinal);
        }
    }
}
