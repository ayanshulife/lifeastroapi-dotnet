using System;

namespace LifeAstroApi
{
    /// <summary>
    /// What the API reported about one call besides its body: the request id, your credit balance after
    /// the call, your plan's per-minute rate limit and whether the answer came from the API's cache.
    /// Passed to <c>OnResponse</c> (client-level or per call) once for every final HTTP response,
    /// success or error; never for attempts that were retried, nor for network failures. Immutable,
    /// with value equality.
    /// </summary>
    public sealed class ResponseInfo : IEquatable<ResponseInfo>
    {
        /// <summary>Creates the info.</summary>
        /// <param name="status">HTTP status of the response.</param>
        /// <param name="path">Request path, e.g. <c>/v1/panchang/basic</c>.</param>
        /// <param name="requestId"><c>X-Request-ID</c>, or null.</param>
        /// <param name="creditsRemaining"><c>X-Credits-Remaining</c>, or null.</param>
        /// <param name="rateLimit"><c>X-RateLimit-*</c>, or null.</param>
        /// <param name="cache"><c>X-Cache</c>, or null.</param>
        /// <param name="language"><c>Content-Language</c>, or null.</param>
        public ResponseInfo(int status, string path, string? requestId, long? creditsRemaining, RateLimit? rateLimit, string? cache, string? language)
        {
            Status = status;
            Path = path ?? throw new ArgumentNullException(nameof(path));
            RequestId = requestId;
            CreditsRemaining = creditsRemaining;
            RateLimit = rateLimit;
            Cache = cache;
            Language = language;
        }

        /// <summary>HTTP status of the response.</summary>
        public int Status { get; }

        /// <summary>Request path, e.g. <c>/v1/panchang/basic</c>.</summary>
        public string Path { get; }

        /// <summary><c>X-Request-ID</c>; quote it to support. Null when absent.</summary>
        public string? RequestId { get; }

        /// <summary>
        /// <c>X-Credits-Remaining</c>: the balance after this call (unchanged after a failed call, which is
        /// free). Null on free-tier accounts, whose allowance is not a balance, and on calls that cost
        /// nothing.
        /// </summary>
        public long? CreditsRemaining { get; }

        /// <summary>
        /// <c>X-RateLimit-*</c>: requests per minute, how many are left and when the window resets. Null
        /// unless all three headers are present.
        /// </summary>
        public RateLimit? RateLimit { get; }

        /// <summary><c>X-Cache</c>, e.g. <c>HIT</c> when the API answered from its cache (billed the same). Null when absent.</summary>
        public string? Cache { get; }

        /// <summary><c>Content-Language</c>: the language of the answer. Null when absent.</summary>
        public string? Language { get; }

        /// <inheritdoc/>
        public bool Equals(ResponseInfo? other) =>
            other != null
            && Status == other.Status
            && Path == other.Path
            && RequestId == other.RequestId
            && CreditsRemaining == other.CreditsRemaining
            && Equals(RateLimit, other.RateLimit)
            && Cache == other.Cache
            && Language == other.Language;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as ResponseInfo);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int h = Status;
                h = (h * 397) ^ Path.GetHashCode();
                h = (h * 397) ^ (RequestId?.GetHashCode() ?? 0);
                h = (h * 397) ^ CreditsRemaining.GetHashCode();
                h = (h * 397) ^ (RateLimit?.GetHashCode() ?? 0);
                h = (h * 397) ^ (Cache?.GetHashCode() ?? 0);
                return (h * 397) ^ (Language?.GetHashCode() ?? 0);
            }
        }

        /// <summary>A readable form, for logs.</summary>
        /// <returns>The values that are set.</returns>
        public override string ToString() =>
            "ResponseInfo { Status = " + Status + ", Path = " + Path
            + (RequestId != null ? ", RequestId = " + RequestId : string.Empty)
            + (CreditsRemaining.HasValue ? ", CreditsRemaining = " + CreditsRemaining.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty)
            + (RateLimit != null ? ", RateLimit = " + RateLimit : string.Empty)
            + (Cache != null ? ", Cache = " + Cache : string.Empty)
            + (Language != null ? ", Language = " + Language : string.Empty)
            + " }";
    }

    /// <summary>Your plan's per-minute rate limit, from the <c>X-RateLimit-*</c> response headers. Immutable, with value equality.</summary>
    public sealed class RateLimit : IEquatable<RateLimit>
    {
        /// <summary>Creates the rate limit.</summary>
        /// <param name="limit">Requests allowed per minute.</param>
        /// <param name="remaining">Requests left in the current window.</param>
        /// <param name="resetAt">When the window resets.</param>
        public RateLimit(long limit, long remaining, DateTimeOffset resetAt)
        {
            Limit = limit;
            Remaining = remaining;
            ResetAt = resetAt;
        }

        /// <summary>Requests allowed per minute (<c>X-RateLimit-Limit</c>).</summary>
        public long Limit { get; }

        /// <summary>Requests left in the current window (<c>X-RateLimit-Remaining</c>).</summary>
        public long Remaining { get; }

        /// <summary>When the window resets (<c>X-RateLimit-Reset</c>, Unix seconds), in UTC.</summary>
        public DateTimeOffset ResetAt { get; }

        /// <inheritdoc/>
        public bool Equals(RateLimit? other) =>
            other != null && Limit == other.Limit && Remaining == other.Remaining && ResetAt.Equals(other.ResetAt);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as RateLimit);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (((Limit.GetHashCode() * 397) ^ Remaining.GetHashCode()) * 397) ^ ResetAt.GetHashCode();
            }
        }

        /// <summary>A readable form, e.g. <c>249/250, resets 2026-11-21T10:00:00Z</c>.</summary>
        /// <returns>The rate limit.</returns>
        public override string ToString() =>
            Remaining.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + Limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ", resets " + ResetAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }
}
