using System;
using System.Globalization;

namespace LifeAstroApi
{
    /// <summary>
    /// A local time-of-day parameter, sent as 24-hour <c>HH:mm</c>, or <c>HH:mm:ss</c> when it has
    /// seconds. Converts implicitly from a <see cref="string"/> (sent exactly as given), a
    /// <see cref="TimeSpan"/> (a time of day, 0 to 24 hours), the time of day of a
    /// <see cref="DateTime"/> or <see cref="DateTimeOffset"/> and, on .NET 8 and later, a
    /// <c>TimeOnly</c>:
    /// <code>
    /// new BirthInput("1990-01-15", "10:30", "Asia/Kolkata", 19.076, 72.8777);
    /// new BirthInput(entity.BirthDate, entity.BirthTime, entity.BirthTz, entity.Lat, entity.Lon); // DateTime + TimeSpan
    /// </code>
    /// Formatting always uses the invariant culture: digits are ASCII whatever the current culture.
    /// </summary>
    public readonly struct LifeAstroTime : IEquatable<LifeAstroTime>
    {
        private readonly string? _value;

        /// <summary>A time from its wire text, sent exactly as given.</summary>
        /// <param name="value">The time, normally <c>HH:mm</c>.</param>
        public LifeAstroTime(string? value)
        {
            _value = value;
        }

        /// <summary>The text sent on the wire, e.g. <c>10:30</c>; null for an empty value.</summary>
        public string? Value => _value;

        /// <summary>Whether this holds no value (null or empty text); an empty value is not sent.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(_value);

        /// <summary>A time of day, as <c>HH:mm</c> or <c>HH:mm:ss</c>.</summary>
        /// <param name="timeOfDay">The time since midnight, at least zero and less than 24 hours.</param>
        /// <returns>The time parameter.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative or 24 hours or more.</exception>
        public static LifeAstroTime FromTimeSpan(TimeSpan timeOfDay)
        {
            if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(nameof(timeOfDay), timeOfDay, "A time of day must be at least 00:00 and less than 24:00.");
            }

            return new LifeAstroTime(Format(timeOfDay.Hours, timeOfDay.Minutes, timeOfDay.Seconds));
        }

        /// <summary>The time of day of a <see cref="DateTime"/>, as <c>HH:mm</c> or <c>HH:mm:ss</c>.</summary>
        /// <param name="dateTime">The moment; its date and kind are ignored.</param>
        /// <returns>The time parameter.</returns>
        public static LifeAstroTime FromDateTime(DateTime dateTime) => FromTimeSpan(dateTime.TimeOfDay);

        /// <summary>The local time of day of a <see cref="DateTimeOffset"/>.</summary>
        /// <param name="dateTime">The moment; its time at its own offset is sent.</param>
        /// <returns>The time parameter.</returns>
        public static LifeAstroTime FromDateTimeOffset(DateTimeOffset dateTime) => FromTimeSpan(dateTime.TimeOfDay);

        /// <summary>A time from its wire text.</summary>
        /// <param name="value">The time, normally <c>HH:mm</c>.</param>
        public static implicit operator LifeAstroTime(string? value) => new LifeAstroTime(value);

        /// <summary>A time of day.</summary>
        /// <param name="timeOfDay">The time since midnight.</param>
        public static implicit operator LifeAstroTime(TimeSpan timeOfDay) => FromTimeSpan(timeOfDay);

        /// <summary>The time of day of a <see cref="DateTime"/>.</summary>
        /// <param name="dateTime">The moment.</param>
        public static implicit operator LifeAstroTime(DateTime dateTime) => FromDateTime(dateTime);

        /// <summary>The local time of day of a <see cref="DateTimeOffset"/>.</summary>
        /// <param name="dateTime">The moment.</param>
        public static implicit operator LifeAstroTime(DateTimeOffset dateTime) => FromDateTimeOffset(dateTime);

#if NET6_0_OR_GREATER
        /// <summary>A <c>TimeOnly</c>, as <c>HH:mm</c> or <c>HH:mm:ss</c>.</summary>
        /// <param name="time">The time.</param>
        /// <returns>The time parameter.</returns>
        public static LifeAstroTime FromTimeOnly(TimeOnly time) => new LifeAstroTime(Format(time.Hour, time.Minute, time.Second));

        /// <summary>A <c>TimeOnly</c>.</summary>
        /// <param name="time">The time.</param>
        public static implicit operator LifeAstroTime(TimeOnly time) => FromTimeOnly(time);
#endif

        private static string Format(int hours, int minutes, int seconds) =>
            seconds == 0
                ? hours.ToString("00", CultureInfo.InvariantCulture) + ":" + minutes.ToString("00", CultureInfo.InvariantCulture)
                : hours.ToString("00", CultureInfo.InvariantCulture) + ":" + minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + seconds.ToString("00", CultureInfo.InvariantCulture);

        /// <inheritdoc/>
        public bool Equals(LifeAstroTime other) => string.Equals(_value ?? string.Empty, other._value ?? string.Empty, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is LifeAstroTime other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value ?? string.Empty);

        /// <summary>The wire text, or an empty string.</summary>
        /// <returns>The text sent on the wire.</returns>
        public override string ToString() => _value ?? string.Empty;

        /// <summary>Value equality.</summary>
        /// <param name="left">A time.</param>
        /// <param name="right">Another time.</param>
        /// <returns><c>true</c> when both send the same text.</returns>
        public static bool operator ==(LifeAstroTime left, LifeAstroTime right) => left.Equals(right);

        /// <summary>Value inequality.</summary>
        /// <param name="left">A time.</param>
        /// <param name="right">Another time.</param>
        /// <returns><c>true</c> when they send different text.</returns>
        public static bool operator !=(LifeAstroTime left, LifeAstroTime right) => !left.Equals(right);
    }
}
