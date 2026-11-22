using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Pdf;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>
    /// Every renderer gives byte for byte the HTML of the Node.js SDK's templates: the inputs of Node's own
    /// PDF tests, the example responses in all ten languages, plain and branded, and edge cases
    /// (tool/pdf-goldens.mjs).
    /// </summary>
    public class PdfGoldenTests
    {
        private static readonly string[] Languages = { "en", "hi", "mr", "gu", "bn", "te", "ta", "kn", "ml", "pa" };

        public static IEnumerable<object[]> CaseIds() => PdfFixtures.Cases.Keys.Select(id => new object[] { id });

        public static IEnumerable<object[]> GoldenIds() =>
            PdfFixtures.Read("cases.json").GetProperty("goldens").EnumerateArray().Select(id => new object[] { id.GetString()! });

        [Theory]
        [MemberData(nameof(CaseIds))]
        public void RendersLikeNode(string id)
        {
            GoldenCase c = PdfFixtures.Cases[id];
            Assert.Equal(c.Sha256, PdfFixtures.Sha256(PdfFixtures.Renderers[c.Renderer](c.Report, c.Branding)));
        }

        [Theory]
        [MemberData(nameof(GoldenIds))]
        public void GivesTheStoredHtmlOfNode(string id)
        {
            byte[] file = File.ReadAllBytes(Path.Combine(PdfFixtures.Directory, "golden", id + ".html"));
            GoldenCase c = PdfFixtures.Cases[id];
            string html = PdfFixtures.Renderers[c.Renderer](c.Report, c.Branding);
            string expected = new UTF8Encoding(false, true).GetString(file);
            Assert.True(html == expected, id + " " + PdfFixtures.FirstDifference(expected, html));
            Assert.Equal(file, Encoding.UTF8.GetBytes(html));
        }

        [Fact]
        public void EveryRendererIsCoveredInEveryLanguage()
        {
            Assert.Equal(10, PdfFixtures.Renderers.Count);
            foreach (string renderer in PdfFixtures.Renderers.Keys)
            {
                string key = renderer == "Horoscope" ? "language" : "Locale";
                var seen = new HashSet<string>(PdfFixtures.Cases.Values
                    .Where(c => c.Renderer == renderer && c.Report.TryGetProperty(key, out _))
                    .Select(c => c.Report.GetProperty(key).ToString()));
                Assert.All(Languages, language => Assert.Contains(language, seen));
                Assert.Contains(PdfFixtures.Cases.Values, c => c.Renderer == renderer && c.Branding != null);
            }
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("tr-TR")]
        [InlineData("ar-SA")]
        [InlineData("fa-IR")]
        [InlineData("th-TH")]
        [InlineData("hi-IN")]
        public void TheCultureNeverChangesTheHtml(string culture)
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            CultureInfo beforeUi = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                CultureInfo.CurrentUICulture = new CultureInfo(culture);
                foreach (GoldenCase c in PdfFixtures.Cases.Values)
                {
                    string html = PdfFixtures.Renderers[c.Renderer](c.Report, c.Branding);
                    Assert.True(c.Sha256 == PdfFixtures.Sha256(html), c.Id + " differs under " + culture);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
                CultureInfo.CurrentUICulture = beforeUi;
            }
        }

        [Fact]
        public void TheTimeZoneOfTheMachineIsNotUsed()
        {
            // A time without an offset is UTC here, on every machine.
            Assert.Contains("2026-11-22 10:05 UTC", PdfReports.RenderKundliLiteHtml("{\"GeneratedAt\":\"2026-11-22T10:05:00\"}"), StringComparison.Ordinal);
            Assert.Contains("2026-11-22 04:35 UTC", PdfReports.RenderKundliLiteHtml("{\"GeneratedAt\":\"2026-11-22T10:05:00+05:30\"}"), StringComparison.Ordinal);
        }

        [Fact]
        public void RendersTheSameFromSeveralThreads()
        {
            GoldenCase[] cases = PdfFixtures.Cases.Values.ToArray();
            int failures = 0;
            Parallel.For(0, cases.Length * 3, i =>
            {
                GoldenCase c = cases[i % cases.Length];
                if (PdfFixtures.Sha256(PdfFixtures.Renderers[c.Renderer](c.Report, c.Branding)) != c.Sha256)
                {
                    Interlocked.Increment(ref failures);
                }
            });
            Assert.Equal(0, failures);
        }
    }
}
