using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Params;
using Xunit;

namespace LifeAstroApi.Tests
{
    /// <summary>The "retries", "abort &amp; timeout" and network cases of the Node SDK's client.test.ts, plus .NET cancellation.</summary>
    public class RetryTests
    {
        private static readonly ChartPlanetsParams Planets = new ChartPlanetsParams(Fixtures.Arjun);
        private static readonly PanchangTithiParams TithiParams = new PanchangTithiParams(1, 2, "UTC") { Date = "2027-01-15" };
        private readonly StubHandler _stub = new StubHandler();

        [Fact]
        public async Task RetriesA429ThenSucceeds()
        {
            int n = 0;
            _stub.Respond(_ => Interlocked.Increment(ref n) == 1
                ? Reply.Json(429, "{\"error\":{\"code\":\"rate_limited\"}}").With("Retry-After", "0")
                : Reply.Ok("{\"data\":{\"ok\":true}}"));
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.MaxRetries = 2);
            JsonElement res = await client.Chart.PlanetsAsync(Planets);
            Assert.True(res.GetProperty("ok").GetBoolean());
            Assert.Equal(2, _stub.Calls.Count);
        }

        [Fact]
        public async Task GivesUpAfterMaxRetries()
        {
            _stub.Respond(Reply.Json(503, "{\"error\":{\"code\":\"unavailable\"}}").With("Retry-After", "0"));
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.MaxRetries = 2);
            ServerException e = await Assert.ThrowsAsync<ServerException>(() => client.Chart.PlanetsAsync(Planets));
            Assert.Equal(503, e.Status);
            Assert.Equal(3, _stub.Calls.Count); // initial + 2 retries
        }

        [Fact]
        public async Task ClampsANegativeMaxRetriesToZero()
        {
            _stub.Respond(Reply.Json(503, "{\"error\":{\"code\":\"unavailable\"}}").With("Retry-After", "0"));
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.MaxRetries = -5);
            Assert.Equal(0, client.MaxRetries);
            await Assert.ThrowsAsync<ServerException>(() => client.Chart.PlanetsAsync(Planets));
            Assert.Single(_stub.Calls);
        }

        [Fact]
        public async Task DoesNotRetryANonRetryable4xx()
        {
            _stub.Respond(Reply.Json(400, "{\"error\":{\"code\":\"invalid_param\"}}"));
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.MaxRetries = 3);
            await Assert.ThrowsAsync<BadRequestException>(() => client.Chart.PlanetsAsync(Planets));
            Assert.Single(_stub.Calls);
        }

        [Theory]
        [InlineData(408)]
        [InlineData(425)]
        [InlineData(429)]
        [InlineData(500)]
        [InlineData(502)]
        [InlineData(503)]
        [InlineData(504)]
        public async Task RetriesEveryRetryableStatus(int status)
        {
            int n = 0;
            _stub.Respond(_ => Interlocked.Increment(ref n) == 1 ? Reply.Json(status, "{}").With("Retry-After", "0") : Reply.Ok("{\"ok\":1}"));
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.MaxRetries = 1);
            Assert.Equal(1, (await client.Meta.VersionAsync()).GetProperty("ok").GetInt32());
            Assert.Equal(2, _stub.Calls.Count);
        }

        [Fact]
        public async Task A501IsNotRetried()
        {
            _stub.Respond(Reply.Json(501, "{}"));
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.MaxRetries = 2);
            await Assert.ThrowsAsync<ServerException>(() => client.Meta.VersionAsync());
            Assert.Single(_stub.Calls);
        }

        [Fact]
        public async Task HonoursRetryAfterBetweenAttempts()
        {
            int n = 0;
            _stub.Respond(_ => Interlocked.Increment(ref n) == 1 ? Reply.Json(429, "{}").With("Retry-After", "7") : Reply.Ok("{}"));
            TimeSpan? seen = null;
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.MaxRetries = 1);
            client.Core.Backoff = (attempt, retryAfter) =>
            {
                seen = HttpCore.DefaultBackoff(attempt, retryAfter);
                return TimeSpan.Zero;
            };
            await client.Meta.VersionAsync();
            Assert.Equal(TimeSpan.FromSeconds(7), seen);
        }

        [Fact]
        public void BackoffIsExponentialWithJitterAndHonoursRetryAfterUpToThirtySeconds()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                long baseMs = Math.Min(500L << attempt, 8000L);
                double ms = HttpCore.DefaultBackoff(attempt, null).TotalMilliseconds;
                Assert.True(ms >= baseMs && ms < baseMs + 250, attempt + " -> " + ms);
            }

            Assert.Equal(TimeSpan.FromSeconds(7), HttpCore.DefaultBackoff(0, TimeSpan.FromSeconds(7)));
            Assert.Equal(TimeSpan.FromSeconds(30), HttpCore.DefaultBackoff(0, TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public async Task RetriesANetworkErrorThenSucceeds()
        {
            using var server = new RawServer(async (n, stream) =>
            {
                if (n == 1)
                {
                    return; // closes without a response
                }

                await RawServer.AnswerAsync(stream, "{\"data\":{\"ok\":true}}");
            });
            using var client = new LifeAstroClient(new LifeAstroClientOptions { ApiKey = "dv_live_test", BaseUrl = server.BaseUrl, MaxRetries = 2 });
            client.Core.Backoff = (_, _) => TimeSpan.Zero;
            JsonElement res = await client.Chart.PlanetsAsync(Planets);
            Assert.True(res.GetProperty("ok").GetBoolean());
            Assert.Equal(2, server.Connections);
        }

        [Fact]
        public async Task SurfacesNetworkFailuresAsConnectionExceptions()
        {
            using var client = new LifeAstroClient(new LifeAstroClientOptions { ApiKey = "dv_live_test", BaseUrl = "http://127.0.0.1:" + RawServer.ClosedPort(), MaxRetries = 0 });
            LifeAstroConnectionException e = await Assert.ThrowsAsync<LifeAstroConnectionException>(() => client.Panchang.TithiAsync(TithiParams));
            Assert.Equal("connection_error", e.Code);
            Assert.Equal(0, e.Status);
            Assert.StartsWith("Network request failed", e.Message);
            Assert.NotNull(e.InnerException);
        }

        [Fact]
        public async Task TimesOutWithCodeTimeout()
        {
            _stub.Respond(Reply.Ok("{}").Delayed(3000));
            using LifeAstroClient client = Fixtures.Client(_stub);
            var watch = Stopwatch.StartNew();
            LifeAstroTimeoutException e = await Assert.ThrowsAsync<LifeAstroTimeoutException>(
                () => client.Panchang.TithiAsync(TithiParams, new RequestOptions { Timeout = TimeSpan.FromMilliseconds(200) }));
            Assert.Equal("timeout", e.Code);
            Assert.Equal("Request timed out after 200ms", e.Message);
            Assert.True(watch.ElapsedMilliseconds < 2000, "returned before the server answered: " + watch.ElapsedMilliseconds);
        }

        [Fact]
        public async Task TheTimeoutBoundsTheBodyDownloadToo()
        {
            // Headers arrive at once; the body never finishes.
            using var server = new RawServer(async (n, stream) =>
            {
                await RawServer.ReadRequestAsync(stream);
                await RawServer.WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 100\r\n\r\n{\"partial\":");
                await stream.FlushAsync();
                await Task.Delay(5000);
            });
            using var client = new LifeAstroClient(new LifeAstroClientOptions
            {
                ApiKey = "dv_live_test",
                BaseUrl = server.BaseUrl,
                MaxRetries = 0,
                Timeout = TimeSpan.FromMilliseconds(400),
            });
            var watch = Stopwatch.StartNew();
            LifeAstroTimeoutException e = await Assert.ThrowsAsync<LifeAstroTimeoutException>(() => client.Meta.VersionAsync());
            Assert.Equal("Request timed out after 400ms", e.Message);
            Assert.True(watch.ElapsedMilliseconds < 3000, "the body read was not bounded: " + watch.ElapsedMilliseconds);
        }

        [Fact]
        public async Task TimeoutsAreRetriedLikeNetworkErrors()
        {
            int n = 0;
            _stub.Respond(_ => Interlocked.Increment(ref n) == 1 ? Reply.Ok("{}").Delayed(3000) : Reply.Ok("{\"ok\":2}"));
            using LifeAstroClient client = Fixtures.Client(_stub, o =>
            {
                o.MaxRetries = 1;
                o.Timeout = TimeSpan.FromMilliseconds(300);
            });
            Assert.Equal(2, (await client.Meta.VersionAsync()).GetProperty("ok").GetInt32());
        }

        [Fact]
        public async Task CallerCancellationThrowsOperationCanceledAndIsNotRetried()
        {
            _stub.Respond(Reply.Ok("{}").Delayed(5000));
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.MaxRetries = 2);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var watch = Stopwatch.StartNew();
            OperationCanceledException e = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.Panchang.TithiAsync(TithiParams, cancellationToken: cts.Token));
            Assert.Equal(cts.Token, e.CancellationToken);
            Assert.True(watch.ElapsedMilliseconds < 3000);
            Assert.Single(_stub.Calls);
        }

        [Fact]
        public async Task APreCancelledTokenSendsNothing()
        {
            using LifeAstroClient client = Fixtures.Client(_stub);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Meta.VersionAsync(cancellationToken: cts.Token));
            Assert.Empty(_stub.Calls);
        }

        [Fact]
        public async Task CancellingDuringTheRetryWaitStopsTheCall()
        {
            _stub.Respond(Reply.Json(503, "{}").With("Retry-After", "10"));
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.MaxRetries = 2);
            client.Core.Backoff = HttpCore.DefaultBackoff;
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var watch = Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Meta.VersionAsync(cancellationToken: cts.Token));
            Assert.True(watch.ElapsedMilliseconds < 5000);
            Assert.Single(_stub.Calls);
        }

        [Fact]
        public async Task TheHttpClientsOwnTimeoutCountsAsATimeout()
        {
            _stub.Respond(Reply.Ok("{}").Delayed(3000));
            var http = new System.Net.Http.HttpClient(_stub) { Timeout = TimeSpan.FromMilliseconds(200) };
            using var client = new LifeAstroClient(http, new LifeAstroClientOptions { ApiKey = "dv_live_test", BaseUrl = Fixtures.BaseUrl, MaxRetries = 0 });
            await Assert.ThrowsAsync<LifeAstroTimeoutException>(() => client.Meta.VersionAsync());
        }

        [Fact]
        public void ARefusedConnectionIsASocketFailure()
        {
            // Guard for SurfacesNetworkFailuresAsConnectionExceptions: the closed port really refuses.
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Assert.ThrowsAny<SocketException>(() => socket.Connect("127.0.0.1", RawServer.ClosedPort()));
        }
    }
}
