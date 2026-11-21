using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Params;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LifeAstroApi.Extensions.DependencyInjection.Tests
{
    public class ServiceCollectionTests
    {
        /// <summary>Records requests instead of sending them.</summary>
        private sealed class RecordingHandler : HttpMessageHandler
        {
            internal List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                lock (Requests)
                {
                    Requests.Add(request);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"version\":\"test\"}", Encoding.UTF8, "application/json"),
                });
            }
        }

        private static IConfiguration Config(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        [Fact]
        public async Task OptionsBindFromConfiguration()
        {
            IConfiguration config = Config(new Dictionary<string, string?>
            {
                ["LifeAstroApi:ApiKey"] = "dv_live_fromconfig",
                ["LifeAstroApi:BaseUrl"] = "https://sdk.test/",
                ["LifeAstroApi:Timeout"] = "00:00:12",
                ["LifeAstroApi:MaxRetries"] = "4",
                ["LifeAstroApi:DefaultHeaders:X-Tenant"] = "acme",
            });
            var handler = new RecordingHandler();
            var services = new ServiceCollection();
            services.AddLifeAstro(config.GetSection("LifeAstroApi")).ConfigurePrimaryHttpMessageHandler(() => handler);

            using ServiceProvider sp = services.BuildServiceProvider();
            LifeAstroClientOptions options = sp.GetRequiredService<IOptions<LifeAstroClientOptions>>().Value;
            Assert.Equal("dv_live_fromconfig", options.ApiKey);
            Assert.Equal(TimeSpan.FromSeconds(12), options.Timeout);
            Assert.Equal(4, options.MaxRetries);

            LifeAstroClient client = sp.GetRequiredService<LifeAstroClient>();
            Assert.Same(client, sp.GetRequiredService<LifeAstroClient>()); // a singleton
            Assert.Equal("https://sdk.test", client.BaseUrl);
            Assert.Equal(TimeSpan.FromSeconds(12), client.Timeout);
            Assert.Equal(4, client.MaxRetries);

            await client.Panchang.TithiAsync(new PanchangTithiParams(19.076, 72.8777, "Asia/Kolkata") { Date = "2027-01-15" });
            HttpRequestMessage sent = Assert.Single(handler.Requests);
            Assert.Equal("https://sdk.test/v1/panchang/tithi?date=2027-01-15&lat=19.076&lon=72.8777&tz=Asia%2FKolkata", sent.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer dv_live_fromconfig", string.Join(",", sent.Headers.GetValues("Authorization")));
            Assert.Equal("acme", string.Join(",", sent.Headers.GetValues("X-Tenant")));
            Assert.Equal("lifeastroapi-dotnet/" + LifeAstroClient.Version, string.Join(" ", sent.Headers.GetValues("User-Agent")));
        }

        [Fact]
        public async Task OptionsFromCodeAndAnOnResponseHook()
        {
            var handler = new RecordingHandler();
            var seen = new List<ResponseInfo>();
            var services = new ServiceCollection();
            services.AddLifeAstro(
                Config(new Dictionary<string, string?> { ["ApiKey"] = "dv_live_x", ["BaseUrl"] = "https://sdk.test" }),
                o => o.OnResponse = seen.Add)
                .ConfigurePrimaryHttpMessageHandler(() => handler);
            using ServiceProvider sp = services.BuildServiceProvider();
            await sp.GetRequiredService<LifeAstroClient>().Meta.VersionAsync();
            Assert.Single(seen);
            Assert.Equal("/v1/meta/version", seen[0].Path);

            var services2 = new ServiceCollection();
            services2.AddLifeAstro(o =>
            {
                o.ApiKey = "dv_live_code";
                o.BaseUrl = "https://sdk.test";
            }).ConfigurePrimaryHttpMessageHandler(() => handler);
            using ServiceProvider sp2 = services2.BuildServiceProvider();
            await sp2.GetRequiredService<LifeAstroClient>().Meta.VersionAsync();
            Assert.Equal("Bearer dv_live_code", string.Join(",", handler.Requests[1].Headers.GetValues("Authorization")));
        }

        [Fact]
        public void TheDefaultsApplyWhenTheSectionIsEmpty()
        {
            var services = new ServiceCollection();
            services.AddLifeAstro(Config(new Dictionary<string, string?> { ["ApiKey"] = "dv_live_x" }));
            using ServiceProvider sp = services.BuildServiceProvider();
            LifeAstroClient client = sp.GetRequiredService<LifeAstroClient>();
            Assert.Equal(LifeAstroClient.DefaultBaseUrl, client.BaseUrl);
            Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
            Assert.Equal(2, client.MaxRetries);
        }

        [Fact]
        public void AUserRegistrationWinsWhetherItComesBeforeOrAfter()
        {
            var mine = new LifeAstroClient(new LifeAstroClientOptions { ApiKey = "dv_live_mine", BaseUrl = "https://mine.test" });

            var before = new ServiceCollection();
            before.AddSingleton(mine);
            before.AddLifeAstro(o => o.ApiKey = "dv_live_theirs");
            using (ServiceProvider sp = before.BuildServiceProvider())
            {
                Assert.Same(mine, sp.GetRequiredService<LifeAstroClient>());
            }

            var after = new ServiceCollection();
            after.AddLifeAstro(o => o.ApiKey = "dv_live_theirs");
            after.AddSingleton(mine);
            using (ServiceProvider sp = after.BuildServiceProvider())
            {
                Assert.Same(mine, sp.GetRequiredService<LifeAstroClient>());
            }

            var twice = new ServiceCollection();
            twice.AddLifeAstro(o => o.ApiKey = "dv_live_a");
            twice.AddLifeAstro(o => o.ApiKey = "dv_live_b");
            Assert.Single(twice, d => d.ServiceType == typeof(LifeAstroClient));
            mine.Dispose();
        }

        [Fact]
        public void AMissingKeySaysSoWhenTheClientIsResolved()
        {
            string? before = Environment.GetEnvironmentVariable(LifeAstroClient.ApiKeyEnvironmentVariable);
            if (!string.IsNullOrEmpty(before))
            {
                return; // the machine has a key in its environment; nothing to check
            }

            var services = new ServiceCollection();
            services.AddLifeAstro(Config(new Dictionary<string, string?>()));
            using ServiceProvider sp = services.BuildServiceProvider();
            InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<LifeAstroClient>());
            Assert.Contains("LIFEASTRO_API_KEY", e.Message);
        }

        [Fact]
        public void TheHttpClientIsTheNamedFactoryClientWithNoTimeoutOfItsOwn()
        {
            var services = new ServiceCollection();
            IHttpClientBuilder builder = services.AddLifeAstro(o => o.ApiKey = "dv_live_x");
            Assert.Equal(LifeAstroServiceCollectionExtensions.HttpClientName, builder.Name);
            using ServiceProvider sp = services.BuildServiceProvider();
            HttpClient http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(LifeAstroServiceCollectionExtensions.HttpClientName);
            Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
        }

        [Fact]
        public void ArgumentsAreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddLifeAstro(o => { }));
            Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddLifeAstro((IConfiguration)null!));
            Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddLifeAstro((Action<LifeAstroClientOptions>)null!));
        }
    }
}
