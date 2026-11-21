using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LifeAstroApi
{
    /// <summary>
    /// Settings of a <see cref="LifeAstroClient"/>. The client copies them when it is created, so later
    /// changes to this object do not affect it. In ASP.NET Core, <c>services.AddLifeAstro(...)</c> (package
    /// <c>LifeAstroApi.Extensions.DependencyInjection</c>) binds them from configuration:
    /// <code>
    /// "LifeAstroApi": { "ApiKey": "dv_live_...", "BaseUrl": "https://api.lifeastroapi.com", "Timeout": "00:00:30", "MaxRetries": 2 }
    /// </code>
    /// </summary>
    public sealed class LifeAstroClientOptions
    {
        /// <summary>
        /// The API key (<c>dv_live_...</c>). When null or blank, the <c>LIFEASTRO_API_KEY</c> environment
        /// variable is used; without either, creating the client throws. Get one at
        /// https://lifeastroapi.com/dashboard/keys
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// The API base URL; default <c>https://api.lifeastroapi.com</c>. Override it for a local server
        /// (<c>http://localhost:8080</c>). A trailing slash is removed.
        /// </summary>
        public string BaseUrl { get; set; } = LifeAstroClient.DefaultBaseUrl;

        /// <summary>
        /// The per-request timeout, from sending the request to the last byte of the body; default 30
        /// seconds. Each retry gets the full timeout again.
        /// </summary>
        public TimeSpan Timeout { get; set; } = LifeAstroClient.DefaultTimeout;

        /// <summary>
        /// Automatic retries on 408, 425, 429, 500, 502, 503, 504, network errors and timeouts, with
        /// exponential backoff that honours <c>Retry-After</c>; default 2. A negative value counts as 0
        /// (one attempt).
        /// </summary>
        public int MaxRetries { get; set; } = LifeAstroClient.DefaultMaxRetries;

        /// <summary>
        /// Called with the <see cref="ResponseInfo"/> of every call: request id, credits left, rate limit,
        /// cache and language. Use it to log usage or warn when credits run low. Runs once per final
        /// response, success or HTTP error, before a per-call hook; an exception it throws is ignored.
        /// </summary>
        public Action<ResponseInfo>? OnResponse { get; set; }

        /// <summary>Extra headers sent on every request. They win over the SDK's own headers.</summary>
        public IDictionary<string, string> DefaultHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The System.Text.Json options the generic methods (<c>BasicAsync&lt;T&gt;</c>) deserialize your
        /// types with. Default: <see cref="LifeAstroJson.DefaultOptions"/> (snake_case names, case
        /// insensitive, numbers also read from strings).
        /// </summary>
        public JsonSerializerOptions? JsonSerializerOptions { get; set; }
    }
}
