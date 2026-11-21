using System;
using System.Globalization;

namespace LifeAstroApi
{
    /// <summary>
    /// A birth (or event) moment and its place: the chart input of <c>Chart</c>, <c>Dasha</c>,
    /// <c>Milan</c> (<c>boy</c> / <c>girl</c>), transit-style methods (<c>birth</c>) and Western pairs
    /// (<c>personA</c> / <c>personB</c>). Each method sends it under the wire names the API reads,
    /// exactly as the Node.js SDK does: unprefixed <c>date</c>, <c>time</c>, <c>tz</c>, <c>lat</c>,
    /// <c>lon</c> for a chart, <c>birth.*</c> for the natal chart of a transit, <c>boy.*</c> /
    /// <c>girl.*</c> for kundli matching and <c>personA.*</c> / <c>personB.*</c> for Western pairs.
    /// Immutable, with value equality.
    /// <code>
    /// var arjun = new BirthInput("1990-01-15", "10:30", "Asia/Kolkata", 19.076, 72.8777);
    /// </code>
    /// </summary>
    /// <remarks>
    /// <c>Lat</c> and <c>Lon</c> may be left out only for the window methods
    /// (<c>Transit.SadeSatiWindowsAsync</c>, <c>Transit.WindowsAsync</c>), which need just the natal
    /// Moon; see <see cref="WithoutPlace(LifeAstroDate, LifeAstroTime, string)"/>.
    /// </remarks>
    public sealed class BirthInput : IEquatable<BirthInput>
    {
        /// <summary>A birth moment and place.</summary>
        /// <param name="date">Birth date, <c>YYYY-MM-DD</c>: a string, <see cref="DateTime"/>, <see cref="DateTimeOffset"/> or <c>DateOnly</c>.</param>
        /// <param name="time">Local birth time, 24-hour <c>HH:mm</c>: a string, <see cref="TimeSpan"/>, <see cref="DateTime"/> or <c>TimeOnly</c>.</param>
        /// <param name="tz">Time zone: an IANA name (<c>Asia/Kolkata</c>) or a UTC offset (<c>+05:30</c>).</param>
        /// <param name="lat">Latitude in decimal degrees, north positive.</param>
        /// <param name="lon">Longitude in decimal degrees, east positive.</param>
        /// <exception cref="ArgumentException">A value is missing, or a coordinate is not a finite number.</exception>
        public BirthInput(LifeAstroDate date, LifeAstroTime time, string tz, double lat, double lon)
            : this(date, time, tz, (double?)lat, lon)
        {
        }

        /// <summary>A birth moment at a <see cref="LifeAstroApi.Place"/> (its <c>Tz</c> is the birth time zone).</summary>
        /// <param name="date">Birth date, <c>YYYY-MM-DD</c>.</param>
        /// <param name="time">Local birth time, <c>HH:mm</c>.</param>
        /// <param name="place">Where the birth took place.</param>
        /// <exception cref="ArgumentNullException"><paramref name="place"/> is null.</exception>
        public BirthInput(LifeAstroDate date, LifeAstroTime time, Place place)
            : this(date, time, Wire.NotNull(place, nameof(place)).Tz, place.Lat, place.Lon)
        {
        }

        private BirthInput(LifeAstroDate date, LifeAstroTime time, string tz, double? lat, double? lon)
        {
            if (date.IsEmpty)
            {
                throw new ArgumentException("A birth date is required.", nameof(date));
            }

            if (time.IsEmpty)
            {
                throw new ArgumentException("A birth time is required.", nameof(time));
            }

            Date = date.Value!;
            Time = time.Value!;
            Tz = Wire.RequireText(tz, nameof(tz));
            if (lat.HasValue)
            {
                Wire.RequireFinite(lat.Value, nameof(lat));
                Wire.RequireFinite(lon!.Value, nameof(lon));
            }

            Lat = lat;
            Lon = lon;
        }

        /// <summary>
        /// A birth moment without a place, for the window methods that need only the natal Moon
        /// (<c>Transit.SadeSatiWindowsAsync</c>, <c>Transit.WindowsAsync</c>).
        /// </summary>
        /// <param name="date">Birth date, <c>YYYY-MM-DD</c>.</param>
        /// <param name="time">Local birth time, <c>HH:mm</c>.</param>
        /// <param name="tz">An IANA name or a UTC offset.</param>
        /// <returns>The birth input with no <c>Lat</c> / <c>Lon</c>.</returns>
        public static BirthInput WithoutPlace(LifeAstroDate date, LifeAstroTime time, string tz) =>
            new BirthInput(date, time, tz, null, null);

        /// <summary>Birth date as sent, <c>YYYY-MM-DD</c>.</summary>
        public string Date { get; }

        /// <summary>Local birth time as sent, 24-hour <c>HH:mm</c>.</summary>
        public string Time { get; }

        /// <summary>Time zone: an IANA name (<c>Asia/Kolkata</c>) or a UTC offset (<c>+05:30</c>).</summary>
        public string Tz { get; }

        /// <summary>Latitude in decimal degrees, north positive; null only for <see cref="WithoutPlace"/>.</summary>
        public double? Lat { get; }

        /// <summary>Longitude in decimal degrees, east positive; null only for <see cref="WithoutPlace"/>.</summary>
        public double? Lon { get; }

        /// <summary>Whether <see cref="Lat"/> and <see cref="Lon"/> are set.</summary>
        public bool HasPlace => Lat.HasValue;

        /// <summary>The place of this birth, or null when it has no coordinates.</summary>
        public Place? Place => Lat.HasValue ? new Place(Lat.Value, Lon!.Value, Tz) : null;

        /// <inheritdoc/>
        public bool Equals(BirthInput? other) =>
            other != null
            && string.Equals(Date, other.Date, StringComparison.Ordinal)
            && string.Equals(Time, other.Time, StringComparison.Ordinal)
            && string.Equals(Tz, other.Tz, StringComparison.Ordinal)
            && Nullable.Equals(Lat, other.Lat)
            && Nullable.Equals(Lon, other.Lon);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as BirthInput);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int h = StringComparer.Ordinal.GetHashCode(Date);
                h = (h * 397) ^ StringComparer.Ordinal.GetHashCode(Time);
                h = (h * 397) ^ StringComparer.Ordinal.GetHashCode(Tz);
                h = (h * 397) ^ Lat.GetHashCode();
                return (h * 397) ^ Lon.GetHashCode();
            }
        }

        /// <summary>The birth as <c>BirthInput { Date = 1990-01-15, Time = 10:30, ... }</c>.</summary>
        /// <returns>A readable form.</returns>
        public override string ToString() =>
            "BirthInput { Date = " + Date + ", Time = " + Time + ", Tz = " + Tz
            + (Lat.HasValue
                ? ", Lat = " + Lat.Value.ToString("R", CultureInfo.InvariantCulture) + ", Lon = " + Lon!.Value.ToString("R", CultureInfo.InvariantCulture)
                : string.Empty)
            + " }";
    }
}
