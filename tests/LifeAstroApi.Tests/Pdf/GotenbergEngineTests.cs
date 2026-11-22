using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Pdf;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>The Gotenberg engine sends the right form, against a stand-in server on 127.0.0.1.</summary>
    public class GotenbergEngineTests
    {
        private sealed class Posted
        {
            internal string Head { get; set; } = string.Empty;

            internal Dictionary<string, string> Fields { get; } = new Dictionary<string, string>();

            internal List<(string Name, string Html)> Files { get; } = new List<(string, string)>();
        }

        private static async Task<Posted> ReadPostAsync(NetworkStream stream)
        {
            var bytes = new MemoryStream();
            var buffer = new byte[65536];
            int headEnd;
            while ((headEnd = Encoding.Latin1.GetString(bytes.ToArray()).IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
            {
                int n = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (n == 0)
                {
                    throw new IOException("no request");
                }

                bytes.Write(buffer, 0, n);
            }

            var posted = new Posted { Head = Encoding.Latin1.GetString(bytes.ToArray(), 0, headEnd) };
            int length = 0;
            string boundary = string.Empty;
            foreach (string line in posted.Head.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    length = int.Parse(line.Substring(15).Trim(), CultureInfo.InvariantCulture);
                }

                int at = line.IndexOf("boundary=", StringComparison.Ordinal);
                if (at >= 0)
                {
                    boundary = line.Substring(at + 9).Trim('"');
                }
            }

            while (bytes.Length < headEnd + 4 + length)
            {
                int n = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (n == 0)
                {
                    break;
                }

                bytes.Write(buffer, 0, n);
            }

            string body = Encoding.Latin1.GetString(bytes.ToArray(), headEnd + 4, length);
            foreach (string part in body.Split("--" + boundary))
            {
                int start = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (start < 0)
                {
                    continue;
                }

                string head = part.Substring(0, start);
                string value = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(part.Substring(start + 4, part.Length - start - 6)));
                string? name = Quoted(head, " name=\"");
                string? file = Quoted(head, "filename=\"");
                if (file != null)
                {
                    Assert.Equal("files", name);
                    Assert.Contains("text/html", head, StringComparison.Ordinal);
                    posted.Files.Add((file, value));
                }
                else
                {
                    posted.Fields[name!] = value;
                }
            }

            return posted;
        }

        private static string? Quoted(string text, string before)
        {
            int at = text.IndexOf(before, StringComparison.Ordinal);
            return at < 0 ? null : text.Substring(at + before.Length, text.IndexOf('"', at + before.Length) - at - before.Length);
        }

        private static RawServer Server(TaskCompletionSource<Posted> seen, int status = 200, string body = "%PDF-1.4 stub") =>
            new RawServer(async (_, stream) =>
            {
                seen.TrySetResult(await ReadPostAsync(stream));
                await RawServer.WriteAsync(
                    stream,
                    "HTTP/1.1 " + status + " X\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body);
                await stream.FlushAsync();
            });

        [Fact]
        public async Task SendsTheHtmlAndThePageGeometry()
        {
            var seen = new TaskCompletionSource<Posted>();
            using RawServer server = Server(seen);
            var headers = new Dictionary<string, string> { ["Authorization"] = "Basic dXNlcjpwYXNz" };
            using var engine = new GotenbergPdfEngine(server.BaseUrl + "/gotenberg/", headers);

            byte[] pdf = await engine.HtmlToPdfAsync("<p>नमस्ते ਫ਼</p>", new PdfOptions
            {
                Format = PdfPaperFormat.Letter,
                Landscape = true,
                Margin = new PdfMargin { Top = "25.4mm", Left = "0.5in" },
            });

            Assert.Equal("%PDF-1.4 stub", Encoding.ASCII.GetString(pdf));
            Posted posted = await seen.Task;
            Assert.StartsWith("POST /gotenberg/forms/chromium/convert/html HTTP/1.1", posted.Head, StringComparison.Ordinal);
            Assert.Contains("Authorization: Basic dXNlcjpwYXNz", posted.Head, StringComparison.Ordinal);
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["paperWidth"] = "11",
                    ["paperHeight"] = "8.5",
                    ["marginTop"] = "1",
                    ["marginBottom"] = "0",
                    ["marginLeft"] = "0.5",
                    ["marginRight"] = "0",
                    ["preferCssPageSize"] = "true",
                    ["printBackground"] = "true",
                    ["landscape"] = "false",
                    ["scale"] = "1",
                },
                posted.Fields);
            // Beside the parameters, the CSS that makes them win over the document's own.
            const string Css = "@page { margin-top: 25.4mm !important; margin-left: 0.5in !important; size: 11in 8.5in !important; }\n";
            Assert.Equal(new[] { ("index.html", "<p>नमस्ते ਫ਼</p>\n<style>" + Css + "</style>") }, posted.Files);
        }

        [Fact]
        public async Task TheOptionsCssIsInTheDocumentOnce()
        {
            var seen = new TaskCompletionSource<Posted>();
            using RawServer server = Server(seen);
            using var engine = new GotenbergPdfEngine(server.BaseUrl);
            await PdfReports.HtmlToPdfAsync("<p>x</p>", new PdfOptions { PrintBackground = false }, engine);
            Posted posted = await seen.Task;
            Assert.Equal("false", posted.Fields["printBackground"]);
            Assert.Equal(2, posted.Files[0].Html.Split("economy !important").Length - 1); // the two properties, once each
            Assert.Equal(1, posted.Files[0].Html.Split("<style>").Length - 1);
        }

        [Fact]
        public async Task SendsTheRunningHeaderAndFooterAsDocuments()
        {
            var seen = new TaskCompletionSource<Posted>();
            using RawServer server = Server(seen);
            using var engine = new GotenbergPdfEngine(server.BaseUrl);
            PdfOptions options = new PdfOptions().ForBrihad("<b>H</b>", "<i>✦ F</i>", new PdfMargin("26mm", "16mm"));

            await engine.HtmlToPdfAsync("<p>x</p>", options);

            Posted posted = await seen.Task;
            Assert.Equal("false", posted.Fields["preferCssPageSize"]);
            Assert.Equal(3, posted.Files.Count);
            Assert.Equal(("index.html", PdfPrintCss.Apply("<p>x</p>", options)), posted.Files[0]);
            Assert.Contains("@bottom-center { content: none !important; }", posted.Files[0].Html, StringComparison.Ordinal);
            Assert.Equal("header.html", posted.Files[1].Name);
            Assert.Contains("<body style=\"margin:0\"><b>H</b></body>", posted.Files[1].Html, StringComparison.Ordinal);
            Assert.Equal("footer.html", posted.Files[2].Name);
            Assert.Contains("<i>✦ F</i>", posted.Files[2].Html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task PrintsAReportInOneCall()
        {
            var seen = new TaskCompletionSource<Posted>();
            using RawServer server = Server(seen);
            using var engine = new GotenbergPdfEngine(server.BaseUrl);
            System.Text.Json.JsonElement report = PdfFixtures.Example("KundliLite", "pa");
            await PdfReports.KundliLitePdfAsync(report, engine: engine);
            Assert.Equal(PdfReports.RenderKundliLiteHtml(report), (await seen.Task).Files[0].Html);
        }

        [Fact]
        public async Task AnErrorStatusThrows()
        {
            using RawServer server = Server(new TaskCompletionSource<Posted>(), 503, "busy");
            using var engine = new GotenbergPdfEngine(server.BaseUrl);
            var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync("<p>x</p>"));
            Assert.Equal(503, error.Status);
            Assert.Equal("render_failed", error.Code);
            Assert.Contains("Gotenberg returned 503: busy", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SomethingElseThanAPdfThrows()
        {
            using RawServer server = Server(new TaskCompletionSource<Posted>(), 200, "<html>proxy login</html>");
            using var engine = new GotenbergPdfEngine(server.BaseUrl);
            var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync("<p>x</p>"));
            Assert.Equal("render_failed", error.Code);
        }

        [Fact]
        public async Task ASilentServiceTimesOutAndCanBeCancelled()
        {
            using var server = new RawServer((_, _) => Task.Delay(TimeSpan.FromSeconds(30)));
            using var engine = new GotenbergPdfEngine(server.BaseUrl);
            var fast = new PdfOptions { Timeout = TimeSpan.FromMilliseconds(200), PrintTimeout = TimeSpan.FromMilliseconds(200) };
            var timeout = await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync("<p>x</p>", fast));
            Assert.Equal("timeout", timeout.Code);

            using var cancel = new CancellationTokenSource(200);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.HtmlToPdfAsync("<p>x</p>", null, cancel.Token));
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 200)]
        [InlineData(true, 200)]
        public async Task DisposeWhilePrintsRunSettlesEveryCall(bool ownClient, int afterMilliseconds)
        {
            using var trap = new UnobservedTrap();
            using var server = new RawServer((_, _) => Task.Delay(TimeSpan.FromSeconds(30)));
            using var http = new HttpClient();
            var engine = new GotenbergPdfEngine(server.BaseUrl, httpClient: ownClient ? http : null);
            var forever = new PdfOptions { Timeout = Timeout.InfiniteTimeSpan };
            var prints = new List<Task<byte[]>>();
            for (int i = 0; i < 6; i++)
            {
                prints.Add(engine.HtmlToPdfAsync("<p>x</p>", forever));
            }

            await Task.Delay(afterMilliseconds);
            engine.Dispose();
            foreach (Task<byte[]> print in prints)
            {
                var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => print.WaitAsync(TimeSpan.FromSeconds(20)));
                Assert.Equal("engine_closed", error.Code);
            }

            await trap.AssertNothingAsync();
        }

        [Fact]
        public async Task AnUnreachableServiceIsAConnectionError()
        {
            using var engine = new GotenbergPdfEngine("http://127.0.0.1:" + RawServer.ClosedPort());
            var error = await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync("<p>x</p>"));
            Assert.Equal("connection_error", error.Code);
        }

        [Fact]
        public async Task YourHttpClientStaysYours()
        {
            var handler = new StubHandler();
            handler.Respond(Reply.Ok("%PDF-1.4 mine"));
            using var http = new HttpClient(handler);
            var engine = new GotenbergPdfEngine("https://pdf.example.test", httpClient: http);
            Assert.NotEmpty(await engine.HtmlToPdfAsync("<p>x</p>"));
            engine.Dispose();
            Assert.Equal("engine_closed", (await Assert.ThrowsAsync<LifeAstroPdfException>(() => engine.HtmlToPdfAsync("<p>x</p>"))).Code);
            Assert.Equal(System.Net.HttpStatusCode.OK, (await http.GetAsync("https://pdf.example.test/health")).StatusCode);
        }

        [Theory]
        [InlineData("localhost:3000")]
        [InlineData("ftp://host")]
        [InlineData("")]
        public void TheBaseUrlMustBeHttp(string url) => Assert.Throws<ArgumentException>(() => new GotenbergPdfEngine(url));
    }
}
