using System;

namespace LifeAstroApi
{
    /// <summary>
    /// Options for one call: a timeout, the raw body, or an <c>OnResponse</c> hook. Pass it as the
    /// <c>options</c> argument of any method.
    /// <code>
    /// var opts = new RequestOptions
    /// {
    ///     Timeout = TimeSpan.FromSeconds(5),
    ///     OnResponse = info => logger.LogInformation("{Credits} credits left", info.CreditsRemaining),
    /// };
    /// await client.Panchang.BasicAsync(p, opts);
    /// </code>
    /// One instance can be shared by concurrent calls as long as nobody changes it.
    /// </summary>
    public sealed class RequestOptions
    {
        private TimeSpan? _timeout;

        /// <summary>
        /// The timeout for this call instead of the client's, from sending the request to the last byte
        /// of the body; null to use the client's. Must be positive.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
        public TimeSpan? Timeout
        {
            get => _timeout;
            set
            {
                if (value.HasValue && value.Value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Timeout must be positive.");
                }

                _timeout = value;
            }
        }

        /// <summary>
        /// Return the body exactly as received, without unwrapping a <c>{ data, meta }</c> envelope if one
        /// is present. Default false.
        /// </summary>
        public bool Raw { get; set; }

        /// <summary>
        /// Called with this call's <see cref="ResponseInfo"/>, after the client-level hook. An exception it
        /// throws is ignored.
        /// </summary>
        public Action<ResponseInfo>? OnResponse { get; set; }
    }
}
