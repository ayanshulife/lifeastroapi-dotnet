using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LifeAstroApi
{
    /// <summary>
    /// The LifeAstroAPI client.
    /// <code>
    /// using var client = new LifeAstroClient(new LifeAstroClientOptions { ApiKey = "dv_live_..." }); // or set LIFEASTRO_API_KEY
    ///
    /// JsonElement day = await client.Panchang.BasicAsync(
    ///     new PanchangBasicParams(19.076, 72.8777, "Asia/Kolkata") { Lang = Lang.Gu });
    ///
    /// var arjun = new BirthInput("1990-01-15", "10:30", "Asia/Kolkata", 19.076, 72.8777);
    /// JsonElement planets = await client.Chart.PlanetsAsync(new ChartPlanetsParams(arjun));
    /// </code>
    /// </summary>
    /// <remarks>
    /// Every resource group is a property (<c>Panchang</c>, <c>Chart</c>, <c>Milan</c>,
    /// <c>Western.Natal</c> and so on) with one async method per endpoint. Each method returns the
    /// response JSON as a <see cref="JsonElement"/>, or maps it to your own type with the generic
    /// overload (<c>BasicAsync&lt;MyDay&gt;(...)</c>).
    /// <para>
    /// The client is immutable and thread-safe: create one and share it across the application (a
    /// singleton). <see cref="Dispose"/> releases the HTTP client it created; an
    /// <see cref="HttpClient"/> you passed in is left open.
    /// </para>
    /// </remarks>
    public sealed partial class LifeAstroClient : IDisposable
    {
        /// <summary>SDK version, sent in the <c>User-Agent</c> header (<c>lifeastroapi-dotnet/1.0.0</c>).</summary>
        public const string Version = "1.0.0";

        /// <summary>The production API.</summary>
        public const string DefaultBaseUrl = "https://api.lifeastroapi.com";

        /// <summary>The environment variable read when no API key is given.</summary>
        public const string ApiKeyEnvironmentVariable = "LIFEASTRO_API_KEY";

        /// <summary>Automatic retries unless <see cref="LifeAstroClientOptions.MaxRetries"/> says otherwise.</summary>
        public const int DefaultMaxRetries = 2;

        /// <summary>Per-request timeout unless <see cref="LifeAstroClientOptions.Timeout"/> says otherwise: 30 seconds.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpCore _core;

        /// <summary>A client with the key from the <c>LIFEASTRO_API_KEY</c> environment variable and the defaults.</summary>
        /// <exception cref="InvalidOperationException">The variable is not set.</exception>
        public LifeAstroClient()
            : this(null, new LifeAstroClientOptions(), Environment.GetEnvironmentVariable)
        {
        }

        /// <summary>A client with this API key and the defaults for everything else.</summary>
        /// <param name="apiKey">The API key (<c>dv_live_...</c>); null or blank reads <c>LIFEASTRO_API_KEY</c>.</param>
        /// <exception cref="InvalidOperationException">There is no API key.</exception>
        public LifeAstroClient(string? apiKey)
            : this(null, new LifeAstroClientOptions { ApiKey = apiKey }, Environment.GetEnvironmentVariable)
        {
        }

        /// <summary>A client with these settings. The client creates (and disposes) its own <see cref="HttpClient"/>.</summary>
        /// <param name="options">The settings; copied, so later changes do not affect the client.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
        /// <exception cref="InvalidOperationException">There is no API key.</exception>
        /// <exception cref="ArgumentException">The base URL is not an http(s) URL, or the timeout is not positive.</exception>
        public LifeAstroClient(LifeAstroClientOptions options)
            : this(null, Wire.NotNull(options, nameof(options)), Environment.GetEnvironmentVariable)
        {
        }

        /// <summary>
        /// A client that sends its requests with your <see cref="HttpClient"/>, for example one from
        /// <c>IHttpClientFactory</c>, or one with a proxy or custom TLS. The client never disposes it.
        /// Requests use absolute URLs from <see cref="LifeAstroClientOptions.BaseUrl"/>; the
        /// <see cref="HttpClient.BaseAddress"/> is ignored. The SDK applies its own timeout per call, so
        /// give the <see cref="HttpClient"/> a longer <see cref="HttpClient.Timeout"/> (or
        /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>) than <see cref="LifeAstroClientOptions.Timeout"/>.
        /// </summary>
        /// <param name="httpClient">The HTTP client; not disposed by this client.</param>
        /// <param name="options">The settings, or null for the defaults (key from <c>LIFEASTRO_API_KEY</c>).</param>
        /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is null.</exception>
        /// <exception cref="InvalidOperationException">There is no API key.</exception>
        public LifeAstroClient(HttpClient httpClient, LifeAstroClientOptions? options = null)
            : this(Wire.NotNull(httpClient, nameof(httpClient)), options ?? new LifeAstroClientOptions(), Environment.GetEnvironmentVariable)
        {
        }

        /// <summary>The constructor behind the others; <paramref name="env"/> is replaced in tests.</summary>
        internal LifeAstroClient(HttpClient? httpClient, LifeAstroClientOptions options, Func<string, string?> env)
        {
            string? key = !string.IsNullOrWhiteSpace(options.ApiKey) ? options.ApiKey : env(ApiKeyEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "LifeAstro: an API key is required. Set LifeAstroClientOptions.ApiKey or the " + ApiKeyEnvironmentVariable
                    + " environment variable. Get one at https://lifeastroapi.com/dashboard/keys");
            }

            string baseUrl = NormaliseBaseUrl(options.BaseUrl);
            if (options.Timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), options.Timeout, "LifeAstroClientOptions.Timeout must be positive.");
            }

            var headers = (options.DefaultHeaders ?? new Dictionary<string, string>())
                .Where(h => !string.IsNullOrEmpty(h.Key) && h.Value != null)
                .ToList();
            bool owns = httpClient == null;
            HttpClient http = httpClient ?? CreateHttpClient(options.Timeout);
            _core = new HttpCore(
                http,
                owns,
                key!.Trim(),
                baseUrl,
                options.Timeout,
                Math.Max(0, options.MaxRetries),
                "lifeastroapi-dotnet/" + Version,
                headers,
                options.OnResponse,
                options.JsonSerializerOptions ?? LifeAstroJson.DefaultOptions);
            InitResources();
        }

        /// <summary>The API base URL this client calls, without a trailing slash.</summary>
        public string BaseUrl => _core.BaseUrl;

        /// <summary>The per-request timeout.</summary>
        public TimeSpan Timeout => _core.Timeout;

        /// <summary>How many times a call is retried on 408, 425, 429, 500, 502, 503, 504, network errors and timeouts.</summary>
        public int MaxRetries => _core.MaxRetries;

        internal HttpCore Core => _core;

        /// <summary>
        /// Calls any <c>/v1/*</c> endpoint directly, for endpoints this version does not wrap yet.
        /// <code>
        /// JsonElement t = await client.RequestAsync("/v1/panchang/tithi", new Dictionary&lt;string, object?&gt;
        /// {
        ///     ["lat"] = 28.61, ["lon"] = 77.2, ["tz"] = "Asia/Kolkata", ["date"] = "2027-01-15",
        /// });
        /// </code>
        /// </summary>
        /// <param name="path">The path, e.g. <c>/v1/panchang/tithi</c>.</param>
        /// <param name="query">
        /// Query parameters by wire name, or null. Values are strings, numbers, booleans, the SDK's enums,
        /// <see cref="LifeAstroDate"/> / <see cref="LifeAstroTime"/>, <see cref="DateTime"/> (sent as
        /// <c>YYYY-MM-DD</c>) or <see cref="TimeSpan"/> (sent as <c>HH:mm</c>); null and empty values are
        /// left out. Formatting is culture-invariant.
        /// </param>
        /// <param name="options">Per-call timeout, raw body or <c>OnResponse</c> hook, or null.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The response JSON.</returns>
        /// <exception cref="LifeAstroException">An API error or a network failure.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public Task<JsonElement> RequestAsync(
            string path,
            IEnumerable<KeyValuePair<string, object?>>? query = null,
            RequestOptions? options = null,
            CancellationToken cancellationToken = default) =>
            _core.SendAsync<JsonElement>(Wire.NotNull(path, nameof(path)), ToWire(query), options, cancellationToken);

        /// <summary>
        /// Calls any <c>/v1/*</c> endpoint directly and maps the response to <typeparamref name="T"/> with
        /// System.Text.Json (<see cref="LifeAstroClientOptions.JsonSerializerOptions"/>).
        /// </summary>
        /// <typeparam name="T">Your type; <see cref="JsonElement"/>, <c>JsonNode</c>, a dictionary or a DTO.</typeparam>
        /// <param name="path">The path, e.g. <c>/v1/panchang/tithi</c>.</param>
        /// <param name="query">Query parameters by wire name, or null; see <see cref="RequestAsync(string, IEnumerable{KeyValuePair{string, object}}, RequestOptions, CancellationToken)"/>.</param>
        /// <param name="options">Per-call timeout, raw body or <c>OnResponse</c> hook, or null.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The mapped response; default when the response is JSON null or empty.</returns>
        /// <exception cref="LifeAstroException">An API error, a network failure, or a response that does not map to <typeparamref name="T"/> (code <c>invalid_response</c>).</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public Task<T?> RequestAsync<T>(
            string path,
            IEnumerable<KeyValuePair<string, object?>>? query = null,
            RequestOptions? options = null,
            CancellationToken cancellationToken = default) =>
            _core.SendAsync<T>(Wire.NotNull(path, nameof(path)), ToWire(query), options, cancellationToken);

        /// <summary>
        /// Releases the HTTP client this client created (an <see cref="HttpClient"/> passed to the
        /// constructor is left open). Calls made afterwards throw <see cref="ObjectDisposedException"/>.
        /// </summary>
        public void Dispose() => _core.Dispose();

        /// <summary>Shows the base URL and a masked key (<c>dv_live_****</c>), never the key itself.</summary>
        /// <returns>A description of the client.</returns>
        public override string ToString() =>
            "LifeAstroClient { BaseUrl = " + _core.BaseUrl + ", ApiKey = " + _core.MaskedKey + ", Version = " + Version + " }";

        /// <summary>Sends a generated method's parameters.</summary>
        internal Task<T?> SendAsync<T>(LifeAstroParams parameters, RequestOptions? options, CancellationToken cancellationToken) =>
            _core.SendAsync<T>(parameters.ToPath(), parameters.ToQuery(), options, cancellationToken);

        /// <summary>Sends a generated method that has no parameters.</summary>
        internal Task<T?> SendAsync<T>(string path, RequestOptions? options, CancellationToken cancellationToken) =>
            _core.SendAsync<T>(path, null, options, cancellationToken);

        private static IEnumerable<KeyValuePair<string, string>>? ToWire(IEnumerable<KeyValuePair<string, object?>>? query)
        {
            if (query == null)
            {
                return null;
            }

            var list = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, object?> pair in query)
            {
                string? value = QueryString.ToWire(pair.Value);
                if (pair.Key != null && value != null)
                {
                    list.Add(new KeyValuePair<string, string>(pair.Key, value));
                }
            }

            return list;
        }

        private static string NormaliseBaseUrl(string? baseUrl)
        {
            string url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(uri.Host))
            {
                throw new ArgumentException("LifeAstroClientOptions.BaseUrl must be an http or https URL: \"" + baseUrl + "\".", nameof(baseUrl));
            }

            return url;
        }

        private static HttpClient CreateHttpClient(TimeSpan timeout)
        {
#if NET8_0_OR_GREATER
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                // A long-lived client still follows DNS changes.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = timeout,
            };
#else
            var handler = new HttpClientHandler();
            if (handler.SupportsAutomaticDecompression)
            {
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            }
#endif

            // The SDK bounds every call with its own timeout (per call, retries included).
            return new HttpClient(handler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }
    }
}
