using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeAstroApi.Pdf;
using LifeAstroApi.Pdf.Templates;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>The Brihad running header and footer, table of contents and section tagging, and the two-pass print.</summary>
    public class PdfBrihadTests
    {
        private static Js Value(JsonElement value) =>
            value.ValueKind == JsonValueKind.Null ? Js.Undefined : JsJson.Convert(value);

        [Fact]
        public void HeaderAndFooterTemplatesAreNodes()
        {
            foreach (JsonElement row in PdfFixtures.Read("brihad.json").GetProperty("templates").EnumerateArray())
            {
                Js report = Value(row.GetProperty("report"));
                Js caller = Value(row.GetProperty("caller"));
                Assert.Equal(row.GetProperty("header").GetString(), KundliBrihadSections.brihadHeaderTemplate(report, caller).Str());
                Assert.Equal(row.GetProperty("footer").GetString(), KundliBrihadSections.brihadFooterTemplate(report, caller).Str());
            }
        }

        [Fact]
        public void TableOfContentsIsNodes()
        {
            JsonElement tables = PdfFixtures.Read("brihad.json").GetProperty("toc");
            Assert.Equal(10, tables.GetArrayLength());
            foreach (JsonElement row in tables.EnumerateArray())
            {
                string heading = row.GetProperty("heading").GetString()!;
                Assert.Equal(heading, PdfReports.TocHeading(Js.Table("Locale", row.GetProperty("locale").GetString()!)));
                Assert.Equal(
                    row.GetProperty("html").GetString(),
                    KundliBrihadSections.buildBrihadToc(Value(row.GetProperty("entries")), Js.S(heading)).Str());
            }

            Assert.Equal(string.Empty, KundliBrihadSections.buildBrihadToc(Js.Arr(), Js.S("Contents")).Str());
        }

        [Fact]
        public void SectionTaggingIsNodes()
        {
            foreach (JsonElement row in PdfFixtures.Read("brihad.json").GetProperty("tagged").EnumerateArray())
            {
                GoldenCase c = PdfFixtures.Cases[row.GetProperty("id").GetString()!];
                string html = PdfReports.RenderKundliBrihadHtml(c.Report, c.Branding);
                Js tagged = KundliBrihadSections.tagBrihadSectionsForToc(Js.S(html));
                string[] expected = row.GetProperty("titles").EnumerateArray().Select(t => t.GetString()!).ToArray();
                Assert.Equal(expected, tagged.Get("titles").Iterate().Select(t => t.Str()).ToArray());
                Assert.Equal(row.GetProperty("sha256").GetString(), PdfFixtures.Sha256(tagged.Get("html").Str()));
            }
        }

        public static IEnumerable<object[]> WrapperRuns() => Enumerable
            .Range(0, PdfFixtures.Read("wrapper.json").GetProperty("runs").GetArrayLength())
            .Select(i => new object[] { i });

        [Theory]
        [MemberData(nameof(WrapperRuns))]
        public async Task TheWrapperPrintsAndReturnsWhatNodesDoes(int run)
        {
            JsonElement fixture = PdfFixtures.Read("wrapper.json");
            byte[] printed = fixture.GetProperty("pdf").GetBytesFromBase64();
            JsonElement expected = fixture.GetProperty("runs")[run];
            GoldenCase c = PdfFixtures.Cases[expected.GetProperty("id").GetString()!];
            int[] pages = expected.GetProperty("pages").EnumerateArray().Select(p => p.GetInt32()).ToArray();
            var engine = new RecordingEngine((call, html) =>
            {
                if (call != 1)
                {
                    return printed;
                }

                // Each section is on the page it was on for Node (two may share one).
                var links = new List<(int, string)>();
                for (int i = 0; html.Contains("<a href=\"https://toc.lifeastroapi.invalid/T" + i + "\"", System.StringComparison.Ordinal); i++)
                {
                    links.Add((pages[i], "https://toc.lifeastroapi.invalid/T" + i));
                }

                return SmallPdf.Create(160, links);
            });

            byte[] pdf = await PdfReports.KundliBrihadPdfAsync(
                c.Report,
                PdfFixtures.Branding(expected.GetProperty("branding")),
                PdfFixtures.Options(expected.GetProperty("given")),
                engine);

            // Both prints, the options' CSS included, are the HTML that Node gave its browser.
            Assert.Equal(expected.GetProperty("html").EnumerateArray().Select(h => h.GetString()), engine.Html.Select(PdfFixtures.Sha256));

            // And the print parameters are the ones Node gave it.
            JsonElement node = expected.GetProperty("options");
            PdfOptions options = engine.Options[1]!;
            PageSetup setup = PageSetup.From(options);
            bool running = node.TryGetProperty("displayHeaderFooter", out JsonElement shown) && shown.GetBoolean();
            Assert.Equal(running, setup.DisplayHeaderFooter);
            if (running)
            {
                Assert.Equal(node.GetProperty("headerTemplate").GetString(), setup.Header);
                Assert.Equal(node.GetProperty("footerTemplate").GetString(), setup.Footer);
            }

            Assert.Equal(node.GetProperty("format").GetString(), options.Format.ToString());
            Assert.Equal(node.GetProperty("landscape").GetBoolean(), options.Landscape);
            Assert.Equal(node.GetProperty("printBackground").GetBoolean(), setup.PrintBackground);
            Assert.Equal(node.GetProperty("preferCSSPageSize").GetBoolean(), setup.PreferCssPageSize);
            Assert.Equal(node.TryGetProperty("scale", out JsonElement scale) ? scale.GetDouble() : 1, setup.Scale);
            if (node.TryGetProperty("timeout", out JsonElement timeout))
            {
                // Node's one timeout is Timeout here; printing gets at least as long.
                Assert.Equal(timeout.GetDouble(), setup.LoadTimeout.TotalMilliseconds);
                Assert.True(setup.PrintTimeout >= setup.LoadTimeout);
            }

            JsonElement margin = node.GetProperty("margin");
            Assert.Equal(margin.GetProperty("top").GetString(), options.Margin!.Top);
            Assert.Equal(margin.GetProperty("bottom").GetString(), options.Margin.Bottom);
            Assert.Equal(margin.GetProperty("left").GetString(), options.Margin.Left);
            Assert.Equal(margin.GetProperty("right").GetString(), options.Margin.Right);
            string actual = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pdf)).ToLowerInvariant();
            Assert.Equal(expected.GetProperty("pdf").GetString(), actual);
        }

        [Fact]
        public async Task PrintsTwiceWithTheRealPageNumbersThenBookmarks()
        {
            JsonElement report = PdfFixtures.Example("KundliBrihad", "pa");
            int sections = 0;
            var engine = new RecordingEngine((call, html) =>
            {
                if (call != 1)
                {
                    return SmallPdf.Create((sections * 3) + 5);
                }

                // The first print: every marker is a link. Put marker i on page 3 + 2i.
                var links = new List<(int, string)>();
                while (html.Contains("<a href=\"https://toc.lifeastroapi.invalid/T" + sections + "\"", System.StringComparison.Ordinal))
                {
                    links.Add((3 + (2 * sections), "https://toc.lifeastroapi.invalid/T" + sections));
                    sections++;
                }

                return SmallPdf.Create((sections * 3) + 5, links);
            });

            byte[] pdf = await PdfReports.KundliBrihadPdfAsync(report, new PdfBranding { CompanyName = "Acme & Co" }, engine: engine);

            Assert.True(sections > 10);
            Assert.Equal(2, engine.Html.Count);
            string final = engine.Html[1];
            Assert.DoesNotContain("toc.lifeastroapi.invalid", final, StringComparison.Ordinal);
            Assert.Contains("⟦T0⟧", engine.Html[0], StringComparison.Ordinal);
            Assert.DoesNotContain("⟦T", final, StringComparison.Ordinal); // the locators never reach the customer's PDF
            Assert.Equal(sections, final.Split("<span style=\"color:#fff;font-size:1px;\">&nbsp;</span>").Length - 1);
            string toc = final.Substring(final.IndexOf("class=\"section toc-section\"", System.StringComparison.Ordinal));
            toc = toc.Substring(0, toc.IndexOf("</section>", System.StringComparison.Ordinal));
            Assert.Contains(PdfText.Escape(PdfReports.TocHeading(Js.Table("Locale", "pa"))), toc, StringComparison.Ordinal);
            string[] pages = toc.Split("class=\"toc-page\"").Skip(1)
                .Select(p => p.Substring(p.IndexOf('>') + 1, p.IndexOf('<') - p.IndexOf('>') - 1))
                .ToArray();
            Assert.Equal(Enumerable.Range(0, sections).Select(i => (3 + (2 * i)).ToString()), pages);

            string text = System.Text.Encoding.Latin1.GetString(pdf);
            Assert.Contains("/Type /Outlines", text, StringComparison.Ordinal);
            Assert.Contains("/PageMode /UseOutlines", text, StringComparison.Ordinal);
            Assert.Equal(sections, text.Split("/Title (").Length - 1);

            PdfOptions options = engine.Options[1]!;
            Assert.True(options.DisplayHeaderFooter);
            Assert.Contains("</span> Acme &amp; Co", options.HeaderTemplate, StringComparison.Ordinal);
            Assert.Contains("Generated using LifeAstroAPI", options.FooterTemplate, StringComparison.Ordinal);
            Assert.Equal(("26mm", "16mm", "0mm", "0mm"), (options.Margin!.Top, options.Margin.Bottom, options.Margin.Left, options.Margin.Right));
        }

        [Fact]
        public async Task WithoutPageNumbersItPrintsWithoutATableOfContents()
        {
            JsonElement report = PdfFixtures.Example("KundliBrihad", "en");
            var engine = new RecordingEngine((_, _) => SmallPdf.Create(4));
            await PdfReports.KundliBrihadPdfAsync(report, engine: engine);
            Assert.Equal(2, engine.Html.Count);
            Assert.Equal(PdfPrintCss.Apply(PdfReports.RenderKundliBrihadHtml(report), engine.Options[1]), engine.Html[1]);

            var opaque = new RecordingEngine();
            await PdfReports.KundliBrihadPdfAsync(report, engine: opaque);
            Assert.Equal(PdfPrintCss.Apply(PdfReports.RenderKundliBrihadHtml(report), opaque.Options[1]), opaque.Html[1]);
        }

        [Fact]
        public async Task TheRunningHeaderUsesTheReportsBrandingUnderTheCallers()
        {
            JsonElement report = JsonSerializer.SerializeToElement(new
            {
                Branding = new { CompanyName = "Report Co", FooterText = "Report footer" },
            });
            var engine = new RecordingEngine((_, _) => SmallPdf.Create(2));
            await PdfReports.KundliBrihadPdfAsync(report, engine: engine);
            Assert.Contains("Report Co", engine.Options[0]!.HeaderTemplate, StringComparison.Ordinal);
            Assert.Contains("Report footer", engine.Options[0]!.FooterTemplate, StringComparison.Ordinal);

            var branded = new RecordingEngine((_, _) => SmallPdf.Create(2));
            var margin = new PdfMargin("30mm", "20mm");
            var options = new PdfOptions { Margin = margin };
            await PdfReports.KundliBrihadPdfAsync(report, new PdfBranding { FooterText = "My footer", CompanyName = " " }, options, branded);
            Assert.Contains("Report Co", branded.Options[0]!.HeaderTemplate, StringComparison.Ordinal);
            Assert.Contains("My footer", branded.Options[0]!.FooterTemplate, StringComparison.Ordinal);
            PdfMargin used = branded.Options[0]!.Margin!;
            Assert.Equal(("30mm", "20mm", "0mm", "0mm"), (used.Top, used.Bottom, used.Left, used.Right));
            Assert.Null(options.DisplayHeaderFooter); // the caller's options are not changed
            Assert.Same(margin, options.Margin);
        }

        [Fact]
        public void EveryMarkerIsTheFirstChildOfItsSectionWithNoElementBetween()
        {
            // The CSS that gives the markers no height finds them as span:first-child and span:first-child + span.
            const string Marker = "<span style=\"color:#fff;font-size:1px;\">";
            string[] languages = { "en", "hi", "ml", "pa" };
            foreach (string language in languages)
            {
                string html = PdfReports.RenderKundliBrihadHtml(PdfFixtures.Example("KundliBrihad", language));
                Js tagged = KundliBrihadSections.tagBrihadSectionsForToc(Js.S(html));
                int markers = 0;
                foreach (string section in tagged.Get("html").Str().Split("<section ").Skip(1))
                {
                    string rest = section.Substring(section.IndexOf('>', StringComparison.Ordinal) + 1);
                    if (!rest.Contains("⟦T", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int inSection = 0;
                    while (rest.StartsWith(Marker, StringComparison.Ordinal))
                    {
                        rest = rest.Substring(rest.IndexOf("</span>", StringComparison.Ordinal) + "</span>".Length);
                        inSection++;
                    }

                    Assert.InRange(inSection, 1, 2);
                    Assert.DoesNotContain("⟦T", rest, StringComparison.Ordinal);
                    markers += inSection;
                }

                Assert.Equal(tagged.Get("titles").Iterate().Count(), markers);
            }
        }
    }
}
