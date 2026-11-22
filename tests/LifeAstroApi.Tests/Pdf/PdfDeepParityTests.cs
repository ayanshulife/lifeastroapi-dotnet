using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LifeAstroApi.Pdf;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>
    /// The reference every SDK shares: 432 full, untrimmed reports (3 people x 12 endpoints x 10 languages,
    /// and branded), rendered byte-identical to Node. Skipped where pdf_work/ is not present, as on GitHub.
    /// </summary>
    public class PdfDeepParityTests
    {
        public static IEnumerable<object[]> Cases()
        {
            if (PdfWork.Directory is null)
            {
                yield break;
            }

            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(PdfWork.Directory, "manifest.json")));
            foreach (JsonElement c in manifest.RootElement.GetProperty("cases").EnumerateArray())
            {
                yield return new object[]
                {
                    c.GetProperty("id").GetString()!,
                    c.GetProperty("input").GetString()!,
                    c.GetProperty("renderer").GetString()!,
                    c.GetProperty("branding").GetRawText(),
                    c.GetProperty("sha256").GetString()!,
                };
            }
        }

        [PdfWorkTheory]
        [MemberData(nameof(Cases))]
        public void RendersTheFullReportLikeNode(string id, string input, string renderer, string brandingJson, string sha256)
        {
            string work = PdfWork.Directory!;
            using JsonDocument report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(work, "inputs", input)));
            using JsonDocument branding = JsonDocument.Parse(brandingJson);
            string html = PdfFixtures.Renderers[renderer](report.RootElement, PdfFixtures.Branding(branding.RootElement));
            if (PdfFixtures.Sha256(html) == sha256)
            {
                return;
            }

            string expected = Path.Combine(work, "expected", id + ".html");
            string detail = File.Exists(expected) ? PdfFixtures.FirstDifference(File.ReadAllText(expected), html) : "differs";
            Assert.Fail(id + " " + detail);
        }

        [PdfWorkTheory]
        [InlineData("hi")]
        [InlineData("mr")]
        [InlineData("gu")]
        [InlineData("bn")]
        [InlineData("te")]
        [InlineData("ta")]
        [InlineData("kn")]
        [InlineData("ml")]
        [InlineData("pa")]
        public void EveryLanguageDrawsThePlanetsThatEnglishDraws(string language)
        {
            string inputs = Path.Combine(PdfWork.Directory!, "inputs");
            foreach (string person in new[] { "chennai", "kolkata", "mumbai" })
            {
                string english = PdfReports.RenderKundliBrihadHtml(
                    File.ReadAllText(Path.Combine(inputs, person + ".kundli-brihad.en.json")));
                string translated = PdfReports.RenderKundliBrihadHtml(
                    File.ReadAllText(Path.Combine(inputs, person + ".kundli-brihad." + language + ".json")));

                Assert.True(ChartReader.Planets(english).Count >= 135, person);
                Assert.Equal(ChartReader.Planets(english), ChartReader.Planets(translated));
                Assert.Equal(ChartReader.Glyphs(english), ChartReader.Glyphs(translated));
            }
        }

        [PdfWorkFact]
        public void TheReferenceHasAll432Cases() => Assert.Equal(432, Cases().Count());
    }
}
