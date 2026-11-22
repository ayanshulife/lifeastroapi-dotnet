using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Params;
using LifeAstroApi.Pdf;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>The public surface: inputs, errors, the one call per report.</summary>
    public class PdfReportsTests
    {
        public static IEnumerable<object[]> Renderers() => PdfFixtures.Renderers.Keys.Select(name => new object[] { name });

        [Fact]
        public async Task TakesWhatTheClientReturnsWithoutAConversion()
        {
            JsonElement sample = PdfFixtures.Example("KundliLite", "hi");
            var stub = new StubHandler();
            stub.Respond(Reply.Ok("{\"data\":" + sample.GetRawText() + ",\"meta\":{}}"));
            using LifeAstroClient client = Fixtures.Client(stub);

            JsonElement report = await client.Reports.KundliLiteAsync(new ReportsKundliLiteParams(Fixtures.Arjun) { Lang = Lang.Hi });

            Assert.Equal(PdfReports.RenderKundliLiteHtml(sample), PdfReports.RenderKundliLiteHtml(report));
        }

        [Theory]
        [MemberData(nameof(Renderers))]
        public void JsonTextGivesTheSameHtml(string renderer)
        {
            JsonElement report = PdfFixtures.Example(renderer, "pa");
            MethodInfo fromText = typeof(PdfReports).GetMethod("Render" + renderer + "Html", new[] { typeof(string), typeof(PdfBranding) })!;
            string expected = PdfFixtures.Renderers[renderer](report, null);
            Assert.Equal(expected, fromText.Invoke(null, new object?[] { report.GetRawText(), null }));
            Assert.Equal(expected, fromText.Invoke(null, new object?[] { ((char)0xFEFF) + report.GetRawText(), null }));
        }

        [Theory]
        [MemberData(nameof(Renderers))]
        public async Task OneCallPerReportHandsTheHtmlToTheEngine(string renderer)
        {
            JsonElement report = PdfFixtures.Example(renderer, "ta");
            var branding = new PdfBranding { CompanyName = "Acme Astro" };
            var options = new PdfOptions { Scale = 0.9 };

            var engine = new RecordingEngine();
            Assert.NotEmpty(await PdfFixtures.Printers[renderer](report, branding, options, engine, CancellationToken.None));
            MethodInfo fromText = typeof(PdfReports).GetMethod(
                renderer + "PdfAsync",
                new[] { typeof(string), typeof(PdfBranding), typeof(PdfOptions), typeof(IPdfEngine), typeof(CancellationToken) })!;
            var textEngine = new RecordingEngine();
            await (Task<byte[]>)fromText.Invoke(null, new object?[] { report.GetRawText(), branding, options, textEngine, CancellationToken.None })!;

            string html = PdfFixtures.Renderers[renderer](report, branding);
            if (renderer == "KundliBrihad")
            {
                // No links in the recorded answer, so the second print is the document without a TOC.
                string printed = PdfPrintCss.Apply(html, engine.Options[1]);
                Assert.Equal(printed, engine.Html[1]);
                Assert.Equal(printed, textEngine.Html[1]);
            }
            else
            {
                Assert.Equal(new[] { html }, engine.Html);
                Assert.Equal(new[] { html }, textEngine.Html);
                Assert.Same(options, engine.Options[0]);
            }

            Assert.Equal(0.9, engine.Options[0]!.Scale);
        }

        [Fact]
        public async Task HtmlToPdfPrintsCustomHtml()
        {
            var engine = new RecordingEngine();
            await PdfReports.HtmlToPdfAsync("<p>hello</p>", engine: engine);
            Assert.Equal(new[] { "<p>hello</p>" }, engine.Html);
            await Assert.ThrowsAsync<ArgumentNullException>(() => PdfReports.HtmlToPdfAsync(null!, engine: engine));
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("\"a string\"")]
        [InlineData("null")]
        [InlineData("{\"Subject\":{\"Name\":\"\\ud83d\"}}")]
        public async Task BadInputIsAnArgumentException(string json)
        {
            Assert.ThrowsAny<ArgumentException>(() => PdfReports.RenderKundliLiteHtml(json));
            var engine = new RecordingEngine();
            await Assert.ThrowsAnyAsync<ArgumentException>(() => PdfReports.KundliLitePdfAsync(json, engine: engine));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => PdfReports.KundliBrihadPdfAsync(json, engine: engine));
            Assert.Empty(engine.Html);
        }

        [Fact]
        public async Task AShapeTheReportCannotPrintIsAnArgumentException()
        {
            // A null inside a list prints since SYNC5; text where the template formats a number still cannot.
            const string Json = "{\"shadbala\":[{\"total_rupas\":\"x\"}]}";
            Assert.ThrowsAny<ArgumentException>(() => PdfReports.RenderKundliBrihadHtml(Json));
            var engine = new RecordingEngine();
            await Assert.ThrowsAnyAsync<ArgumentException>(() => PdfReports.KundliBrihadPdfAsync(Json, engine: engine));
            Assert.Empty(engine.Html);
        }

        [Fact]
        public void EndlessNestingIsAnArgumentExceptionAndNotAStackOverflow()
        {
            string nested = "{\"kp\":" + new string('[', 5000) + new string(']', 5000) + "}";
            using JsonDocument document = JsonDocument.Parse(nested, new JsonDocumentOptions { MaxDepth = 6000 });
            Assert.Throws<ArgumentException>(() => PdfReports.RenderKundliBrihadHtml(document.RootElement));
        }

        [Fact]
        public void AMissingElementIsAnArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PdfReports.RenderKundliLiteHtml(default(JsonElement)));
            Assert.Throws<ArgumentNullException>(() => PdfReports.RenderKundliLiteHtml((string)null!));
        }

        [Fact]
        public async Task ACancelledTokenPrintsNothing()
        {
            var engine = new RecordingEngine();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => PdfReports.KundliBrihadPdfAsync("{}", engine: engine, cancellationToken: cancel.Token));
            Assert.Empty(engine.Html);
        }

        [Fact]
        public void A4PrintOptionsAreNodes()
        {
            PdfPrintGeometry a4 = PdfReports.A4PrintOptions;
            Assert.Equal((595, 842), (a4.Width, a4.Height));
            Assert.Equal((51, 43, 62, 43), (a4.MarginTop, a4.MarginRight, a4.MarginBottom, a4.MarginLeft));
            Assert.Equal(595 / 72d, a4.WidthInches);
            Assert.Equal(842 / 72d, a4.HeightInches);
            Assert.Equal(62 / 72d, a4.MarginBottomInches);
        }

        [Fact]
        public void PdfFailuresAreLifeAstroExceptions()
        {
            LifeAstroException error = new LifeAstroPdfException("no browser", "browser_not_found");
            Assert.Equal("browser_not_found", error.Code);
            Assert.Equal(0, error.Status);
        }

        [Fact]
        public void BrandingCannotBreakOutOfTheHtmlOrTheCss()
        {
            string html = PdfReports.RenderKundliLiteHtml("{}", new PdfBranding
            {
                CompanyName = "<script>alert(1)</script>",
                LogoUrl = "javascript:alert(1)",
                PrimaryColor = "red}body{display:none",
                WatermarkText = "</style><script>",
            });
            Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
            Assert.DoesNotContain("display:none}", html.Replace(" ", string.Empty).Replace("footer{display:none;}", string.Empty), StringComparison.Ordinal);
            Assert.Contains("--brand-primary: #1a1a1a", html, StringComparison.Ordinal);
        }
    }
}
