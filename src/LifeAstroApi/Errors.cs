using System;
using System.Text;
using System.Text.Json;

namespace LifeAstroApi
{
    /// <summary>
    /// Base class of every exception the SDK throws for a failed call.
    /// </summary>
    /// <remarks>
    /// Every non-2xx response becomes a subclass for its status, carrying the API's error
    /// <see cref="Code"/>, the HTTP <see cref="Status"/> and the <see cref="RequestId"/> to quote to
    /// support: <see cref="BadRequestException"/> (400), <see cref="AuthenticationException"/> (401),
    /// <see cref="PaymentRequiredException"/> (402), <see cref="PermissionException"/> (403),
    /// <see cref="NotFoundException"/> (404), <see cref="RateLimitException"/> (429, with
    /// <see cref="RateLimitException.RetryAfter"/>), <see cref="ServerException"/> (5xx). A call that
    /// gets no HTTP response throws <see cref="LifeAstroConnectionException"/> (status 0), or its
    /// subclass <see cref="LifeAstroTimeoutException"/> when it took longer than the timeout. A call
    /// you cancel through its <c>CancellationToken</c> throws <see cref="OperationCanceledException"/>
    /// instead.
    /// </remarks>
    public class LifeAstroException : Exception
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">A readable message (the API's own message for HTTP errors).</param>
        /// <param name="code">A machine-readable code, e.g. <c>insufficient_credits</c>.</param>
        /// <param name="status">The HTTP status, 0 for network failures.</param>
        /// <param name="requestId">The API request id, or null.</param>
        /// <param name="body">The parsed response body, or null.</param>
        /// <param name="innerException">The underlying exception, or null.</param>
        public LifeAstroException(string message, string code, int status, string? requestId, JsonElement? body, Exception? innerException)
            : base(message, innerException)
        {
            Code = code;
            Status = status;
            RequestId = requestId;
            Body = body;
        }

        /// <summary>
        /// The machine-readable error code, e.g. <c>insufficient_credits</c>, <c>rate_limited</c>,
        /// <c>invalid_param</c>, <c>timeout</c>, <c>connection_error</c>, <c>invalid_response</c>.
        /// </summary>
        public string Code { get; }

        /// <summary>The HTTP status; 0 for network failures and timeouts.</summary>
        public int Status { get; }

        /// <summary>
        /// The API request id (<c>request_id</c> of the error body, else the <c>X-Request-ID</c> header).
        /// Quote it to support. Null when there was none.
        /// </summary>
        public string? RequestId { get; }

        /// <summary>The parsed response body (a JSON string for a body that is not JSON), when there was one.</summary>
        public JsonElement? Body { get; }

        /// <summary>The type, message, status, code and request id, e.g. <c>... (401 unauthorized, request req_1)</c>.</summary>
        /// <returns>A one-line description followed by the inner exception, if any.</returns>
        public override string ToString()
        {
            var s = new StringBuilder(GetType().FullName).Append(": ").Append(Message).Append(" (");
            if (Status != 0)
            {
                s.Append(Status).Append(' ');
            }

            s.Append(Code);
            if (RequestId != null)
            {
                s.Append(", request ").Append(RequestId);
            }

            s.Append(')');
            if (InnerException != null)
            {
                s.Append(" ---> ").Append(InnerException);
            }

            if (StackTrace != null)
            {
                s.Append(Environment.NewLine).Append(StackTrace);
            }

            return s.ToString();
        }

        /// <summary>
        /// The exception for an error response: the subclass for its status, with the code, message and
        /// request id of the API's <c>{ "error": { code, message, request_id } }</c> body.
        /// </summary>
        /// <param name="status">The HTTP status.</param>
        /// <param name="body">The parsed body, or null.</param>
        /// <param name="retryAfter">The <c>Retry-After</c> wait, or null.</param>
        /// <param name="headerRequestId">The <c>X-Request-ID</c> header, used when the body has no id.</param>
        /// <returns>The exception to throw.</returns>
        public static LifeAstroException FromResponse(int status, JsonElement? body, TimeSpan? retryAfter, string? headerRequestId)
        {
            string? code = null;
            string? message = null;
            string? requestId = null;
            if (body.HasValue && body.Value.ValueKind == JsonValueKind.Object
                && body.Value.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)
            {
                code = Text(error, "code");
                message = Text(error, "message");
                requestId = Text(error, "request_id");
            }

            code ??= DefaultCode(status);
            message ??= "HTTP " + status;
            requestId ??= headerRequestId;
            switch (status)
            {
                case 400:
                    return new BadRequestException(message, code, status, requestId, body);
                case 401:
                    return new AuthenticationException(message, code, status, requestId, body);
                case 402:
                    return new PaymentRequiredException(message, code, status, requestId, body);
                case 403:
                    return new PermissionException(message, code, status, requestId, body);
                case 404:
                    return new NotFoundException(message, code, status, requestId, body);
                case 429:
                    return new RateLimitException(message, code, status, requestId, body, retryAfter);
                default:
                    return status >= 500
                        ? new ServerException(message, code, status, requestId, body)
                        : new LifeAstroException(message, code, status, requestId, body, null);
            }
        }

        private static string? Text(JsonElement node, string field) =>
            node.TryGetProperty(field, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static string DefaultCode(int status)
        {
            if (status == 0)
            {
                return "connection_error";
            }

            if (status >= 500)
            {
                return "internal_error";
            }

            return status >= 400 ? "bad_request" : "error";
        }
    }

    /// <summary>HTTP 400: a parameter is missing, malformed or out of range.</summary>
    public class BadRequestException : LifeAstroException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">The API's message.</param>
        /// <param name="code">The API's error code.</param>
        /// <param name="status">The HTTP status (400).</param>
        /// <param name="requestId">The request id, or null.</param>
        /// <param name="body">The parsed response body, or null.</param>
        public BadRequestException(string message, string code, int status, string? requestId, JsonElement? body)
            : base(message, code, status, requestId, body, null)
        {
        }
    }

    /// <summary>
    /// HTTP 401: missing or invalid API key. (Not to be confused with
    /// <c>System.Security.Authentication.AuthenticationException</c>; qualify it as
    /// <c>LifeAstroApi.AuthenticationException</c> if both namespaces are imported.)
    /// </summary>
    public class AuthenticationException : LifeAstroException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">The API's message.</param>
        /// <param name="code">The API's error code.</param>
        /// <param name="status">The HTTP status (401).</param>
        /// <param name="requestId">The request id, or null.</param>
        /// <param name="body">The parsed response body, or null.</param>
        public AuthenticationException(string message, string code, int status, string? requestId, JsonElement? body)
            : base(message, code, status, requestId, body, null)
        {
        }
    }

    /// <summary>HTTP 402: out of credits, or the trial is used up.</summary>
    public class PaymentRequiredException : LifeAstroException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">The API's message.</param>
        /// <param name="code">The API's error code.</param>
        /// <param name="status">The HTTP status (402).</param>
        /// <param name="requestId">The request id, or null.</param>
        /// <param name="body">The parsed response body, or null.</param>
        public PaymentRequiredException(string message, string code, int status, string? requestId, JsonElement? body)
            : base(message, code, status, requestId, body, null)
        {
        }
    }

    /// <summary>HTTP 403: the key is valid but may not make this call (plan, IP allow-list, suspended).</summary>
    public class PermissionException : LifeAstroException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">The API's message.</param>
        /// <param name="code">The API's error code.</param>
        /// <param name="status">The HTTP status (403).</param>
        /// <param name="requestId">The request id, or null.</param>
        /// <param name="body">The parsed response body, or null.</param>
        public PermissionException(string message, string code, int status, string? requestId, JsonElement? body)
            : base(message, code, status, requestId, body, null)
        {
        }
    }

    /// <summary>HTTP 404: unknown path or id.</summary>
    public class NotFoundException : LifeAstroException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">The API's message.</param>
        /// <param name="code">The API's error code.</param>
        /// <param name="status">The HTTP status (404).</param>
        /// <param name="requestId">The request id, or null.</param>
        /// <param name="body">The parsed response body, or null.</param>
        public NotFoundException(string message, string code, int status, string? requestId, JsonElement? body)
            : base(message, code, status, requestId, body, null)
        {
        }
    }

    /// <summary>
    /// HTTP 429: the per-minute rate limit or the daily quota is used up. Retried automatically
    /// (honouring <c>Retry-After</c>) before this is thrown.
    /// </summary>
    public class RateLimitException : LifeAstroException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">The API's message.</param>
        /// <param name="code">The API's error code.</param>
        /// <param name="status">The HTTP status (429).</param>
        /// <param name="requestId">The request id, or null.</param>
        /// <param name="body">The parsed response body, or null.</param>
        /// <param name="retryAfter">The <c>Retry-After</c> wait, or null.</param>
        public RateLimitException(string message, string code, int status, string? requestId, JsonElement? body, TimeSpan? retryAfter)
            : base(message, code, status, requestId, body, null)
        {
            RetryAfter = retryAfter;
        }

        /// <summary>
        /// How long to wait before trying again, from the <c>Retry-After</c> header (seconds or an HTTP
        /// date); null when the API sent none.
        /// </summary>
        public TimeSpan? RetryAfter { get; }
    }

    /// <summary>HTTP 5xx: the API failed. 500, 502, 503 and 504 are retried automatically before this is thrown.</summary>
    public class ServerException : LifeAstroException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">The API's message.</param>
        /// <param name="code">The API's error code.</param>
        /// <param name="status">The HTTP status.</param>
        /// <param name="requestId">The request id, or null.</param>
        /// <param name="body">The parsed response body, or null.</param>
        public ServerException(string message, string code, int status, string? requestId, JsonElement? body)
            : base(message, code, status, requestId, body, null)
        {
        }
    }

    /// <summary>
    /// The request got no HTTP response: DNS failure, refused or reset connection, TLS failure
    /// (code <c>connection_error</c>). <see cref="LifeAstroException.Status"/> is 0. Network failures
    /// are retried automatically before this is thrown.
    /// </summary>
    public class LifeAstroConnectionException : LifeAstroException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">What happened.</param>
        /// <param name="code"><c>connection_error</c> or <c>timeout</c>.</param>
        /// <param name="innerException">The underlying exception, or null.</param>
        public LifeAstroConnectionException(string message, string code, Exception? innerException)
            : base(message, code, 0, null, null, innerException)
        {
        }
    }

    /// <summary>
    /// The call took longer than its timeout (code <c>timeout</c>,
    /// <see cref="LifeAstroException.Status"/> 0). The timeout covers the whole call, including the body
    /// download. Retried automatically before this is thrown.
    /// </summary>
    public class LifeAstroTimeoutException : LifeAstroConnectionException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">What happened, e.g. <c>Request timed out after 30000ms</c>.</param>
        /// <param name="innerException">The underlying exception, or null.</param>
        public LifeAstroTimeoutException(string message, Exception? innerException)
            : base(message, "timeout", innerException)
        {
        }
    }
}
