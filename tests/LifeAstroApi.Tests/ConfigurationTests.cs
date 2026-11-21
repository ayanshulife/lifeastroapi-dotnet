using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace LifeAstroApi.Tests
{
    /// <summary>The "configuration" cases of the Node SDK's client.test.ts, plus .NET specifics.</summary>
    public class ConfigurationTests
    {
        private readonly StubHandler _stub = new StubHandler();

        private LifeAstroClient Make(LifeAstroClientOptions options, Func<string, string?> env) =>
            new LifeAstroClient(new HttpClient(_stub), options, env);

        [Fact]
        public void ThrowsWhenNoApiKeyIsProvided()
        {
            InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => Make(new LifeAstroClientOptions(), _ => null));
            Assert.Contains("API key", e.Message);
            Assert.Contains("LIFEASTRO_API_KEY", e.Message);
            Assert.Throws<InvalidOperationException>(() => Make(new LifeAstroClientOptions { ApiKey = "  " }, _ => ""));
        }

        [Fact]
        public async Task ReadsTheApiKeyFromTheEnvironment()
        {
            var env = new Dictionary<string, string?> { ["LIFEASTRO_API_KEY"] = "dv_live_env" };
            using (LifeAstroClient client = Make(new LifeAstroClientOptions { BaseUrl = Fixtures.BaseUrl }, name => env.TryGetValue(name, out string? v) ? v : null))
            {
                await client.Meta.VersionAsync();
            }

            Assert.Equal("Bearer dv_live_env", _stub.Last.Header("Authorization"));
        }

        [Fact]
        public async Task AnExplicitKeyWinsOverTheEnvironment()
        {
            using (LifeAstroClient client = Make(new LifeAstroClientOptions { ApiKey = " dv_live_explicit ", BaseUrl = Fixtures.BaseUrl }, _ => "dv_live_env"))
            {
                await client.Meta.VersionAsync();
            }

            Assert.Equal("Bearer dv_live_explicit", _stub.Last.Header("Authorization"));
        }

        [Fact]
        public void TheDefaults()
        {
            using var client = new LifeAstroClient("dv_live_str");
            Assert.Equal("https://api.lifeastroapi.com", client.BaseUrl);
            Assert.Equal(LifeAstroClient.DefaultBaseUrl, client.BaseUrl);
            Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
            Assert.Equal(2, client.MaxRetries);
            var options = new LifeAstroClientOptions();
            Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
            Assert.Equal(2, options.MaxRetries);
            Assert.Null(options.ApiKey);
        }

        [Fact]
        public void NeverPrintsTheKey()
        {
            using var client = new LifeAstroClient("dv_live_abcdef123456");
            string s = client.ToString();
            Assert.DoesNotContain("abcdef123456", s);
            Assert.Contains("dv_live_****", s);
        }

        [Fact]
        public async Task NormalisesAndChecksTheBaseUrl()
        {
            using (LifeAstroClient client = Make(new LifeAstroClientOptions { ApiKey = "dv_live_x", BaseUrl = " https://sdk.test/// " }, _ => null))
            {
                Assert.Equal("https://sdk.test", client.BaseUrl);
                await client.Meta.VersionAsync();
            }

            Assert.Equal("https://sdk.test/v1/meta/version", _stub.Last.Uri.ToString());
            Assert.Throws<ArgumentException>(() => new LifeAstroClient(new LifeAstroClientOptions { ApiKey = "k", BaseUrl = "ftp://x" }));
            Assert.Throws<ArgumentException>(() => new LifeAstroClient(new LifeAstroClientOptions { ApiKey = "k", BaseUrl = "not a url" }));
            Assert.ThrowsAny<ArgumentException>(() => new LifeAstroClient(new LifeAstroClientOptions { ApiKey = "k", Timeout = TimeSpan.Zero }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RequestOptions { Timeout = TimeSpan.FromSeconds(-1) });
        }

        [Fact]
        public async Task UsesAnInjectedHttpClientAndLeavesItOpen()
        {
            var http = new HttpClient(_stub);
            using (var client = new LifeAstroClient(http, new LifeAstroClientOptions { ApiKey = "dv_live_x", BaseUrl = Fixtures.BaseUrl }))
            {
                await client.Meta.VersionAsync();
            }

            // Still usable after the LifeAstro client is disposed.
            using (var again = new LifeAstroClient(http, new LifeAstroClientOptions { ApiKey = "dv_live_x", BaseUrl = Fixtures.BaseUrl }))
            {
                await again.Meta.VersionAsync();
            }

            Assert.Equal(2, _stub.Calls.Count);
            Assert.Equal("{}", await http.GetStringAsync(Fixtures.BaseUrl + "/direct"));
        }

        [Fact]
        public async Task ADisposedClientRefusesCalls()
        {
            LifeAstroClient client = Fixtures.Client(_stub);
            client.Dispose();
            client.Dispose(); // twice is fine
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.Meta.VersionAsync());
        }

        [Fact]
        public void OptionsAreCopiedAtConstruction()
        {
            var options = new LifeAstroClientOptions { ApiKey = "dv_live_x", BaseUrl = "https://a.test", MaxRetries = 1 };
            using var client = new LifeAstroClient(options);
            options.BaseUrl = "https://b.test";
            options.MaxRetries = 5;
            Assert.Equal("https://a.test", client.BaseUrl);
            Assert.Equal(1, client.MaxRetries);
        }

        [Fact]
        public void ArgumentsAreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => new LifeAstroClient((LifeAstroClientOptions)null!));
            Assert.Throws<ArgumentNullException>(() => new LifeAstroClient((HttpClient)null!));
            using LifeAstroClient client = Fixtures.Client(_stub);
            Assert.Throws<ArgumentNullException>(() => { _ = client.Chart.PlanetsAsync(null!); });
            Assert.Throws<ArgumentNullException>(() => { _ = client.RequestAsync(null!); });
        }

        [Fact]
        public void VersionMatchesThePackageVersion()
        {
            string? dir = AppContext.BaseDirectory;
            string? props = null;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "Directory.Build.props");
                if (File.Exists(candidate) && File.ReadAllText(candidate).Contains("<Version>"))
                {
                    props = candidate;
                    break;
                }

                dir = Path.GetDirectoryName(dir);
            }

            Assert.NotNull(props);
            string version = Regex.Match(File.ReadAllText(props!), "<Version>([^<]+)</Version>").Groups[1].Value;
            Assert.Equal(LifeAstroClient.Version, version);

            string? informational = typeof(LifeAstroClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Assert.Equal(version, informational!.Split('+')[0]);
        }
    }
}
