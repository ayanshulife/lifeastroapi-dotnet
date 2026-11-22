using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LifeAstroApi.Pdf;
using LifeAstroApi.Pdf.Engine;
using Xunit;

namespace LifeAstroApi.Tests.Pdf
{
    /// <summary>What the engines decide before any browser runs: options, lookup, command line.</summary>
    public class PdfEngineSetupTests
    {
        [Theory]
        [InlineData("26mm", 26 / 25.4)]
        [InlineData("1cm", 1 / 2.54)]
        [InlineData("0.5IN", 0.5)]
        [InlineData("20px", 20 / 96d)]
        [InlineData(" 48 ", 0.5)]
        [InlineData(".5in", 0.5)]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        public void CssLengthsAreInches(string? length, double inches) => Assert.Equal(inches, PageSetup.Inches(length), 10);

        [Theory]
        [InlineData("1em")]
        [InlineData("10 %")]
        [InlineData("1,5cm")]
        [InlineData("abc")]
        public void OtherLengthsThrow(string length) => Assert.Throws<ArgumentException>(() => PageSetup.Inches(length));

        [Fact]
        public void TheDefaultsAreNodes()
        {
            PageSetup setup = PageSetup.From(null);
            Assert.Equal((8.2677, 11.6929), (setup.PaperWidth, setup.PaperHeight));
            Assert.True(setup.PrintBackground);
            Assert.True(setup.PreferCssPageSize);
            Assert.False(setup.DisplayHeaderFooter);
            Assert.Equal(1, setup.Scale);
            Assert.Equal(TimeSpan.FromSeconds(30), setup.LoadTimeout);
            Assert.Equal(TimeSpan.FromMinutes(2), setup.PrintTimeout);
        }

        [Fact]
        public void OptionsBecomeThePageSetup()
        {
            PageSetup setup = PageSetup.From(new PdfOptions
            {
                Format = PdfPaperFormat.Letter,
                Landscape = true,
                PreferCssPageSize = true,
                DisplayHeaderFooter = true,
                Margin = new PdfMargin { Top = "1in" },
            });
            Assert.Equal((11, 8.5), (setup.PaperWidth, setup.PaperHeight));
            Assert.False(setup.PreferCssPageSize); // never with a running header
            Assert.Equal("<span></span>", setup.Header);
            Assert.Equal((1, 0), (setup.MarginTop, setup.MarginBottom));
        }

        [Fact]
        public void BadOptionsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PageSetup.From(new PdfOptions { Scale = 3 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => PageSetup.From(new PdfOptions { Scale = double.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => PageSetup.From(new PdfOptions { Timeout = TimeSpan.Zero }));
            Assert.Throws<ArgumentOutOfRangeException>(() => PageSetup.From(new PdfOptions { Format = (PdfPaperFormat)99 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChromePdfEngine(new ChromePdfEngineOptions { MaxConcurrency = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChromePdfEngine(new ChromePdfEngineOptions { LaunchTimeout = TimeSpan.Zero }));
            Assert.Equal(Timeout.InfiniteTimeSpan, PageSetup.From(new PdfOptions { Timeout = Timeout.InfiniteTimeSpan }).PrintTimeout);
        }

        private static string? Find(BrowserPlatform platform, Dictionary<string, string> environment, params string[] files) =>
            BrowserLocator.Find(platform, name => environment.TryGetValue(name, out string? v) ? v : null, files.Contains);

        [Fact]
        public void WindowsFindsTheEdgeItComesWith()
        {
            var environment = new Dictionary<string, string>
            {
                ["ProgramW6432"] = @"C:\Program Files",
                ["ProgramFiles"] = @"C:\Program Files",
                ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
                ["LOCALAPPDATA"] = @"C:\Users\a\AppData\Local",
            };
            const string Edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
            const string Chrome = @"C:\Users\a\AppData\Local\Google\Chrome\Application\chrome.exe";
            Assert.Equal(Edge, Find(BrowserPlatform.Windows, environment, Edge));
            Assert.Equal(Chrome, Find(BrowserPlatform.Windows, environment, Edge, Chrome));
            Assert.Null(Find(BrowserPlatform.Windows, environment));
        }

        [Fact]
        public void MacFindsTheApplications()
        {
            var environment = new Dictionary<string, string> { ["HOME"] = "/Users/a" };
            const string Edge = "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
            const string Chrome = "/Users/a/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
            Assert.Equal(Edge, Find(BrowserPlatform.MacOS, environment, Edge));
            Assert.Equal(Chrome, Find(BrowserPlatform.MacOS, environment, Edge, Chrome));
        }

        [Fact]
        public void LinuxSearchesThePathButNeverTheWorkingDirectory()
        {
            var environment = new Dictionary<string, string> { ["PATH"] = ".:bin::/opt/tools" };
            Assert.Equal("/opt/tools/chromium", Find(BrowserPlatform.Linux, environment, "/opt/tools/chromium"));
            Assert.Equal("/snap/bin/chromium", Find(BrowserPlatform.Linux, environment, "/snap/bin/chromium"));
            Assert.Null(Find(BrowserPlatform.Linux, environment, "./chromium", "bin/chromium", "/chromium"));
        }

        [Fact]
        public void TheEnvironmentOverrides()
        {
            var environment = new Dictionary<string, string> { ["LIFEASTRO_CHROME"] = "/a/chrome", ["CHROME_PATH"] = "/b/chrome" };
            Assert.Equal("/a/chrome", Find(BrowserPlatform.Linux, environment, "/a/chrome", "/b/chrome"));
            var missing = Assert.Throws<LifeAstroPdfException>(() => Find(BrowserPlatform.Linux, environment, "/b/chrome"));
            Assert.Equal("browser_not_found", missing.Code);
            Assert.Contains("/a/chrome", missing.Message, StringComparison.Ordinal);

            environment.Remove("LIFEASTRO_CHROME");
            Assert.Equal("/b/chrome", Find(BrowserPlatform.Linux, environment, "/b/chrome", "/usr/bin/chromium"));
            Assert.Equal("/usr/bin/chromium", Find(BrowserPlatform.Linux, environment, "/usr/bin/chromium"));
        }

        [Theory]
        [InlineData((int)BrowserPlatform.Windows, "Microsoft Edge")]
        [InlineData((int)BrowserPlatform.MacOS, "/Applications")]
        [InlineData((int)BrowserPlatform.Linux, "fonts-noto-core")]
        public void AMissingBrowserSaysWhatToInstall(int platform, string advice)
        {
            LifeAstroPdfException error = BrowserLocator.NotFound((BrowserPlatform)platform);
            Assert.Equal("browser_not_found", error.Code);
            Assert.Contains(advice, error.Message, StringComparison.Ordinal);
            Assert.Contains("LIFEASTRO_CHROME", error.Message, StringComparison.Ordinal);
            Assert.Contains("GotenbergPdfEngine", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void TheSandboxIsOffOnlyWhenAskedFor()
        {
            var options = new ChromePdfEngineOptions();
            options.Arguments.Add("--lang=en");
            List<string> safe = BrowserProcess.Arguments(options, "/tmp/profile", false);
            Assert.DoesNotContain("--no-sandbox", safe);
            Assert.Contains("--user-data-dir=/tmp/profile", safe);
            Assert.Contains("--remote-debugging-port=0", safe);
            Assert.Equal("about:blank", safe[safe.Count - 1]);
            Assert.Equal("--lang=en", safe[safe.Count - 2]);
            Assert.Contains("--no-sandbox", BrowserProcess.Arguments(options, "/tmp/profile", true));
        }

        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("a b", "\"a b\"")]
        [InlineData("", "\"\"")]
        [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [InlineData("C:\\dir with space\\", "\"C:\\dir with space\\\\\"")]
        [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
        [InlineData("C:\\no\\space", "C:\\no\\space")]
        public void ArgumentsAreQuoted(string argument, string quoted) =>
            Assert.Equal(quoted, BrowserProcess.CommandLine(new[] { argument }));
    }
}
