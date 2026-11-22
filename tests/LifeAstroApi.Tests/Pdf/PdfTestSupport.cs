using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Pdf;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>One case of Resources/Pdf/cases.json.</summary>
    internal sealed class GoldenCase
    {
        internal GoldenCase(string id, string renderer, JsonElement report, PdfBranding? branding, string sha256)
        {
            Id = id;
            Renderer = renderer;
            Report = report;
            Branding = branding;
            Sha256 = sha256;
        }

        internal string Id { get; }

        internal string Renderer { get; }

        internal JsonElement Report { get; }

        internal PdfBranding? Branding { get; }

        internal string Sha256 { get; }
    }

    /// <summary>The fixtures that tool/pdf-goldens.mjs records from the Node.js SDK's source.</summary>
    internal static class PdfFixtures
    {
        private static readonly Lazy<IReadOnlyDictionary<string, GoldenCase>> AllCases =
            new Lazy<IReadOnlyDictionary<string, GoldenCase>>(LoadCases);

        internal static readonly IReadOnlyDictionary<string, Func<JsonElement, PdfBranding?, string>> Renderers =
            new Dictionary<string, Func<JsonElement, PdfBranding?, string>>
            {
                ["KundliLite"] = PdfReports.RenderKundliLiteHtml,
                ["KundliDetailed"] = PdfReports.RenderKundliDetailedHtml,
                ["KundliBrihad"] = PdfReports.RenderKundliBrihadHtml,
                ["MatchMaking"] = PdfReports.RenderMatchMakingHtml,
                ["DashaAnalysis"] = PdfReports.RenderDashaAnalysisHtml,
                ["Numerology"] = PdfReports.RenderNumerologyHtml,
                ["SadeSati"] = PdfReports.RenderSadeSatiHtml,
                ["MangalDosha"] = PdfReports.RenderMangalDoshaHtml,
                ["Varshaphal"] = PdfReports.RenderVarshaphalHtml,
                ["Horoscope"] = PdfReports.RenderHoroscopeHtml,
            };

        internal static readonly IReadOnlyDictionary<string, Func<JsonElement, PdfBranding?, PdfOptions?, IPdfEngine?, CancellationToken, Task<byte[]>>> Printers =
            new Dictionary<string, Func<JsonElement, PdfBranding?, PdfOptions?, IPdfEngine?, CancellationToken, Task<byte[]>>>
            {
                ["KundliLite"] = PdfReports.KundliLitePdfAsync,
                ["KundliDetailed"] = PdfReports.KundliDetailedPdfAsync,
                ["KundliBrihad"] = PdfReports.KundliBrihadPdfAsync,
                ["MatchMaking"] = PdfReports.MatchMakingPdfAsync,
                ["DashaAnalysis"] = PdfReports.DashaAnalysisPdfAsync,
                ["Numerology"] = PdfReports.NumerologyPdfAsync,
                ["SadeSati"] = PdfReports.SadeSatiPdfAsync,
                ["MangalDosha"] = PdfReports.MangalDoshaPdfAsync,
                ["Varshaphal"] = PdfReports.VarshaphalPdfAsync,
                ["Horoscope"] = PdfReports.HoroscopePdfAsync,
            };

        internal static string Directory => Path.Combine(AppContext.BaseDirectory, "Resources", "Pdf");

        internal static IReadOnlyDictionary<string, GoldenCase> Cases => AllCases.Value;

        internal static JsonElement Read(string name)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Directory, name)));
            return document.RootElement.Clone();
        }

        internal static string Sha256(string text)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>The camelCase branding of the fixtures and of pdf_work/manifest.json.</summary>
        internal static PdfBranding? Branding(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? Field(string name) => value.TryGetProperty(name, out JsonElement field) ? field.GetString() : null;
            return new PdfBranding
            {
                CompanyName = Field("companyName"),
                LogoUrl = Field("logoUrl"),
                PrimaryColor = Field("primaryColor"),
                SecondaryColor = Field("secondaryColor"),
                FooterText = Field("footerText"),
                WatermarkText = Field("watermarkText"),
            };
        }

        /// <summary>The Node SDK's option names, as tool/pdf-goldens.mjs records them.</summary>
        internal static PdfOptions Options(JsonElement given)
        {
            var options = new PdfOptions();
            if (given.ValueKind != JsonValueKind.Object)
            {
                return options;
            }

            foreach (JsonProperty option in given.EnumerateObject())
            {
                switch (option.Name)
                {
                    case "format": options.Format = Enum.Parse<PdfPaperFormat>(option.Value.GetString()!); break;
                    case "landscape": options.Landscape = option.Value.GetBoolean(); break;
                    case "printBackground": options.PrintBackground = option.Value.GetBoolean(); break;
                    case "preferCSSPageSize": options.PreferCssPageSize = option.Value.GetBoolean(); break;
                    case "displayHeaderFooter": options.DisplayHeaderFooter = option.Value.GetBoolean(); break;
                    case "headerTemplate": options.HeaderTemplate = option.Value.GetString(); break;
                    case "footerTemplate": options.FooterTemplate = option.Value.GetString(); break;
                    case "scale": options.Scale = option.Value.GetDouble(); break;
                    case "timeoutMs": options.Timeout = TimeSpan.FromMilliseconds(option.Value.GetDouble()); break;
                    case "margin": options.Margin = Margin(option.Value); break;
                    default: throw new InvalidOperationException("an option the tests do not know: " + option.Name);
                }
            }

            return options;
        }

        internal static PdfMargin? Margin(JsonElement given)
        {
            if (given.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? Side(string name) => given.TryGetProperty(name, out JsonElement side) ? side.GetString() : null;
            return new PdfMargin(Side("top"), Side("bottom"), Side("left"), Side("right"));
        }

        /// <summary>A copy of a JSON object with one property set.</summary>
        internal static JsonElement With(JsonElement value, string key, string text)
        {
            Dictionary<string, object> copy = value.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value);
            copy[key] = text;
            return JsonSerializer.SerializeToElement(copy);
        }

        /// <summary>What is at the path.</summary>
        internal static JsonElement At(JsonElement value, JsonElement[] path)
        {
            foreach (JsonElement step in path)
            {
                value = step.ValueKind == JsonValueKind.Number ? value[step.GetInt32()] : value.GetProperty(step.GetString()!);
            }

            return value;
        }

        /// <summary>A copy of a JSON value with what is at the path replaced; the key order stays.</summary>
        internal static JsonElement Replace(JsonElement value, JsonElement[] path, JsonElement replacement)
        {
            JsonNode root = JsonNode.Parse(value.GetRawText())!;
            JsonNode parent = root;
            for (int i = 0; i < path.Length - 1; i++)
            {
                parent = path[i].ValueKind == JsonValueKind.Number ? parent[path[i].GetInt32()]! : parent[path[i].GetString()!]!;
            }

            JsonElement last = path[path.Length - 1];
            JsonNode? with = JsonNode.Parse(replacement.GetRawText());
            if (last.ValueKind == JsonValueKind.Number)
            {
                parent[last.GetInt32()] = with;
            }
            else
            {
                parent[last.GetString()!] = with;
            }

            using JsonDocument changed = JsonDocument.Parse(root.ToJsonString());
            return changed.RootElement.Clone();
        }

        /// <summary>An example report (unbranded) in a language, for the tests that print.</summary>
        internal static JsonElement Example(string renderer, string language)
        {
            string key = renderer == "Horoscope" ? "language" : "Locale";
            // The example responses are the biggest inputs of their renderer.
            return Cases.Values
                .Where(c => c.Renderer == renderer && c.Branding is null
                    && c.Report.TryGetProperty(key, out JsonElement value) && value.ToString() == language)
                .OrderByDescending(c => c.Report.GetRawText().Length)
                .First().Report;
        }

        internal static string FirstDifference(string expected, string actual)
        {
            int at = 0;
            int shorter = Math.Min(expected.Length, actual.Length);
            while (at < shorter && expected[at] == actual[at])
            {
                at++;
            }

            if (at == expected.Length && at == actual.Length)
            {
                return "identical";
            }

            int from = Math.Max(0, at - 120);
            return "differs from Node at UTF-16 unit " + at
                + "\n--- Node ---\n" + expected.Substring(from, Math.Min(300, expected.Length - from))
                + "\n--- .NET ---\n" + actual.Substring(from, Math.Min(300, actual.Length - from));
        }

        private static IReadOnlyDictionary<string, GoldenCase> LoadCases()
        {
            JsonElement file = Read("cases.json");
            JsonElement[] inputs = file.GetProperty("inputs").EnumerateArray().ToArray();
            var cases = new Dictionary<string, GoldenCase>();
            foreach (JsonElement c in file.GetProperty("cases").EnumerateArray())
            {
                JsonElement report = inputs[c.GetProperty("input").GetInt32()];
                JsonElement set = c.GetProperty("set");
                if (set.ValueKind == JsonValueKind.Object)
                {
                    report = With(report, set.GetProperty("key").GetString()!, set.GetProperty("value").GetString()!);
                }

                string id = c.GetProperty("id").GetString()!;
                cases.Add(id, new GoldenCase(
                    id,
                    c.GetProperty("renderer").GetString()!,
                    report,
                    Branding(c.GetProperty("branding")),
                    c.GetProperty("sha256").GetString()!));
            }

            return cases;
        }
    }

    /// <summary>The shared reference of every SDK, pdf_work/ (432 full reports), when it is on this machine.</summary>
    internal static class PdfWork
    {
        internal const string Variable = "LIFEASTRO_PDF_WORK";

        internal static string? Directory { get; } = Find();

        internal static string SkipReason =>
            "No pdf_work reference here (../LifeAstroAPI/pdf_work, or set " + Variable + "): the deep parity test needs it.";

        private static string? Find()
        {
            string? path = Environment.GetEnvironmentVariable(Variable);
            if (string.IsNullOrEmpty(path))
            {
                string? root = RepositoryRoot();
                path = root is null ? null : Path.Combine(root, "..", "LifeAstroAPI", "pdf_work");
            }

            return path != null && File.Exists(Path.Combine(path, "manifest.json")) ? path : null;
        }

        private static string? RepositoryRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "LifeAstroApi.sln")))
                {
                    return dir.FullName;
                }
            }

            return null;
        }
    }

    internal static class TestBrowser
    {
        internal const string NoSandboxVariable = "LIFEASTRO_TEST_NO_SANDBOX";

        internal static string? Executable { get; } = ChromePdfEngine.FindExecutable();

        internal static string SkipReason =>
            "No Chrome, Chromium or Microsoft Edge on this machine (set LIFEASTRO_CHROME to point at one).";

        /// <summary>
        /// Ubuntu 24.04 (GitHub's runners) does not let an unprivileged Chrome build its sandbox. The tests
        /// print only their own fixtures, so they opt in to --no-sandbox on Linux CI. The library never does.
        /// </summary>
        internal static ChromePdfEngineOptions Options()
        {
            bool linuxCi = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                && (IsOn("CI") || IsOn("GITHUB_ACTIONS"));
            return new ChromePdfEngineOptions
            {
                NoSandbox = linuxCi || IsOn(NoSandboxVariable) ? true : (bool?)null,
                LaunchTimeout = TimeSpan.FromSeconds(90),
            };
        }

        private static bool IsOn(string variable)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }
    }

    /// <summary>A test that needs a browser.</summary>
    public sealed class BrowserFactAttribute : FactAttribute
    {
        public BrowserFactAttribute()
        {
            if (TestBrowser.Executable is null)
            {
                Skip = TestBrowser.SkipReason;
            }
        }
    }

    /// <summary>A theory that needs a browser.</summary>
    public sealed class BrowserTheoryAttribute : TheoryAttribute
    {
        public BrowserTheoryAttribute()
        {
            if (TestBrowser.Executable is null)
            {
                Skip = TestBrowser.SkipReason;
            }
        }
    }

    /// <summary>A test that reads a PDF back with qpdf.</summary>
    public sealed class QpdfFactAttribute : FactAttribute
    {
        public QpdfFactAttribute()
        {
            if (Qpdf.Executable is null)
            {
                Skip = "No qpdf on this machine's PATH: it is the independent reader of the bookmarks here.";
            }
        }
    }

    internal static class Qpdf
    {
        internal static string? Executable { get; } = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Where(directory => directory.Length > 0)
            .SelectMany(directory => new[] { Path.Combine(directory, "qpdf"), Path.Combine(directory, "qpdf.exe") })
            .FirstOrDefault(File.Exists);
    }

    /// <summary>A test that needs pdf_work.</summary>
    public sealed class PdfWorkFactAttribute : FactAttribute
    {
        public PdfWorkFactAttribute()
        {
            if (PdfWork.Directory is null)
            {
                Skip = PdfWork.SkipReason;
            }
        }
    }

    /// <summary>A theory that needs pdf_work.</summary>
    public sealed class PdfWorkTheoryAttribute : TheoryAttribute
    {
        public PdfWorkTheoryAttribute()
        {
            if (PdfWork.Directory is null)
            {
                Skip = PdfWork.SkipReason;
            }
        }
    }

    /// <summary>What the North Indian charts of a document draw, whatever the language of the labels.</summary>
    internal static class ChartReader
    {
        /// <summary>Every planet as "chart x,y colour", in drawing order: the colour names the planet.</summary>
        internal static List<string> Planets(string html)
        {
            var found = new List<string>();
            string[] charts = html.Split("<svg viewBox=\"0 0 100 100\"");
            for (int i = 1; i < charts.Length; i++)
            {
                string svg = charts[i].Substring(0, charts[i].IndexOf("</svg>", StringComparison.Ordinal));
                foreach (string text in svg.Split("<text ").Skip(1))
                {
                    foreach (string planet in text.Split("<tspan fill=\"").Skip(1))
                    {
                        string colour = planet.Substring(0, planet.IndexOf('"', StringComparison.Ordinal));
                        found.Add(i + " " + Attribute(text, "x") + "," + Attribute(text, "y") + " " + colour);
                    }
                }
            }

            return found;
        }

        /// <summary>The number of planet cards with their picture.</summary>
        internal static int Glyphs(string html) => html.Split("<div class=\"graha-glyph\">").Length - 1;

        private static string Attribute(string tag, string name)
        {
            int at = tag.IndexOf(name + "=\"", StringComparison.Ordinal) + name.Length + 2;
            return tag.Substring(at, tag.IndexOf('"', at) - at);
        }
    }

    /// <summary>A real engine, keeping what went in and what came out.</summary>
    internal sealed class ForwardingEngine : IPdfEngine
    {
        private readonly IPdfEngine _inner;

        internal ForwardingEngine(IPdfEngine inner)
        {
            _inner = inner;
        }

        internal List<(string Html, PdfOptions? Options, byte[] Pdf)> Prints { get; } = new List<(string, PdfOptions?, byte[])>();

        public async Task<byte[]> HtmlToPdfAsync(string html, PdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            byte[] pdf = await _inner.HtmlToPdfAsync(html, options, cancellationToken);
            Prints.Add((html, options, pdf));
            return pdf;
        }
    }

    /// <summary>Failed tasks of the SDK that nobody observed.</summary>
    internal sealed class UnobservedTrap : IDisposable
    {
        private readonly List<string> _caught = new List<string>();
        private readonly EventHandler<UnobservedTaskExceptionEventArgs> _handler;

        internal UnobservedTrap()
        {
            _handler = (sender, e) =>
            {
                foreach (Exception error in e.Exception.Flatten().InnerExceptions)
                {
                    if (error is LifeAstroPdfException || error.ToString().Contains("LifeAstroApi.Pdf.", StringComparison.Ordinal))
                    {
                        lock (_caught)
                        {
                            _caught.Add(error.GetType().Name + ": " + error.Message);
                        }
                    }
                }
            };
            TaskScheduler.UnobservedTaskException += _handler;
        }

        internal async Task AssertNothingAsync()
        {
            for (int round = 0; round < 3; round++)
            {
                await Task.Delay(100); // what nobody awaits still needs a moment to fail
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            lock (_caught)
            {
                Assert.Empty(_caught);
            }
        }

        public void Dispose() => TaskScheduler.UnobservedTaskException -= _handler;
    }

    /// <summary>An engine that keeps what it was asked to print.</summary>
    internal sealed class RecordingEngine : IPdfEngine
    {
        private readonly Func<int, string, byte[]> _answer;

        internal RecordingEngine(Func<int, string, byte[]>? answer = null)
        {
            _answer = answer ?? ((_, _) => Encoding.ASCII.GetBytes("%PDF-recorded"));
        }

        internal List<string> Html { get; } = new List<string>();

        internal List<PdfOptions?> Options { get; } = new List<PdfOptions?>();

        public Task<byte[]> HtmlToPdfAsync(string html, PdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Html.Add(html);
            Options.Add(options);
            return Task.FromResult(_answer(Html.Count, html));
        }
    }

    /// <summary>Reads back what PdfOutline writes, as a PDF reader does (ISO 32000-1, 7.3.4.2).</summary>
    internal static class PdfReader
    {
        /// <summary>The bookmark titles, in the file's order.</summary>
        internal static List<string> BookmarkTitles(byte[] pdf)
        {
            string text = Encoding.Latin1.GetString(pdf);
            string opening = "/Title (" + (char)0xFE + (char)0xFF;
            var titles = new List<string>();
            for (int at = text.IndexOf(opening, StringComparison.Ordinal); at >= 0; at = text.IndexOf(opening, at + 1, StringComparison.Ordinal))
            {
                (List<int> bytes, int end) = Literal(text, at + "/Title (".Length);
                if (string.CompareOrdinal(text, end, " /Parent", 0, 8) != 0)
                {
                    continue; // the document information's title
                }

                Assert.True(bytes.Count % 2 == 0, "a UTF-16 title with an odd number of bytes");
                var title = new StringBuilder();
                for (int i = 2; i < bytes.Count; i += 2)
                {
                    title.Append((char)((bytes[i] << 8) | bytes[i + 1]));
                }

                titles.Add(title.ToString());
            }

            return titles;
        }

        /// <summary>The trailer a reader uses: the last one.</summary>
        internal static string LastTrailer(byte[] pdf)
        {
            string text = Encoding.Latin1.GetString(pdf);
            return text.Substring(text.LastIndexOf("trailer", StringComparison.Ordinal));
        }

        private static (List<int> Bytes, int End) Literal(string text, int from)
        {
            var bytes = new List<int>();
            int depth = 0;
            for (int i = from; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\')
                {
                    char e = text[++i];
                    switch (e)
                    {
                        case 'n': bytes.Add('\n'); break;
                        case 'r': bytes.Add('\r'); break;
                        case 't': bytes.Add('\t'); break;
                        case 'b': bytes.Add('\b'); break;
                        case 'f': bytes.Add('\f'); break;
                        case '\n': break; // a line continuation
                        case '\r':
                            i += i + 1 < text.Length && text[i + 1] == '\n' ? 1 : 0;
                            break;
                        default:
                            if (e < '0' || e > '7')
                            {
                                bytes.Add(e);
                                break;
                            }

                            int value = e - '0';
                            for (int digits = 1; digits < 3 && text[i + 1] >= '0' && text[i + 1] <= '7'; digits++)
                            {
                                value = (value * 8) + (text[++i] - '0');
                            }

                            bytes.Add(value & 0xFF);
                            break;
                    }
                }
                else if (c == '\r')
                {
                    // An unescaped end of line, CR, LF or CR LF, is one LF.
                    i += i + 1 < text.Length && text[i + 1] == '\n' ? 1 : 0;
                    bytes.Add('\n');
                }
                else if (c == ')' && depth == 0)
                {
                    return (bytes, i + 1);
                }
                else
                {
                    depth += c == '(' ? 1 : c == ')' ? -1 : 0;
                    bytes.Add(c);
                }
            }

            throw new FormatException("an unterminated literal string");
        }
    }

    /// <summary>What is on the paper of a PDF that Chromium printed, as far as the tests need it.</summary>
    internal static class PdfInk
    {
        internal readonly struct Box
        {
            internal Box(double left, double bottom, double right, double top)
            {
                Left = left;
                Bottom = bottom;
                Right = right;
                Top = top;
            }

            internal double Left { get; }

            internal double Bottom { get; }

            internal double Right { get; }

            internal double Top { get; }
        }

        /// <summary>The rectangle of every link, in points from the bottom left.</summary>
        internal static List<Box> Links(byte[] pdf) => Encoding.Latin1.GetString(pdf)
            .Split("/Rect [")
            .Skip(1)
            .Select(rest => Numbers(rest.Substring(0, rest.IndexOf(']', StringComparison.Ordinal))))
            .Select(n => new Box(Math.Min(n[0], n[2]), Math.Min(n[1], n[3]), Math.Max(n[0], n[2]), Math.Max(n[1], n[3])))
            .ToList();

        /// <summary>The first page's width and height, in points.</summary>
        internal static (double Width, double Height) Paper(byte[] pdf)
        {
            string text = Encoding.Latin1.GetString(pdf);
            int at = text.IndexOf("/MediaBox [", StringComparison.Ordinal) + "/MediaBox [".Length;
            double[] box = Numbers(text.Substring(at, text.IndexOf(']', at) - at));
            return (box[2] - box[0], box[3] - box[1]);
        }

        /// <summary>The drawing operators: every stream, inflated.</summary>
        internal static string Content(byte[] pdf)
        {
            string text = Encoding.Latin1.GetString(pdf);
            var all = new StringBuilder();
            int at = 0;
            while ((at = text.IndexOf("stream", at, StringComparison.Ordinal)) >= 0)
            {
                // The keyword follows the stream's dictionary (">> stream" from Skia, ">>\nstream" from others).
                int before = at - 1;
                while (before > 0 && (text[before] == ' ' || text[before] == '\r' || text[before] == '\n'))
                {
                    before--;
                }

                at += "stream".Length;
                if (before < 1 || text[before] != '>' || text[before - 1] != '>')
                {
                    continue;
                }

                int from = text.IndexOf('\n', at) + 1;
                int end = text.IndexOf("endstream", from, StringComparison.Ordinal);
                try
                {
                    using var inflate = new ZLibStream(new MemoryStream(pdf, from, end - from), CompressionMode.Decompress);
                    using var plain = new MemoryStream();
                    inflate.CopyTo(plain);
                    all.Append(Encoding.Latin1.GetString(plain.ToArray()));
                }
                catch (InvalidDataException)
                {
                    all.Append(text, from, end - from); // stored as it is
                }

                at = end;
            }

            Assert.True(all.Length > 0, "no content stream found");
            return all.ToString();
        }

        private static double[] Numbers(string text) => text
            .Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(n => double.Parse(n, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
    }

    /// <summary>A minimal PDF in Chromium's layout: a cross-reference table, plain dictionaries, link annotations.</summary>
    internal static class SmallPdf
    {
        /// <param name="pages">The number of pages.</param>
        /// <param name="links">(page, uri) pairs.</param>
        /// <param name="title">The document information's title.</param>
        internal static byte[] Create(int pages, IReadOnlyList<(int Page, string Uri)>? links = null, string? title = null)
        {
            links ??= Array.Empty<(int, string)>();
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [" + string.Join(" ", Enumerable.Range(3, pages).Select(n => n + " 0 R")) + "] /Count " + pages + " >>",
            };
            for (int page = 1; page <= pages; page++)
            {
                string[] annotations = links
                    .Select((link, i) => link.Page == page ? (3 + pages + i) + " 0 R" : string.Empty)
                    .Where(r => r.Length > 0)
                    .ToArray();
                string annots = annotations.Length == 0 ? string.Empty : " /Annots [" + string.Join(" ", annotations) + "]";
                objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842]" + annots + " >>");
            }

            foreach ((int _, string uri) in links)
            {
                objects.Add("<< /Type /Annot /Subtype /Link /A << /S /URI /URI (" + uri + ") >> >>");
            }

            string infoRef = string.Empty;
            if (title != null)
            {
                objects.Add("<< /Title (" + title + ") >>");
                infoRef = " /Info " + objects.Count + " 0 R";
            }

            var text = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(text.Length);
                text.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
            }

            int xref = text.Length;
            text.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (int offset in offsets)
            {
                text.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            }

            text.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R").Append(infoRef).Append(" >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            return Encoding.Latin1.GetBytes(text.ToString());
        }
    }
}
