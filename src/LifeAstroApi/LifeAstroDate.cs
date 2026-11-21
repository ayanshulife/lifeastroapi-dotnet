using System;
using System.Globalization;

namespace LifeAstroApi
{
    /// <summary>
    /// A date parameter, sent as <c>YYYY-MM-DD</c>. Converts implicitly from a <see cref="string"/>
    /// (sent exactly as given), a <see cref="DateTime"/> or <see cref="DateTimeOffset"/> (their date
    /// part; the time and the offset are ignored) and, on .NET 8 and later, a <c>DateOnly</c>, so every
    /// date property and constructor parameter takes any of them:
    /// <code>
    /// new PanchangBasicParams(mumbai) { Date = "2027-01-15" };
    /// new PanchangBasicParams(mumbai) { Date = DateTime.Today };
    /// </code>
    /// Formatting always uses the invariant culture and the Gregorian calendar, whatever the current
    /// culture of the thread (a Hijri or Thai Buddhist calendar never reaches the URL).
    /// </summary>
    public readonly struct LifeAstroDate : IEquatable<LifeAstroDate>
    {
        private readonly string? _value;

        /// <summary>A date from its wire text, sent exactly as given.</summary>
        /// <param name="value">The date, normally <c>YYYY-MM-DD</c>.</param>
        public LifeAstroDate(string? value)
        {
            _value = value;
        }

        /// <summary>The text sent on the wire, e.g. <c>2027-01-15</c>; null for an empty value.</summary>
        public string? Value => _value;

        /// <summary>Whether this holds no value (null or empty text); an empty value is not sent.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(_value);

        /// <summary>The date of a <see cref="DateTime"/>, as <c>YYYY-MM-DD</c>.</summary>
        /// <param name="date">The date; its time of day and kind are ignored.</param>
        /// <returns>The date parameter.</returns>
        public static LifeAstroDate FromDateTime(DateTime date) =>
            new LifeAstroDate(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        /// <summary>The local date of a <see cref="DateTimeOffset"/>, as <c>YYYY-MM-DD</c>.</summary>
        /// <param name="date">The moment; its date at its own offset is sent.</param>
        /// <returns>The date parameter.</returns>
        public static LifeAstroDate FromDateTimeOffset(DateTimeOffset date) => FromDateTime(date.DateTime);

        /// <summary>A date from its wire text.</summary>
        /// <param name="value">The date, normally <c>YYYY-MM-DD</c>.</param>
        public static implicit operator LifeAstroDate(string? value) => new LifeAstroDate(value);

        /// <summary>The date of a <see cref="DateTime"/>.</summary>
        /// <param name="date">The date; its time of day is ignored.</param>
        public static implicit operator LifeAstroDate(DateTime date) => FromDateTime(date);

        /// <summary>The local date of a <see cref="DateTimeOffset"/>.</summary>
        /// <param name="date">The moment.</param>
        public static implicit operator LifeAstroDate(DateTimeOffset date) => FromDateTimeOffset(date);

#if NET6_0_OR_GREATER
        /// <summary>A <c>DateOnly</c>, as <c>YYYY-MM-DD</c>.</summary>
        /// <param name="date">The date.</param>
        /// <returns>The date parameter.</returns>
        public static LifeAstroDate FromDateOnly(DateOnly date) =>
            new LifeAstroDate(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        /// <summary>A <c>DateOnly</c>.</summary>
        /// <param name="date">The date.</param>
        public static implicit operator LifeAstroDate(DateOnly date) => FromDateOnly(date);
#endif

        /// <inheritdoc/>
        public bool Equals(LifeAstroDate other) => string.Equals(_value ?? string.Empty, other._value ?? string.Empty, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is LifeAstroDate other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value ?? string.Empty);

        /// <summary>The wire text, or an empty string.</summary>
        /// <returns>The text sent on the wire.</returns>
        public override string ToString() => _value ?? string.Empty;

        /// <summary>Value equality.</summary>
        /// <param name="left">A date.</param>
        /// <param name="right">Another date.</param>
        /// <returns><c>true</c> when both send the same text.</returns>
        public static bool operator ==(LifeAstroDate left, LifeAstroDate right) => left.Equals(right);

        /// <summary>Value inequality.</summary>
        /// <param name="left">A date.</param>
        /// <param name="right">Another date.</param>
        /// <returns><c>true</c> when they send different text.</returns>
        public static bool operator !=(LifeAstroDate left, LifeAstroDate right) => !left.Equals(right);
    }
}
