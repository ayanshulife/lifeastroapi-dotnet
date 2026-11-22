using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Pdf;
using LifeAstroApi.Pdf.Engine;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>One browser for the tests that can share one.</summary>
    public sealed class SharedBrowser : IAsyncLifetime
    {
        private ChromePdfEngine? _engine;

        internal ChromePdfEngine Engine => _engine ??= new ChromePdfEngine(TestBrowser.Options());

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (_engine != null)
            {
                await _engine.DisposeAsync();
            }
        }
    }

    /// <summary>HTML to PDF with a real headless browser. Skipped with a reason where none is installed.</summary>
    public class ChromePdfEngineTests : IClassFixture<SharedBrowser>
    {
        private static readonly PdfOptions Patient = new PdfOptions { Timeout = TimeSpan.FromMinutes(2) };

        private readonly SharedBrowser _shared;

        public ChromePdfEngineTests(SharedBrowser shared)
        {
            _shared = shared;
        }

        public static IEnumerable<object[]> Renderers() => PdfFixtures.Renderers.Keys.Select(name => new object[] { name });

        private static string Pages(int count) => "<!doctype html><html><body>"
            + string.Concat(Enumerable.Range(1, count).Take(count - 1)
                .Select(i => "<div style=\"break-after:page\">page " + i + "</div>"))
            + "<div>last</div></body></html>";

        private static void AssertPdf(byte[] pdf, int atLeastPages = 1)
        {
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
            Assert.True(PdfOutline.PageCount(pdf) >= atLeastPages);
            string text = Encoding.Latin1.GetString(pdf).TrimEnd();
            Assert.EndsWith("%%EOF", text, StringComparison.Ordinal);
        }

        [BrowserTheory]
        [MemberData(nameof(Renderers))]
        public async Task EveryReportPrints(string renderer)
        {
            JsonElement report = PdfFixtures.Example(renderer, "pa");
            byte[] pdf = await PdfFixtures.Printers[renderer](
                report, new PdfBranding { CompanyName = "Acme Astro" }, Patient, _shared.Engine, CancellationToken.None);
            AssertPdf(pdf, 2); // the cover is a page of its own
        }

        [BrowserFact]
        public async Task HonoursPaperSizeMarginsAndLandscape()
        {
            byte[] pdf = await _shared.Engine.HtmlToPdfAsync(
                "<!doctype html><html><body><p>x</p></body></html>",
                new PdfOptions { Format = PdfPaperFormat.Letter, Landscape = true, Margin = new PdfMargin("10mm", "1cm") });
            // Letter landscape: 792 x 612 points.
            Assert.Contains("/MediaBox [0 0 792 612]", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        }

        [BrowserFact]
        public async Task AMarginWinsOverPageCssSideBySide()
        {
            const string Html = "<!doctype html><html><head><style>@page { size: A4; margin: 5mm; }</style></head>"
                + "<body style=\"margin:0\"><a href=\"https://corner.invalid/\" style=\"display:block;height:40px\">x</a></body></html>";
            byte[] plain = await _shared.Engine.HtmlToPdfAsync(Html, Patient);
            PdfInk.Box was = PdfInk.Links(plain).Single();
            Assert.InRange(was.Left, 13.2, 15.2); // 5mm
            Assert.InRange(was.Top, 826.2, 829.2);

            var options = new PdfOptions { Timeout = Patient.Timeout, Margin = new PdfMargin { Top = "40mm", Left = "0" } };
            PdfInk.Box now = PdfInk.Links(await _shared.Engine.HtmlToPdfAsync(Html, options)).Single();
            Assert.InRange(now.Left, -1, 1);
            Assert.InRange(now.Top, 727, 730.5); // 40mm
            Assert.InRange(now.Right, was.Right - 1, was.Right + 1); // not set: the page's own stays
        }

        [BrowserTheory]
        [InlineData(PdfPaperFormat.A4, true, 841.9, 595.3)]
        [InlineData(PdfPaperFormat.A5, false, 419.5, 595.3)]
        [InlineData(PdfPaperFormat.Letter, false, 612, 792)]
        public async Task FormatAndLandscapeWinOverAReportsPageSize(PdfPaperFormat format, bool landscape, double width, double height)
        {
            var options = new PdfOptions { Timeout = Patient.Timeout, Format = format, Landscape = landscape };
            byte[] pdf = await PdfReports.KundliLitePdfAsync(PdfFixtures.Example("KundliLite", "en"), options: options, engine: _shared.Engine);
            (double printedWidth, double printedHeight) = PdfInk.Paper(pdf);
            Assert.InRange(printedWidth, width - 1.5, width + 1.5);
            Assert.InRange(printedHeight, height - 1.5, height + 1.5);
        }

        [BrowserFact]
        public async Task PrintBackgroundFalsePrintsNoFillEvenWhereTheDocumentAsksForExactColours()
        {
            const string Html = "<!doctype html><html><head><style>html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }</style></head>"
                + "<body><div style=\"background:#00ff00;height:100px\"></div><p>x</p></body></html>";
            const string Green = "0 1 0 rg";
            Assert.Contains(Green, PdfInk.Content(await _shared.Engine.HtmlToPdfAsync(Html, Patient)), StringComparison.Ordinal);
            var economy = new PdfOptions { Timeout = Patient.Timeout, PrintBackground = false };
            Assert.DoesNotContain(Green, PdfInk.Content(await _shared.Engine.HtmlToPdfAsync(Html, economy)), StringComparison.Ordinal);
        }

        [BrowserFact]
        public async Task PrintBackgroundFalsePaintsNothingWhiteOverTheWatermark()
        {
            // A see-through fill over a watermark: economy printing alone paints it solid white.
            const string Html = "<!doctype html><html><head><style>html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }"
                + "</style></head><body><div style=\"position:fixed;z-index:-1;font-size:80pt;color:#ff0000\">MARK</div>"
                + "<div style=\"background:rgba(0,0,255,0.1);height:300px\"></div><p>x</p></body></html>";
            var economy = new PdfOptions { Timeout = Patient.Timeout, PrintBackground = false };
            string ink = PdfInk.Content(await _shared.Engine.HtmlToPdfAsync(Html, economy));
            int mark = ink.IndexOf("1 0 0 rg", StringComparison.Ordinal);
            Assert.True(mark >= 0, "the watermark did not print");
            Assert.DoesNotContain("1 1 1 rg", ink.Substring(mark), StringComparison.Ordinal); // no white after it
        }

        [BrowserFact]
        public async Task TheCallersFooterTakesThePlaceOfTheReportsFooterLine()
        {
            const string Red = "1 0 0 rg";
            const string Html = "<!doctype html><html><head><style>@page { margin: 25mm; "
                + "@bottom-center { content: \"the report's\"; color: #ff0000; } }</style></head><body><p>x</p></body></html>";
            Assert.Contains(Red, PdfInk.Content(await _shared.Engine.HtmlToPdfAsync(Html, Patient)), StringComparison.Ordinal);

            var options = new PdfOptions
            {
                Timeout = Patient.Timeout,
                DisplayHeaderFooter = true,
                FooterTemplate = "<span style=\"font-size:9px;color:#0000ff\">mine</span>",
                Margin = new PdfMargin { Top = "25mm", Bottom = "25mm" },
            };
            Assert.DoesNotContain(Red, PdfInk.Content(await _shared.Engine.HtmlToPdfAsync(Html, options)), StringComparison.Ordinal);
        }

        [BrowserFact]
        public async Task AWatermarkAddsNoPage()
        {
            JsonElement report = PdfFixtures.Example("KundliDetailed", "ml");
            byte[] plain = await PdfReports.KundliDetailedPdfAsync(report, options: Patient, engine: _shared.Engine);
            byte[] marked = await PdfReports.KundliDetailedPdfAsync(
                report, new PdfBranding { WatermarkText = "DRAFT FOR REVIEW ONLY" }, Patient, _shared.Engine);
            Assert.Equal(PdfOutline.PageCount(plain), PdfOutline.PageCount(marked));
        }

        [BrowserFact]
        public async Task LinkAnnotationsGiveThePageOfEachMarker()
        {
            var html = new StringBuilder("<!doctype html><html><body>");
            string[] titles = { "पहला", "Second", "తెలుగు" };
            for (int i = 0; i < titles.Length; i++)
            {
                html.Append("<section style=\"page-break-before:always\"><h1>").Append(titles[i]).Append("</h1>")
                    .Append("<a href=\"https://toc.lifeastroapi.invalid/T").Append(i).Append("\">⟦T").Append(i).Append("⟧</a></section>");
            }

            byte[] pdf = await _shared.Engine.HtmlToPdfAsync(html.Append("</body></html>").ToString());
            Dictionary<string, int> pages = PdfOutline.LinkTargetPages(pdf);
            Assert.Equal(new[] { 1, 2, 3 }, Enumerable.Range(0, 3).Select(i => pages["https://toc.lifeastroapi.invalid/T" + i]));

            byte[] outlined = PdfOutline.AddOutline(pdf, new[]
            {
                new PdfOutlineItem("भाग 1: जन्म", 1),
                new PdfOutlineItem("Planets (D1)", 2, 2),
                new PdfOutlineItem("తెలుగు", 9),
            });
            Assert.Equal(3, PdfOutline.PageCount(outlined));
            Assert.Contains("/PageMode /UseOutlines", Encoding.Latin1.GetString(outlined), StringComparison.Ordinal);
        }

        [BrowserFact]
        public async Task BrihadHasATableOfContentsAndBookmarks()
        {
            JsonElement report = PdfFixtures.Example("KundliBrihad", "ml");
            byte[] pdf = await PdfReports.KundliBrihadPdfAsync(
                report, new PdfBranding { CompanyName = "Acme Astro" }, Patient, _shared.Engine);
            AssertPdf(pdf, 10);
            Assert.Contains("/Type /Outlines", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
            // The links that located the sections are only in the first print.
            Assert.Empty(PdfOutline.LinkTargetPages(pdf));

            List<string> titles = PdfReader.BookmarkTitles(pdf);
            Assert.Contains(titles, title => title.StartsWith("ഭാഗം 1", StringComparison.Ordinal));
            Assert.Contains("/Info ", PdfReader.LastTrailer(pdf), StringComparison.Ordinal); // so readers still show the title
        }

        [BrowserTheory]
        [InlineData("en")]
        [InlineData("pa")]
        public async Task PartOneStartsOnThePageAfterTheContents(string language)
        {
            var engine = new ForwardingEngine(_shared.Engine);
            await PdfReports.KundliBrihadPdfAsync(PdfFixtures.Example("KundliBrihad", language), options: Patient, engine: engine);
            Assert.Equal(2, engine.Prints.Count);
            int partOne = PdfOutline.LinkTargetPages(engine.Prints[0].Pdf)["https://toc.lifeastroapi.invalid/T0"];

            // The final document cut after its contents: the cover and the contents alone.
            string final = engine.Prints[1].Html;
            int contents = final.IndexOf("<section class=\"section toc-section\">", StringComparison.Ordinal);
            int end = final.IndexOf("</section>", contents, StringComparison.Ordinal) + "</section>".Length;
            byte[] front = await _shared.Engine.HtmlToPdfAsync(final.Substring(0, end) + "</body></html>", engine.Prints[1].Options);

            Assert.Equal(PdfOutline.PageCount(front) + 1, partOne);

            // The markers take no height: the contents add their own pages and move nothing else.
            byte[] plain = await _shared.Engine.HtmlToPdfAsync(
                PdfReports.RenderKundliBrihadHtml(PdfFixtures.Example("KundliBrihad", language)), engine.Prints[1].Options);
            byte[] cover = await _shared.Engine.HtmlToPdfAsync(
                final.Substring(0, contents) + "</body></html>", engine.Prints[1].Options);
            int contentsPages = PdfOutline.PageCount(front) - PdfOutline.PageCount(cover);
            Assert.Equal(PdfOutline.PageCount(plain) + contentsPages, PdfOutline.PageCount(engine.Prints[1].Pdf));
            Assert.Equal(PdfOutline.PageCount(engine.Prints[0].Pdf), PdfOutline.PageCount(engine.Prints[1].Pdf));
        }

        [BrowserFact]
        public async Task SafeUnderConcurrentUse()
        {
            int[] wanted = Enumerable.Range(1, 12).ToArray();
            byte[][] pdfs = await Task.WhenAll(wanted.Select(n => _shared.Engine.HtmlToPdfAsync(Pages(n), Patient)));
            Assert.Equal(wanted, pdfs.Select(PdfOutline.PageCount));
        }

        [BrowserFact]
        public async Task AConnectedEngineLeavesTheBrowserRunning()
        {
            await using var owner = new ChromePdfEngine(TestBrowser.Options());
            await owner.StartAsync();
            string[] lines = File.ReadAllLines(Path.Combine(owner.ProfileDirectory!, "DevToolsActivePort"));
            var options = new ChromePdfEngineOptions { WebSocketDebuggerUrl = "ws://127.0.0.1:" + lines[0] + lines[1] };
            await using (var guest = new ChromePdfEngine(options))
            {
                AssertPdf(await guest.HtmlToPdfAsync(Pages(2)), 2);
                Assert.Null(guest.ProcessId);
            }

            AssertPdf(await owner.HtmlToPdfAsync(Pages(1)));
        }

        [BrowserFact]
        public async Task DisposeStopsTheBrowserAndDeletesTheProfile()
        {
            var engine = new ChromePdfEngine(TestBrowser.Options());
            AssertPdf(await engine.HtmlToPdfAsync(Pages(1)));
            int pid = engine.ProcessId!.Value;
            string profile = engine.ProfileDirectory!;
            Assert.True(Directory.Exists(profile));
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Equal(
                    UnixFileMode.None,
                    new DirectoryInfo(profile).UnixFileMode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
            }

            await engine.DisposeAsync();
            await engine.DisposeAsync();

            Assert.False(Directory.Exists(profile), "the profile is left behind: " + profile);
            Assert.False(IsRunning(pid), "the browser still runs: " + pid);
            var late = await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync(Pages(1)));
            Assert.Equal("engine_closed", late.Code);
            Assert.Equal("engine_closed", (await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.StartAsync())).Code);
        }

        [BrowserFact]
        public async Task DisposeDuringAPrintEndsIt()
        {
            var engine = new ChromePdfEngine(TestBrowser.Options());
            await engine.StartAsync();
            string profile = engine.ProfileDirectory!;
            Task<byte[]> print = engine.HtmlToPdfAsync(Endless());
            await Task.Delay(300);
            await engine.DisposeAsync();
            Assert.Equal("engine_closed", (await Assert.ThrowsAsync<LifeAstroPdfException>(() => print)).Code);
            Assert.False(Directory.Exists(profile));
        }

        [BrowserTheory]
        [InlineData(false, -1)]
        [InlineData(false, 0)]
        [InlineData(false, 400)]
        [InlineData(true, 150)]
        public async Task DisposeWhilePrintsRunSettlesEveryCall(bool synchronous, int afterMilliseconds)
        {
            using var trap = new UnobservedTrap();
            var engine = new ChromePdfEngine(TestBrowser.Options());
            if (afterMilliseconds >= 0)
            {
                await engine.StartAsync(); // at -1 the dispose lands in the launch
            }

            string? profile = engine.ProfileDirectory;
            var forever = new PdfOptions { Timeout = Timeout.InfiniteTimeSpan }; // only the dispose can end these
            var prints = new List<Task<byte[]>>();
            for (int i = 0; i < 6; i++)
            {
                prints.Add(engine.HtmlToPdfAsync(Endless(), forever)); // four in tabs, two waiting
            }

            prints.Add(PdfReports.KundliBrihadPdfAsync(PdfFixtures.Example("KundliBrihad", "en"), options: forever, engine: engine));
            await Task.Delay(Math.Max(0, afterMilliseconds));

            if (synchronous)
            {
                await Task.Run(() => engine.Dispose()).WaitAsync(TimeSpan.FromMinutes(1));
            }
            else
            {
                await engine.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMinutes(1));
            }

            foreach (Task<byte[]> print in prints)
            {
                var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => print.WaitAsync(TimeSpan.FromSeconds(30)));
                Assert.Equal("engine_closed", error.Code);
            }

            Assert.False(profile != null && Directory.Exists(profile));
            Assert.Null(engine.ProcessId);
            await trap.AssertNothingAsync();
        }

        [BrowserFact]
        public async Task ABrowserCrashMidPrintSettlesEveryCall()
        {
            using var trap = new UnobservedTrap();
            var engine = new ChromePdfEngine(TestBrowser.Options());
            await engine.StartAsync();
            string profile = engine.ProfileDirectory!;
            var forever = new PdfOptions { Timeout = Timeout.InfiniteTimeSpan };
            Task<byte[]>[] prints = Enumerable.Range(0, 4).Select(_ => engine.HtmlToPdfAsync(Endless(), forever)).ToArray();
            await Task.Delay(500);

            using (Process browser = Process.GetProcessById(engine.ProcessId!.Value))
            {
                browser.Kill(entireProcessTree: true); // the browser this test started
            }

            foreach (Task<byte[]> print in prints)
            {
                var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => print.WaitAsync(TimeSpan.FromSeconds(30)));
                Assert.Equal("browser_error", error.Code);
            }

            await engine.DisposeAsync();
            Assert.False(Directory.Exists(profile));
            await trap.AssertNothingAsync();
        }

        [BrowserFact]
        public async Task ACrashedTabFailsItsPrintAtOnce()
        {
            using var trap = new UnobservedTrap();
            await using var engine = new ChromePdfEngine(TestBrowser.Options());
            await engine.StartAsync();
            var forever = new PdfOptions { Timeout = Timeout.InfiniteTimeSpan };
            Task<byte[]> print = engine.HtmlToPdfAsync(Endless(), forever); // no timeout would ever end it
            await Task.Delay(700);

            // A second connection crashes the process of every tab.
            string[] address = File.ReadAllLines(Path.Combine(engine.ProfileDirectory!, "DevToolsActivePort"));
            DevToolsConnection saboteur = await DevToolsConnection.ConnectAsync(
                new Uri("ws://127.0.0.1:" + address[0] + address[1]), true, CancellationToken.None);
            try
            {
                JsonElement targets = await saboteur.SendAsync("Target.getTargets", null, null, CancellationToken.None);
                foreach (JsonElement target in targets.GetProperty("targetInfos").EnumerateArray())
                {
                    if (target.GetProperty("type").GetString() != "page")
                    {
                        continue;
                    }

                    string id = target.GetProperty("targetId").GetString()!;
                    JsonElement attached = await saboteur.SendAsync(
                        "Target.attachToTarget",
                        json =>
                        {
                            json.WriteString("targetId", id);
                            json.WriteBoolean("flatten", true);
                        },
                        null,
                        CancellationToken.None);
                    using var moment = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        await saboteur.SendAsync("Page.crash", null, attached.GetProperty("sessionId").GetString(), moment.Token);
                    }
                    catch (Exception e) when (e is LifeAstroPdfException || e is OperationCanceledException)
                    {
                        // A crashed tab does not answer.
                    }
                }

                var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => print.WaitAsync(TimeSpan.FromSeconds(20)));
                Assert.Equal("browser_error", error.Code);
            }
            finally
            {
                saboteur.Close();
            }

            AssertPdf(await engine.HtmlToPdfAsync(Pages(1), Patient));
            await trap.AssertNothingAsync();
        }

        [BrowserFact]
        public async Task AKilledWrapperLeavesNoBrowserOrProfile()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // no wrapper there
            }

            var engine = new ChromePdfEngine(TestBrowser.Options());
            await engine.StartAsync();
            string profile = engine.ProfileDirectory!;

            using (Process shell = Process.GetProcessById(engine.ProcessId!.Value))
            {
                shell.Kill(); // the shell alone: the browser lives on
                await shell.WaitForExitAsync();
            }

            // It still prints, and a browser that has printed is the one that writes its profile as it quits.
            Task<byte[]>[] prints =
            {
                PdfReports.KundliBrihadPdfAsync(PdfFixtures.Example("KundliBrihad", "en"), options: Patient, engine: engine),
                engine.HtmlToPdfAsync(Pages(250), Patient),
                engine.HtmlToPdfAsync(Pages(250), Patient),
            };
            await Task.WhenAll(prints);
            await engine.DisposeAsync();
            Assert.False(Directory.Exists(profile));
            await Task.Delay(1500); // a browser still shutting down would write it again
            Assert.False(Directory.Exists(profile), "the browser outlived the engine and wrote its profile again: " + profile);
        }

        [BrowserFact]
        public async Task DisposingAConnectedEngineLeavesNoTabsThere()
        {
            await using var owner = new ChromePdfEngine(TestBrowser.Options());
            await owner.StartAsync();
            string[] address = File.ReadAllLines(Path.Combine(owner.ProfileDirectory!, "DevToolsActivePort"));
            var options = new ChromePdfEngineOptions { WebSocketDebuggerUrl = "ws://127.0.0.1:" + address[0] + address[1] };
            var guest = new ChromePdfEngine(options);
            var forever = new PdfOptions { Timeout = Timeout.InfiniteTimeSpan };
            Task<byte[]>[] prints = Enumerable.Range(0, 3).Select(_ => guest.HtmlToPdfAsync(Endless(), forever)).ToArray();
            await Task.Delay(700);
            await guest.DisposeAsync();
            foreach (Task<byte[]> print in prints)
            {
                Assert.Equal("engine_closed", (await Assert.ThrowsAsync<LifeAstroPdfException>(() => print)).Code);
            }

            using var http = new HttpClient();
            int tabs = int.MaxValue;
            for (int attempt = 0; attempt < 50 && tabs > 1; attempt++)
            {
                await Task.Delay(100);
                using JsonDocument list = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:" + address[0] + "/json/list"));
                tabs = list.RootElement.EnumerateArray().Count(tab => tab.GetProperty("type").GetString() == "page");
            }

            Assert.True(tabs <= 1, tabs + " tabs are left in the browser");
        }

        [BrowserFact]
        public void DisposeDoesNotDeadlockWithAWaitingContext()
        {
            var engine = new ChromePdfEngine(TestBrowser.Options());
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // A UI thread or classic ASP.NET: continuations posted here would never run.
                    SynchronizationContext.SetSynchronizationContext(new NeverRuns());
#pragma warning disable xUnit1031 // blocking is what is tested
                    engine.StartAsync().GetAwaiter().GetResult();
                    AssertPdf(engine.HtmlToPdfAsync(Pages(1)).GetAwaiter().GetResult());
#pragma warning restore xUnit1031
                    engine.Dispose();
                }
                catch (Exception e)
                {
                    error = e;
                }
            });
            thread.IsBackground = true;
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "deadlock");
            Assert.Null(error);
        }

        [BrowserFact]
        public async Task CancellationClosesTheTabAndTheEngineStaysUsable()
        {
            await using var engine = new ChromePdfEngine(TestBrowser.Options());
            await engine.StartAsync();
            int pid = engine.ProcessId!.Value;
            using var cancel = new CancellationTokenSource(500);
            var watch = Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.HtmlToPdfAsync(Endless(), null, cancel.Token));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), "cancelling took " + watch.Elapsed);
            AssertPdf(await engine.HtmlToPdfAsync(Pages(1)));
            Assert.Equal(pid, engine.ProcessId);

            string port = File.ReadAllLines(Path.Combine(engine.ProfileDirectory!, "DevToolsActivePort"))[0];
            using var http = new HttpClient();
            int tabs = int.MaxValue;
            for (int attempt = 0; attempt < 50 && tabs > 1; attempt++)
            {
                await Task.Delay(100);
                using JsonDocument list = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:" + port + "/json/list"));
                tabs = list.RootElement.EnumerateArray().Count(tab => tab.GetProperty("type").GetString() == "page");
            }

            Assert.True(tabs <= 1, tabs + " tabs are open after cancelling"); // the one it starts with
        }

        [BrowserFact]
        public async Task AWrapperThatDropsThePipeStillWorks()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // no pipe there
            }

            string wrapper = Path.Combine(Path.GetTempPath(), "lifeastro-test-wrapper-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(wrapper, "#!/bin/sh\nexec \"" + TestBrowser.Executable + "\" \"$@\" 3<&- 4>&-\n");
                File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                ChromePdfEngineOptions options = TestBrowser.Options();
                options.ExecutablePath = wrapper;
                await using var engine = new ChromePdfEngine(options);
                AssertPdf(await engine.HtmlToPdfAsync(Pages(1)));
            }
            finally
            {
                File.Delete(wrapper);
            }
        }

        [BrowserFact]
        public async Task LoadingHasATimeout()
        {
            await using var engine = new ChromePdfEngine(TestBrowser.Options());
            await engine.StartAsync();
            var options = new PdfOptions { Timeout = TimeSpan.FromSeconds(2) };
            var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync(Endless(), options));
            Assert.Equal("timeout", error.Code);
            AssertPdf(await engine.HtmlToPdfAsync(Pages(1)));
        }

        [BrowserFact]
        public async Task PrintingHasATimeout()
        {
            await using var engine = new ChromePdfEngine(TestBrowser.Options());
            await engine.StartAsync();
            var options = new PdfOptions { PrintTimeout = TimeSpan.FromMilliseconds(50) };
            var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync(Pages(800), options));
            Assert.Equal("timeout", error.Code);
            AssertPdf(await engine.HtmlToPdfAsync(Pages(1), Patient));
        }

        [BrowserFact]
        public async Task ABrowserThatDiedIsStartedAgain()
        {
            await using var engine = new ChromePdfEngine(TestBrowser.Options());
            await engine.StartAsync();
            int pid = engine.ProcessId!.Value;
            string profile = engine.ProfileDirectory!;
            using (Process browser = Process.GetProcessById(pid))
            {
                browser.Kill(entireProcessTree: true); // the browser this test started
                await browser.WaitForExitAsync();
            }

            byte[]? pdf = null;
            for (int attempt = 0; attempt < 20 && pdf is null; attempt++)
            {
                try
                {
                    pdf = await engine.HtmlToPdfAsync(Pages(1));
                }
                catch (LifeAstroPdfException)
                {
                    await Task.Delay(100); // the socket may not know yet
                }
            }

            AssertPdf(pdf!);
            Assert.NotEqual(pid, engine.ProcessId);
            Assert.False(Directory.Exists(profile));
        }

        [BrowserFact]
        public async Task LeavesNoBrowserAfterACancelledStart()
        {
            var engine = new ChromePdfEngine(TestBrowser.Options());
            using var cancel = new CancellationTokenSource(30);
            try
            {
                await engine.StartAsync(cancel.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Null(engine.ProcessId);
            }

            await engine.DisposeAsync();
            Assert.Null(engine.ProfileDirectory);
        }

        [BrowserFact]
        public async Task TheBrowserDoesNotOutliveTheApplication()
        {
            BrowserProcess browser = BrowserProcess.Launch(TestBrowser.Options());
            try
            {
                using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                await browser.WaitForDevToolsAsync(startup.Token);
                Assert.True(Directory.Exists(browser.ProfileDirectory));
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return; // there the job object does it, which only the end of this process would show
                }

                // What the kernel does when this process dies, however it dies: the pipe closes. Nothing else.
                browser.CloseLifeline();
                Assert.True(await browser.WaitForExitAsync(TimeSpan.FromSeconds(20)), "the browser outlived its pipe");
                Assert.False(Directory.Exists(browser.ProfileDirectory), "the profile outlived the browser");
            }
            finally
            {
                await browser.StopAsync(TimeSpan.Zero);
            }
        }

        [Fact]
        public async Task AMissingExecutableSaysSo()
        {
            string missing = Path.Combine(Path.GetTempPath(), "no-such-browser-" + Guid.NewGuid().ToString("N"));
            await using var engine = new ChromePdfEngine(new ChromePdfEngineOptions { ExecutablePath = missing });
            var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync("<p>x</p>"));
            Assert.Equal("browser_not_found", error.Code);
            Assert.Contains(missing, error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AnExecutableThatQuitsIsReported()
        {
            string[] quitters = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { Path.Combine(Environment.SystemDirectory, "where.exe") }
                : new[] { "/usr/bin/false", "/bin/false" };
            string quitter = quitters.First(File.Exists);
            await using var engine = new ChromePdfEngine(new ChromePdfEngineOptions { ExecutablePath = quitter });
            var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync("<p>x</p>"));
            Assert.Equal("browser_error", error.Code);
            Assert.Null(engine.ProcessId);
        }

        [Fact]
        public async Task TheBrowserDoesNotRunInTheApplicationsDirectory()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // a script stands in for the browser here
            }

            string home = Path.Combine(Path.GetTempPath(), "lifeastro-test-cwd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(home);
            try
            {
                string browser = Path.Combine(home, "browser");
                File.WriteAllText(browser, "#!/bin/sh\n: > started-here\nexit 3\n");
                File.SetUnixFileMode(browser, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                await using var engine = new ChromePdfEngine(new ChromePdfEngineOptions { ExecutablePath = browser });

                await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync("<p>x</p>"));

                Assert.True(File.Exists(Path.Combine(home, "started-here")), "started in another directory");
            }
            finally
            {
                Directory.Delete(home, true);
            }
        }

        [Fact]
        public async Task AnUnreachableDebuggerUrlIsAConnectionError()
        {
            var options = new ChromePdfEngineOptions { WebSocketDebuggerUrl = "ws://127.0.0.1:" + RawServer.ClosedPort() + "/devtools/browser/x" };
            await using var engine = new ChromePdfEngine(options);
            var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync("<p>x</p>"));
            Assert.Equal("connection_error", error.Code);
        }

        /// <summary>A document that never finishes loading: its fonts promise never settles.</summary>
        private static string Endless() =>
            "<!doctype html><html><body><script>Object.defineProperty(document, 'fonts', { value: { ready: new Promise(() => {}) } });</script>x</body></html>";

        private static bool IsRunning(int pid)
        {
            try
            {
                using Process process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private sealed class NeverRuns : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
            }
        }
    }
}
