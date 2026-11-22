using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using LifeAstroApi.Pdf;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>The bookmark writer against Node's addPdfOutline, and the reader of link targets.</summary>
    public class PdfOutlineTests
    {
        [Fact]
        public void AddsTheSameBytesAsNode()
        {
            foreach (JsonElement row in PdfFixtures.Read("outline.json").EnumerateArray())
            {
                byte[] pdf = row.GetProperty("pdf").GetBytesFromBase64();
                List<PdfOutlineItem> items = row.GetProperty("items").EnumerateArray()
                    .Select(i => new PdfOutlineItem(
                        i.GetProperty("title").GetString()!,
                        i.GetProperty("page").GetInt32(),
                        i.GetProperty("level").GetInt32()))
                    .ToList();

                byte[] outlined = PdfOutline.AddOutline(pdf, items);

                Assert.Equal(Encoding.Latin1.GetString(row.GetProperty("expected").GetBytesFromBase64()), Encoding.Latin1.GetString(outlined));
                Assert.Equal(row.GetProperty("pages").GetInt32(), PdfOutline.PageCount(outlined));
                Assert.Equal(pdf, outlined.Take(pdf.Length).ToArray());
            }
        }

        [Fact]
        public void EveryUtf16UnitOfATitleReadsBack()
        {
            var items = new List<PdfOutlineItem>();
            for (int high = 0; high < 0x100; high++)
            {
                char[] units = Enumerable.Range(0, 0x100).Select(low => (char)((high << 8) | low)).ToArray();
                items.Add(new PdfOutlineItem("T" + new string(units) + "T", 1));
            }

            byte[] pdf = SmallPdf.Create(1);
            Assert.Equal(items.Select(i => i.Title), PdfReader.BookmarkTitles(PdfOutline.AddOutline(pdf, items)));
        }

        [Fact]
        public void MalayalamTitlesKeepTheirScript()
        {
            string[] titles = { "ഭാഗം 1: അടിസ്ഥാനങ്ങൾ", "ഊ", "ലഗ്നം ਕੁੰਡਲੀ" }; // U+0D0A alone is CR LF
            byte[] pdf = SmallPdf.Create(2);
            byte[] outlined = PdfOutline.AddOutline(pdf, titles.Select(t => new PdfOutlineItem(t, 1)).ToArray());
            Assert.Equal(titles, PdfReader.BookmarkTitles(outlined));

            string added = Encoding.Latin1.GetString(outlined, pdf.Length, outlined.Length - pdf.Length);
            Assert.DoesNotContain("\r", added, StringComparison.Ordinal);
            Assert.Contains("\\r", added, StringComparison.Ordinal);
            Assert.Contains("\\n", added, StringComparison.Ordinal);
        }

        [QpdfFact]
        public void QpdfReadsTheMalayalamTitle()
        {
            string[] titles = { "ഭാഗം 1: അടിസ്ഥാനങ്ങൾ", "ഊ ലഗ്നം ਕੁੰਡਲੀ" };
            byte[] outlined = PdfOutline.AddOutline(SmallPdf.Create(2), titles.Select(t => new PdfOutlineItem(t, 2)).ToArray());
            string file = Path.Combine(Path.GetTempPath(), "lifeastro-test-outline-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                File.WriteAllBytes(file, outlined);
                var start = new ProcessStartInfo(Qpdf.Executable!)
                {
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                };
                foreach (string argument in new[] { "--json=1", "--json-key=outlines", file })
                {
                    start.ArgumentList.Add(argument);
                }

                using Process qpdf = Process.Start(start)!;
                string json = qpdf.StandardOutput.ReadToEnd();
                qpdf.WaitForExit();
                Assert.Equal(0, qpdf.ExitCode);
                using JsonDocument read = JsonDocument.Parse(json);
                Assert.Equal(titles, read.RootElement.GetProperty("outlines").EnumerateArray().Select(o => o.GetProperty("title").GetString()));
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void TheTrailerKeepsTheDocumentInformation()
        {
            byte[] once = PdfOutline.AddOutline(SmallPdf.Create(2, title: "Brihad Kundli"), new[] { new PdfOutlineItem("First", 1) });
            Assert.Contains("/Info 5 0 R", PdfReader.LastTrailer(once), StringComparison.Ordinal);

            byte[] twice = PdfOutline.AddOutline(once, new[] { new PdfOutlineItem("Second", 2) });
            Assert.Contains("/Info 5 0 R", PdfReader.LastTrailer(twice), StringComparison.Ordinal);

            byte[] untitled = PdfOutline.AddOutline(SmallPdf.Create(2), new[] { new PdfOutlineItem("First", 1) });
            Assert.DoesNotContain("/Info", PdfReader.LastTrailer(untitled), StringComparison.Ordinal);
        }

        [Fact]
        public void EveryByteOfTheCatalogIsKept()
        {
            string binary = new string(Enumerable.Range(0x80, 0x80).Select(b => (char)b).ToArray());
            string pdf = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Lang (" + binary + ") >>\nendobj\n"
                + "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
                + "3 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\n"
                + "trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n";
            byte[] source = Encoding.Latin1.GetBytes(pdf);
            byte[] outlined = PdfOutline.AddOutline(source, new[] { new PdfOutlineItem("x", 1) });
            Assert.Contains("/Lang (" + binary + ")", Encoding.Latin1.GetString(outlined, source.Length, outlined.Length - source.Length), StringComparison.Ordinal);
        }

        [Fact]
        public void NothingToAddGivesTheSameArray()
        {
            byte[] pdf = SmallPdf.Create(2);
            Assert.Same(pdf, PdfOutline.AddOutline(pdf, new List<PdfOutlineItem>()));
            Assert.Same(pdf, PdfOutline.AddOutline(pdf, new[] { new PdfOutlineItem("  ", 1) }));
        }

        [Fact]
        public void ASecondOutlineReplacesTheFirst()
        {
            byte[] once = PdfOutline.AddOutline(SmallPdf.Create(2), new[] { new PdfOutlineItem("First", 1) });
            string twice = Encoding.Latin1.GetString(PdfOutline.AddOutline(once, new[] { new PdfOutlineItem("Second", 2) }));
            string catalog = twice.Substring(twice.LastIndexOf("1 0 obj\n<< /Type /Catalog", StringComparison.Ordinal));
            catalog = catalog.Substring(0, catalog.IndexOf("endobj", StringComparison.Ordinal));
            Assert.Equal(1, catalog.Split("/Outlines ").Length - 1);
            Assert.Equal(1, catalog.Split("/PageMode").Length - 1);
        }

        [Fact]
        public void ThePageTreeGivesTheOrderNotTheFile()
        {
            // Page object 4 comes first in the file and second in the tree.
            string pdf = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"
                + "4 0 obj\n<< /Type /Page /Parent 2 0 R /Annots [5 0 R] >>\nendobj\n"
                + "3 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\n"
                + "2 0 obj\n<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>\nendobj\n"
                + "5 0 obj\n<< /Subtype /Link /A << /S /URI /URI (https://example.invalid/livestream) >> >>\nendobj\n"
                + "trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n";
            Dictionary<string, int> pages = PdfOutline.LinkTargetPages(Encoding.Latin1.GetBytes(pdf));
            Assert.Equal(2, pages["https://example.invalid/livestream"]);
        }

        [Fact]
        public void LinkTargetsKeepTheFirstPage()
        {
            byte[] pdf = SmallPdf.Create(4, new[] { (2, "https://a.invalid/x"), (3, "https://a.invalid/x"), (4, "https://a.invalid/y") });
            Dictionary<string, int> pages = PdfOutline.LinkTargetPages(pdf);
            Assert.Equal(2, pages["https://a.invalid/x"]);
            Assert.Equal(4, pages["https://a.invalid/y"]);
        }

        [Fact]
        public void BytesInsideAStreamAreNotObjects()
        {
            string text = Encoding.Latin1.GetString(SmallPdf.Create(1)).Replace(
                "xref\n0 ",
                "8 0 obj\n<< /Length 30 >>\nstream\n99 0 obj << /Type /Page >>\nendstream\nendobj\nxref\n0 ");
            Assert.Equal(1, PdfOutline.PageCount(Encoding.Latin1.GetBytes(text)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not a pdf")]
        [InlineData("trailer << /Root 1 0 R >>")]
        public void SomethingElseThanAPdfThrows(string text)
        {
            var error = Assert.Throws<LifeAstroPdfException>(() => PdfOutline.PageCount(Encoding.Latin1.GetBytes(text)));
            Assert.Equal("invalid_pdf", error.Code);
        }
    }
}
