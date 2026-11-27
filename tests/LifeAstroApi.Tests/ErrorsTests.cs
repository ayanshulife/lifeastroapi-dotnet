using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeAstroApi.Params;
using Xunit;

namespace LifeAstroApi.Tests
{
    /// <summary>The "errors" and "error status mapping" cases of the Node SDK's client.test.ts.</summary>
    public class ErrorsTests
    {
        private readonly StubHandler _stub = new StubHandler();

        private async Task<LifeAstroException> Fail(int status, string body, IDictionary<string, string>? headers = null)
        {
            _stub.Respond(Reply.Json(status, body).With(headers ?? new Dictionary<string, string>()));
            using LifeAstroClient client = Fixtures.Client(_stub);
            return await Assert.ThrowsAnyAsync<LifeAstroException>(() => client.Chart.PlanetsAsync(new ChartPlanetsParams(Fixtures.Arjun)));
        }

        [Fact]
        public async Task Maps401ToAuthenticationExceptionWithCodeAndRequestId()
        {
            LifeAstroException e = await Fail(401, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"bad key\",\"request_id\":\"req_1\"}}");
            Assert.IsType<AuthenticationException>(e);
            Assert.Equal("unauthorized", e.Code);
            Assert.Equal(401, e.Status);
            Assert.Equal("req_1", e.RequestId);
            Assert.Equal("bad key", e.Message);
            Assert.True(e.Body.HasValue);
            Assert.Contains("401 unauthorized, request req_1", e.ToString());
        }

        [Fact]
        public async Task Maps402ToPaymentRequired()
        {
            Assert.IsType<PaymentRequiredException>(await Fail(402, "{\"error\":{\"code\":\"insufficient_credits\",\"message\":\"no credits\"}}"));
        }

        [Fact]
        public async Task Maps400ToBadRequest()
        {
            LifeAstroException e = await Fail(400, "{\"error\":{\"code\":\"invalid_param\",\"message\":\"bad lat\"}}");
            Assert.IsType<BadRequestException>(e);
            Assert.Equal("invalid_param", e.Code);
        }

        [Fact]
        public async Task Maps429ToRateLimitWithRetryAfter()
        {
            LifeAstroException e = await Fail(429, "{\"error\":{\"code\":\"rate_limited\",\"message\":\"slow down\"}}", new Dictionary<string, string> { ["Retry-After"] = "7" });
            RateLimitException r = Assert.IsType<RateLimitException>(e);
            Assert.Equal(TimeSpan.FromSeconds(7), r.RetryAfter);
        }

        [Fact]
        public async Task ParsesTheHttpDateFormOfRetryAfter()
        {
            LifeAstroException e = await Fail(429, "{\"error\":{\"code\":\"rate_limited\"}}", new Dictionary<string, string> { ["Retry-After"] = "Wed, 21 Oct 2099 07:28:00 GMT" });
            TimeSpan? ra = Assert.IsType<RateLimitException>(e).RetryAfter;
            Assert.NotNull(ra);
            Assert.True(ra!.Value > TimeSpan.Zero);
            Assert.Equal(TimeSpan.FromSeconds(60), HttpCore.ParseRetryAfter("Wed, 21 Oct 2026 07:28:00 GMT", DateTimeOffset.Parse("2026-10-21T07:27:00Z", System.Globalization.CultureInfo.InvariantCulture)));
            Assert.Equal(TimeSpan.Zero, HttpCore.ParseRetryAfter("-3", DateTimeOffset.UtcNow));
            Assert.Equal(TimeSpan.FromMilliseconds(1500), HttpCore.ParseRetryAfter(" 1.5 ", DateTimeOffset.UtcNow));
            Assert.Null(HttpCore.ParseRetryAfter("soon", DateTimeOffset.UtcNow));
            Assert.Null(HttpCore.ParseRetryAfter("", DateTimeOffset.UtcNow));
        }

        [Fact]
        public async Task AFractionalRetryAfterHeaderReachesTheParser()
        {
            LifeAstroException e = await Fail(429, "{}", new Dictionary<string, string> { ["Retry-After"] = "1.5" });
            Assert.Equal(TimeSpan.FromMilliseconds(1500), Assert.IsType<RateLimitException>(e).RetryAfter);
        }

        [Theory]
        [InlineData(403, typeof(PermissionException))]
        [InlineData(404, typeof(NotFoundException))]
        [InlineData(500, typeof(ServerException))]
        [InlineData(503, typeof(ServerException))]
        [InlineData(418, typeof(LifeAstroException))]
        public async Task StatusMapping(int status, Type type)
        {
            LifeAstroException e = await Fail(status, "{\"error\":{\"code\":\"x\",\"message\":\"m\"}}");
            Assert.Equal(type, e.GetType());
            Assert.Equal(status, e.Status);
            Assert.IsAssignableFrom<LifeAstroException>(e);
        }

        [Fact]
        public async Task DefaultCodesWhenTheBodyHasNoError()
        {
            LifeAstroException e = await Fail(502, "<html>bad gateway</html>", new Dictionary<string, string> { ["X-Request-ID"] = "hdr_1" });
            Assert.Equal("non_json_response", e.Code);
            Assert.IsType<ServerException>(e);
            Assert.Contains("HTTP 502", e.Message);
            Assert.Contains("<html>bad gateway</html>", e.Message);
            Assert.Equal("hdr_1", e.RequestId);
            Assert.Equal("<html>bad gateway</html>", e.Body!.Value.GetString());
            Assert.Equal("bad_request", (await Fail(409, "{}")).Code);
        }

        [Fact]
        public void TheConnectionExceptionsHaveStatusZero()
        {
            var timeout = new LifeAstroTimeoutException("Request timed out after 5ms", null);
            Assert.Equal(0, timeout.Status);
            Assert.Equal("timeout", timeout.Code);
            Assert.IsAssignableFrom<LifeAstroConnectionException>(timeout);
            Assert.Contains("(timeout)", timeout.ToString());
        }
    }
}
