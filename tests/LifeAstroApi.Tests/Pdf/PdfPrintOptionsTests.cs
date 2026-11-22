using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Pdf;
using LifeAstroApi.Pdf.Templates;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>The print options reach the paper: the hand-written port of the Node SDK's render.ts.</summary>
    public class PdfPrintOptionsTests
    {
        private static readonly Lazy<JsonElement> Vectors = new Lazy<JsonElement>(() => PdfFixtures.Read("branding.json"));

        private static PdfMargin BrihadMargin()
        {
            Js margin = KundliBrihadSections.BRIHAD_PDF_MARGIN;
            return new PdfMargin(margin.Get("top").Str(), margin.Get("bottom").Str(), margin.Get("left").Str(), margin.Get("right").Str());
        }

        [Fact]
        public void TheCssIsNodes()
        {
            JsonElement rows = Vectors.Value.GetProperty("printCss");
            Assert.True(rows.GetArrayLength() > 150);
            foreach (JsonElement row in rows.EnumerateArray())
            {
                JsonElement given = row.GetProperty("options");
                string expected = row.GetProperty("css").GetString()!;
                Assert.Equal(given.GetRawText() + " -> " + expected, given.GetRawText() + " -> " + PdfPrintCss.Build(PdfFixtures.Options(given)));
            }
        }

        [Fact]
        public void EachMarginWinsOverPageCss()
        {
            string css = PdfPrintCss.Build(new PdfOptions { Margin = new PdfMargin { Top = "40mm", Left = "0" } });
            Assert.Equal("@page { margin-top: 40mm !important; margin-left: 0px !important; }\n", css);
            Assert.Contains("margin-top: 12px", PdfPrintCss.Build(new PdfOptions { Margin = new PdfMargin { Top = "12" } }), StringComparison.Ordinal);
            var hostile = new PdfMargin { Top = "1mm}body{display:none", Bottom = " " };
            Assert.Equal(string.Empty, PdfPrintCss.Build(new PdfOptions { Margin = hostile }));
        }

        [Fact]
        public void FormatAndLandscapeSetThePaperUnlessTheCssSizeIsAskedFor()
        {
            Assert.Contains("size: 8.5in 11in !important;", PdfPrintCss.Build(new PdfOptions { Format = PdfPaperFormat.Letter }), StringComparison.Ordinal);
            Assert.Contains("size: 297mm 210mm !important;", PdfPrintCss.Build(new PdfOptions { Landscape = true }), StringComparison.Ordinal);
            Assert.Equal(string.Empty, PdfPrintCss.Build(new PdfOptions { Format = PdfPaperFormat.A5, PreferCssPageSize = true }));
            Assert.Equal(string.Empty, PdfPrintCss.Build(new PdfOptions { Format = PdfPaperFormat.A5, PreferCssPageSize = false }));
            Assert.Equal(string.Empty, PdfPrintCss.Build(new PdfOptions { Format = PdfPaperFormat.A4, Landscape = false }));
            Assert.Equal(string.Empty, PdfPrintCss.Build(new PdfOptions { Format = (PdfPaperFormat)99 }));
        }

        [Fact]
        public void NoBackgroundsAndTheCallersFooterWork()
        {
            Assert.Contains("print-color-adjust: economy !important", PdfPrintCss.Build(new PdfOptions { PrintBackground = false }), StringComparison.Ordinal);
            var footer = new PdfOptions { DisplayHeaderFooter = true, FooterTemplate = "<b>x</b>" };
            Assert.Contains("content: none !important", PdfPrintCss.Build(footer), StringComparison.Ordinal);
            Assert.Equal(string.Empty, PdfPrintCss.Build(new PdfOptions { FooterTemplate = "<b>x</b>" }));
        }

        [Fact]
        public void NoBackgroundsLeaveNothingPaintedOverTheWatermark()
        {
            string css = PdfPrintCss.Build(new PdfOptions { PrintBackground = false });
            const string Rule = "body *, body *::before, body *::after { background: transparent !important; box-shadow: none !important; }\n";
            Assert.StartsWith(Rule, css, StringComparison.Ordinal); // the first rule of the block
            Assert.Equal(string.Empty, PdfPrintCss.Build(new PdfOptions()));
        }

        [Fact]
        public async Task DefaultOptionsLeaveTheHtmlUntouched()
        {
            const string Html = "<!doctype html><html><body><p>x</p></body></html>";
            Assert.Same(Html, PdfPrintCss.Apply(Html, null));
            Assert.Same(Html, PdfPrintCss.Apply(Html, new PdfOptions()));
            var onlyTimeAndScale = new PdfOptions { Scale = 0.9, Timeout = TimeSpan.FromMinutes(3), PrintTimeout = TimeSpan.FromMinutes(9) };
            Assert.Same(Html, PdfPrintCss.Apply(Html, onlyTimeAndScale));

            foreach (string renderer in PdfFixtures.Renderers.Keys.Where(name => name != "KundliBrihad"))
            {
                JsonElement report = PdfFixtures.Example(renderer, "bn");
                var engine = new RecordingEngine();
                await PdfFixtures.Printers[renderer](report, null, new PdfOptions(), engine, CancellationToken.None);
                Assert.Equal(new[] { PdfFixtures.Renderers[renderer](report, null) }, engine.Html);
            }
        }

        [Fact]
        public async Task EveryEngineGetsTheDocumentWithTheOptionsInIt()
        {
            var options = new PdfOptions { Landscape = true, Margin = new PdfMargin { Top = "40mm" } };
            const string Style = "\n<style>@page { margin-top: 40mm !important; size: 297mm 210mm !important; }\n</style>";
            JsonElement report = PdfFixtures.Example("KundliLite", "en");

            var engine = new RecordingEngine();
            await PdfReports.KundliLitePdfAsync(report, options: options, engine: engine);
            await PdfReports.HtmlToPdfAsync("<p>x</p>", options, engine);

            Assert.Equal(new[] { PdfReports.RenderKundliLiteHtml(report) + Style, "<p>x</p>" + Style }, engine.Html);
            // An engine of the SDK behind it adds nothing a second time.
            Assert.Same(engine.Html[1], PdfPrintCss.Apply(engine.Html[1], options));
        }

        [Fact]
        public void MarginsMergeSideBySide()
        {
            PdfMargin defaults = BrihadMargin();
            Assert.Equal(("26mm", "16mm", "0mm", "0mm"), (defaults.Top, defaults.Bottom, defaults.Left, defaults.Right));
            foreach (JsonElement row in Vectors.Value.GetProperty("merged").EnumerateArray())
            {
                PdfMargin merged = PdfOptions.MergeMargin(defaults, PdfFixtures.Margin(row.GetProperty("margin")));
                JsonElement result = row.GetProperty("result");
                Assert.Equal(
                    (result.GetProperty("top").GetString(), result.GetProperty("bottom").GetString(),
                        result.GetProperty("left").GetString(), result.GetProperty("right").GetString()),
                    (merged.Top, merged.Bottom, merged.Left, merged.Right));
            }
        }

        [Fact]
        public async Task TheCallersOptionsWinInBrihad()
        {
            var options = new PdfOptions
            {
                Margin = new PdfMargin { Top = "40mm", Bottom = " ", Left = "0" },
                HeaderTemplate = "<p>my header</p>",
                FooterTemplate = "<p>my footer</p>",
            };
            var engine = new RecordingEngine();
            await PdfReports.KundliBrihadPdfAsync("{}", options: options, engine: engine);

            PdfOptions used = engine.Options[0]!;
            Assert.Equal(("40mm", "16mm", "0", "0mm"), (used.Margin!.Top, used.Margin.Bottom, used.Margin.Left, used.Margin.Right));
            Assert.Equal(("<p>my header</p>", "<p>my footer</p>"), (used.HeaderTemplate, used.FooterTemplate));
            Assert.True(used.DisplayHeaderFooter);

            // The caller's own object is left as it was.
            Assert.Null(options.DisplayHeaderFooter);
            Assert.Null(options.Margin.Right);
            Assert.Equal(" ", options.Margin.Bottom);
        }

        [Fact]
        public async Task BrihadsRunningHeaderIsOffOnlyWhenTheCallerSaysFalse()
        {
            foreach (bool? said in new bool?[] { null, true, false })
            {
                var engine = new RecordingEngine();
                await PdfReports.KundliBrihadPdfAsync("{}", options: new PdfOptions { DisplayHeaderFooter = said }, engine: engine);
                bool on = said != false;
                Assert.Equal(on, PageSetup.From(engine.Options[0]).DisplayHeaderFooter);
                Assert.Equal(!on, PageSetup.From(engine.Options[0]).PreferCssPageSize);
                Assert.Equal(on, engine.Html[0].Contains("@bottom-center { content: none !important; }", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task BlankCallerBrandingGivesTheSameBrihadHeaderAndFooter()
        {
            const string Report = "{\"Branding\":{\"CompanyName\":\"Json Co\",\"PrimaryColor\":\"#0b5cad\",\"FooterText\":\"Json footer\"}}";
            string white = " \t\n" + (char)0xA0 + (char)0xFEFF;
            var blank = new PdfBranding
            {
                CompanyName = white,
                LogoUrl = white,
                PrimaryColor = white,
                SecondaryColor = white,
                FooterText = white,
                WatermarkText = white,
            };
            var without = new RecordingEngine();
            var with = new RecordingEngine();
            await PdfReports.KundliBrihadPdfAsync(Report, engine: without);
            await PdfReports.KundliBrihadPdfAsync(Report, blank, engine: with);

            Assert.Contains("Json Co", with.Options[0]!.HeaderTemplate, StringComparison.Ordinal);
            Assert.Contains("color:#0b5cad", with.Options[0]!.HeaderTemplate, StringComparison.Ordinal);
            Assert.Contains("Json footer", with.Options[0]!.FooterTemplate, StringComparison.Ordinal);
            Assert.Equal(without.Options[0]!.HeaderTemplate, with.Options[0]!.HeaderTemplate);
            Assert.Equal(without.Options[0]!.FooterTemplate, with.Options[0]!.FooterTemplate);
            Assert.Equal(without.Html, with.Html);
        }

        [Fact]
        public async Task TheBrihadPaletteKnowsWhichColourIsTheCallers()
        {
            const string Report = "{\"Branding\":{\"PrimaryColor\":\"#1a1a1a\",\"SecondaryColor\":\"#666666\"}}";
            var reports = new RecordingEngine();
            await PdfReports.KundliBrihadPdfAsync(Report, engine: reports);
            Assert.Contains("#9e2b25", reports.Options[0]!.HeaderTemplate, StringComparison.Ordinal); // saffron

            var callers = new RecordingEngine();
            await PdfReports.KundliBrihadPdfAsync(Report, new PdfBranding { PrimaryColor = "#1a1a1a" }, engine: callers);
            Assert.Contains("color:#1a1a1a", callers.Options[0]!.HeaderTemplate, StringComparison.Ordinal); // black, as asked
        }

        [Fact]
        public async Task ALongBrihadCanBeAllowedMoreTimeToPrint()
        {
            var options = new PdfOptions { PrintTimeout = TimeSpan.FromMinutes(12) };
            var engine = new RecordingEngine();
            await PdfReports.KundliBrihadPdfAsync("{}", options: options, engine: engine);
            Assert.All(engine.Options, used => Assert.Equal(TimeSpan.FromMinutes(12), PageSetup.From(used).PrintTimeout));

            var loading = new RecordingEngine();
            await PdfReports.KundliBrihadPdfAsync("{}", options: new PdfOptions { Timeout = TimeSpan.FromMinutes(2) }, engine: loading);
            Assert.All(loading.Options, used => Assert.Equal(TimeSpan.FromMinutes(8), PageSetup.From(used).PrintTimeout));
        }
    }
}
