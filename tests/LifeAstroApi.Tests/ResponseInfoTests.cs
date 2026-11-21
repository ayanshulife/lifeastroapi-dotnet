using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LifeAstroApi.Params;
using Xunit;

namespace LifeAstroApi.Tests
{
    /// <summary>The four "onResponse (credits, rate limit, request id)" cases of the Node SDK's client.test.ts.</summary>
    public class ResponseInfoTests
    {
        private static readonly Dictionary<string, string> AllHeaders = new Dictionary<string, string>
        {
            ["X-Request-ID"] = "52a520f261d3db37",
            ["X-Credits-Remaining"] = "64043",
            ["X-RateLimit-Limit"] = "250",
            ["X-RateLimit-Remaining"] = "249",
            ["X-RateLimit-Reset"] = "1794718800",
            ["X-Cache"] = "HIT",
            ["Content-Language"] = "gu",
        };

        private readonly StubHandler _stub = new StubHandler();

        [Fact]
        public async Task ReportsEveryHeaderOnASuccessClientLevelAndPerCall()
        {
            _stub.Respond(Reply.Ok("{\"ok\":1}").With(AllHeaders));
            var seen = new ConcurrentQueue<ResponseInfo>();
            var perCall = new ConcurrentQueue<ResponseInfo>();
            var order = new ConcurrentQueue<string>();
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.OnResponse = i =>
            {
                seen.Enqueue(i);
                order.Enqueue("client");
            });
            await client.Panchang.BasicAsync(
                new PanchangBasicParams(1, 2, "UTC") { Lang = Lang.Gu },
                new RequestOptions
                {
                    OnResponse = i =>
                    {
                        perCall.Enqueue(i);
                        order.Enqueue("call");
                    },
                });
            Assert.Single(seen);
            Assert.Equal(seen.ToList(), perCall.ToList());
            Assert.Equal(new[] { "client", "call" }, order.ToArray());
            Assert.Equal(
                new ResponseInfo(200, "/v1/panchang/basic", "52a520f261d3db37", 64043L, new RateLimit(250, 249, DateTimeOffset.FromUnixTimeSeconds(1794718800)), "HIT", "gu"),
                seen.Single());
        }

        [Fact]
        public async Task LeavesOutHeadersTheApiDidNotSend()
        {
            _stub.Respond(Reply.Ok("{\"ok\":1}").With("X-Request-ID", "abc"));
            var seen = new ConcurrentQueue<ResponseInfo>();
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.OnResponse = seen.Enqueue);
            await client.Meta.VersionAsync();
            Assert.Equal(new[] { new ResponseInfo(200, "/v1/meta/version", "abc", null, null, null, null) }, seen.ToArray());
        }

        [Fact]
        public async Task ReportsTheFinalErrorResponseOnceNotTheRetriedAttempts()
        {
            int n = 0;
            _stub.Respond(_ => Reply.Json(503, "{\"error\":{\"code\":\"billing_unavailable\",\"message\":\"x\"}}")
                .With("X-Request-ID", "r" + Interlocked.Increment(ref n))
                .With("Retry-After", "0"));
            var seen = new ConcurrentQueue<ResponseInfo>();
            using LifeAstroClient client = Fixtures.Client(_stub, o =>
            {
                o.MaxRetries = 2;
                o.OnResponse = seen.Enqueue;
            });
            await Assert.ThrowsAnyAsync<LifeAstroException>(() => client.Meta.VersionAsync());
            Assert.Equal(3, n);
            ResponseInfo only = Assert.Single(seen);
            Assert.Equal(503, only.Status);
            Assert.Equal("r3", only.RequestId);
        }

        [Fact]
        public async Task AThrowingHookNeverBreaksTheCall()
        {
            _stub.Respond(Reply.Ok("{\"v\":7}").With(AllHeaders));
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.OnResponse = _ => throw new InvalidOperationException("logger down"));
            var res = await client.Meta.VersionAsync(new RequestOptions { OnResponse = _ => throw new InvalidOperationException("also down") });
            Assert.Equal(7, res.GetProperty("v").GetInt32());
        }

        [Fact]
        public async Task NetworkFailuresDoNotReachTheHook()
        {
            var seen = new ConcurrentQueue<ResponseInfo>();
            using var client = new LifeAstroClient(new LifeAstroClientOptions
            {
                ApiKey = "dv_live_test",
                BaseUrl = "http://127.0.0.1:" + RawServer.ClosedPort(),
                MaxRetries = 0,
                OnResponse = seen.Enqueue,
            });
            await Assert.ThrowsAsync<LifeAstroConnectionException>(() => client.Meta.VersionAsync());
            Assert.Empty(seen);
        }

        [Fact]
        public async Task ReadsFractionalAndInvalidNumbersLikeNode()
        {
            _stub.Respond(Reply.Ok("{}").With("X-Credits-Remaining", "12.9").With("X-RateLimit-Limit", "x"));
            ResponseInfo? info = null;
            using LifeAstroClient client = Fixtures.Client(_stub, o => o.OnResponse = i => info = i);
            await client.Meta.VersionAsync();
            Assert.Equal(12L, info!.CreditsRemaining);
            Assert.Null(info.RateLimit);
        }
    }
}
