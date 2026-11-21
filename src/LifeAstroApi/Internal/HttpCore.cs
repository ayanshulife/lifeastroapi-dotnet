using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LifeAstroApi
{
    /// <summary>
    /// The request pipeline behind every method, a port of <c>src/http.ts</c> of the Node.js SDK (and
    /// <c>HttpCore</c> of the Java SDK): URL and query building, bearer auth, a timeout that bounds the
    /// whole call including the body, JSON decoding, <c>{ data, meta }</c> envelope unwrapping, error
    /// mapping, bounded retries with exponential backoff that honour <c>Retry-After</c>, and the
    /// <c>OnResponse</c> hook. Immutable and thread-safe.
    /// </summary>
    internal sealed class HttpCore : IDisposable
    {
        /// <summary>Statuses retried automatically (with backoff, up to <c>MaxRetries</c> times).</summary>
        internal static readonly int[] RetryableStatus = { 408, 425, 429, 500, 502, 503, 504 };

        private static readonly JsonElement JsonNull = ParseNull();

        private static readonly object RandomLock = new object();
        private static readonly Random Jitter = new Random();

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _apiKey;
        private readonly KeyValuePair<string, string>[] _defaultHeaders;
        private readonly Action<ResponseInfo>? _onResponse;
        private readonly JsonSerializerOptions _json;
        private int _disposed;

        internal HttpCore(
            HttpClient http,
            bool ownsHttp,
            string apiKey,
            string baseUrl,
            TimeSpan timeout,
            int maxRetries,
            string userAgent,
            IEnumerable<KeyValuePair<string, string>> defaultHeaders,
            Action<ResponseInfo>? onResponse,
            JsonSerializerOptions json)
        {
            _http = http;
            _ownsHttp = ownsHttp;
            _apiKey = apiKey;
            BaseUrl = baseUrl;
            Timeout = timeout;
            MaxRetries = maxRetries;
            UserAgent = userAgent;
            _defaultHeaders = defaultHeaders.ToArray();
            _onResponse = onResponse;
            _json = json;
        }

        internal string BaseUrl { get; }

        internal TimeSpan Timeout { get; }

        internal int MaxRetries { get; }

        internal string UserAgent { get; }

        /// <summary>The wait between attempts; replaced in tests.</summary>
        internal Func<int, TimeSpan?, TimeSpan> Backoff { get; set; } = DefaultBackoff;

        internal string MaskedKey
        {
            get
            {
                int cut = _apiKey.LastIndexOf('_');
                return (cut < 0 ? string.Empty : _apiKey.Substring(0, cut + 1)) + "****";
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsHttp)
            {
                _http.Dispose();
            }
        }

        /// <summary>One call: every attempt, the hooks, and the mapping to <typeparamref name="T"/>.</summary>
        internal async Task<T?> SendAsync<T>(
            string path,
            IEnumerable<KeyValuePair<string, string>>? query,
            RequestOptions? options,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(LifeAstroClient), "This LifeAstroClient has been disposed.");
            }

            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            string p = path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
            var uri = new Uri(BaseUrl + p + QueryString.Encode(query), UriKind.Absolute);
            TimeSpan timeout = options?.Timeout ?? Timeout;
            Result result = await ExecuteAsync(uri, p, timeout, options, cancellationToken).ConfigureAwait(false);
            return Convert<T>(p, result);
        }

        private async Task<Result> ExecuteAsync(Uri uri, string path, TimeSpan timeout, RequestOptions? options, CancellationToken cancellationToken)
        {
            for (int attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Step step = await AttemptAsync(uri, path, timeout, options, attempt, cancellationToken).ConfigureAwait(false);
                if (step.Error != null)
                {
                    throw step.Error;
                }

                if (step.Done)
                {
                    return step.Result;
                }

                await DelayAsync(step.Delay, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// One attempt: its outcome is a value, an error to throw, or a retry after a delay. The final
        /// response, success or error, goes to the hooks; retried attempts and network failures do not.
        /// </summary>
        private async Task<Step> AttemptAsync(Uri uri, string path, TimeSpan timeout, RequestOptions? options, int attempt, CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (HttpRequestMessage request = NewRequest(uri))
            {
                cts.CancelAfter(timeout);
                HttpResponseMessage response;
                byte[] bytes;
                try
                {
                    // ResponseContentRead: the task completes after the whole body is buffered, under the
                    // same token, so the timeout bounds the body download too.
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException e) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("The LifeAstroAPI call was cancelled by the caller.", e, cancellationToken);
                }
                catch (OperationCanceledException e)
                {
                    // Our timeout, or the HttpClient's own Timeout when that is shorter.
                    return Failed(attempt, TimedOut(timeout, e));
                }
                catch (HttpRequestException e)
                {
                    return Failed(attempt, NetworkFailure(e));
                }
                catch (IOException e)
                {
                    return Failed(attempt, NetworkFailure(e));
                }

                using (response)
                {
                    try
                    {
                        bytes = await ReadBodyAsync(response, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException e) when (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("The LifeAstroAPI call was cancelled by the caller.", e, cancellationToken);
                    }
                    catch (OperationCanceledException e)
                    {
                        return Failed(attempt, TimedOut(timeout, e));
                    }
                    catch (HttpRequestException e)
                    {
                        return Failed(attempt, NetworkFailure(e));
                    }
                    catch (IOException e)
                    {
                        return Failed(attempt, NetworkFailure(e));
                    }

                    int status = (int)response.StatusCode;
                    JsonElement? body = Decode(bytes);
                    string? headerRequestId = Header(response, "X-Request-ID");
                    if (status >= 200 && status < 300)
                    {
                        Emit(options, status, path, response);
                        JsonElement value = options?.Raw == true ? (body ?? JsonNull) : Unwrap(body);
                        return Step.Success(new Result(value, body, status, headerRequestId));
                    }

                    TimeSpan? retryAfter = ParseRetryAfter(Header(response, "Retry-After"), DateTimeOffset.UtcNow);
                    LifeAstroException error = LifeAstroException.FromResponse(status, body, retryAfter, headerRequestId);
                    if (Array.IndexOf(RetryableStatus, status) >= 0 && attempt < MaxRetries)
                    {
                        return Step.Retry(Backoff(attempt, retryAfter));
                    }

                    Emit(options, status, path, response);
                    return Step.Fail(error);
                }
            }
        }

        /// <summary>A failure without an HTTP response: retried while attempts are left.</summary>
        private Step Failed(int attempt, LifeAstroException failure) =>
            attempt < MaxRetries ? Step.Retry(Backoff(attempt, null)) : Step.Fail(failure);

        private HttpRequestMessage NewRequest(Uri uri)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            // Default headers come last, so a caller can override anything, Authorization included.
            foreach (KeyValuePair<string, string> h in _defaultHeaders)
            {
                request.Headers.Remove(h.Key);
                request.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            return request;
        }

        private static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken token)
        {
#if NET5_0_OR_GREATER
            byte[] raw = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
#else
            // The content is already buffered (ResponseContentRead), so this does no network I/O.
            token.ThrowIfCancellationRequested();
            byte[] raw = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
            return Decompress(raw, response.Content.Headers.ContentEncoding);
        }

        /// <summary>
        /// A gzip or deflate body that the HTTP handler did not decode (an <see cref="HttpClient"/> you
        /// passed in without automatic decompression).
        /// </summary>
        private static byte[] Decompress(byte[] raw, ICollection<string> encodings)
        {
            if (raw.Length == 0 || encodings.Count == 0)
            {
                return raw;
            }

            string enc = encodings.Last().Trim().ToLowerInvariant();
            if (enc != "gzip" && enc != "deflate")
            {
                return raw;
            }

            try
            {
                using (var input = new MemoryStream(raw))
                using (Stream unzip = enc == "gzip"
                    ? new GZipStream(input, CompressionMode.Decompress)
                    : OpenDeflate(input, raw))
                using (var output = new MemoryStream(raw.Length * 4))
                {
                    unzip.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch (InvalidDataException)
            {
                return raw;
            }
        }

        private static Stream OpenDeflate(MemoryStream input, byte[] raw)
        {
            // HTTP "deflate" is zlib-wrapped (RFC 1950): skip the 2-byte header when it is there.
            if (raw.Length > 2 && (raw[0] & 0x0F) == 8 && ((raw[0] << 8) | raw[1]) % 31 == 0)
            {
                input.Position = 2;
            }

            return new DeflateStream(input, CompressionMode.Decompress);
        }

        /// <summary>The body as JSON; text that is not JSON as a JSON string (as the Node.js SDK returns it); null when empty.</summary>
        private static JsonElement? Decode(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return null;
            }

            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, start, bytes.Length - start)))
                {
                    return doc.RootElement.Clone();
                }
            }
            catch (JsonException)
            {
                string text = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
                using (JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(text)))
                {
                    return doc.RootElement.Clone();
                }
            }
        }

        /// <summary>
        /// The API returns bare JSON today. A <c>{ data, meta }</c> envelope is unwrapped if a response
        /// ever carries one, so adding it on the server is not a breaking change.
        /// </summary>
        private static JsonElement Unwrap(JsonElement? body)
        {
            if (!body.HasValue)
            {
                return JsonNull;
            }

            JsonElement b = body.Value;
            return b.ValueKind == JsonValueKind.Object && b.TryGetProperty("data", out JsonElement data) ? data : b;
        }

        private T? Convert<T>(string path, Result result)
        {
            if (typeof(T) == typeof(JsonElement))
            {
                return (T)(object)result.Value;
            }

            if (typeof(T) == typeof(JsonElement?))
            {
                return (T)(object)(JsonElement?)result.Value;
            }

            if (result.Value.ValueKind == JsonValueKind.Null || result.Value.ValueKind == JsonValueKind.Undefined)
            {
                return default;
            }

            try
            {
                return result.Value.Deserialize<T>(_json);
            }
            catch (Exception e) when (e is JsonException || e is NotSupportedException || e is InvalidOperationException || e is ArgumentException)
            {
                throw new LifeAstroException(
                    "Could not read the response of " + path + " as " + typeof(T).FullName + ": " + e.Message,
                    "invalid_response",
                    result.Status,
                    result.RequestId,
                    result.Body,
                    e);
            }
        }

        private void Emit(RequestOptions? options, int status, string path, HttpResponseMessage response)
        {
            Action<ResponseInfo>? perCall = options?.OnResponse;
            if (_onResponse == null && perCall == null)
            {
                return;
            }

            ResponseInfo info = ReadInfo(status, path, response);
            Invoke(_onResponse, info);
            Invoke(perCall, info);
        }

        private static void Invoke(Action<ResponseInfo>? hook, ResponseInfo info)
        {
            if (hook == null)
            {
                return;
            }

            try
            {
                hook(info);
            }
#pragma warning disable CA1031 // A logging hook must never break the call.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        internal static ResponseInfo ReadInfo(int status, string path, HttpResponseMessage response)
        {
            long? limit = Number(Header(response, "X-RateLimit-Limit"));
            long? remaining = Number(Header(response, "X-RateLimit-Remaining"));
            long? reset = Number(Header(response, "X-RateLimit-Reset"));
            RateLimit? rateLimit = limit.HasValue && remaining.HasValue && reset.HasValue
                ? new RateLimit(limit.Value, remaining.Value, DateTimeOffset.FromUnixTimeSeconds(reset.Value))
                : null;
            return new ResponseInfo(
                status,
                path,
                Header(response, "X-Request-ID"),
                Number(Header(response, "X-Credits-Remaining")),
                rateLimit,
                Header(response, "X-Cache"),
                Header(response, "Content-Language"));
        }

        /// <summary>The first value of a response (or content) header, raw and unvalidated; null when absent or empty.</summary>
        internal static string? Header(HttpResponseMessage response, string name)
        {
            string? value = First(response.Headers, name) ?? (response.Content != null ? First(response.Content.Headers, name) : null);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static string? First(System.Net.Http.Headers.HttpHeaders headers, string name)
        {
#if NET6_0_OR_GREATER
            if (headers.NonValidated.TryGetValues(name, out System.Net.Http.Headers.HeaderStringValues values))
            {
                foreach (string v in values)
                {
                    return v;
                }
            }

            return null;
#else
            return headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
#endif
        }

        private static long? Number(string? value)
        {
            if (value == null || value.Trim().Length == 0)
            {
                return null;
            }

            string s = value.Trim();
            if (long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n))
            {
                return n;
            }

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                && !double.IsNaN(d) && !double.IsInfinity(d) && d >= long.MinValue && d <= long.MaxValue)
            {
                return (long)Math.Floor(d);
            }

            return null;
        }

        /// <summary>
        /// A <c>Retry-After</c> header: delta-seconds (<c>120</c>, also <c>1.5</c>) or an HTTP date
        /// (<c>Wed, 21 Oct 2026 07:28:00 GMT</c>). Null when absent or unreadable.
        /// </summary>
        internal static TimeSpan? ParseRetryAfter(string? value, DateTimeOffset now)
        {
            if (value == null || value.Trim().Length == 0)
            {
                return null;
            }

            string s = value.Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double secs))
            {
                if (double.IsNaN(secs) || double.IsInfinity(secs))
                {
                    return null;
                }

                return TimeSpan.FromMilliseconds(Math.Round(Math.Max(0, secs) * 1000));
            }

            if (DateTimeOffset.TryParseExact(s, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at)
                || DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out at))
            {
                TimeSpan wait = at - now;
                return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
            }

            return null;
        }

        /// <summary>
        /// The wait before retry number <paramref name="attempt"/> (0-based): <c>Retry-After</c> capped at
        /// 30 s, otherwise 0.5 s, 1 s, 2 s, ... up to 8 s, plus up to 0.25 s of jitter.
        /// </summary>
        internal static TimeSpan DefaultBackoff(int attempt, TimeSpan? retryAfter)
        {
            if (retryAfter.HasValue)
            {
                double ms = Math.Min(Math.Max(retryAfter.Value.TotalMilliseconds, 0), 30_000);
                return TimeSpan.FromMilliseconds(ms);
            }

            long baseMs = Math.Min(500L << Math.Min(attempt, 10), 8_000L);
            int jitter;
            lock (RandomLock)
            {
                jitter = Jitter.Next(250);
            }

            return TimeSpan.FromMilliseconds(baseMs + jitter);
        }

        private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            delay <= TimeSpan.Zero
                ? (cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask)
                : Task.Delay(delay, cancellationToken);

        private static LifeAstroTimeoutException TimedOut(TimeSpan timeout, Exception cause) =>
            new LifeAstroTimeoutException(
                "Request timed out after " + ((long)timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + "ms", cause);

        private static LifeAstroConnectionException NetworkFailure(Exception cause)
        {
            Exception root = cause;
            while (root.InnerException != null && (root is HttpRequestException || root is IOException))
            {
                root = root.InnerException;
            }

            string detail = string.IsNullOrEmpty(root.Message) ? root.GetType().Name : root.GetType().Name + ": " + root.Message;
            return new LifeAstroConnectionException("Network request failed (" + detail + ")", "connection_error", cause);
        }

        private static JsonElement ParseNull()
        {
            using (JsonDocument doc = JsonDocument.Parse("null"))
            {
                return doc.RootElement.Clone();
            }
        }

        /// <summary>What to do after an attempt: return a value, throw an error, or retry after a delay.</summary>
        private readonly struct Step
        {
            private Step(bool done, Result result, LifeAstroException? error, TimeSpan delay)
            {
                Done = done;
                Result = result;
                Error = error;
                Delay = delay;
            }

            internal bool Done { get; }

            internal Result Result { get; }

            internal LifeAstroException? Error { get; }

            internal TimeSpan Delay { get; }

            internal static Step Success(Result result) => new Step(true, result, null, TimeSpan.Zero);

            internal static Step Fail(LifeAstroException error) => new Step(false, default, error, TimeSpan.Zero);

            internal static Step Retry(TimeSpan delay) => new Step(false, default, null, delay);
        }

        /// <summary>A successful call: the value to return, the body as received and its status and request id.</summary>
        private readonly struct Result
        {
            internal Result(JsonElement value, JsonElement? body, int status, string? requestId)
            {
                Value = value;
                Body = body;
                Status = status;
                RequestId = requestId;
            }

            internal JsonElement Value { get; }

            internal JsonElement? Body { get; }

            internal int Status { get; }

            internal string? RequestId { get; }
        }
    }
}
